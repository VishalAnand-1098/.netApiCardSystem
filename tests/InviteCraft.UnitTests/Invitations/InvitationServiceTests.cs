using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Features.Invitations;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Entities.Catalog;
using InviteCraft.Domain.Entities.Invitations;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace InviteCraft.UnitTests.Invitations;

public class InvitationServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly IInvitationRepository _invitations = Substitute.For<IInvitationRepository>();
    private readonly ITemplateRepository _templates = Substitute.For<ITemplateRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly IImageProcessor _images = Substitute.For<IImageProcessor>();
    private readonly InvitationService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Font _font = new() { FamilyName = "Great Vibes", DisplayName = "Great Vibes", IsActive = true };
    private readonly Template _template;
    private readonly TemplateEditableField _bride;
    private readonly TemplateEditableField _date;
    private readonly TemplateEditableField _note;
    private readonly TemplateEditableField _photo;

    public InvitationServiceTests()
    {
        _storage.GetPublicUrl(Arg.Any<string>()).Returns(ci =>
            ci.Arg<string>().StartsWith("public/", StringComparison.Ordinal) ? "/media/" + ci.Arg<string>()[7..] : null);
        _sut = new InvitationService(_invitations, _templates, _unitOfWork, _storage, _images,
            new TemplateMapper(_storage), Substitute.For<IAuditLogger>(), new FixedClock(Now), NullLogger<InvitationService>.Instance);

        _bride = Field("bride_name", EditableFieldType.Text, required: true, maxLength: 20, defaultValue: "Ananya");
        _date = Field("wedding_date", EditableFieldType.Date, required: true);
        _note = Field("note", EditableFieldType.MultilineText, required: false, permissions: new FieldPermissions(true, true, true, true, CanMove: true));
        _photo = Field("couple_photo", EditableFieldType.Image, required: false);

        _template = new Template
        {
            Name = "Royal Maroon Wedding", Slug = "royal-maroon-wedding", LayoutVersion = 3, SupportedLanguages = ["en", "hi"],
            Category = new Category { Name = "Wedding", Slug = "wedding" },
        };
        foreach (var f in new[] { _bride, _date, _note, _photo }) _template.EditableFields.Add(f);
        _template.FontConfigurations.Add(new TemplateFontConfiguration { FontId = _font.Id, Font = _font, IsDefault = true });

        _templates.GetAggregateAsync(_template.Id, false, Arg.Any<CancellationToken>()).Returns(_template);
        _templates.GetPublishedAggregateBySlugAsync("royal-maroon-wedding", Arg.Any<CancellationToken>()).Returns(_template);
        _templates.GetActivePackagesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<Package>());
    }

    private static TemplateEditableField Field(string key, EditableFieldType type, bool required, int? maxLength = null,
        string? defaultValue = null, FieldPermissions? permissions = null) => new()
    {
        FieldKey = key, FieldType = type, IsRequired = required, MaxLength = maxLength, DefaultValue = defaultValue, IsActive = true,
        X = 0.1m, Y = 0.4m, Width = 0.8m, Height = 0.1m,
        Style = TemplateJson.Serialize(new TextStyle(null, 24, "#112233")),
        Permissions = TemplateJson.Serialize(permissions ?? FieldPermissions.Default),
    };

    private Invitation StoredInvitation(uint version = 7) => new()
    {
        UserId = _userId, TemplateId = _template.Id, Title = "Our wedding", Language = "en", LayoutVersion = 3, Version = version,
    };

    private Invitation Stored(Invitation invitation)
    {
        _invitations.GetOwnedAsync(_userId, invitation.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(invitation);
        return invitation;
    }

    private static SaveInvitationContentRequest Save(uint version, params InvitationValueInput[] values) => new(version, null, null, values);
    private static InvitationValueInput Value(string key, string? value, FieldStyleOverride? style = null, FieldPosition? position = null) => new(key, value, style, position);

    // ------------------------------------------------------------------ create

    [Fact]
    public async Task Create_starts_a_draft_prefilled_with_template_defaults_in_the_first_supported_language()
    {
        Invitation? added = null;
        await _invitations.AddAsync(Arg.Do<Invitation>(i => added = i), Arg.Any<CancellationToken>());
        _invitations.GetOwnedAsync(_userId, Arg.Any<Guid>(), true, Arg.Any<CancellationToken>()).Returns(_ => added);

        var result = await _sut.CreateAsync(_userId, new CreateInvitationRequest("Royal-Maroon-Wedding", null, null), Ct);

        result.IsSuccess.ShouldBeTrue(result.FirstError?.Message);
        added!.Title.ShouldBe("Royal Maroon Wedding");
        added.Language.ShouldBe("en");
        added.LayoutVersion.ShouldBe(3);
        added.Status.ShouldBe(InvitationStatus.Draft);
        added.FieldValues.Single().Value.ShouldBe("Ananya");        // only the field with a default
        result.Value.Invitation.Values.Single().FieldKey.ShouldBe("bride_name");
        result.Value.Invitation.Completion.ShouldBe(new InvitationCompletionDto(2, 1, false));
    }

    [Fact]
    public async Task Create_rejects_unsupported_languages_unknown_templates_and_the_invitation_limit()
    {
        (await _sut.CreateAsync(_userId, new CreateInvitationRequest("royal-maroon-wedding", null, "fr"), Ct))
            .FirstError!.Code.ShouldBe("Invitation.InvalidLanguage");

        (await _sut.CreateAsync(_userId, new CreateInvitationRequest("no-such-template", null, null), Ct))
            .FirstError.ShouldBe(TemplateErrors.NotFound);

        _invitations.CountOwnedAsync(_userId, Arg.Any<CancellationToken>()).Returns(InvitationService.MaxInvitationsPerUser);
        (await _sut.CreateAsync(_userId, new CreateInvitationRequest("royal-maroon-wedding", null, null), Ct))
            .FirstError!.Code.ShouldBe("Invitation.LimitReached");
    }

    // ------------------------------------------------------------------ save

    [Fact]
    public async Task Save_stores_values_and_style_and_becomes_Ready_when_required_fields_are_filled()
    {
        var invitation = Stored(StoredInvitation());

        var result = await _sut.SaveContentAsync(_userId, invitation.Id, Save(7,
            Value("bride_name", "  दुल्हन  ", new FieldStyleOverride(_font.Id, 30, "#AA0000", "left")),
            Value("wedding_date", "2027-02-14")), Ct);

        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors.Select(e => $"{e.Field}: {e.Message}")));
        invitation.Status.ShouldBe(InvitationStatus.Ready);
        var bride = invitation.FieldValues.Single(v => v.FieldId == _bride.Id);
        bride.Value.ShouldBe("दुल्हन");
        TemplateJson.Deserialize<FieldStyleOverride>(bride.StyleOverride).ShouldBe(new FieldStyleOverride(_font.Id, 30, "#aa0000", "left"));
        result.Value.Completion.IsComplete.ShouldBeTrue();
        _invitations.Received(1).Touch(invitation);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_clears_fields_that_are_not_sent_and_goes_back_to_Draft()
    {
        var invitation = Stored(StoredInvitation());
        invitation.Status = InvitationStatus.Ready;
        invitation.FieldValues.Add(new InvitationFieldValue { InvitationId = invitation.Id, FieldId = _bride.Id, Value = "Ananya" });
        invitation.FieldValues.Add(new InvitationFieldValue { InvitationId = invitation.Id, FieldId = _date.Id, Value = "2027-02-14" });

        var result = await _sut.SaveContentAsync(_userId, invitation.Id, Save(7, Value("bride_name", "Ananya")), Ct);

        result.IsSuccess.ShouldBeTrue();
        invitation.FieldValues.Select(v => v.FieldId).ShouldBe([_bride.Id]);
        invitation.Status.ShouldBe(InvitationStatus.Draft);
    }

    [Fact]
    public async Task Save_with_a_stale_version_is_a_conflict_and_changes_nothing()
    {
        var invitation = Stored(StoredInvitation(version: 7));

        var result = await _sut.SaveContentAsync(_userId, invitation.Id, Save(6, Value("bride_name", "Ananya")), Ct);

        result.FirstError.ShouldBe(InvitationErrors.VersionConflict);
        invitation.FieldValues.ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Another_users_invitation_looks_like_it_does_not_exist()
    {
        var someoneElses = StoredInvitation();   // not registered with the repository for this user

        (await _sut.SaveContentAsync(_userId, someoneElses.Id, Save(7), Ct)).FirstError.ShouldBe(InvitationErrors.NotFound);
        (await _sut.GetEditorAsync(_userId, someoneElses.Id, Ct)).FirstError.ShouldBe(InvitationErrors.NotFound);
        (await _sut.DeleteAsync(_userId, someoneElses.Id, Ct)).FirstError.ShouldBe(InvitationErrors.NotFound);
    }

    [Fact]
    public async Task Save_reports_every_invalid_field_at_once_and_stores_nothing()
    {
        var invitation = Stored(StoredInvitation());

        var result = await _sut.SaveContentAsync(_userId, invitation.Id, Save(7,
            Value("bride_name", new string('x', 21)),                                         // too long
            Value("wedding_date", "tomorrow"),                                                 // bad date
            Value("nope", "x"),                                                                // unknown field
            Value("bride_name", "again"),                                                      // duplicate
            Value("note", "hi", new FieldStyleOverride(Guid.NewGuid(), null, null, null))), Ct); // font not in template

        result.IsFailure.ShouldBeTrue();
        result.Errors.Select(e => e.Code).ShouldBe(
            ["Invitation.FieldInvalid", "Invitation.FieldInvalid", "Invitation.UnknownField", "Invitation.DuplicateField", "Invitation.StyleNotAllowed"], ignoreOrder: true);
        result.Errors.Select(e => e.Field).ShouldContain("wedding_date");
        invitation.FieldValues.ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Moving_a_field_needs_the_permission_and_is_stored_when_allowed()
    {
        var invitation = Stored(StoredInvitation());

        var denied = await _sut.SaveContentAsync(_userId, invitation.Id, Save(7, Value("bride_name", "Ananya", position: new FieldPosition(0.1m, 0.5m))), Ct);
        denied.Errors.Single().Code.ShouldBe("Invitation.MoveNotAllowed");

        var allowed = await _sut.SaveContentAsync(_userId, invitation.Id, Save(7, Value("note", "Hello", position: new FieldPosition(0.1m, 0.55m))), Ct);
        allowed.IsSuccess.ShouldBeTrue();
        allowed.Value.Values.Single().Position.ShouldBe(new FieldPosition(0.1m, 0.55m));
    }

    [Fact]
    public async Task An_image_value_must_refer_to_an_uploaded_file_of_this_invitation()
    {
        var invitation = Stored(StoredInvitation());
        var asset = Guid.NewGuid();

        var missing = await _sut.SaveContentAsync(_userId, invitation.Id, Save(7, Value("couple_photo", asset.ToString())), Ct);
        missing.Errors.Single().Code.ShouldBe("Invitation.ImageNotFound");

        _storage.ExistsAsync(InvitationStorageKeys.Web(invitation.Id, asset), Arg.Any<CancellationToken>()).Returns(true);
        var ok = await _sut.SaveContentAsync(_userId, invitation.Id, Save(7, Value("couple_photo", asset.ToString())), Ct);

        ok.IsSuccess.ShouldBeTrue();
        ok.Value.Values.Single().ImageUrl.ShouldBe($"/media/invitations/{invitation.Id}/{asset}/web.webp");
    }

    [Fact]
    public async Task Language_must_be_supported_by_the_template()
    {
        var invitation = Stored(StoredInvitation());

        var result = await _sut.SaveContentAsync(_userId, invitation.Id, new SaveInvitationContentRequest(7, null, "ta", []), Ct);
        result.FirstError!.Code.ShouldBe("Invitation.InvalidLanguage");

        var hindi = await _sut.SaveContentAsync(_userId, invitation.Id, new SaveInvitationContentRequest(7, "  My card ", "hi", []), Ct);
        hindi.IsSuccess.ShouldBeTrue();
        (invitation.Language, invitation.Title).ShouldBe(("hi", "My card"));
    }

    [Fact]
    public async Task A_purchased_invitation_cannot_be_edited_after_its_editing_period()
    {
        var invitation = Stored(StoredInvitation());
        invitation.UnlockedPackageId = Guid.NewGuid();
        invitation.UnlockedAt = Now.AddDays(-40);
        invitation.EditableUntil = Now.AddDays(-10);

        (await _sut.SaveContentAsync(_userId, invitation.Id, Save(7), Ct)).FirstError.ShouldBe(InvitationErrors.EditingClosed);

        invitation.EditableUntil = Now.AddDays(5);
        (await _sut.SaveContentAsync(_userId, invitation.Id, Save(7), Ct)).IsSuccess.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ delete

    [Fact]
    public async Task Drafts_can_be_deleted_but_purchased_invitations_cannot()
    {
        var draft = Stored(StoredInvitation());
        (await _sut.DeleteAsync(_userId, draft.Id, Ct)).IsSuccess.ShouldBeTrue();
        _invitations.Received(1).Remove(draft);

        var paid = Stored(StoredInvitation());
        paid.UnlockedPackageId = Guid.NewGuid();
        (await _sut.DeleteAsync(_userId, paid.Id, Ct)).FirstError.ShouldBe(InvitationErrors.Unlocked);
        _invitations.DidNotReceive().Remove(paid);
    }

    // ------------------------------------------------------------------ duplicate

    [Fact]
    public async Task Duplicate_copies_text_style_and_position_but_not_photos_into_a_new_draft()
    {
        var source = Stored(StoredInvitation());
        source.Status = InvitationStatus.Ready;
        source.UnlockedPackageId = Guid.NewGuid();
        var style = TemplateJson.Serialize(new FieldStyleOverride(null, 30, "#ff0000", null));
        source.FieldValues.Add(new InvitationFieldValue { InvitationId = source.Id, FieldId = _bride.Id, Value = "Meera", StyleOverride = style });
        source.FieldValues.Add(new InvitationFieldValue { InvitationId = source.Id, FieldId = _date.Id, Value = "2027-02-14" });
        source.FieldValues.Add(new InvitationFieldValue { InvitationId = source.Id, FieldId = _photo.Id, Value = Guid.NewGuid().ToString() });

        Invitation? added = null;
        await _invitations.AddAsync(Arg.Do<Invitation>(i => added = i), Arg.Any<CancellationToken>());
        _invitations.GetOwnedAsync(_userId, Arg.Is<Guid>(g => g != source.Id), true, Arg.Any<CancellationToken>()).Returns(ci => added is not null && ci.ArgAt<Guid>(1) == added.Id ? added : null);

        var result = await _sut.DuplicateAsync(_userId, source.Id, Ct);

        result.IsSuccess.ShouldBeTrue(result.FirstError?.Message);
        added.ShouldNotBeNull();
        added.Id.ShouldNotBe(source.Id);
        added.Title.ShouldBe("Our wedding (copy)");
        added.IsUnlocked.ShouldBeFalse();
        added.Status.ShouldBe(InvitationStatus.Ready); // both required fields are filled
        added.FieldValues.Select(v => v.FieldId).ShouldBe([_bride.Id, _date.Id], ignoreOrder: true);
        added.FieldValues.Single(v => v.FieldId == _bride.Id).StyleOverride.ShouldBe(style);
        await _unitOfWork.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Duplicate_does_not_pile_up_copy_suffixes_and_respects_ownership_and_limit()
    {
        var source = Stored(StoredInvitation());
        source.Title = "Our wedding (copy)";
        Invitation? added = null;
        await _invitations.AddAsync(Arg.Do<Invitation>(i => added = i), Arg.Any<CancellationToken>());
        _invitations.GetOwnedAsync(_userId, Arg.Is<Guid>(g => g != source.Id), true, Arg.Any<CancellationToken>()).Returns(ci => added is not null && ci.ArgAt<Guid>(1) == added.Id ? added : null);

        (await _sut.DuplicateAsync(_userId, source.Id, Ct)).IsSuccess.ShouldBeTrue();
        added!.Title.ShouldBe("Our wedding (copy)");

        (await _sut.DuplicateAsync(_userId, Guid.NewGuid(), Ct)).FirstError.ShouldBe(InvitationErrors.NotFound);

        _invitations.CountOwnedAsync(_userId, Arg.Any<CancellationToken>()).Returns(InvitationService.MaxInvitationsPerUser);
        (await _sut.DuplicateAsync(_userId, source.Id, Ct)).FirstError!.Code.ShouldBe(InvitationErrors.LimitReached(InvitationService.MaxInvitationsPerUser).Code);
    }

    // ------------------------------------------------------------------ image upload

    [Fact]
    public async Task Upload_rejects_non_image_fields_unknown_fields_and_files_that_are_not_images()
    {
        var invitation = Stored(StoredInvitation());
        FileUpload File(string name = "photo.png") => new(new MemoryStream([1, 2, 3]), name, "image/png", 3);

        (await _sut.UploadImageAsync(_userId, invitation.Id, "bride_name", File(), Ct)).FirstError.ShouldBe(InvitationErrors.NotImageField);
        (await _sut.UploadImageAsync(_userId, invitation.Id, "missing", File(), Ct)).FirstError!.Code.ShouldBe("Invitation.UnknownField");

        _images.Inspect(Arg.Any<Stream>()).Returns((ImageInfo?)null);
        (await _sut.UploadImageAsync(_userId, invitation.Id, "couple_photo", File("evil.png"), Ct)).FirstError.ShouldBe(TemplateErrors.UnsupportedImage);
        await _storage.DidNotReceive().SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_stores_a_private_original_and_a_public_web_copy()
    {
        var invitation = Stored(StoredInvitation());
        _images.Inspect(Arg.Any<Stream>()).Returns(new ImageInfo("png", "image/png", "png", 1200, 800));
        _images.CreateWebVariant(Arg.Any<Stream>(), Arg.Any<int>(), Arg.Any<int>()).Returns([9, 9, 9]);

        var result = await _sut.UploadImageAsync(_userId, invitation.Id, "couple_photo",
            new FileUpload(new MemoryStream([1, 2, 3]), "us.png", "image/png", 3), Ct);

        result.IsSuccess.ShouldBeTrue(result.FirstError?.Message);
        result.Value.Url.ShouldBe($"/media/invitations/{invitation.Id}/{result.Value.AssetId}/web.webp");
        await _storage.Received(1).SaveAsync(Arg.Is<string>(k => k.StartsWith("private/invitations/") && k.EndsWith("original.png")), Arg.Any<Stream>(), "image/png", Arg.Any<CancellationToken>());
        await _storage.Received(1).SaveAsync(Arg.Is<string>(k => k.StartsWith("public/invitations/") && k.EndsWith("web.webp")), Arg.Any<Stream>(), "image/webp", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_rejects_tiny_and_oversized_images()
    {
        var invitation = Stored(StoredInvitation());
        FileUpload File(long length = 3) => new(new MemoryStream([1, 2, 3]), "a.png", "image/png", length);

        _images.Inspect(Arg.Any<Stream>()).Returns(new ImageInfo("png", "image/png", "png", 50, 50));
        (await _sut.UploadImageAsync(_userId, invitation.Id, "couple_photo", File(), Ct)).FirstError!.Code.ShouldBe("TemplateAsset.Dimensions");
        (await _sut.UploadImageAsync(_userId, invitation.Id, "couple_photo", File(InvitationService.MaxImageBytes + 1), Ct)).FirstError!.Code.ShouldBe("TemplateAsset.TooLarge");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
