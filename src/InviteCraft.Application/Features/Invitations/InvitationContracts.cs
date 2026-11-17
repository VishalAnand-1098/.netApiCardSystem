using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Invitations;

/// <summary>Per-field style changes a customer made. Only keys the template's permissions allow are accepted.</summary>
public sealed record FieldStyleOverride(Guid? FontId, decimal? FontSizePt, string? Color, string? Align);

/// <summary>Where the customer moved a field to (normalised 0–1, top-left of its frame). Size is the template's.</summary>
public sealed record FieldPosition(decimal X, decimal Y);

public sealed record InvitationValueInput(string FieldKey, string? Value, FieldStyleOverride? Style, FieldPosition? Position);

/// <param name="ImageUrl">Browser URL when the field is an Image field and <paramref name="Value"/> is an uploaded asset id.</param>
public sealed record InvitationValueDto(string FieldKey, string? Value, string? ImageUrl, FieldStyleOverride? Style, FieldPosition? Position);

public sealed record InvitationCompletionDto(int RequiredTotal, int RequiredFilled, bool IsComplete);

/// <param name="Version">Concurrency token: send it back with the next save; a mismatch means another tab/device saved first.</param>
/// <param name="TemplateLayoutVersion">Greater than <paramref name="LayoutVersion"/> when the designer changed the template after this invitation was started.</param>
public sealed record InvitationDto(
    Guid Id,
    string Title,
    string Language,
    InvitationStatus Status,
    InvitationVisibility Visibility,
    Guid TemplateId,
    string TemplateSlug,
    string TemplateName,
    int LayoutVersion,
    int TemplateLayoutVersion,
    bool IsUnlocked,
    DateTimeOffset? EditableUntil,
    uint Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<InvitationValueDto> Values,
    InvitationCompletionDto Completion);

/// <summary>Everything the editor needs in one call: the customer's invitation plus the template that drives the form and the card.</summary>
public sealed record InvitationEditorDto(InvitationDto Invitation, TemplateDetailDto Template);

public sealed record InvitationListItemDto(
    Guid Id,
    string Title,
    InvitationStatus Status,
    string TemplateName,
    string TemplateSlug,
    string? ThumbnailUrl,
    bool IsUnlocked,
    DateTimeOffset UpdatedAt);

public sealed record InvitationListRow(
    Guid Id,
    string Title,
    InvitationStatus Status,
    Guid TemplateId,
    string TemplateName,
    string TemplateSlug,
    Guid? ThumbnailAssetId,
    bool IsUnlocked,
    DateTimeOffset UpdatedAt);

/// <summary>?search=&amp;status=Draft|Ready|Archived&amp;sortBy=updatedAt|title|createdAt&amp;sortDir=&amp;page=&amp;pageSize=</summary>
public sealed record InvitationQuery : PagedRequest
{
    public InvitationStatus? Status { get; init; }
}

public sealed record CreateInvitationRequest(string TemplateSlug, string? Title, string? Language);

/// <summary>
/// Replaces the invitation's content: every active field is set to the value given here, and active fields
/// that are missing from <paramref name="Values"/> are cleared. Idempotent, so it suits autosave.
/// </summary>
public sealed record SaveInvitationContentRequest(uint Version, string? Title, string? Language, IReadOnlyList<InvitationValueInput> Values);

public sealed record InvitationImageDto(Guid AssetId, string Url, int Width, int Height);

public static class InvitationStorageKeys
{
    public static string Original(Guid invitationId, Guid assetId, string extension) =>
        $"{Common.Interfaces.IFileStorage.PrivatePrefix}invitations/{invitationId}/{assetId}/original.{extension}";

    public static string Web(Guid invitationId, Guid assetId) =>
        $"{Common.Interfaces.IFileStorage.PublicPrefix}invitations/{invitationId}/{assetId}/web.webp";

    public static string PublicFolder(Guid invitationId, Guid assetId) =>
        $"{Common.Interfaces.IFileStorage.PublicPrefix}invitations/{invitationId}/{assetId}/";

    public static string PrivateFolder(Guid invitationId, Guid assetId) =>
        $"{Common.Interfaces.IFileStorage.PrivatePrefix}invitations/{invitationId}/{assetId}/";
}
