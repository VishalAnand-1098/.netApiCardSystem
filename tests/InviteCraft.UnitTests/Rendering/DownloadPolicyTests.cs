using InviteCraft.Application.Features.Rendering;
using InviteCraft.Domain.Entities.Invitations;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;

namespace InviteCraft.UnitTests.Rendering;

public class DownloadPolicyTests
{
    private static readonly Package Free = new() { Code = Package.FreeCode, Name = "Free", AllowedFormats = ["Jpg", "Png"], MaxDpi = 150, RemovesWatermark = true };
    private static readonly Package Basic = new() { Code = Package.BasicCode, Name = "Basic", AllowedFormats = ["Jpg", "Png"], MaxDpi = 150, MaxDownloads = 5, RemovesWatermark = true };
    private static readonly Package Luxury = new()
    {
        Code = Package.LuxuryCode, Name = "Luxury", AllowedFormats = ["Jpg", "Png", "Pdf", "PrintPdf"], MaxDpi = 300, RemovesWatermark = true,
    };

    private static readonly Package[] Packages = [Free, Basic, Luxury];

    private static Template TemplateOf(TemplateTier tier) => new() { Tier = tier, WidthMm = 127, HeightMm = 178 };

    [Fact]
    public void An_unpaid_design_cannot_download_any_file()
    {
        foreach (var tier in new[] { TemplateTier.Premium, TemplateTier.Free })
        {
            var entitlement = DownloadPolicy.Resolve(new Invitation(), TemplateOf(tier), Packages);

            entitlement.PackageCode.ShouldBe(DownloadPolicy.PreviewCode);
            entitlement.PreviewOnly.ShouldBeTrue();
            entitlement.Formats.ShouldBeEmpty();
            entitlement.MaxDownloads.ShouldBe(0);
        }
    }

    [Fact]
    public void A_purchased_invitation_gets_exactly_what_its_package_includes()
    {
        var invitation = new Invitation { UnlockedPackageId = Basic.Id };

        var entitlement = DownloadPolicy.Resolve(invitation, TemplateOf(TemplateTier.Premium), Packages);

        entitlement.PackageCode.ShouldBe("BASIC");
        entitlement.Watermark.ShouldBeFalse();
        entitlement.MaxDownloads.ShouldBe(5);
        entitlement.Formats.ShouldNotContain(DownloadFormat.Pdf);
    }

    [Fact]
    public void A_purchase_of_a_higher_package_unlocks_pdf_and_print_pdf()
    {
        var entitlement = DownloadPolicy.Resolve(new Invitation { UnlockedPackageId = Luxury.Id }, TemplateOf(TemplateTier.Premium), Packages);

        entitlement.Formats.ShouldBe([DownloadFormat.Jpg, DownloadFormat.Png, DownloadFormat.Pdf, DownloadFormat.PrintPdf]);
        entitlement.MaxDpi.ShouldBe(300);
    }

    [Fact]
    public void If_the_purchased_package_was_removed_the_invitation_cannot_download()
    {
        var entitlement = DownloadPolicy.Resolve(new Invitation { UnlockedPackageId = Guid.NewGuid() }, TemplateOf(TemplateTier.Premium), Packages);

        entitlement.PreviewOnly.ShouldBeTrue();
        entitlement.Formats.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(96, new[] { 96 })]
    [InlineData(150, new[] { 96, 150 })]
    [InlineData(300, new[] { 96, 150, 300 })]
    [InlineData(600, new[] { 96, 150, 300 })]
    public void Resolutions_offered_stop_at_the_package_limit(int maxDpi, int[] expected)
    {
        var entitlement = new DownloadEntitlement("X", "X", [DownloadFormat.Jpg], maxDpi, null, false, false);

        DownloadPolicy.AllowedDpis(entitlement).ShouldBe(expected);
    }

    [Theory]
    [InlineData(127, 96, 480)]
    [InlineData(127, 150, 750)]
    [InlineData(127, 300, 1500)]
    [InlineData(178, 96, 672)]    // 178 mm = 672.76 CSS px, trimmed to whole pixels
    [InlineData(178, 150, 1050)]
    [InlineData(178, 300, 2100)]
    public void Pixel_sizes_match_what_the_renderer_produces(int millimetres, int dpi, int expected) =>
        DownloadPolicy.PixelsFor(millimetres, dpi).ShouldBe(expected);

    [Theory]
    [InlineData(DownloadFormat.Jpg, "image/jpeg", "jpg")]
    [InlineData(DownloadFormat.Png, "image/png", "png")]
    [InlineData(DownloadFormat.Pdf, "application/pdf", "pdf")]
    [InlineData(DownloadFormat.PrintPdf, "application/pdf", "pdf")]
    public void File_types_follow_the_format(DownloadFormat format, string mime, string extension) =>
        DownloadPolicy.FileType(format).ShouldBe((mime, extension));
}
