using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using InviteCraft.Application.Features.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InviteCraft.Infrastructure.Payments;

/// <summary>
/// Razorpay over HTTPS (https://razorpay.com/docs/api/). Authenticated with the key id/secret as HTTP Basic credentials; money is
/// always in paise. Failures surface as <see cref="HttpRequestException"/> with a message that never contains credentials.
/// </summary>
internal sealed class RazorpayGateway(IHttpClientFactory httpClients, IOptions<PaymentSettings> options, ILogger<RazorpayGateway> logger) : IPaymentGateway
{
    public const string HttpClientName = "razorpay";
    private const int MaxReceiptLength = 40;

    private readonly PaymentSettings _settings = options.Value;

    public string Provider => PaymentSettings.RazorpayProvider;
    public string PublicKeyId => _settings.KeyId;

    public async Task<GatewayOrder> CreateOrderAsync(GatewayOrderRequest request, CancellationToken cancellationToken = default)
    {
        using var client = httpClients.CreateClient(HttpClientName);
        using var response = await client.PostAsJsonAsync("orders", new
        {
            amount = request.AmountPaise,
            currency = request.Currency,
            receipt = request.Receipt.Length > MaxReceiptLength ? request.Receipt[..MaxReceiptLength] : request.Receipt,
            notes = request.Notes,
        }, cancellationToken);

        var json = await ReadAsync(response, "create order", cancellationToken);
        var root = json.RootElement;
        return new GatewayOrder(root.GetProperty("id").GetString()!, root.GetProperty("amount").GetInt64());
    }

    public async Task<GatewayPayment?> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        using var client = httpClients.CreateClient(HttpClientName);
        using var response = await client.GetAsync($"payments/{Uri.EscapeDataString(paymentId)}", cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            return null; // Razorpay answers 400 "The id provided does not exist" for unknown payments

        using var json = await ReadAsync(response, "fetch payment", cancellationToken);
        return GatewayPayments.Parse(json.RootElement);
    }

    public async Task<GatewayPayment> CapturePaymentAsync(string paymentId, long amountPaise, string currency, CancellationToken cancellationToken = default)
    {
        using var client = httpClients.CreateClient(HttpClientName);
        using var response = await client.PostAsJsonAsync($"payments/{Uri.EscapeDataString(paymentId)}/capture", new { amount = amountPaise, currency }, cancellationToken);
        using var json = await ReadAsync(response, "capture payment", cancellationToken);
        return GatewayPayments.Parse(json.RootElement);
    }

    public async Task<GatewayRefund> RefundPaymentAsync(string paymentId, long amountPaise, string reason, CancellationToken cancellationToken = default)
    {
        using var client = httpClients.CreateClient(HttpClientName);
        using var response = await client.PostAsJsonAsync($"payments/{Uri.EscapeDataString(paymentId)}/refund",
            new { amount = amountPaise, speed = "normal", notes = new { reason = reason.Length > 200 ? reason[..200] : reason } }, cancellationToken);
        using var json = await ReadAsync(response, "refund payment", cancellationToken);
        var root = json.RootElement;
        return new GatewayRefund(root.GetProperty("id").GetString()!, root.GetProperty("amount").GetInt64(),
            root.TryGetProperty("status", out var status) ? status.GetString() ?? "processed" : "processed");
    }

    public bool VerifyCheckoutSignature(string gatewayOrderId, string paymentId, string signature) =>
        PaymentSignatures.IsValid(_settings.KeySecret, PaymentSignatures.CheckoutMessage(gatewayOrderId, paymentId), signature);

    public bool VerifyWebhookSignature(string rawBody, string signature) =>
        PaymentSignatures.IsValid(_settings.WebhookSecret, rawBody, signature);

    private async Task<JsonDocument> ReadAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Razorpay explains errors as { "error": { "code", "description", "field" } } — useful in logs, and free of secrets.
            var description = TryDescribe(body);
            logger.LogError("Razorpay {Action} failed with {Status}: {Description}", action, (int)response.StatusCode, description);
            throw new HttpRequestException($"Razorpay could not {action} ({(int)response.StatusCode}): {description}", null, response.StatusCode);
        }

        return JsonDocument.Parse(body);
    }

    private static string TryDescribe(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("description", out var description))
                return description.GetString() ?? "unknown error";
        }
        catch (JsonException)
        {
            // not JSON
        }

        return body.Length > 200 ? body[..200] : body;
    }

    /// <summary>Basic-auth header for the named HTTP client (see DI).</summary>
    public static AuthenticationHeaderValue BasicAuth(PaymentSettings settings) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.KeyId}:{settings.KeySecret}")));
}
