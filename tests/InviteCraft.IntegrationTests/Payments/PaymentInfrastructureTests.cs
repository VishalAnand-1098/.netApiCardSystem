using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using InviteCraft.Api.Contracts;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Features.Payments;
using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Identity;
using InviteCraft.Infrastructure.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InviteCraft.IntegrationTests.Payments;

/// <summary>HMAC signatures as Razorpay defines them, checked against vectors computed independently (Python's hmac module).</summary>
public class PaymentSignatureTests
{
    private const string CheckoutSignature = "e6128c1c1b29c598dc0ab58bdd64927e79516caa253bdfdcec603b33606068bf";
    private const string WebhookSignature = "f53520f9a4c7a158c35e4e741871bac35079a62acfe8194ce6b5da74b69ad6fc";
    private const string WebhookBody = """{"event":"payment.captured","payload":{"payment":{"entity":{"id":"pay_1"}}}}""";

    [Fact]
    public void The_checkout_signature_is_hmac_sha256_of_order_id_pipe_payment_id_in_lower_hex()
    {
        PaymentSignatures.Sign("s3cr3t-key", PaymentSignatures.CheckoutMessage("order_Abc123", "pay_Xyz789")).ShouldBe(CheckoutSignature);
        PaymentSignatures.IsValid("s3cr3t-key", "order_Abc123|pay_Xyz789", CheckoutSignature).ShouldBeTrue();
    }

    [Fact]
    public void The_webhook_signature_is_hmac_sha256_of_the_exact_body()
    {
        PaymentSignatures.IsValid("whsec-1", WebhookBody, WebhookSignature).ShouldBeTrue();
        PaymentSignatures.IsValid("whsec-1", WebhookBody + " ", WebhookSignature).ShouldBeFalse(); // one extra byte breaks it
    }

    [Theory]
    [InlineData("wrong-secret", "order_Abc123|pay_Xyz789", CheckoutSignature)]
    [InlineData("s3cr3t-key", "order_Abc123|pay_OTHER", CheckoutSignature)]
    [InlineData("s3cr3t-key", "order_Abc123|pay_Xyz789", "")]
    [InlineData("s3cr3t-key", "order_Abc123|pay_Xyz789", "abc")]
    [InlineData("", "order_Abc123|pay_Xyz789", CheckoutSignature)]
    public void Anything_else_is_rejected(string secret, string message, string signature) =>
        PaymentSignatures.IsValid(secret, message, signature).ShouldBeFalse();

    [Fact]
    public void Upper_case_hex_from_a_client_still_verifies_because_the_comparison_is_on_the_value()
    {
        PaymentSignatures.IsValid("s3cr3t-key", "order_Abc123|pay_Xyz789", CheckoutSignature.ToUpperInvariant()).ShouldBeTrue();
    }
}

public class RazorpayGatewayTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly PaymentSettings Settings = new() { Provider = "Razorpay", KeyId = "rzp_test_key", KeySecret = "key_secret", WebhookSecret = "whsec", ApiBaseUrl = "https://api.razorpay.com/v1/" };

    private sealed class Recorder(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }

    private static (RazorpayGateway Gateway, Recorder Recorder) Build(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var recorder = new Recorder(respond);
        return (new RazorpayGateway(new RecordingFactory(recorder), Options.Create(Settings), NullLogger<RazorpayGateway>.Instance), recorder);
    }

    private sealed class RecordingFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            name.ShouldBe(RazorpayGateway.HttpClientName);
            var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri(Settings.ApiBaseUrl) };
            client.DefaultRequestHeaders.Authorization = RazorpayGateway.BasicAuth(Settings);
            return client;
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Creating_an_order_posts_paise_currency_receipt_and_notes_with_basic_credentials()
    {
        var (gateway, recorder) = Build(_ => Json("""{"id":"order_Abc123","amount":29900,"currency":"INR","status":"created"}"""));

        var order = await gateway.CreateOrderAsync(new GatewayOrderRequest("ICO-2026-ABC123", 29900, "INR", new Dictionary<string, string> { ["package"] = "PREMIUM" }), Ct);

        order.ShouldBe(new GatewayOrder("order_Abc123", 29900));
        var (request, body) = recorder.Calls.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.ToString().ShouldBe("https://api.razorpay.com/v1/orders");
        request.Headers.Authorization!.Scheme.ShouldBe("Basic");
        Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)).ShouldBe("rzp_test_key:key_secret");

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("amount").GetInt64().ShouldBe(29900);
        json.RootElement.GetProperty("currency").GetString().ShouldBe("INR");
        json.RootElement.GetProperty("receipt").GetString().ShouldBe("ICO-2026-ABC123");
        json.RootElement.GetProperty("notes").GetProperty("package").GetString().ShouldBe("PREMIUM");
    }

    [Fact]
    public async Task Receipts_longer_than_razorpays_limit_are_shortened_instead_of_rejected()
    {
        var (gateway, recorder) = Build(_ => Json("""{"id":"order_x","amount":100}"""));

        await gateway.CreateOrderAsync(new GatewayOrderRequest(new string('R', 80), 100, "INR", new Dictionary<string, string>()), Ct);

        using var json = JsonDocument.Parse(recorder.Calls.Single().Body);
        json.RootElement.GetProperty("receipt").GetString()!.Length.ShouldBe(40);
    }

    [Fact]
    public async Task Fetching_a_payment_parses_it_and_keeps_only_non_personal_fields()
    {
        var (gateway, recorder) = Build(_ => Json("""
            {"id":"pay_1","order_id":"order_Abc123","status":"captured","amount":29900,"currency":"INR","method":"upi","email":"a@b.c","contact":"+911234567890","vpa":"me@upi","card":{"last4":"4242"}}
            """));

        var payment = await gateway.GetPaymentAsync("pay_1", Ct);

        payment.ShouldNotBeNull();
        (payment!.Id, payment.OrderId, payment.Status, payment.AmountPaise, payment.Currency, payment.Method).ShouldBe(("pay_1", "order_Abc123", "captured", 29900L, "INR", "upi"));
        payment.Raw.ShouldNotContain("a@b.c");
        payment.Raw.ShouldNotContain("me@upi");
        payment.Raw.ShouldNotContain("4242");
        recorder.Calls.Single().Request.RequestUri!.ToString().ShouldBe("https://api.razorpay.com/v1/payments/pay_1");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task An_unknown_payment_id_is_null_not_an_exception(HttpStatusCode status)
    {
        var (gateway, _) = Build(_ => Json("""{"error":{"code":"BAD_REQUEST_ERROR","description":"The id provided does not exist"}}""", status));

        (await gateway.GetPaymentAsync("pay_nope", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Capturing_posts_the_amount_and_currency()
    {
        var (gateway, recorder) = Build(_ => Json("""{"id":"pay_1","order_id":"order_Abc123","status":"captured","amount":29900,"currency":"INR"}"""));

        var payment = await gateway.CapturePaymentAsync("pay_1", 29900, "INR", Ct);

        payment.Status.ShouldBe("captured");
        recorder.Calls.Single().Request.RequestUri!.ToString().ShouldBe("https://api.razorpay.com/v1/payments/pay_1/capture");
        recorder.Calls.Single().Body.ShouldContain("29900");
    }

    [Fact]
    public async Task Gateway_errors_surface_with_the_providers_explanation_and_never_the_credentials()
    {
        var (gateway, _) = Build(_ => Json("""{"error":{"code":"BAD_REQUEST_ERROR","description":"Authentication failed"}}""", HttpStatusCode.Unauthorized));

        var error = await Should.ThrowAsync<HttpRequestException>(() => gateway.CreateOrderAsync(new GatewayOrderRequest("r", 100, "INR", new Dictionary<string, string>()), Ct));

        error.Message.ShouldContain("Authentication failed");
        error.Message.ShouldContain("401");
        error.Message.ShouldNotContain("key_secret");
        error.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void Checkout_and_webhook_signatures_use_different_secrets()
    {
        var (gateway, _) = Build(_ => Json("{}"));
        var checkout = PaymentSignatures.Sign("key_secret", "order_1|pay_1");
        var webhook = PaymentSignatures.Sign("whsec", "{\"a\":1}");

        gateway.VerifyCheckoutSignature("order_1", "pay_1", checkout).ShouldBeTrue();
        gateway.VerifyCheckoutSignature("order_1", "pay_1", PaymentSignatures.Sign("whsec", "order_1|pay_1")).ShouldBeFalse();
        gateway.VerifyWebhookSignature("{\"a\":1}", webhook).ShouldBeTrue();
        gateway.VerifyWebhookSignature("{\"a\":1}", PaymentSignatures.Sign("key_secret", "{\"a\":1}")).ShouldBeFalse();
    }
}

public class SimulatedGatewayTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static SimulatedGateway Build() => new(Options.Create(new PaymentSettings { Provider = "Simulated", KeySecret = "dev-secret", WebhookSecret = "dev-webhook" }));

    [Fact]
    public async Task A_simulated_payment_round_trips_through_the_same_signature_check_as_the_real_thing()
    {
        var gateway = Build();
        var order = await gateway.CreateOrderAsync(new GatewayOrderRequest("r", 29900, "INR", new Dictionary<string, string>()), Ct);

        var paid = gateway.CreatePayment(order.Id, 29900, success: true);

        gateway.VerifyCheckoutSignature(paid.GatewayOrderId, paid.PaymentId, paid.Signature).ShouldBeTrue();
        gateway.VerifyCheckoutSignature(paid.GatewayOrderId, paid.PaymentId, "0".PadLeft(64, '0')).ShouldBeFalse();
        var fetched = await gateway.GetPaymentAsync(paid.PaymentId, Ct);
        (fetched!.Status, fetched.AmountPaise, fetched.OrderId).ShouldBe(("captured", 29900L, order.Id));
    }

    [Fact]
    public async Task A_simulated_failure_is_reported_as_failed_and_unknown_ids_as_missing()
    {
        var gateway = Build();
        var failed = gateway.CreatePayment("order_sim_abc", 100, success: false);

        (await gateway.GetPaymentAsync(failed.PaymentId, Ct))!.Status.ShouldBe("failed");
        (await gateway.GetPaymentAsync("pay_real_looking", Ct)).ShouldBeNull();
        (await gateway.GetPaymentAsync("pay_sim_s_abc_notanumber", Ct)).ShouldBeNull();
    }

    [Fact]
    public void Webhooks_are_signed_with_the_webhook_secret_not_the_key_secret()
    {
        var gateway = Build();
        var signature = gateway.SignWebhook("{}");

        gateway.VerifyWebhookSignature("{}", signature).ShouldBeTrue();
        signature.ShouldBe(PaymentSignatures.Sign("dev-webhook", "{}"));
    }
}

public class PaymentSettingsValidatorTests
{
    private static ValidateOptionsResult Validate(PaymentSettings settings, string environment) =>
        new PaymentSettingsValidator(new FakeEnvironment(environment)).Validate(null, settings);

    private static PaymentSettings Razorpay() => new() { Provider = "Razorpay", KeyId = "rzp_live_x", KeySecret = "s", WebhookSecret = "w" };

    [Fact]
    public void The_simulated_provider_is_refused_anywhere_customers_could_pay()
    {
        var result = Validate(new PaymentSettings { Provider = "Simulated" }, Environments.Production);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("only allowed in the Development");
        Validate(new PaymentSettings { Provider = "Simulated" }, "Staging").Failed.ShouldBeTrue();
        Validate(new PaymentSettings { Provider = "simulated" }, Environments.Development).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Razorpay_needs_its_keys_and_webhook_secret_and_an_https_url()
    {
        Validate(Razorpay(), Environments.Production).Succeeded.ShouldBeTrue();

        var noKey = Razorpay();
        noKey.KeyId = "";
        Validate(noKey, Environments.Production).FailureMessage.ShouldContain("KeyId");

        var noWebhook = Razorpay();
        noWebhook.WebhookSecret = " ";
        Validate(noWebhook, Environments.Production).FailureMessage.ShouldContain("WebhookSecret");

        var http = Razorpay();
        http.ApiBaseUrl = "http://api.razorpay.com/v1/";
        Validate(http, Environments.Production).FailureMessage.ShouldContain("https");
    }

    [Fact]
    public void An_unknown_provider_and_silly_timeouts_are_rejected()
    {
        Validate(new PaymentSettings { Provider = "Stripe" }, Environments.Development).FailureMessage.ShouldContain("Razorpay");

        var slow = Razorpay();
        slow.TimeoutSeconds = 600;
        slow.ExpirySweepSeconds = 1;
        var failures = Validate(slow, Environments.Production).Failures!.ToList();
        failures.Count.ShouldBe(2);
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}

/// <summary>Authentication and request-shape behaviour of the payment endpoints (no database needed).</summary>
public class PaymentEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string SomeId = "0193a1b2-0000-7000-8000-000000000001";

    private string CustomerToken() => factory.Services.GetRequiredService<IAccessTokenService>()
        .Create(new User { Email = "c@example.com", FullName = "Customer" }, [RoleNames.Customer]).Token;

    private HttpRequestMessage Authorised(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CustomerToken());
        return request;
    }

    [Theory]
    [InlineData("GET", $"/api/v1/checkout/options/{SomeId}")]
    [InlineData("POST", "/api/v1/coupons/validate")]
    [InlineData("POST", "/api/v1/checkout/orders")]
    [InlineData("POST", "/api/v1/checkout/verify")]
    [InlineData("POST", $"/api/v1/checkout/orders/{SomeId}/cancel")]
    [InlineData("GET", $"/api/v1/orders/{SomeId}")]
    [InlineData("POST", "/api/v1/dev/payments/simulate")]
    public async Task Checkout_endpoints_require_sign_in(string method, string url)
    {
        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Creating_an_order_validates_the_request_shape_before_touching_the_database()
    {
        var response = await _client.SendAsync(Authorised(HttpMethod.Post, "/api/v1/checkout/orders",
            new { invitationId = SomeId, packageCode = "PREMIUM", idempotencyKey = "short" }), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(Ct);
        body!.Errors!.Select(e => e.Field).ShouldContain("idempotencyKey");
    }

    [Fact]
    public async Task Verifying_a_payment_with_malformed_details_is_rejected_by_validation()
    {
        var response = await _client.SendAsync(Authorised(HttpMethod.Post, "/api/v1/checkout/verify",
            new { orderId = SomeId, gatewayOrderId = "order bad id!", paymentId = "pay_1", signature = "x" }), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_coupon_request_without_a_code_is_rejected()
    {
        var response = await _client.SendAsync(Authorised(HttpMethod.Post, "/api/v1/coupons/validate",
            new { invitationId = SomeId, packageCode = "PREMIUM", code = "" }), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------ webhook

    private Task<HttpResponseMessage> Webhook(string body, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/razorpay") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (signature is not null) request.Headers.Add("X-Razorpay-Signature", signature);
        return _client.SendAsync(request, Ct);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task The_webhook_needs_no_login_but_rejects_a_missing_or_wrong_signature(string? signature)
    {
        var response = await Webhook("""{"event":"payment.captured"}""", signature);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(Ct);
        body!.Errors!.Single().Code.ShouldBe("Webhook.InvalidSignature");
    }

    [Fact]
    public async Task A_correctly_signed_but_malformed_webhook_is_a_client_error_and_stores_nothing()
    {
        const string body = "this is not json";
        var gateway = (ISimulatedGateway)factory.Services.GetRequiredService<IPaymentGateway>();

        var response = await Webhook(body, gateway.SignWebhook(body));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ApiResponse<object>>(Ct))!.Errors!.Single().Code.ShouldBe("Webhook.Malformed");
    }

    [Fact]
    public async Task The_test_host_runs_the_simulated_gateway_so_no_test_can_reach_a_real_provider()
    {
        factory.Services.GetRequiredService<IPaymentGateway>().Provider.ShouldBe("Simulated");
        await Task.CompletedTask;
    }
}
