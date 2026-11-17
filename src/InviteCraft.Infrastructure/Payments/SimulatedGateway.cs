using InviteCraft.Application.Features.Payments;
using Microsoft.Extensions.Options;

namespace InviteCraft.Infrastructure.Payments;

/// <summary>
/// Development stand-in for Razorpay so the entire flow — checkout, signature verification, webhook, unlock — can be exercised without
/// keys or network access. It signs exactly like the real thing; payment ids carry what the "gateway" would remember
/// (<c>pay_sim_{s|f}_{orderGuid}_{amountPaise}</c>), so it holds no state. Refused outside Development by <see cref="PaymentSettingsValidator"/>.
/// </summary>
internal sealed class SimulatedGateway(IOptions<PaymentSettings> options) : IPaymentGateway, ISimulatedGateway
{
    private const string OrderPrefix = "order_sim_";
    private const string PaymentPrefix = "pay_sim_";

    private readonly PaymentSettings _settings = options.Value;

    public string Provider => PaymentSettings.SimulatedProvider;
    public string PublicKeyId => string.IsNullOrWhiteSpace(_settings.KeyId) ? "rzp_test_simulated" : _settings.KeyId;

    public Task<GatewayOrder> CreateOrderAsync(GatewayOrderRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GatewayOrder(OrderPrefix + Guid.NewGuid().ToString("N"), request.AmountPaise));

    public Task<GatewayPayment?> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        var parts = paymentId.Split('_');
        // pay_sim_{s|f}_{orderGuid}_{amountPaise}
        if (parts.Length != 5 || !paymentId.StartsWith(PaymentPrefix, StringComparison.Ordinal) || !long.TryParse(parts[4], out var paise))
            return Task.FromResult<GatewayPayment?>(null);

        var success = parts[2] == "s";
        var payment = new GatewayPayment(paymentId, OrderPrefix + parts[3], success ? "captured" : "failed", paise, "INR", "upi",
            success ? null : "BAD_REQUEST_ERROR", success ? null : "Payment failed (simulated)", "{}");
        return Task.FromResult<GatewayPayment?>(payment);
    }

    public Task<GatewayPayment> CapturePaymentAsync(string paymentId, long amountPaise, string currency, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GatewayPayment(paymentId, string.Empty, "captured", amountPaise, currency, "upi", null, null, "{}"));

    public Task<GatewayRefund> RefundPaymentAsync(string paymentId, long amountPaise, string reason, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GatewayRefund("rfnd_sim_" + Guid.NewGuid().ToString("N"), amountPaise, "processed"));

    public bool VerifyCheckoutSignature(string gatewayOrderId, string paymentId, string signature) =>
        PaymentSignatures.IsValid(_settings.KeySecret, PaymentSignatures.CheckoutMessage(gatewayOrderId, paymentId), signature);

    public bool VerifyWebhookSignature(string rawBody, string signature) =>
        PaymentSignatures.IsValid(_settings.WebhookSecret, rawBody, signature);

    public SimulatedPaymentDto CreatePayment(string gatewayOrderId, long amountPaise, bool success)
    {
        var orderGuid = gatewayOrderId.StartsWith(OrderPrefix, StringComparison.Ordinal) ? gatewayOrderId[OrderPrefix.Length..] : gatewayOrderId;
        var paymentId = $"{PaymentPrefix}{(success ? "s" : "f")}_{orderGuid}_{amountPaise}";
        return new SimulatedPaymentDto(gatewayOrderId, paymentId, PaymentSignatures.Sign(_settings.KeySecret, PaymentSignatures.CheckoutMessage(gatewayOrderId, paymentId)));
    }

    public string SignWebhook(string rawBody) => PaymentSignatures.Sign(_settings.WebhookSecret, rawBody);
}
