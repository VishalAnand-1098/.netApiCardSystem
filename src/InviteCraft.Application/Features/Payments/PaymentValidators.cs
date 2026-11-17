using System.Text.RegularExpressions;
using FluentValidation;
using InviteCraft.Application.Common.Validation;

namespace InviteCraft.Application.Features.Payments;

internal static partial class PaymentPatterns
{
    [GeneratedRegex("^[A-Za-z0-9_:.-]{8,100}$")]
    public static partial Regex IdempotencyKey();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{1,29}$")]
    public static partial Regex PackageCode();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,100}$")]
    public static partial Regex GatewayId();
}

/// <summary>Shape-level checks only; pricing, availability and coupon rules live in the checkout service.</summary>
internal sealed class CreateOrderRequestValidator : AppValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.InvitationId).NotEmpty().WithMessage("Choose an invitation.");
        RuleFor(x => x.PackageCode).NotEmpty().Matches(PaymentPatterns.PackageCode()).WithMessage("Choose a package.");
        RuleFor(x => x.CouponCode).MaximumLength(40).WithMessage("That coupon code is too long.");
        RuleFor(x => x.IdempotencyKey).NotEmpty().Matches(PaymentPatterns.IdempotencyKey())
            .WithMessage("The checkout reference is missing or malformed. Reload the page and try again.");
    }
}

internal sealed class VerifyPaymentRequestValidator : AppValidator<VerifyPaymentRequest>
{
    public VerifyPaymentRequestValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.GatewayOrderId).NotEmpty().Matches(PaymentPatterns.GatewayId()).WithMessage("Invalid payment details.");
        RuleFor(x => x.PaymentId).NotEmpty().Matches(PaymentPatterns.GatewayId()).WithMessage("Invalid payment details.");
        RuleFor(x => x.Signature).NotEmpty().MaximumLength(256).WithMessage("Invalid payment details.");
    }
}

internal sealed class ValidateCouponRequestValidator : AppValidator<ValidateCouponRequest>
{
    public ValidateCouponRequestValidator()
    {
        RuleFor(x => x.InvitationId).NotEmpty();
        RuleFor(x => x.PackageCode).NotEmpty().Matches(PaymentPatterns.PackageCode());
        RuleFor(x => x.Code).NotEmpty().WithMessage("Enter a coupon code.").MaximumLength(40).WithMessage("That coupon code is too long.");
    }
}
