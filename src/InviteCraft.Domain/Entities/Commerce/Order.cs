using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Identity;
using InviteCraft.Domain.Entities.Invitations;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Domain.Entities.Commerce;

public class Order : AuditableEntity
{
    /// <summary>Customer-facing number, e.g. "ICO-2026-000123".</summary>
    public string OrderNumber { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public string Currency { get; set; } = Currencies.Inr;
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    /// <summary>GST portion of the (tax-inclusive) total, stored for invoices.</summary>
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public Guid? CouponId { get; set; }
    /// <summary>Razorpay order id ("order_…").</summary>
    public string? GatewayOrderId { get; set; }
    /// <summary>Client-supplied key so repeated "Pay" clicks reuse the same pending order.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin): two "mark paid" paths cannot both win.</summary>
    public uint Version { get; set; }

    public User User { get; set; } = null!;
    public Coupon? Coupon { get; set; }
    public ICollection<OrderItem> Items { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
}

public class OrderItem : Entity
{
    public Guid OrderId { get; set; }
    public Guid InvitationId { get; set; }
    public Guid TemplateId { get; set; }
    public Guid PackageId { get; set; }
    /// <summary>Snapshots so order history stays correct if the template/package is renamed later.</summary>
    public string TemplateName { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }

    public Order Order { get; set; } = null!;
    public Invitation Invitation { get; set; } = null!;
    public Template Template { get; set; } = null!;
    public Package Package { get; set; } = null!;
}
