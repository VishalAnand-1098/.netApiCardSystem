using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Identity;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Domain.Entities.Commerce;

public class Coupon : SoftDeletableEntity
{
    /// <summary>Case-insensitive (citext) code, e.g. "DIWALI25".</summary>
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DiscountType DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public decimal? MinOrderAmount { get; set; }
    /// <summary>Empty = applies to all packages.</summary>
    public List<Guid> ApplicablePackageIds { get; set; } = [];
    /// <summary>Empty = applies to all categories.</summary>
    public List<Guid> ApplicableCategoryIds { get; set; } = [];
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    /// <summary>Null = unlimited total redemptions.</summary>
    public int? UsageLimit { get; set; }
    public int PerUserLimit { get; set; } = 1;
    public int UsedCount { get; set; }
    public bool IsActive { get; set; } = true;

    public uint Version { get; set; }

    public ICollection<CouponRedemption> Redemptions { get; set; } = [];
}

/// <summary>
/// Reserved when an order is created, Redeemed when it is paid, Released if it fails or expires —
/// so concurrent checkouts cannot exceed the usage limit.
/// </summary>
public class CouponRedemption : CreationAuditedEntity
{
    public Guid CouponId { get; set; }
    public Guid UserId { get; set; }
    public Guid OrderId { get; set; }
    public decimal DiscountAmount { get; set; }
    public CouponRedemptionStatus Status { get; set; } = CouponRedemptionStatus.Reserved;
    public DateTimeOffset? RedeemedAt { get; set; }

    public Coupon Coupon { get; set; } = null!;
    public User User { get; set; } = null!;
    public Order Order { get; set; } = null!;
}
