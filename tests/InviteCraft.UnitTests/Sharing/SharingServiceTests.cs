using System.Text.RegularExpressions;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Content;
using InviteCraft.Application.Features.Invitations;
using InviteCraft.Application.Features.Payments;
using InviteCraft.Application.Features.Rendering;
using InviteCraft.Application.Features.Sharing;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Entities.Catalog;
using InviteCraft.Domain.Entities.Invitations;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;
using InviteCraft.UnitTests.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace InviteCraft.UnitTests.Sharing;

public partial class SharingServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private const string Origin = "https://cards.example";

    private readonly IInvitationRepository _invitations = Substitute.For<IInvitationRepository>();
    private readonly ITemplateRepository _templates = Substitute.For<ITemplateRepository>();
    private readonly IContentRepository _content = Substitute.For<IContentRepository>();
    private readonly ICardRenderer _renderer = Substitute.For<ICardRenderer>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IPersistenceConflicts _conflicts = Substitute.For<IPersistenceConflicts>();
    private readonly SharingSettings _settings = new() { PublicOrigin = Origin, RenderImages = true };
    private readonly SharingService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Template _template;
    private readonly TemplateAsset _thumbnail;
    private readonly Invitation _invitation;

    [GeneratedRegex("^[a-z2-9]{10}$")]
    private static partial Regex SlugShape();

    public SharingServiceTests()
    {
        _storage.GetPublicUrl(Arg.Any<string>()).Returns(ci =>
            ci.Arg<string>().StartsWith("public/", StringComparison.Ordinal) ? "/media/" + ci.Arg<string>()[7..] : null);
        _renderer.RenderAsync(Arg.Any<CardRenderRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RenderedFile([1, 2, 3], "image/jpeg", "jpg", 800, 1200));

        _template = new Template { Name = "Royal Maroon Wedding", Slug = "royal-maroon-wedding", Tier = TemplateTier.Premium, Category = new Category { Name = "Wedding", Slug = "wedding" } };
        var field = new TemplateEditableField
        {
            FieldKey = "bride_name", FieldType = EditableFieldType.Text, IsActive = true, X = 0.1m, Y = 0.1m, Width = 0.8m, Height = 0.1m,
            Style = TemplateJson.Serialize(new TextStyle(null, 24, "#112233")), Permissions = TemplateJson.Serialize(FieldPermissions.Default),
        };
        _template.EditableFields.Add(field);
        _thumbnail = new TemplateAsset { TemplateId = _template.Id, AssetType = TemplateAssetType.Thumbnail, StorageKey = "x", MimeType = "image/webp" };
        _template.Assets.Add(_thumbnail);

        _invitation = new Invitation { UserId = _userId, TemplateId = _template.Id, Title = "Ananya & Rohan", Language = "en" };
        _invitation.FieldValues.Add(new InvitationFieldValue { InvitationId = _invitation.Id, FieldId = field.Id, Value = "Ananya" });

        _invitations.GetOwnedAsync(_userId, _invitation.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_invitation);
        _invitations.GetBySlugAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ci => _invitation.ShareSlug == ci.Arg<string>() ? _invitation : null);
        _templates.GetAggregateAsync(_template.Id, false, Arg.Any<CancellationToken>()).Returns(_template);

        _sut = new SharingService(_invitations, _templates, _content, _renderer, _storage, _unitOfWork, _conflicts,
            new TemplateMapper(_storage), Substitute.For<IAuditLogger>(), new FixedClock(Now), Options.Create(_settings),
            NullLogger<SharingService>.Instance);
    }

    private Task<Result<InvitationSharingDto>> Update(InvitationVisibility visibility) =>
        _sut.UpdateAsync(_userId, _invitation.Id, new UpdateSharingRequest(visibility), Origin, Ct);

    // ------------------------------------------------------------------ owner

    [Fact]
    public async Task Turning_sharing_on_mints_a_readable_slug_renders_the_preview_image_and_builds_the_links()
    {
        var result = await Update(InvitationVisibility.LinkOnly);

        result.IsSuccess.ShouldBeTrue(result.FirstError?.Message);
        var dto = result.Value;
        dto.ShareCode.ShouldNotBeNull();
        SlugShape().IsMatch(dto.ShareCode).ShouldBeTrue(dto.ShareCode);
        dto.ShareUrl.ShouldBe($"{Origin}/s/{dto.ShareCode}");
        dto.WhatsAppUrl.ShouldStartWith("https://wa.me/?text=");
        dto.WhatsAppUrl.ShouldContain(Uri.EscapeDataString($"{Origin}/s/{dto.ShareCode}").Replace("%2F", "%2F")); // the link is URL-encoded into the message
        dto.Watermarked.ShouldBeTrue(); // premium, not purchased

        _invitation.PreviewImageKey.ShouldBe($"public/share/{dto.ShareCode}.jpg");
        _invitation.LastRenderedAt.ShouldBe(Now);
        await _storage.Received(1).SaveAsync(_invitation.PreviewImageKey!, Arg.Any<Stream>(), "image/jpeg", Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task The_link_survives_turning_sharing_off_and_on_again()
    {
        var first = (await Update(InvitationVisibility.LinkOnly)).Value.ShareCode;
        await Update(InvitationVisibility.Private);
        var second = (await Update(InvitationVisibility.LinkOnly)).Value.ShareCode;

        second.ShouldBe(first);
    }

    [Fact]
    public async Task Turning_sharing_off_removes_the_public_image_and_the_public_endpoint_stops_answering()
    {
        var slug = (await Update(InvitationVisibility.LinkOnly)).Value.ShareCode!;
        var key = $"public/share/{slug}.jpg";

        var off = await Update(InvitationVisibility.Private);

        off.Value.ShareCode.ShouldBeNull();
        off.Value.ShareUrl.ShouldBeNull();
        _invitation.PreviewImageKey.ShouldBeNull();
        await _storage.Received(1).DeleteAsync(key, Arg.Any<CancellationToken>());
        (await _sut.GetBySlugAsync(slug, Ct)).FirstError.ShouldBe(SharingErrors.NotFound);
        (await _sut.GetPreviewAsync(slug, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Public_gallery_visibility_is_not_offered_yet_and_foreign_invitations_are_not_found()
    {
        (await Update(InvitationVisibility.Public)).FirstError.ShouldBe(SharingErrors.PublicNotSupported);
        (await _sut.UpdateAsync(Guid.NewGuid(), _invitation.Id, new UpdateSharingRequest(InvitationVisibility.LinkOnly), Origin, Ct))
            .FirstError.ShouldBe(InvitationErrors.NotFound);
        (await _sut.GetAsync(Guid.NewGuid(), _invitation.Id, Origin, Ct)).FirstError.ShouldBe(InvitationErrors.NotFound);
    }

    [Fact]
    public async Task A_render_problem_never_blocks_sharing_the_template_thumbnail_stands_in()
    {
        _renderer.RenderAsync(Arg.Any<CardRenderRequest>(), Arg.Any<CancellationToken>())
            .Returns<RenderedFile>(_ => throw new InvalidOperationException("no browser"));

        var result = await Update(InvitationVisibility.LinkOnly);

        result.IsSuccess.ShouldBeTrue();
        _invitation.PreviewImageKey.ShouldBeNull();
        var publicView = await _sut.GetBySlugAsync(result.Value.ShareCode!, Ct);
        publicView.Value.ImageUrl.ShouldBe($"/media/templates/{_template.Id}/{_thumbnail.Id}/thumb.webp");
    }

    [Fact]
    public async Task No_image_is_rendered_when_rendering_is_switched_off()
    {
        _settings.RenderImages = false;

        (await Update(InvitationVisibility.LinkOnly)).IsSuccess.ShouldBeTrue();

        await _renderer.DidNotReceive().RenderAsync(Arg.Any<CardRenderRequest>(), Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------ public view

    [Fact]
    public async Task The_public_view_returns_the_card_data_marks_premium_unpurchased_as_watermarked_and_counts_the_view()
    {
        var slug = (await Update(InvitationVisibility.LinkOnly)).Value.ShareCode!;

        var result = await _sut.GetBySlugAsync(slug, Ct);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value;
        dto.Title.ShouldBe("Ananya & Rohan");
        dto.Watermarked.ShouldBeTrue();
        dto.ImageUrl.ShouldBe($"/media/share/{slug}.jpg");
        dto.Values.ShouldHaveSingleItem().FieldKey.ShouldBe("bride_name");
        dto.Template.Slug.ShouldBe("royal-maroon-wedding");
        await _invitations.Received(1).IncrementViewCountAsync(_invitation.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_purchased_invitation_is_shared_without_the_watermark()
    {
        _invitation.UnlockedPackageId = Guid.NewGuid();
        var slug = (await Update(InvitationVisibility.LinkOnly)).Value.ShareCode!;

        (await _sut.GetBySlugAsync(slug, Ct)).Value.Watermarked.ShouldBeFalse();
        _renderer.ReceivedCalls().ShouldAllBe(c => ((CardRenderRequest)c.GetArguments()[0]!).Watermark == null);
    }

    [Fact]
    public async Task Unknown_and_expired_links_answer_not_found()
    {
        (await _sut.GetBySlugAsync("nope2nope2", Ct)).FirstError.ShouldBe(SharingErrors.NotFound);

        var slug = (await Update(InvitationVisibility.LinkOnly)).Value.ShareCode!;
        _invitation.ShareExpiresAt = Now.AddHours(-1);
        (await _sut.GetBySlugAsync(slug, Ct)).FirstError.ShouldBe(SharingErrors.NotFound);
        (await _sut.GetPreviewAsync(slug, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_stale_preview_image_is_refreshed_on_the_next_public_view_but_a_fresh_one_is_not()
    {
        var slug = (await Update(InvitationVisibility.LinkOnly)).Value.ShareCode!;
        _renderer.ClearReceivedCalls();

        // fresh: rendered now, not edited since
        _invitation.UpdatedAt = Now.AddMinutes(-5);
        await _sut.GetBySlugAsync(slug, Ct);
        await _renderer.DidNotReceive().RenderAsync(Arg.Any<CardRenderRequest>(), Arg.Any<CancellationToken>());

        // stale: edited after the last render
        _invitation.UpdatedAt = Now.AddMinutes(5);
        await _sut.GetBySlugAsync(slug, Ct);
        await _renderer.Received(1).RenderAsync(Arg.Any<CardRenderRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_og_preview_does_not_count_a_view()
    {
        var slug = (await Update(InvitationVisibility.LinkOnly)).Value.ShareCode!;

        var preview = await _sut.GetPreviewAsync(slug, Ct);

        preview.ShouldNotBeNull();
        preview.Title.ShouldBe("Ananya & Rohan");
        preview.TemplateName.ShouldBe("Royal Maroon Wedding");
        preview.ImageUrl.ShouldBe($"/media/share/{slug}.jpg");
        await _invitations.DidNotReceive().IncrementViewCountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
