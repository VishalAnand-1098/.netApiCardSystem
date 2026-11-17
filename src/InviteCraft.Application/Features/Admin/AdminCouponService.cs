using System.Text.RegularExpressions;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Payments;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Admin;

internal sealed partial class AdminCouponService(
    IAdminRepository repository,
    ICouponRepository coupons,
    IUnitOfWork unitOfWork,
    IPersistenceConflicts conflicts,
    ICurrentUser currentUser,
    IAuditLogger audit) : IAdminCouponService
{
    [GeneratedRegex("^[A-Z0-9][A-Z0-9_-]{2,39}$")]
    private static partial Regex CodePattern();

    public Task<PagedResult<AdminCouponDto>> ListAsync(AdminCouponQuery query, CancellationToken cancellationToken = default) =>
        repository.ListCouponsAsync(query, cancellationToken);

    public async Task<Result<AdminCouponDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var coupon = await coupons.GetByIdAsync(id, cancellationToken);
        return coupon is null ? AdminErrors.CouponNotFound : ToDto(coupon);
    }

    public async Task<Result<AdminCouponDto>> CreateAsync(SaveCouponRequest request, CancellationToken cancellationToken = default)
    {
        var errors = await ValidateAsync(request, cancellationToken);
        if (errors.Count > 0)
            return Result.Failure<AdminCouponDto>(errors);

        var code = Normalise(request.Code);
        if (await coupons.ExistsAsync(c => c.Code == code, cancellationToken))
            return AdminErrors.CouponCodeTaken;

        var coupon = new Coupon();
        Apply(coupon, request, code);
        await coupons.AddAsync(coupon, cancellationToken);
        audit.Log("CouponCreated", nameof(Coupon), coupon.Id.ToString(), currentUser.UserId, new { coupon.Code });

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (conflicts.IsConflict(ex))
        {
            conflicts.DiscardChanges(); // the unique index on the code caught a race (or a soft-deleted coupon with this code)
            return AdminErrors.CouponCodeTaken;
        }
        return ToDto(coupon);
    }

    public async Task<Result<AdminCouponDto>> UpdateAsync(Guid id, SaveCouponRequest request, CancellationToken cancellationToken = default)
    {
        var coupon = await coupons.GetByIdAsync(id, cancellationToken);
        if (coupon is null)
            return AdminErrors.CouponNotFound;

        var errors = await ValidateAsync(request, cancellationToken);
        if (errors.Count > 0)
            return Result.Failure<AdminCouponDto>(errors);
        if (request.UsageLimit is { } limit && limit < coupon.UsedCount)
            return AdminErrors.UsageBelowUsed(coupon.UsedCount);

        var code = Normalise(request.Code);
        if (code != coupon.Code.ToUpperInvariant() && await coupons.ExistsAsync(c => c.Code == code && c.Id != id, cancellationToken))
            return AdminErrors.CouponCodeTaken;

        Apply(coupon, request, code);
        audit.Log("CouponUpdated", nameof(Coupon), id.ToString(), currentUser.UserId, new { coupon.Code });
        return await SaveAsync(coupon, cancellationToken);
    }

    public async Task<Result<AdminCouponDto>> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        var coupon = await coupons.GetByIdAsync(id, cancellationToken);
        if (coupon is null)
            return AdminErrors.CouponNotFound;

        coupon.IsActive = isActive;
        audit.Log(isActive ? "CouponActivated" : "CouponDeactivated", nameof(Coupon), id.ToString(), currentUser.UserId, new { coupon.Code });
        return await SaveAsync(coupon, cancellationToken);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var coupon = await coupons.GetByIdAsync(id, cancellationToken);
        if (coupon is null)
            return AdminErrors.CouponNotFound;
        if (coupon.UsedCount > 0)
            return Error.Conflict("Coupon.InUse", "This coupon has been used on orders, so it cannot be deleted. Deactivate it instead.");

        coupons.Remove(coupon);
        audit.Log("CouponDeleted", nameof(Coupon), id.ToString(), currentUser.UserId, new { coupon.Code });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Result<AdminCouponDto>> SaveAsync(Coupon coupon, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (conflicts.IsConflict(ex))
        {
            conflicts.DiscardChanges();
            return Error.Conflict("Coupon.Changed", "This coupon was changed (or used) while you were editing. Reload it and try again.");
        }
        return ToDto(coupon);
    }

    private static string Normalise(string code) => code.Trim().ToUpperInvariant();

    private async Task<List<Error>> ValidateAsync(SaveCouponRequest r, CancellationToken cancellationToken)
    {
        var errors = new List<Error>();
        if (!CodePattern().IsMatch(Normalise(r.Code ?? string.Empty)))
            errors.Add(AdminErrors.Coupon("code", "Use 3–40 letters, digits, '-' or '_' (e.g. DIWALI25)."));
        if (r.Description is { Length: > 200 })
            errors.Add(AdminErrors.Coupon("description", "The description can be at most 200 characters."));

        if (r.DiscountType == DiscountType.Percentage)
        {
            if (r.DiscountValue is <= 0 or > 100)
                errors.Add(AdminErrors.Coupon("discountValue", "A percentage must be above 0 and at most 100."));
            if (r.MaxDiscountAmount is <= 0)
                errors.Add(AdminErrors.Coupon("maxDiscountAmount", "The maximum discount must be above 0."));
        }
        else
        {
            if (r.DiscountValue <= 0)
                errors.Add(AdminErrors.Coupon("discountValue", "A fixed discount must be above 0."));
            if (r.MaxDiscountAmount is not null)
                errors.Add(AdminErrors.Coupon("maxDiscountAmount", "A maximum only applies to percentage coupons."));
        }

        if (r.MinOrderAmount is < 0)
            errors.Add(AdminErrors.Coupon("minOrderAmount", "The minimum order cannot be negative."));
        if (r.StartsAt is { } start && r.EndsAt is { } end && end <= start)
            errors.Add(AdminErrors.Coupon("endsAt", "The end must be after the start."));
        if (r.UsageLimit is < 1)
            errors.Add(AdminErrors.Coupon("usageLimit", "The usage limit must be at least 1 (leave it empty for unlimited)."));
        if (r.PerUserLimit is < 1 or > 100)
            errors.Add(AdminErrors.Coupon("perUserLimit", "Per-customer limit must be between 1 and 100."));

        var packages = (r.ApplicablePackageIds ?? []).Distinct().ToList();
        if (packages.Count > 0 && await repository.CountExistingPackagesAsync(packages, cancellationToken) != packages.Count)
            errors.Add(AdminErrors.Coupon("applicablePackageIds", "One of the chosen packages does not exist."));
        var cats = (r.ApplicableCategoryIds ?? []).Distinct().ToList();
        if (cats.Count > 0 && await repository.CountExistingCategoriesAsync(cats, cancellationToken) != cats.Count)
            errors.Add(AdminErrors.Coupon("applicableCategoryIds", "One of the chosen categories does not exist."));
        return errors;
    }

    private static void Apply(Coupon coupon, SaveCouponRequest r, string code)
    {
        coupon.Code = code;
        coupon.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        coupon.DiscountType = r.DiscountType;
        coupon.DiscountValue = r.DiscountValue;
        coupon.MaxDiscountAmount = r.DiscountType == DiscountType.Percentage ? r.MaxDiscountAmount : null;
        coupon.MinOrderAmount = r.MinOrderAmount is > 0 ? r.MinOrderAmount : null;
        coupon.ApplicablePackageIds = (r.ApplicablePackageIds ?? []).Distinct().ToList();
        coupon.ApplicableCategoryIds = (r.ApplicableCategoryIds ?? []).Distinct().ToList();
        coupon.StartsAt = r.StartsAt;
        coupon.EndsAt = r.EndsAt;
        coupon.UsageLimit = r.UsageLimit;
        coupon.PerUserLimit = r.PerUserLimit;
        coupon.IsActive = r.IsActive;
    }

    internal static AdminCouponDto ToDto(Coupon c) => new(c.Id, c.Code, c.Description, c.DiscountType, c.DiscountValue, c.MaxDiscountAmount, c.MinOrderAmount,
        c.ApplicablePackageIds, c.ApplicableCategoryIds, c.StartsAt, c.EndsAt, c.UsageLimit, c.PerUserLimit, c.UsedCount, c.IsActive, c.CreatedAt);
}
