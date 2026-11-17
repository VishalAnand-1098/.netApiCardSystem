using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Features.Content;
using InviteCraft.Application.Features.Invitations;
using InviteCraft.Application.Features.Rendering;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Entities.Catalog;
using InviteCraft.Domain.Entities.Invitations;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace InviteCraft.UnitTests.Rendering;

public class RenderJobProcessorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly IDownloadRepository _downloads = Substitute.For<IDownloadRepository>();
    private readonly IInvitationRepository _invitations = Substitute.For<IInvitationRepository>();
    private readonly ITemplateRepository _templates = Substitute.For<ITemplateRepository>();
    private readonly IContentRepository _content = Substitute.For<IContentRepository>();
    private readonly ICardRenderer _renderer = Substitute.For<ICardRenderer>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly RenderJobProcessor _sut;

    private readonly Template _template;
    private readonly TemplateEditableField _bride;
    private readonly Invitation _invitation;
    private readonly Download _download;

    public RenderJobProcessorTests()
    {
        _storage.GetPublicUrl(Arg.Any<string>()).Returns(ci => "/media/" + ci.Arg<string>().Replace("public/", ""));
        _sut = new RenderJobProcessor(_downloads, _invitations, _templates, _content, _renderer, _storage, _unitOfWork,
            new TemplateMapper(_storage), new FixedClock(Now), NullLogger<RenderJobProcessor>.Instance);

        _bride = new TemplateEditableField { FieldKey = "bride_name", FieldType = EditableFieldType.Text, IsRequired = true, IsActive = true };
        _template = new Template
        {
            Name = "Royal Maroon Wedding", Slug = "royal-maroon-wedding", Code = "IC-1", Tier = TemplateTier.Premium, WidthMm = 127, HeightMm = 178,
            SupportedLanguages = ["en", "hi"], Category = new Category { Name = "Wedding", Slug = "wedding" },
        };
        _template.EditableFields.Add(_bride);

        _invitation = new Invitation { UserId = Guid.NewGuid(), TemplateId = _template.Id, Title = "Wedding", Language = "hi" };
        _invitation.FieldValues.Add(new InvitationFieldValue { InvitationId = _invitation.Id, FieldId = _bride.Id, Value = "अनन्या" });

        _download = new Download
        {
            UserId = _invitation.UserId, InvitationId = _invitation.Id, Invitation = _invitation, Format = DownloadFormat.Png, Dpi = 300,
            Status = DownloadStatus.Queued,
        };

        _downloads.GetForRenderAsync(_download.Id, Arg.Any<CancellationToken>()).Returns(_download);
        _invitations.GetOwnedAsync(_invitation.UserId, _invitation.Id, true, Arg.Any<CancellationToken>()).Returns(_invitation);
        _templates.GetAggregateAsync(_template.Id, false, Arg.Any<CancellationToken>()).Returns(_template);
        _templates.GetActivePackagesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<Package>());
    }

    [Fact]
    public async Task A_queued_download_is_rendered_stored_and_marked_ready()
    {
        CardRenderRequest? request = null;
        _renderer.RenderAsync(Arg.Do<CardRenderRequest>(r => request = r), Arg.Any<CancellationToken>())
            .Returns(new RenderedFile([1, 2, 3, 4], "image/png", "png", 1500, 2100));

        await _sut.ProcessAsync(_download.Id, Ct);

        _download.Status.ShouldBe(DownloadStatus.Ready);
        _download.StorageKey.ShouldBe($"private/downloads/{_download.UserId}/{_download.Id}.png");
        _download.FileSizeBytes.ShouldBe(4);
        _download.CompletedAt.ShouldBe(Now);
        _download.Error.ShouldBeNull();
        await _storage.Received(1).SaveAsync(_download.StorageKey, Arg.Any<Stream>(), "image/png", Arg.Any<CancellationToken>());

        request.ShouldNotBeNull();
        request.Format.ShouldBe(DownloadFormat.Png);
        request.Dpi.ShouldBe(300);
        request.Language.ShouldBe("hi");
        request.Watermark.ShouldBeNull();
        request.Values["bride_name"].Value.ShouldBe("अनन्या");
        request.Template.Code.ShouldBe("IC-1");
    }

    [Fact]
    public async Task Watermarked_downloads_use_the_configured_watermark_text()
    {
        _download.HasWatermark = true;
        _content.GetSettingJsonAsync("watermark", Arg.Any<CancellationToken>()).Returns("""{"text":"Acme · Preview","opacity":0.3}""");
        CardRenderRequest? request = null;
        _renderer.RenderAsync(Arg.Do<CardRenderRequest>(r => request = r), Arg.Any<CancellationToken>())
            .Returns(new RenderedFile([1], "image/jpeg", "jpg", 480, 672));

        await _sut.ProcessAsync(_download.Id, Ct);

        request!.Watermark.ShouldBe(new WatermarkSpec("Acme · Preview", 0.3m));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("""{"opacity":0.2}""")]
    public async Task A_missing_or_broken_watermark_setting_falls_back_to_the_default_text(string? setting)
    {
        _download.HasWatermark = true;
        _content.GetSettingJsonAsync("watermark", Arg.Any<CancellationToken>()).Returns(setting);
        CardRenderRequest? request = null;
        _renderer.RenderAsync(Arg.Do<CardRenderRequest>(r => request = r), Arg.Any<CancellationToken>())
            .Returns(new RenderedFile([1], "image/jpeg", "jpg", 480, 672));

        await _sut.ProcessAsync(_download.Id, Ct);

        request!.Watermark!.Text.ShouldBe("InviteCraft · Preview");
    }

    [Fact]
    public async Task A_renderer_failure_marks_the_download_failed_with_a_customer_safe_message()
    {
        _renderer.RenderAsync(Arg.Any<CardRenderRequest>(), Arg.Any<CancellationToken>()).Returns<RenderedFile>(_ => throw new InvalidOperationException("Chromium crashed: secret internals"));

        await _sut.ProcessAsync(_download.Id, Ct);

        _download.Status.ShouldBe(DownloadStatus.Failed);
        _download.Error.ShouldBe(DownloadErrors.RenderUnavailable.Message);
        _download.Error!.ShouldNotContain("Chromium");
        _download.StorageKey.ShouldBeNull();
        await _storage.DidNotReceive().SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_deleted_invitation_fails_the_job_instead_of_throwing()
    {
        _invitations.GetOwnedAsync(_invitation.UserId, _invitation.Id, true, Arg.Any<CancellationToken>()).Returns((Invitation?)null);

        await _sut.ProcessAsync(_download.Id, Ct);

        _download.Status.ShouldBe(DownloadStatus.Failed);
    }

    [Theory]
    [InlineData(DownloadStatus.Ready)]
    [InlineData(DownloadStatus.Failed)]
    [InlineData(DownloadStatus.Expired)]
    public async Task Finished_downloads_are_never_rendered_twice(DownloadStatus status)
    {
        _download.Status = status;

        await _sut.ProcessAsync(_download.Id, Ct);

        await _renderer.DidNotReceive().RenderAsync(Arg.Any<CardRenderRequest>(), Arg.Any<CancellationToken>());
        _download.Status.ShouldBe(status);
    }

    [Fact]
    public async Task An_unknown_download_is_ignored()
    {
        await _sut.ProcessAsync(Guid.NewGuid(), Ct);

        await _renderer.DidNotReceive().RenderAsync(Arg.Any<CardRenderRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_shutdown_during_rendering_leaves_the_job_to_be_resumed_on_the_next_start()
    {
        using var shutdown = new CancellationTokenSource();
        _renderer.RenderAsync(Arg.Any<CardRenderRequest>(), Arg.Any<CancellationToken>()).Returns<RenderedFile>(_ =>
        {
            shutdown.Cancel();
            throw new OperationCanceledException(shutdown.Token);
        });

        await Should.ThrowAsync<OperationCanceledException>(() => _sut.ProcessAsync(_download.Id, shutdown.Token));

        _download.Status.ShouldBe(DownloadStatus.Rendering); // not Failed: it is re-queued at start-up
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
