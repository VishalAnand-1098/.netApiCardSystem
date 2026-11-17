using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Entities.Identity;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Domain.Entities.Content;

/// <summary>Template review, or a site testimonial when <see cref="TemplateId"/> is null.</summary>
public class Review : SoftDeletableEntity
{
    public Guid UserId { get; set; }
    public Guid? TemplateId { get; set; }
    /// <summary>Set when the reviewer bought the template ("verified purchase").</summary>
    public Guid? OrderId { get; set; }
    public short Rating { get; set; }
    public string? Title { get; set; }
    public string? Comment { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
    /// <summary>Shown in the homepage testimonials section.</summary>
    public bool IsFeatured { get; set; }
    public string? AdminReply { get; set; }

    public bool IsTestimonial => TemplateId is null;

    public User User { get; set; } = null!;
    public Template? Template { get; set; }
    public Order? Order { get; set; }
}

public class Banner : AuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? MobileImageUrl { get; set; }
    public string? CtaText { get; set; }
    public string? CtaUrl { get; set; }
    public BannerPlacement Placement { get; set; } = BannerPlacement.HomeHero;
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
