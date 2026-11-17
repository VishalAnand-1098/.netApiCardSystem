using System.Security.Cryptography;
using System.Text.Json;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Auth;
using InviteCraft.Application.Features.Content;
using InviteCraft.Application.Features.Invitations;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Entities.Invitations;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InviteCraft.Application.Features.Payments;

/// <summary>Customer-facing checkout branding and limits. Bound from the "Payments" configuration section.</summary>
public sealed class CheckoutSettings
{
    public const string SectionName = "Payments";

    public string BusinessName { get; set; } = "InviteCraft";
    public string ThemeColor { get; set; } = "#14213D";
    /// <summary>Unpaid orders a customer may have open at once.</summary>
    public int MaxPendingOrders { get; set; } = 5;
}

public interface ICheckoutService
{
    Task<Result<CheckoutOptionsDto>> GetOptionsAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken = default);
    Task<Result<CouponCheckDto>> ValidateCouponAsync(Guid userId, ValidateCouponRequest request, CancellationToken cancellationToken = default);

    /// <summary>Creates (or returns, for the same idempotency key) a payable order and the details the gateway's checkout needs.</summary>
    Task<Result<CheckoutSessionDto>> CreateOrderAsync(Guid userId, CreateOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Confirms a payment the browser reports as done: signature, then a look at the gateway, then the shared processor.</summary>
    Task<Result<OrderDto>> VerifyPaymentAsync(Guid userId, VerifyPaymentRequest request, CancellationToken cancellationToken = default);

    Task<Result<OrderDto>> GetOrderAsync(Guid userId, Guid orderId, CancellationToken cancellationToken = default);
    Task<Result<OrderDto>> CancelOrderAsync(Guid userId, Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Development only. NotFound unless the simulated provider is active.</summary>
    Task<Result<SimulatedPaymentDto>> SimulatePaymentAsync(Guid userId, SimulatePaymentRequest request, CancellationToken cancellationToken = default);
}

internal sealed class CheckoutService(
    IOrderRepository orders,
    ICouponRepository coupons,
    IInvitationRepository invitations,
    ITemplateRepository templates,
    IUserRepository users,
    IContentRepository content,
    IUnitOfWork unitOfWork,
    IPaymentGateway gateway,
    IPaymentProcessor processor,
    IPersistenceConflicts conflicts,
    IPaymentWebhookService webhooks,
    TemplateMapper mapper,
    IOptions<CheckoutSettings> options,
    IAuditLogger audit,
    TimeProvider timeProvider,
    ILogger<CheckoutService> logger) : ICheckoutService
{
    private const int DefaultExpiryMinutes = 30;
    private const int MaxAttempts = 3;
    private static readonly string[] OrderNumberAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789".Select(c => c.ToString()).ToArray();

    // ------------------------------------------------------------------ options

    public async Task<Result<CheckoutOptionsDto>> GetOptionsAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        var context = await LoadAsync(userId, invitationId, cancellationToken);
        if (context.IsFailure)
            return Result.Failure<CheckoutOptionsDto>(context.Errors);

        var (invitation, template, packages, credit) = context.Value;
        var offers = BuildOffers(invitation, template, packages, credit);
        var current = packages.FirstOrDefault(p => p.Id == invitation.UnlockedPackageId);
        var thumbnail = template.Assets.FirstOrDefault(a => a.AssetType == TemplateAssetType.Thumbnail);

        var dtos = offers.Select(o => new CheckoutPackageDto(
            o.Package.Code, o.Package.Name, o.Package.Description, TemplateJson.Deserialize<List<string>>(o.Package.Features) ?? [],
            o.Package.AllowedFormats, o.Package.MaxDpi, o.Package.MaxDownloads, o.Package.EditWindowDays, o.Package.RemovesWatermark, o.Package.IsRecommended,
            o.ListPrice, o.CompareAt, o.Credit, o.Payable, o.Reason is null, o.Reason)).ToList();

        return new CheckoutOptionsDto(invitation.Id, invitation.Title, template.Name, mapper.ThumbUrl(template.Id, thumbnail?.Id), Currencies.Inr,
            await GstRateAsync(cancellationToken), current?.Code, dtos.FirstOrDefault(p => p.Available && p.IsRecommended)?.Code ?? dtos.FirstOrDefault(p => p.Available)?.Code, dtos);
    }

    // ------------------------------------------------------------------ coupons

    public async Task<Result<CouponCheckDto>> ValidateCouponAsync(Guid userId, ValidateCouponRequest request, CancellationToken cancellationToken = default)
    {
        var quote = await QuoteAsync(userId, request.InvitationId, request.PackageCode, cancellationToken);
        if (quote.IsFailure)
            return Result.Failure<CouponCheckDto>(quote.Errors);

        var code = request.Code.Trim();
        var coupon = await coupons.GetByCodeAsync(code, cancellationToken);
        var used = coupon is null ? 0 : await coupons.CountUserRedemptionsAsync(coupon.Id, userId, cancellationToken);

        // After a reload the customer re-applies the code their own open order already reserved: that must not count against them.
        var now = timeProvider.GetUtcNow();
        var open = coupon is null ? null : await orders.FindPayableAsync(userId, request.InvitationId, quote.Value.Package.Id, now, cancellationToken);
        var holdsReservation = open?.CouponId is { } held && held == coupon!.Id;
        var evaluated = CouponRules.Evaluate(coupon, now, quote.Value.Payable, quote.Value.Package.Id, quote.Value.Template.CategoryId,
            holdsReservation ? Math.Max(0, used - 1) : used, holdsReservation);

        if (evaluated.IsFailure)
            return new CouponCheckDto(false, null, 0, quote.Value.Payable, evaluated.FirstError!.Message);

        var breakdown = PricingRules.Breakdown(quote.Value.Payable, evaluated.Value, await GstRateAsync(cancellationToken));
        return new CouponCheckDto(true, coupon!.Code, breakdown.Discount, breakdown.Total,
            $"{coupon.Code} applied — you save {PricingRules.Format(breakdown.Discount)}.");
    }

    // ------------------------------------------------------------------ create order

    public async Task<Result<CheckoutSessionDto>> CreateOrderAsync(Guid userId, CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        GatewayOrder? gatewayOrder = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var now = timeProvider.GetUtcNow();

            // Same key → same order (page reload, double click, flaky network).
            var existing = await orders.FindByIdempotencyKeyAsync(userId, request.IdempotencyKey, cancellationToken);
            if (existing is not null)
                return await ResumeAsync(existing, request, now, cancellationToken);

            var quote = await QuoteAsync(userId, request.InvitationId, request.PackageCode, cancellationToken);
            if (quote.IsFailure)
                return Result.Failure<CheckoutSessionDto>(quote.Errors);
            var q = quote.Value;

            // A different key for the same purchase (e.g. a second tab): hand back the order that is already open, or replace it if the terms changed.
            var open = await orders.FindPayableAsync(userId, q.Invitation.Id, q.Package.Id, now, cancellationToken);
            if (open is not null)
            {
                if (string.Equals(open.Coupon?.Code, request.CouponCode?.Trim(), StringComparison.OrdinalIgnoreCase) && open.Status != OrderStatus.Paid)
                    return await SessionAsync(open, cancellationToken);
                await CancelCoreAsync(open, cancellationToken);
            }

            if (await orders.CountPayableAsync(userId, now, cancellationToken) >= options.Value.MaxPendingOrders)
                return PaymentErrors.TooManyPending;

            // Coupon: evaluated against the amount actually payable, reserved together with the order.
            Coupon? coupon = null;
            var discount = 0m;
            if (!string.IsNullOrWhiteSpace(request.CouponCode))
            {
                coupon = await coupons.GetByCodeAsync(request.CouponCode.Trim(), cancellationToken);
                var used = coupon is null ? 0 : await coupons.CountUserRedemptionsAsync(coupon.Id, userId, cancellationToken);
                var evaluated = CouponRules.Evaluate(coupon, now, q.Payable, q.Package.Id, q.Template.CategoryId, used);
                if (evaluated.IsFailure)
                    return Result.Failure<CheckoutSessionDto>(evaluated.Errors);
                discount = evaluated.Value;
            }

            var gst = await GstRateAsync(cancellationToken);
            var price = PricingRules.Breakdown(q.Payable, discount, gst);
            var order = new Order
            {
                OrderNumber = NewOrderNumber(now),
                UserId = userId,
                Status = OrderStatus.Pending,
                Currency = Currencies.Inr,
                Subtotal = price.Subtotal,
                DiscountAmount = price.Discount,
                TaxAmount = price.Tax,
                TotalAmount = price.Total,
                CouponId = coupon?.Id,
                Coupon = coupon,
                IdempotencyKey = request.IdempotencyKey,
                ExpiresAt = now.AddMinutes(await ExpiryMinutesAsync(cancellationToken)),
                Notes = JsonSerializer.Serialize(new { package = q.Package.Code, upgradeCredit = q.Credit, listPrice = q.ListPrice }),
            };
            order.Items.Add(new OrderItem
            {
                OrderId = order.Id, InvitationId = q.Invitation.Id, TemplateId = q.Template.Id, PackageId = q.Package.Id,
                TemplateName = q.Template.Name, PackageName = q.Package.Name, UnitPrice = price.Subtotal, Quantity = 1,
                DiscountAmount = price.Discount, LineTotal = price.Total, Invitation = q.Invitation,
            });

            if (coupon is not null)
            {
                coupon.UsedCount++; // the reservation: concurrent checkouts race on the coupon's concurrency token
                await AddRedemptionAsync(order, coupon, userId, discount, cancellationToken);
            }

            try
            {
                if (price.Total == 0)
                {
                    await processor.CompleteFreeOrderAsync(order, cancellationToken);
                    order.Notes = JsonSerializer.Serialize(new { package = q.Package.Code, upgradeCredit = q.Credit, free = true });
                }
                else
                {
                    if (gatewayOrder is null || gatewayOrder.AmountPaise != PricingRules.ToPaise(order.TotalAmount))
                        gatewayOrder = await CreateGatewayOrderAsync(order, q, order.OrderNumber, cancellationToken);
                    if (gatewayOrder is null)
                        return PaymentErrors.GatewayUnavailable;
                    order.GatewayOrderId = gatewayOrder.Id;
                    order.Status = OrderStatus.AwaitingPayment;
                }

                await orders.AddAsync(order, cancellationToken);
                audit.Log("OrderCreated", nameof(Order), order.Id.ToString(), userId,
                    new { order.OrderNumber, package = q.Package.Code, order.TotalAmount, coupon = coupon?.Code });
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (conflicts.IsConflict(ex) && attempt < MaxAttempts)
            {
                // Lost a race on the coupon counter, or the same key arrived twice at once: start over from what is in the database now.
                conflicts.DiscardChanges();
                logger.LogInformation("Checkout for invitation {InvitationId} lost a race (attempt {Attempt}); retrying", request.InvitationId, attempt);
                continue;
            }

            return await SessionAsync(order, cancellationToken);
        }

        return PaymentErrors.KeyReused;
    }

    // ------------------------------------------------------------------ verify

    public async Task<Result<OrderDto>> VerifyPaymentAsync(Guid userId, VerifyPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var order = await orders.GetOwnedAsync(userId, request.OrderId, cancellationToken);
        if (order is null)
            return PaymentErrors.OrderNotFound;
        if (order.GatewayOrderId is null || !string.Equals(order.GatewayOrderId, request.GatewayOrderId, StringComparison.Ordinal))
            return PaymentErrors.GatewayOrderMismatch;

        if (order.Status == OrderStatus.Paid)
            return await ToDtoAsync(order, cancellationToken); // callback repeated, or the webhook got there first

        if (!gateway.VerifyCheckoutSignature(request.GatewayOrderId, request.PaymentId, request.Signature))
        {
            audit.Log("PaymentSignatureRejected", nameof(Order), order.Id.ToString(), userId, new { request.PaymentId });
            logger.LogWarning("Rejected checkout signature for order {OrderNumber}", order.OrderNumber);
            return PaymentErrors.InvalidSignature;
        }

        // The signature proves the payment belongs to this order; ask the gateway what actually happened to it.
        GatewayPayment? payment;
        try
        {
            payment = await gateway.GetPaymentAsync(request.PaymentId, cancellationToken);
            if (payment is { Status: "authorized" })
                payment = await gateway.CapturePaymentAsync(payment.Id, payment.AmountPaise, payment.Currency, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Could not confirm payment {PaymentId} with the gateway; leaving it to the webhook", request.PaymentId);
            return await ToDtoAsync(order, cancellationToken); // still AwaitingPayment: the webhook completes it
        }

        if (payment is null || payment.Status != "captured")
            return await ToDtoAsync(order, cancellationToken);

        var applied = await processor.ApplyCapturedAsync(order.Id, payment, PaymentVerificationSource.Checkout, request.Signature, cancellationToken);
        if (applied.IsFailure)
            return Result.Failure<OrderDto>(applied.Errors);

        var fresh = await orders.GetOwnedAsync(userId, request.OrderId, cancellationToken);
        return await ToDtoAsync(fresh ?? order, cancellationToken);
    }

    // ------------------------------------------------------------------ read / cancel

    public async Task<Result<OrderDto>> GetOrderAsync(Guid userId, Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await orders.GetOwnedAsync(userId, orderId, cancellationToken);
        return order is null ? PaymentErrors.OrderNotFound : await ToDtoAsync(order, cancellationToken);
    }

    public async Task<Result<OrderDto>> CancelOrderAsync(Guid userId, Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await orders.GetOwnedAsync(userId, orderId, cancellationToken);
        if (order is null)
            return PaymentErrors.OrderNotFound;
        if (order.Status is OrderStatus.Pending or OrderStatus.AwaitingPayment)
        {
            await CancelCoreAsync(order, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return await ToDtoAsync(order, cancellationToken);
    }

    // ------------------------------------------------------------------ development simulation

    public async Task<Result<SimulatedPaymentDto>> SimulatePaymentAsync(Guid userId, SimulatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (gateway is not ISimulatedGateway simulated)
            return PaymentErrors.NotAvailable;

        var order = await orders.GetOwnedAsync(userId, request.OrderId, cancellationToken);
        if (order?.GatewayOrderId is null)
            return PaymentErrors.OrderNotFound;

        var payment = simulated.CreatePayment(order.GatewayOrderId, PricingRules.ToPaise(order.TotalAmount), request.Success);

        if (request.DeliverWebhook)
        {
            // Exactly what the gateway would POST, signed with the webhook secret — so the callback-vs-webhook race can be exercised locally.
            var eventName = request.Success ? "payment.captured" : "payment.failed";
            var body = JsonSerializer.Serialize(new
            {
                @event = eventName,
                payload = new
                {
                    payment = new
                    {
                        entity = new
                        {
                            id = payment.PaymentId, order_id = payment.GatewayOrderId, amount = PricingRules.ToPaise(order.TotalAmount), currency = order.Currency,
                            status = request.Success ? "captured" : "failed", method = "upi",
                            error_code = request.Success ? null : "BAD_REQUEST_ERROR", error_description = request.Success ? null : "Payment failed (simulated)",
                        },
                    },
                },
            });
            await webhooks.HandleAsync(body, simulated.SignWebhook(body), $"sim_evt_{Guid.NewGuid():N}", cancellationToken);
        }

        return payment;
    }

    // ------------------------------------------------------------------ internals

    private sealed record Offer(Package Package, decimal ListPrice, decimal? CompareAt, decimal Credit, decimal Payable, string? Reason);

    private sealed record Context(Invitation Invitation, Template Template, IReadOnlyList<Package> Packages, decimal PaidCredit);

    private sealed record Quote(Invitation Invitation, Template Template, Package Package, decimal ListPrice, decimal Credit, decimal Payable);

    private async Task<Result<Context>> LoadAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken)
    {
        var invitation = await invitations.GetOwnedAsync(userId, invitationId, includeValues: false, cancellationToken);
        if (invitation is null)
            return InvitationErrors.NotFound;
        var template = await templates.GetAggregateAsync(invitation.TemplateId, track: false, cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        var packages = await templates.GetActivePackagesAsync(cancellationToken);
        var credit = invitation.IsUnlocked ? await orders.GetPaidAmountAsync(invitation.Id, cancellationToken) : 0m;
        return new Context(invitation, template, packages, credit);
    }

    /// <summary>Packages a design is sold in, with prices. A design with its own price rows is sold only in those packages; otherwise every paid package is offered at its base price.</summary>
    private List<Offer> BuildOffers(Invitation invitation, Template template, IReadOnlyList<Package> packages, decimal paidCredit)
    {
        var now = timeProvider.GetUtcNow();
        var rows = template.Pricing
            .Where(p => p.IsActive && p.Package.IsActive && (p.ValidFrom is null || p.ValidFrom <= now) && (p.ValidTo is null || p.ValidTo > now))
            .ToDictionary(p => p.PackageId);

        var current = packages.FirstOrDefault(p => p.Id == invitation.UnlockedPackageId);
        var offers = new List<Offer>();
        foreach (var package in packages.Where(p => p.Code is not (Package.FreeCode or Package.CustomCode)))
        {
            if (rows.Count > 0 && !rows.ContainsKey(package.Id))
                continue;

            var price = rows.TryGetValue(package.Id, out var row) ? row.Price : package.BasePrice;
            if (price <= 0)
                continue;

            string? reason = null;
            var credit = 0m;
            if (current is not null)
            {
                if (package.SortOrder <= current.SortOrder)
                    reason = package.Id == current.Id ? $"You already have the {current.Name} package." : $"Your {current.Name} package already includes this.";
                else
                    credit = Math.Min(paidCredit, price);
            }

            offers.Add(new Offer(package, price, row?.CompareAtPrice, credit, PricingRules.Round(price - credit), reason));
        }

        return offers;
    }

    private async Task<Result<Quote>> QuoteAsync(Guid userId, Guid invitationId, string packageCode, CancellationToken cancellationToken)
    {
        if (string.Equals(packageCode, Package.FreeCode, StringComparison.OrdinalIgnoreCase))
            return PaymentErrors.FreePackage;

        var context = await LoadAsync(userId, invitationId, cancellationToken);
        if (context.IsFailure)
            return Result.Failure<Quote>(context.Errors);

        var offers = BuildOffers(context.Value.Invitation, context.Value.Template, context.Value.Packages, context.Value.PaidCredit);
        var offer = offers.FirstOrDefault(o => string.Equals(o.Package.Code, packageCode, StringComparison.OrdinalIgnoreCase));
        if (offer is null)
            return PaymentErrors.PackageNotFound;
        if (offer.Reason is not null)
            return PaymentErrors.PackageNotAvailable(offer.Reason);

        return new Quote(context.Value.Invitation, context.Value.Template, offer.Package, offer.ListPrice, offer.Credit, offer.Payable);
    }

    private async Task<Result<CheckoutSessionDto>> ResumeAsync(Order existing, CreateOrderRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var item = existing.Items.FirstOrDefault();
        var sameTerms = item is not null && item.InvitationId == request.InvitationId &&
            string.Equals(PackageCodeOf(existing), request.PackageCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.Coupon?.Code, string.IsNullOrWhiteSpace(request.CouponCode) ? null : request.CouponCode.Trim(), StringComparison.OrdinalIgnoreCase);
        if (!sameTerms)
            return PaymentErrors.KeyReused;

        if (existing.Status == OrderStatus.Paid)
            return await SessionAsync(existing, cancellationToken);
        if (existing.Status is OrderStatus.Pending or OrderStatus.AwaitingPayment && (existing.ExpiresAt is null || existing.ExpiresAt > now))
            return await SessionAsync(existing, cancellationToken);
        return PaymentErrors.OrderClosed;
    }

    private static string? PackageCodeOf(Order order)
    {
        try
        {
            return order.Notes is null ? null : JsonDocument.Parse(order.Notes).RootElement.GetProperty("package").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<GatewayOrder?> CreateGatewayOrderAsync(Order order, Quote quote, string receipt, CancellationToken cancellationToken)
    {
        try
        {
            return await gateway.CreateOrderAsync(new GatewayOrderRequest(receipt, PricingRules.ToPaise(order.TotalAmount), order.Currency,
                new Dictionary<string, string>
                {
                    ["order"] = order.OrderNumber,
                    ["invitation"] = quote.Invitation.Id.ToString(),
                    ["package"] = quote.Package.Code,
                }), cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogError(ex, "Could not create a gateway order for {OrderNumber}", order.OrderNumber);
            return null;
        }
    }

    private async Task AddRedemptionAsync(Order order, Coupon coupon, Guid userId, decimal discount, CancellationToken cancellationToken)
    {
        // The redemption is saved with the order (same unit of work); it is keyed by order id.
        var redemption = new CouponRedemption { CouponId = coupon.Id, UserId = userId, OrderId = order.Id, DiscountAmount = discount, Status = CouponRedemptionStatus.Reserved };
        await orders.AddRedemptionAsync(redemption, cancellationToken);
    }

    /// <summary>Marks an unpaid order cancelled and gives its coupon reservation back. The caller saves.</summary>
    private async Task CancelCoreAsync(Order order, CancellationToken cancellationToken)
    {
        order.Status = OrderStatus.Cancelled;
        order.CancelledAt = timeProvider.GetUtcNow();
        await ReleaseCouponAsync(order, cancellationToken);
        audit.Log("OrderCancelled", nameof(Order), order.Id.ToString(), order.UserId, new { order.OrderNumber });
    }

    private async Task ReleaseCouponAsync(Order order, CancellationToken cancellationToken)
    {
        var redemption = await orders.GetRedemptionAsync(order.Id, cancellationToken);
        if (redemption is { Status: CouponRedemptionStatus.Reserved })
        {
            redemption.Status = CouponRedemptionStatus.Released;
            redemption.Coupon.UsedCount = Math.Max(0, redemption.Coupon.UsedCount - 1);
        }
    }

    private async Task<Result<CheckoutSessionDto>> SessionAsync(Order order, CancellationToken cancellationToken)
    {
        var dto = await ToDtoAsync(order, cancellationToken);
        if (order.Status is not (OrderStatus.Pending or OrderStatus.AwaitingPayment) || order.GatewayOrderId is null)
            return new CheckoutSessionDto(dto, null);

        var user = await users.GetByIdAsync(order.UserId, cancellationToken);
        var item = dto.Items.FirstOrDefault();
        return new CheckoutSessionDto(dto, new PaymentInitDto(
            gateway.Provider, gateway.PublicKeyId, order.GatewayOrderId, PricingRules.ToPaise(order.TotalAmount), order.Currency,
            options.Value.BusinessName, item is null ? order.OrderNumber : $"{item.TemplateName} · {item.PackageName}", options.Value.ThemeColor,
            new PaymentPrefillDto(user?.FullName ?? string.Empty, user?.Email ?? string.Empty, user?.Phone)));
    }

    private static Task<OrderDto> ToDtoAsync(Order order, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult(OrderMapper.ToDto(order));
    }

    private async Task<decimal> GstRateAsync(CancellationToken cancellationToken)
    {
        var json = await content.GetSettingJsonAsync("tax.gst", cancellationToken);
        try
        {
            if (json is not null && JsonDocument.Parse(json).RootElement.TryGetProperty("rate", out var rate) && rate.TryGetDecimal(out var value) && value is >= 0 and < 100)
                return value;
        }
        catch (JsonException)
        {
            // fall through to the default
        }

        return PricingRules.DefaultGstRatePercent;
    }

    private async Task<int> ExpiryMinutesAsync(CancellationToken cancellationToken)
    {
        var json = await content.GetSettingJsonAsync("orders", cancellationToken);
        try
        {
            if (json is not null && JsonDocument.Parse(json).RootElement.TryGetProperty("pendingExpiryMinutes", out var minutes) && minutes.TryGetInt32(out var value) && value is >= 5 and <= 1440)
                return value;
        }
        catch (JsonException)
        {
            // fall through to the default
        }

        return DefaultExpiryMinutes;
    }

    private static string NewOrderNumber(DateTimeOffset now)
    {
        var suffix = string.Concat(Enumerable.Range(0, 6).Select(_ => OrderNumberAlphabet[RandomNumberGenerator.GetInt32(OrderNumberAlphabet.Length)]));
        return $"ICO-{now.Year}-{suffix}";
    }
}

internal static class OrderMapper
{
    public static OrderDto ToDto(Order o) => new(
        o.Id, o.OrderNumber, o.Status, o.Currency, o.Subtotal, o.DiscountAmount, o.TaxAmount, o.TotalAmount, o.Coupon?.Code, o.CreatedAt, o.PaidAt, o.ExpiresAt,
        o.Items.Select(i => new OrderItemDto(i.InvitationId, i.Invitation?.Title ?? i.TemplateName, i.TemplateName, i.PackageName, i.UnitPrice, i.LineTotal)).ToList(),
        o.Payments.OrderBy(p => p.CreatedAt)
            .Select(p => new OrderPaymentDto(p.GatewayPaymentId, p.Status, p.Method, p.Amount, p.CapturedAt, p.Status == PaymentStatus.Failed ? p.ErrorDescription : null)).ToList());
}
