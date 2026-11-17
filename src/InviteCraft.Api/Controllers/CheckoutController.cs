using System.Text;
using InviteCraft.Api.Contracts;
using InviteCraft.Api.Infrastructure;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Features.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InviteCraft.Api.Controllers;

/// <summary>
/// Buying a package for an invitation. Flow: <c>GET checkout/options</c> → (optional) <c>POST coupons/validate</c> →
/// <c>POST checkout/orders</c> (idempotent) → the browser opens the gateway's checkout → <c>POST checkout/verify</c>.
/// The gateway's webhook is the safety net that unlocks the invitation even if the browser never comes back.
/// </summary>
[Route(ApiRoutes.V1)]
[Authorize]
public sealed class CheckoutController(ICheckoutService checkout, ICurrentUser currentUser) : ApiControllerBase
{
    private Guid UserId => currentUser.UserId!.Value;

    /// <summary>The packages this invitation can be bought in, with prices (GST included), upgrade credit and what each includes.</summary>
    [HttpGet("checkout/options/{invitationId:guid}")]
    [ProducesResponseType<ApiResponse<CheckoutOptionsDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CheckoutOptionsDto>>> Options(Guid invitationId, CancellationToken cancellationToken) =>
        FromResult(await checkout.GetOptionsAsync(UserId, invitationId, cancellationToken));

    /// <summary>Checks a coupon against a package and previews the discount. An unusable coupon is a normal answer (<c>valid: false</c>), not an error.</summary>
    [HttpPost("coupons/validate")]
    [EnableRateLimiting(RateLimitPolicies.Payments)]
    [ProducesResponseType<ApiResponse<CouponCheckDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CouponCheckDto>>> ValidateCoupon(ValidateCouponRequest request, CancellationToken cancellationToken) =>
        FromResult(await checkout.ValidateCouponAsync(UserId, request, cancellationToken));

    /// <summary>
    /// Creates the order (or returns the existing one for the same <c>idempotencyKey</c>) and the details the browser needs to open the
    /// gateway's checkout. Free orders (a 100 % coupon) are completed immediately and carry no <c>payment</c> block.
    /// </summary>
    [HttpPost("checkout/orders")]
    [EnableRateLimiting(RateLimitPolicies.Payments)]
    [ProducesResponseType<ApiResponse<CheckoutSessionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<CheckoutSessionDto>>> CreateOrder(CreateOrderRequest request, CancellationToken cancellationToken) =>
        FromResult(await checkout.CreateOrderAsync(UserId, request, cancellationToken));

    /// <summary>Confirms a payment the browser reports as successful: verifies the signature, asks the gateway, then unlocks the invitation. Safe to repeat.</summary>
    [HttpPost("checkout/verify")]
    [EnableRateLimiting(RateLimitPolicies.Payments)]
    [ProducesResponseType<ApiResponse<OrderDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Verify(VerifyPaymentRequest request, CancellationToken cancellationToken) =>
        FromResult(await checkout.VerifyPaymentAsync(UserId, request, cancellationToken));

    /// <summary>Abandons an unpaid order and returns its coupon.</summary>
    [HttpPost("checkout/orders/{orderId:guid}/cancel")]
    [ProducesResponseType<ApiResponse<OrderDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Cancel(Guid orderId, CancellationToken cancellationToken) =>
        FromResult(await checkout.CancelOrderAsync(UserId, orderId, cancellationToken));

    /// <summary>One of my orders (the payment-result pages poll this while the webhook catches up).</summary>
    [HttpGet("orders/{orderId:guid}")]
    [ProducesResponseType<ApiResponse<OrderDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Get(Guid orderId, CancellationToken cancellationToken) =>
        FromResult(await checkout.GetOrderAsync(UserId, orderId, cancellationToken));

    /// <summary>
    /// Development only (404 unless <c>Payments:Provider</c> is <c>Simulated</c>): plays the part of the gateway's checkout and returns the
    /// payment id and signature a real payment would produce. <c>deliverWebhook</c> also sends the signed webhook.
    /// </summary>
    [HttpPost("dev/payments/simulate")]
    [ProducesResponseType<ApiResponse<SimulatedPaymentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SimulatedPaymentDto>>> Simulate(SimulatePaymentRequest request, CancellationToken cancellationToken) =>
        FromResult(await checkout.SimulatePaymentAsync(UserId, request, cancellationToken));
}

/// <summary>Gateway → us. Authenticated by the HMAC signature of the raw body, never by a login.</summary>
[Route(ApiRoutes.V1 + "/webhooks")]
[AllowAnonymous]
public sealed class WebhooksController(IPaymentWebhookService webhooks) : ApiControllerBase
{
    private const int MaxBodyBytes = 512 * 1024;

    /// <summary>
    /// Razorpay webhook (<c>payment.captured</c>, <c>payment.failed</c>, <c>order.paid</c>, <c>refund.processed</c>). Answers 200 once the event is recorded
    /// and handled — duplicates included — and a non-2xx when it should be delivered again.
    /// </summary>
    [HttpPost("razorpay")]
    [RequestSizeLimit(MaxBodyBytes)]
    [Consumes("application/json", "text/plain")]
    public async Task<ActionResult> Razorpay(CancellationToken cancellationToken)
    {
        // The signature covers the exact bytes received, so read the body as-is (no model binding, no re-serialisation).
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);

        var result = await webhooks.HandleAsync(body,
            Request.Headers["X-Razorpay-Signature"].FirstOrDefault(),
            Request.Headers["X-Razorpay-Event-Id"].FirstOrDefault(),
            cancellationToken);

        return FromResult(result, "Received.");
    }
}
