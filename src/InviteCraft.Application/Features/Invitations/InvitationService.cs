using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Entities.Invitations;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace InviteCraft.Application.Features.Invitations;

public interface IInvitationService
{
    Task<Result<InvitationEditorDto>> CreateAsync(Guid userId, CreateInvitationRequest request, CancellationToken cancellationToken = default);
    Task<Result<InvitationEditorDto>> GetEditorAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<InvitationListItemDto>> ListAsync(Guid userId, InvitationQuery query, CancellationToken cancellationToken = default);
    Task<Result<InvitationDto>> SaveContentAsync(Guid userId, Guid id, SaveInvitationContentRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);
    Task<Result<InvitationEditorDto>> DuplicateAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);
    Task<Result<InvitationImageDto>> UploadImageAsync(Guid userId, Guid id, string fieldKey, FileUpload file, CancellationToken cancellationToken = default);
}

internal sealed class InvitationService(
    IInvitationRepository invitations,
    ITemplateRepository templates,
    IUnitOfWork unitOfWork,
    IFileStorage storage,
    IImageProcessor images,
    TemplateMapper mapper,
    IAuditLogger audit,
    TimeProvider timeProvider,
    ILogger<InvitationService> logger) : IInvitationService
{
    public const int MaxInvitationsPerUser = 100;
    public const long MaxImageBytes = 10 * 1024 * 1024;
    public const int MinImageDimension = 100;
    public const int MaxImageDimension = 6000;
    public const int ImageWebMaxDimension = 1600;

    // ------------------------------------------------------------------ create & read

    public async Task<Result<InvitationEditorDto>> CreateAsync(Guid userId, CreateInvitationRequest request, CancellationToken cancellationToken = default)
    {
        if (await invitations.CountOwnedAsync(userId, cancellationToken) >= MaxInvitationsPerUser)
            return InvitationErrors.LimitReached(MaxInvitationsPerUser);

        var template = await templates.GetPublishedAggregateBySlugAsync(request.TemplateSlug.Trim().ToLowerInvariant(), cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        var language = string.IsNullOrWhiteSpace(request.Language)
            ? template.SupportedLanguages.FirstOrDefault() ?? "en"
            : request.Language.Trim().ToLowerInvariant();
        if (template.SupportedLanguages.Count > 0 && !template.SupportedLanguages.Contains(language))
            return InvitationErrors.InvalidLanguage(language);

        var invitation = new Invitation
        {
            UserId = userId,
            TemplateId = template.Id,
            Title = string.IsNullOrWhiteSpace(request.Title) ? template.Name : request.Title.Trim(),
            Language = language,
            Status = InvitationStatus.Draft,
            LayoutVersion = template.LayoutVersion,
        };

        // Start from the template's sample content so the first preview already looks like a real card.
        foreach (var field in template.EditableFields.Where(f => f.IsActive && !string.IsNullOrWhiteSpace(f.DefaultValue)))
        {
            var (value, error) = FieldValueRules.CheckValue(field, field.DefaultValue);
            if (error is null && value is not null)
                invitation.FieldValues.Add(new InvitationFieldValue { InvitationId = invitation.Id, FieldId = field.Id, Value = value });
        }

        await invitations.AddAsync(invitation, cancellationToken);
        audit.Log("InvitationCreated", nameof(Invitation), invitation.Id.ToString(), userId, new { template = template.Code });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetEditorAsync(userId, invitation.Id, cancellationToken);
    }

    public async Task<Result<InvitationEditorDto>> GetEditorAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        var invitation = await invitations.GetOwnedAsync(userId, id, includeValues: true, cancellationToken);
        if (invitation is null)
            return InvitationErrors.NotFound;

        var template = await templates.GetAggregateAsync(invitation.TemplateId, track: false, cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        var packages = await templates.GetActivePackagesAsync(cancellationToken);
        return new InvitationEditorDto(ToDto(invitation, template), mapper.ToDetail(template, packages, includeInactive: false));
    }

    public async Task<PagedResult<InvitationListItemDto>> ListAsync(Guid userId, InvitationQuery query, CancellationToken cancellationToken = default)
    {
        var page = await invitations.SearchOwnedAsync(userId, query, cancellationToken);
        return new PagedResult<InvitationListItemDto>(
            page.Items.Select(r => new InvitationListItemDto(r.Id, r.Title, r.Status, r.TemplateName, r.TemplateSlug,
                mapper.ThumbUrl(r.TemplateId, r.ThumbnailAssetId), r.IsUnlocked, r.UpdatedAt)).ToList(),
            page.Page, page.PageSize, page.TotalCount);
    }

    // ------------------------------------------------------------------ save

    public async Task<Result<InvitationDto>> SaveContentAsync(Guid userId, Guid id, SaveInvitationContentRequest request, CancellationToken cancellationToken = default)
    {
        var invitation = await invitations.GetOwnedAsync(userId, id, includeValues: true, cancellationToken);
        if (invitation is null)
            return InvitationErrors.NotFound;

        var template = await templates.GetAggregateAsync(invitation.TemplateId, track: false, cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        if (IsEditingClosed(invitation))
            return InvitationErrors.EditingClosed;
        if (request.Version != invitation.Version)
            return InvitationErrors.VersionConflict;

        var language = invitation.Language;
        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            language = request.Language.Trim().ToLowerInvariant();
            if (template.SupportedLanguages.Count > 0 && !template.SupportedLanguages.Contains(language))
                return Result.Failure<InvitationDto>(InvitationErrors.InvalidLanguage(language));
        }

        // ---- validate every value against its template field
        var activeFields = template.EditableFields.Where(f => f.IsActive).ToDictionary(f => f.FieldKey);
        var templateFontIds = template.FontConfigurations.Select(c => c.FontId).ToHashSet();
        var errors = new List<Error>();
        var accepted = new Dictionary<string, (TemplateEditableField Field, string? Value, FieldStyleOverride? Style, FieldPosition? Position)>();

        foreach (var input in request.Values)
        {
            if (!activeFields.TryGetValue(input.FieldKey, out var field))
            {
                errors.Add(InvitationErrors.UnknownField(input.FieldKey));
                continue;
            }
            if (accepted.ContainsKey(input.FieldKey))
            {
                errors.Add(InvitationErrors.DuplicateField(input.FieldKey));
                continue;
            }

            var permissions = TemplateJson.Deserialize<FieldPermissions>(field.Permissions) ?? FieldPermissions.Default;

            var (value, valueError) = FieldValueRules.CheckValue(field, input.Value);
            if (valueError is not null)
                errors.Add(InvitationErrors.FieldInvalid(input.FieldKey, valueError));
            else if (value is not null && field.FieldType == EditableFieldType.Image
                     && !await storage.ExistsAsync(InvitationStorageKeys.Web(invitation.Id, Guid.Parse(value)), cancellationToken))
                errors.Add(InvitationErrors.ImageNotFound(input.FieldKey));

            if (FieldValueRules.CheckStyle(input.Style, field, permissions, templateFontIds) is { } styleError)
                errors.Add(InvitationErrors.StyleNotAllowed(input.FieldKey, styleError));
            if (FieldValueRules.CheckPosition(input.Position, field, permissions) is { } moveError)
                errors.Add(InvitationErrors.MoveNotAllowed(input.FieldKey, moveError));

            accepted[input.FieldKey] = (field, value, FieldValueRules.NormaliseStyle(input.Style), FieldValueRules.NormalisePosition(input.Position));
        }

        if (errors.Count > 0)
            return Result.Failure<InvitationDto>(errors);

        // ---- apply: every active field takes the incoming state; fields not sent are cleared
        var existing = invitation.FieldValues.ToDictionary(v => v.FieldId);
        foreach (var field in activeFields.Values)
        {
            existing.TryGetValue(field.Id, out var row);

            if (!accepted.TryGetValue(field.FieldKey, out var incoming) ||
                (incoming.Value is null && incoming.Style is null && incoming.Position is null))
            {
                if (row is not null)
                    invitation.FieldValues.Remove(row);
                continue;
            }

            if (row is null)
            {
                row = new InvitationFieldValue { InvitationId = invitation.Id, FieldId = field.Id };
                invitation.FieldValues.Add(row);
            }
            row.Value = incoming.Value;
            row.StyleOverride = incoming.Style is null ? null : TemplateJson.Serialize(incoming.Style);
            row.FrameOverride = incoming.Position is null ? null : TemplateJson.Serialize(incoming.Position);
        }

        if (!string.IsNullOrWhiteSpace(request.Title))
            invitation.Title = request.Title.Trim();
        invitation.Language = language;

        var completion = Completion(template, invitation.FieldValues);
        if (invitation.Status != InvitationStatus.Archived)
            invitation.Status = completion.IsComplete ? InvitationStatus.Ready : InvitationStatus.Draft;

        invitations.Touch(invitation);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(invitation, template);
    }

    // ------------------------------------------------------------------ delete

    public async Task<Result> DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        var invitation = await invitations.GetOwnedAsync(userId, id, includeValues: false, cancellationToken);
        if (invitation is null)
            return InvitationErrors.NotFound;
        if (invitation.IsUnlocked)
            return InvitationErrors.Unlocked;

        invitations.Remove(invitation); // soft delete
        audit.Log("InvitationDeleted", nameof(Invitation), id.ToString(), userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ------------------------------------------------------------------ duplicate

    /// <summary>Copies text, style and position into a new draft. Photos are not copied (they live in the source invitation's storage).</summary>
    public async Task<Result<InvitationEditorDto>> DuplicateAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        var source = await invitations.GetOwnedAsync(userId, id, includeValues: true, cancellationToken);
        if (source is null)
            return InvitationErrors.NotFound;
        if (await invitations.CountOwnedAsync(userId, cancellationToken) >= MaxInvitationsPerUser)
            return InvitationErrors.LimitReached(MaxInvitationsPerUser);

        var template = await templates.GetAggregateAsync(source.TemplateId, track: false, cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        var title = source.Title.EndsWith(" (copy)", StringComparison.Ordinal) ? source.Title : source.Title + " (copy)";
        var copy = new Invitation
        {
            UserId = userId,
            TemplateId = source.TemplateId,
            Title = title.Length > 150 ? title[..150] : title,
            Language = source.Language,
            Status = InvitationStatus.Draft,
            LayoutVersion = template.LayoutVersion,
        };

        var activeFields = template.EditableFields.Where(f => f.IsActive).ToDictionary(f => f.Id);
        foreach (var value in source.FieldValues.Where(v => activeFields.TryGetValue(v.FieldId, out var f) && f.FieldType != EditableFieldType.Image))
            copy.FieldValues.Add(new InvitationFieldValue
            {
                InvitationId = copy.Id, FieldId = value.FieldId, Value = value.Value,
                StyleOverride = value.StyleOverride, FrameOverride = value.FrameOverride,
            });

        copy.Status = Completion(template, copy.FieldValues).IsComplete ? InvitationStatus.Ready : InvitationStatus.Draft;

        await invitations.AddAsync(copy, cancellationToken);
        audit.Log("InvitationDuplicated", nameof(Invitation), copy.Id.ToString(), userId, new { from = id });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetEditorAsync(userId, copy.Id, cancellationToken);
    }

    // ------------------------------------------------------------------ images

    public async Task<Result<InvitationImageDto>> UploadImageAsync(Guid userId, Guid id, string fieldKey, FileUpload file, CancellationToken cancellationToken = default)
    {
        if (file.Length is <= 0 or > MaxImageBytes)
            return TemplateErrors.ImageTooLarge(MaxImageBytes);

        var invitation = await invitations.GetOwnedAsync(userId, id, includeValues: false, cancellationToken);
        if (invitation is null)
            return InvitationErrors.NotFound;
        if (IsEditingClosed(invitation))
            return InvitationErrors.EditingClosed;

        var template = await templates.GetAggregateAsync(invitation.TemplateId, track: false, cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        var field = template.EditableFields.FirstOrDefault(f => f.IsActive && f.FieldKey == fieldKey);
        if (field is null)
            return InvitationErrors.UnknownField(fieldKey);
        if (field.FieldType != EditableFieldType.Image)
            return InvitationErrors.NotImageField;

        using var buffer = new MemoryStream();
        await file.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaxImageBytes)
            return TemplateErrors.ImageTooLarge(MaxImageBytes);

        buffer.Position = 0;
        var info = images.Inspect(buffer); // by content, never by file name or MIME header
        if (info is null)
            return TemplateErrors.UnsupportedImage;
        if (Math.Min(info.Width, info.Height) < MinImageDimension || Math.Max(info.Width, info.Height) > MaxImageDimension)
            return TemplateErrors.ImageDimensions(MinImageDimension, MaxImageDimension);

        var assetId = Guid.CreateVersion7();
        try
        {
            buffer.Position = 0;
            await storage.SaveAsync(InvitationStorageKeys.Original(id, assetId, info.Extension), buffer, info.MimeType, cancellationToken);

            buffer.Position = 0;
            using var web = new MemoryStream(images.CreateWebVariant(buffer, ImageWebMaxDimension, quality: 85));
            await storage.SaveAsync(InvitationStorageKeys.Web(id, assetId), web, "image/webp", cancellationToken);
        }
        catch
        {
            await DeleteImageFilesAsync(id, assetId);
            throw;
        }

        return new InvitationImageDto(assetId, storage.GetPublicUrl(InvitationStorageKeys.Web(id, assetId))!, info.Width, info.Height);
    }

    // ------------------------------------------------------------------ helpers

    private bool IsEditingClosed(Invitation invitation) =>
        invitation.IsUnlocked && invitation.EditableUntil is { } until && until < timeProvider.GetUtcNow();

    private static InvitationCompletionDto Completion(Template template, IEnumerable<InvitationFieldValue> values)
    {
        var filled = values.Where(v => !string.IsNullOrWhiteSpace(v.Value)).Select(v => v.FieldId).ToHashSet();
        var required = template.EditableFields.Where(f => f.IsActive && f.IsRequired).ToList();
        var done = required.Count(f => filled.Contains(f.Id));
        return new InvitationCompletionDto(required.Count, done, done == required.Count);
    }

    private InvitationDto ToDto(Invitation invitation, Template template)
    {
        var fields = template.EditableFields.Where(f => f.IsActive).ToDictionary(f => f.Id);

        var values = invitation.FieldValues
            .Where(v => fields.ContainsKey(v.FieldId))
            .Select(v =>
            {
                var field = fields[v.FieldId];
                var imageUrl = field.FieldType == EditableFieldType.Image && Guid.TryParse(v.Value, out var assetId)
                    ? storage.GetPublicUrl(InvitationStorageKeys.Web(invitation.Id, assetId))
                    : null;
                return new InvitationValueDto(field.FieldKey, v.Value, imageUrl,
                    TemplateJson.Deserialize<FieldStyleOverride>(v.StyleOverride),
                    TemplateJson.Deserialize<FieldPosition>(v.FrameOverride));
            })
            .OrderBy(v => v.FieldKey, StringComparer.Ordinal)
            .ToList();

        return new InvitationDto(
            invitation.Id, invitation.Title, invitation.Language, invitation.Status, invitation.Visibility,
            template.Id, template.Slug, template.Name, invitation.LayoutVersion, template.LayoutVersion,
            invitation.IsUnlocked, invitation.EditableUntil, invitation.Version, invitation.CreatedAt, invitation.UpdatedAt,
            values, Completion(template, invitation.FieldValues));
    }

    private async Task DeleteImageFilesAsync(Guid invitationId, Guid assetId)
    {
        try
        {
            await storage.DeletePrefixAsync(InvitationStorageKeys.PublicFolder(invitationId, assetId), CancellationToken.None);
            await storage.DeletePrefixAsync(InvitationStorageKeys.PrivateFolder(invitationId, assetId), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete stored files of invitation image {AssetId}", assetId);
        }
    }
}
