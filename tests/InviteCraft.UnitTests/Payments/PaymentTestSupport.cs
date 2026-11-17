using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Features.Payments;
using InviteCraft.Domain.Entities.Catalog;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Entities.Invitations;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;
using NSubstitute;

namespace InviteCraft.UnitTests.Payments;

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>A small, realistic world: one premium design with Basic/Premium/Luxury prices, one customer, one invitation.</summary>
internal sealed class PaymentWorld
{
    public static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    public FixedClock Clock { get; } = new(Now);
    public Guid UserId { get; } = Guid.NewGuid();

    public Package Free { get; } = new() { Code = Package.FreeCode, Name = "Free", SortOrder = 10, BasePrice = 0, AllowedFormats = ["Jpg", "Png"], MaxDpi = 150, RemovesWatermark = true, IsActive = true };
    public Package Basic { get; } = new() { Code = Package.BasicCode, Name = "Basic", SortOrder = 20, BasePrice = 99, AllowedFormats = ["Jpg", "Png"], MaxDpi = 150, MaxDownloads = 5, EditWindowDays = 7, RemovesWatermark = true, IsActive = true };
    public Package Premium { get; } = new() { Code = Package.PremiumCode, Name = "Premium", SortOrder = 30, BasePrice = 249, AllowedFormats = ["Jpg", "Png", "Pdf"], MaxDpi = 300, EditWindowDays = 30, RemovesWatermark = true, IsRecommended = true, IsActive = true };
    public Package Luxury { get; } = new() { Code = Package.LuxuryCode, Name = "Luxury", SortOrder = 40, BasePrice = 499, AllowedFormats = ["Jpg", "Png", "Pdf", "PrintPdf"], MaxDpi = 300, RemovesWatermark = true, IsActive = true };
    public Package Custom { get; } = new() { Code = Package.CustomCode, Name = "Custom", SortOrder = 50, BasePrice = 1999, AllowedFormats = ["Jpg"], MaxDpi = 300, RemovesWatermark = true, IsActive = true };
    public IReadOnlyList<Package> Packages => [Free, Basic, Premium, Luxury, Custom];

    public Category Category { get; } = new() { Name = "Wedding", Slug = "wedding" };
    public Template Template { get; }
    public Invitation Invitation { get; }

    public PaymentWorld()
    {
        Template = new Template { Name = "Royal Maroon Wedding", Slug = "royal-maroon-wedding", Code = "IC-1", Tier = TemplateTier.Premium, CategoryId = Category.Id, Category = Category };
        // Explicit prices: this design is sold as Basic, Premium and Luxury.
        foreach (var (package, price, compare) in new[] { (Basic, 149m, 199m), (Premium, 299m, (decimal?)null), (Luxury, 549m, null) })
            Template.Pricing.Add(new TemplatePricing { TemplateId = Template.Id, PackageId = package.Id, Package = package, Price = price, CompareAtPrice = compare, IsActive = true });

        Invitation = new Invitation { UserId = UserId, TemplateId = Template.Id, Title = "Ananya & Rohan" };
    }

    public Coupon Coupon(string code = "WELCOME10", decimal percent = 10) => new()
    {
        Code = code, DiscountType = DiscountType.Percentage, DiscountValue = percent, PerUserLimit = 1, IsActive = true,
    };

    public static Order PaidOrderFor(Guid userId, Invitation invitation, Package package, decimal total) =>
        new() { UserId = userId, Status = OrderStatus.Paid, Subtotal = total, TotalAmount = total, OrderNumber = "ICO-2026-PAID01", GatewayOrderId = "order_x" };
}
