using System.Text;
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

public class DownloadServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly ClientInfo Client = new(System.Net.IPAddress.Loopback, "UnitTest/1.0");

    private readonly IDownloadRepository _downloads = Substitute.For<IDownloadRepository>();
    private readonly IInvitationRepository _invitations = Substitute.For<IInvitationRepository>();
    private readonly ITemplateRepository _templates = Substitute.For<ITemplateRepository>();
    private readonly IContentRepository _content = Substitute.For<IContentRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IRenderQueue _queue = Substitute.For<IRenderQueue>();
    private readonly IDownloadLinkSigner _signer = Substitute.For<IDownloadLinkSigner>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly DownloadService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Template _template;
    private readonly TemplateEditableField _bride;
    private readonly Invitation _invitation;
    private readonly Package _free = new() { Code = Package.FreeCode, Name = "Free", SortOrder = 10, AllowedFormats = ["Jpg", "Png"], MaxDpi = 150, RemovesWatermark = true };
    private readonly Package _basic = new() { Code = Package.BasicCode, Name = "Basic", SortOrder = 20, AllowedFormats = ["Jpg", "Png"], MaxDpi = 150, MaxDownloads = 2, RemovesWatermark = true };
    private readonly Package _premium = new() { Code = Package.PremiumCode, Name = "Premium", SortOrder = 30, AllowedFormats = ["Jpg", "Png", "Pdf"], MaxDpi = 300, RemovesWatermark = true };
    private readonly Package _luxury = new() { Code = Package.LuxuryCode, Name = "Luxury", SortOrder = 40, AllowedFormats = ["Jpg", "Png", "Pdf", "PrintPdf"], MaxDpi = 300, RemovesWatermark = true };

    public DownloadServiceTests()
    {
        _sut = new DownloadService(_downloads, _invitations, _templates, _content, _unitOfWork, _queue, _signer, _storage,
            Substitute.For<IAuditLogger>(), new FixedClock(Now), NullLogger<DownloadService>.Instance);

        _bride = new TemplateEditableField { FieldKey = "bride_name", FieldType = EditableFieldType.Text, IsRequired = true, IsActive = true };
        _template = new Template
        {
            Name = "Royal Maroon Wedding", Slug = "royal-maroon-wedding", Tier = TemplateTier.Premium, WidthMm = 127, HeightMm = 178,
            Category = new Category { Name = "Wedding", Slug = "wedding" },
        };
        _template.EditableFields.Add(_bride);

        _invitation = new Invitation { UserId = _userId, TemplateId = _template.Id, Title = "Ananya & Rohan's Wedding" };
        _invitation.FieldValues.Add(new InvitationFieldValue { InvitationId = _invitation.Id, FieldId = _bride.Id, Value = "Ananya" });

        _invitations.GetOwnedAsync(_userId, _invitation.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_invitation);
        _templates.GetAggregateAsync(_template.Id, false, Arg.Any<CancellationToken>()).Returns(_template);
        _templates.GetActivePackagesAsync(Arg.Any<CancellationToken>()).Returns([_free, _basic, _premium, _luxury]);
        _downloads.CountInProgressAsync(_userId, Arg.Any<CancellationToken>()).Returns(0);
        _signer.Sign(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>()).Returns("signature");
    }

    private void Unlock(Package package) => _invitation.UnlockedPackageId = package.Id;
    private Task<InviteCraft.Application.Common.Models.Result<DownloadDto>> Create(DownloadFormat format, int? dpi = null) =>
        _sut.CreateAsync(_userId, _invitation.Id, new CreateDownloadRequest(format, dpi), Client, Ct);

    // ------------------------------------------------------------------ options

    [Fact]
    public async Task Options_for_a_premium_design_that_is_not_purchased_offer_only_the_watermarked_preview()
    {
        var result = await _sut.GetOptionsAsync(_userId, _invitation.Id, Ct);

        result.IsSuccess.ShouldBeTrue();
        var options = result.Value;
        options.PackageCode.ShouldBe("PREVIEW");
        options.Watermark.ShouldBeTrue();
        options.CanDownload.ShouldBeTrue();
        options.Formats.Where(f => f.Available).Select(f => f.Format).ShouldBe([DownloadFormat.Jpg]);
        options.Formats.Single(f => f.Format == DownloadFormat.Jpg).Qualities.ShouldBe([new DownloadQualityDto(96, 480, 672, "Web")]);
        options.Formats.Single(f => f.Format == DownloadFormat.Png).Reason!.ShouldContain("Unlock");
    }

    [Fact]
    public async Task Options_tell_a_purchased_customer_the_pixel_sizes_and_remaining_allowance()
    {
        Unlock(_basic);
        _downloads.CountBillableAsync(_invitation.Id, Arg.Any<CancellationToken>()).Returns(1);

        var options = (await _sut.GetOptionsAsync(_userId, _invitation.Id, Ct)).Value;

        options.Watermark.ShouldBeFalse();
        options.MaxDownloads.ShouldBe(2);
        options.RemainingDownloads.ShouldBe(1);
        options.Formats.Single(f => f.Format == DownloadFormat.Png).Qualities.Select(q => q.Dpi).ShouldBe([96, 150]);
        options.Formats.Single(f => f.Format == DownloadFormat.Pdf).Reason.ShouldBe("Included from the Premium package.");
    }

    [Fact]
    public async Task Options_explain_why_downloading_is_blocked()
    {
        _invitation.FieldValues.Clear();

        var options = (await _sut.GetOptionsAsync(_userId, _invitation.Id, Ct)).Value;

        options.CanDownload.ShouldBeFalse();
        options.BlockedReason.ShouldBe(DownloadErrors.NotReady.Message);

        _invitation.FieldValues.Add(new InvitationFieldValue { FieldId = _bride.Id, Value = "Ananya" });
        Unlock(_basic);
        _downloads.CountBillableAsync(_invitation.Id, Arg.Any<CancellationToken>()).Returns(2);

        var used = (await _sut.GetOptionsAsync(_userId, _invitation.Id, Ct)).Value;
        used.CanDownload.ShouldBeFalse();
        used.RemainingDownloads.ShouldBe(0);
    }

    [Fact]
    public async Task Someone_elses_invitation_is_reported_as_not_found()
    {
        var result = await _sut.GetOptionsAsync(Guid.NewGuid(), _invitation.Id, Ct);

        result.FirstError.ShouldBe(InvitationErrors.NotFound);
    }

    // ------------------------------------------------------------------ create

    [Fact]
    public async Task Creating_a_preview_queues_a_watermarked_96_dpi_job_and_hands_it_to_the_workers()
    {
        Download? saved = null;
        await _downloads.AddAsync(Arg.Do<Download>(d => saved = d), Arg.Any<CancellationToken>());

        var result = await Create(DownloadFormat.Jpg);

        result.IsSuccess.ShouldBeTrue(result.FirstError?.Message);
        saved.ShouldNotBeNull();
        saved.UserId.ShouldBe(_userId);
        saved.InvitationId.ShouldBe(_invitation.Id);
        saved.Format.ShouldBe(DownloadFormat.Jpg);
        saved.Dpi.ShouldBe(96);
        saved.HasWatermark.ShouldBeTrue();
        saved.Status.ShouldBe(DownloadStatus.Queued);
        saved.ExpiresAt.ShouldBe(Now.AddDays(DownloadPolicy.FileRetentionDays));
        saved.IpAddress.ShouldBe(System.Net.IPAddress.Loopback);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _queue.Received(1).EnqueueAsync(saved.Id, Arg.Any<CancellationToken>());
        result.Value.Status.ShouldBe(DownloadStatus.Queued);
        result.Value.DownloadUrl.ShouldBeNull();
    }

    [Fact]
    public async Task Formats_outside_the_entitlement_are_refused_and_nothing_is_queued()
    {
        var png = await Create(DownloadFormat.Png);
        var pdf = await Create(DownloadFormat.PrintPdf);

        png.FirstError!.Code.ShouldBe("Download.FormatNotAvailable");
        png.FirstError.Message.ShouldContain("Unlock");
        pdf.FirstError!.Code.ShouldBe("Download.FormatNotAvailable");
        await _queue.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _downloads.DidNotReceive().AddAsync(Arg.Any<Download>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_resolution_above_the_limit_is_refused_and_a_missing_one_defaults_to_the_sharpest()
    {
        Unlock(_basic);
        Download? saved = null;
        await _downloads.AddAsync(Arg.Do<Download>(d => saved = d), Arg.Any<CancellationToken>());

        (await Create(DownloadFormat.Png, 300)).FirstError!.Code.ShouldBe("Download.QualityNotAvailable");
        (await Create(DownloadFormat.Png, 120)).FirstError!.Code.ShouldBe("Download.QualityNotAvailable"); // not one of the offered steps

        (await Create(DownloadFormat.Png)).IsSuccess.ShouldBeTrue();
        saved!.Dpi.ShouldBe(150);
        saved.HasWatermark.ShouldBeFalse();
    }

    [Fact]
    public async Task Pdfs_record_the_packages_density_and_ignore_a_requested_one()
    {
        Unlock(_luxury);
        Download? saved = null;
        await _downloads.AddAsync(Arg.Do<Download>(d => saved = d), Arg.Any<CancellationToken>());

        (await Create(DownloadFormat.PrintPdf, 96)).IsSuccess.ShouldBeTrue();

        saved!.Dpi.ShouldBe(300);
        saved.Format.ShouldBe(DownloadFormat.PrintPdf);
    }

    [Fact]
    public async Task The_package_allowance_stops_further_clean_downloads_but_never_counts_previews()
    {
        Unlock(_basic);
        _downloads.CountBillableAsync(_invitation.Id, Arg.Any<CancellationToken>()).Returns(2);

        var blocked = await Create(DownloadFormat.Jpg);

        blocked.FirstError!.Code.ShouldBe("Download.AllowanceUsed");

        _invitation.UnlockedPackageId = null; // back to the watermarked preview: unlimited and not counted
        (await Create(DownloadFormat.Jpg)).IsSuccess.ShouldBeTrue();
        await _downloads.Received(1).CountBillableAsync(_invitation.Id, Arg.Any<CancellationToken>()); // only the purchased attempt asked
    }

    [Fact]
    public async Task Too_many_renders_in_progress_are_refused()
    {
        _downloads.CountInProgressAsync(_userId, Arg.Any<CancellationToken>()).Returns(DownloadPolicy.MaxInProgressPerUser);

        (await Create(DownloadFormat.Jpg)).FirstError.ShouldBe(DownloadErrors.TooManyInProgress);
    }

    [Fact]
    public async Task An_invitation_with_missing_required_details_cannot_be_downloaded()
    {
        _invitation.FieldValues.Single().Value = "   ";

        (await Create(DownloadFormat.Jpg)).FirstError.ShouldBe(DownloadErrors.NotReady);
    }

    [Fact]
    public async Task Creating_for_someone_elses_invitation_is_not_found()
    {
        var result = await _sut.CreateAsync(Guid.NewGuid(), _invitation.Id, new CreateDownloadRequest(DownloadFormat.Jpg, null), Client, Ct);

        result.FirstError.ShouldBe(InvitationErrors.NotFound);
    }

    // ------------------------------------------------------------------ read & links

    private Download Stored(DownloadStatus status, bool watermark = false, DateTimeOffset? expiresAt = null)
    {
        var download = new Download
        {
            UserId = _userId, InvitationId = _invitation.Id, Invitation = _invitation, Format = DownloadFormat.Jpg, Dpi = 150, Status = status,
            HasWatermark = watermark, StorageKey = "private/downloads/x.jpg", FileSizeBytes = 1234, ExpiresAt = expiresAt ?? Now.AddDays(7),
        };
        _downloads.GetOwnedAsync(_userId, download.Id, Arg.Any<CancellationToken>()).Returns(download);
        _downloads.GetForRenderAsync(download.Id, Arg.Any<CancellationToken>()).Returns(download);
        return download;
    }

    [Fact]
    public async Task A_ready_download_comes_with_a_short_lived_signed_link_and_a_friendly_file_name()
    {
        var download = Stored(DownloadStatus.Ready);

        var dto = (await _sut.GetAsync(_userId, download.Id, Ct)).Value;

        dto.Status.ShouldBe(DownloadStatus.Ready);
        dto.FileName.ShouldBe("ananya-rohan-s-wedding.jpg");
        dto.DownloadUrl.ShouldBe($"/api/v1/downloads/{download.Id}/file?exp={Now.AddMinutes(15).ToUnixTimeSeconds()}&sig=signature");
    }

    [Fact]
    public async Task Previews_are_named_so_they_are_not_mistaken_for_the_final_file()
    {
        var download = Stored(DownloadStatus.Ready, watermark: true);

        (await _sut.GetAsync(_userId, download.Id, Ct)).Value.FileName.ShouldBe("ananya-rohan-s-wedding-preview.jpg");
    }

    [Fact]
    public async Task Files_past_their_retention_date_read_as_expired_and_have_no_link()
    {
        var download = Stored(DownloadStatus.Ready, expiresAt: Now.AddMinutes(-1));

        var dto = (await _sut.GetAsync(_userId, download.Id, Ct)).Value;

        dto.Status.ShouldBe(DownloadStatus.Expired);
        dto.DownloadUrl.ShouldBeNull();
    }

    [Fact]
    public async Task Queued_downloads_have_no_link_and_failed_ones_show_the_reason()
    {
        var queued = Stored(DownloadStatus.Queued);
        (await _sut.GetAsync(_userId, queued.Id, Ct)).Value.DownloadUrl.ShouldBeNull();

        var failed = Stored(DownloadStatus.Failed);
        failed.Error = "Preparing files is temporarily unavailable.";
        (await _sut.GetAsync(_userId, failed.Id, Ct)).Value.Error.ShouldBe("Preparing files is temporarily unavailable.");
    }

    [Fact]
    public async Task Another_customers_download_is_not_found()
    {
        var download = Stored(DownloadStatus.Ready);

        (await _sut.GetAsync(Guid.NewGuid(), download.Id, Ct)).FirstError.ShouldBe(DownloadErrors.NotFound);
    }

    [Fact]
    public async Task The_file_is_streamed_only_for_a_valid_signature()
    {
        var download = Stored(DownloadStatus.Ready);
        _storage.OpenReadAsync("private/downloads/x.jpg", Arg.Any<CancellationToken>()).Returns(_ => new MemoryStream(Encoding.ASCII.GetBytes("jpeg")));
        _signer.IsValid(download.Id, 99, "good", Now).Returns(true);

        (await _sut.OpenFileAsync(download.Id, 99, "bad", Ct)).FirstError.ShouldBe(DownloadErrors.LinkInvalid);

        var file = (await _sut.OpenFileAsync(download.Id, 99, "good", Ct)).Value;
        file.ContentType.ShouldBe("image/jpeg");
        file.FileName.ShouldBe("ananya-rohan-s-wedding.jpg");
        using var reader = new StreamReader(file.Content);
        (await reader.ReadToEndAsync(Ct)).ShouldBe("jpeg");
    }

    [Fact]
    public async Task A_valid_link_still_fails_when_the_file_has_expired_or_vanished()
    {
        var expired = Stored(DownloadStatus.Ready, expiresAt: Now.AddMinutes(-5));
        _signer.IsValid(expired.Id, 99, "good", Now).Returns(true);
        (await _sut.OpenFileAsync(expired.Id, 99, "good", Ct)).FirstError.ShouldBe(DownloadErrors.Expired);

        var missing = Stored(DownloadStatus.Ready);
        _signer.IsValid(missing.Id, 99, "good", Now).Returns(true);
        _storage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Stream?)null);
        (await _sut.OpenFileAsync(missing.Id, 99, "good", Ct)).FirstError.ShouldBe(DownloadErrors.FileMissing);

        var notReady = Stored(DownloadStatus.Rendering);
        _signer.IsValid(notReady.Id, 99, "good", Now).Returns(true);
        (await _sut.OpenFileAsync(notReady.Id, 99, "good", Ct)).FirstError.ShouldBe(DownloadErrors.NotFound);
    }

    // ------------------------------------------------------------------ file names

    [Theory]
    [InlineData("Ananya & Rohan's Wedding", "ananya-rohan-s-wedding")]
    [InlineData("  Café Soirée  ", "cafe-soiree")]
    [InlineData("दुल्हन", "invitation")]
    [InlineData("", "invitation")]
    [InlineData("../../etc/passwd", "etc-passwd")]
    public void File_names_are_ascii_slugs_that_are_safe_in_headers_and_on_disk(string title, string expected) =>
        FileNames.Slug(title).ShouldBe(expected);

    [Fact]
    public void Very_long_titles_are_shortened()
    {
        var slug = FileNames.Slug(new string('a', 200));

        slug.Length.ShouldBeLessThanOrEqualTo(60);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
