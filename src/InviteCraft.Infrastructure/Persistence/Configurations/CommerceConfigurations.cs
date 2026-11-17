using InviteCraft.Domain.Entities.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InviteCraft.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.Property(x => x.OrderNumber).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.GatewayOrderId).HasMaxLength(64);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Notes).HasColumnType("jsonb");
        builder.Property(x => x.Version).IsRowVersion();

        builder.HasIndex(x => x.OrderNumber).IsUnique();
        builder.HasIndex(x => x.GatewayOrderId).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(x => new { x.Status, x.CreatedAt });

        builder.HasOne(x => x.User).WithMany(u => u.Orders).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Coupon).WithMany().HasForeignKey(x => x.CouponId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_orders_amounts",
                "subtotal >= 0 AND discount_amount >= 0 AND tax_amount >= 0 AND total_amount >= 0");
            t.HasCheckConstraint("ck_orders_total", "total_amount = subtotal - discount_amount");
        });
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.Property(x => x.TemplateName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PackageName).HasMaxLength(60).IsRequired();

        builder.HasIndex(x => x.InvitationId);

        builder.HasOne(x => x.Order).WithMany(o => o.Items).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Invitation).WithMany().HasForeignKey(x => x.InvitationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_order_items_quantity", "quantity > 0");
            t.HasCheckConstraint("ck_order_items_amounts", "unit_price >= 0 AND discount_amount >= 0 AND line_total >= 0");
        });
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(x => x.Provider).HasMaxLength(30).IsRequired();
        builder.Property(x => x.GatewayOrderId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.GatewayPaymentId).HasMaxLength(64);
        builder.Property(x => x.GatewaySignature).HasMaxLength(256);
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.Method).HasMaxLength(30);
        builder.Property(x => x.ErrorCode).HasMaxLength(100);
        builder.Property(x => x.ErrorDescription).HasMaxLength(1000);
        builder.Property(x => x.RawResponse).HasColumnType("jsonb");
        builder.Property(x => x.Version).IsRowVersion();

        // The database-level guard against processing the same payment twice.
        builder.HasIndex(x => x.GatewayPaymentId).IsUnique();
        builder.HasIndex(x => x.GatewayOrderId);

        builder.HasOne(x => x.Order).WithMany(o => o.Payments).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t => t.HasCheckConstraint("ck_payments_amount", "amount >= 0"));
    }
}

internal sealed class PaymentWebhookEventConfiguration : IEntityTypeConfiguration<PaymentWebhookEvent>
{
    public void Configure(EntityTypeBuilder<PaymentWebhookEvent> builder)
    {
        builder.Property(x => x.Provider).HasMaxLength(30).IsRequired();
        builder.Property(x => x.EventId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Error).HasMaxLength(2000);

        // Gateway retries of the same event become no-ops.
        builder.HasIndex(x => new { x.Provider, x.EventId }).IsUnique();
        builder.HasIndex(x => x.Status).HasFilter("status IN ('Received', 'Failed')");
    }
}

internal sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.Property(x => x.Code).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.Version).IsRowVersion();

        builder.HasIndex(x => x.Code).IsUnique().HasFilter("is_deleted = false");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_coupons_value", "discount_value > 0");
            t.HasCheckConstraint("ck_coupons_percentage", "discount_type <> 'Percentage' OR discount_value <= 100");
            t.HasCheckConstraint("ck_coupons_limits",
                "per_user_limit >= 1 AND used_count >= 0 AND (usage_limit IS NULL OR usage_limit >= 1)");
            t.HasCheckConstraint("ck_coupons_dates", "ends_at IS NULL OR starts_at IS NULL OR ends_at > starts_at");
        });
    }
}

internal sealed class CouponRedemptionConfiguration : IEntityTypeConfiguration<CouponRedemption>
{
    public void Configure(EntityTypeBuilder<CouponRedemption> builder)
    {
        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasIndex(x => new { x.CouponId, x.UserId });

        builder.HasOne(x => x.Coupon).WithMany(c => c.Redemptions).HasForeignKey(x => x.CouponId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t => t.HasCheckConstraint("ck_coupon_redemptions_amount", "discount_amount >= 0"));
    }
}
