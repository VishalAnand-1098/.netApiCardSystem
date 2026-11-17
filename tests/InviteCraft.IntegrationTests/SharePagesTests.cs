using InviteCraft.Api.Controllers;
using InviteCraft.Application.Features.Sharing;

namespace InviteCraft.IntegrationTests;

/// <summary>The server-rendered OG page crawlers read (plain string building, no host needed).</summary>
public class SharePagesTests
{
    private static readonly SharePreview Preview = new("abc123wxyz", "Ananya & Rohan <script>alert(1)</script>", "Royal Maroon Wedding", "/media/share/abc123wxyz.jpg");

    [Fact]
    public void The_og_page_carries_the_tags_whatsapp_reads_and_forwards_people_to_the_viewing_page()
    {
        var html = SharePages.Og(Preview, "https://cards.example/");

        // non-ASCII (the ·) is numerically encoded, user text is escaped
        html.ShouldContain("""<meta property="og:title" content="Ananya &amp; Rohan &lt;script&gt;alert(1)&lt;/script&gt; &#183; InviteCraft">""");
        html.ShouldContain("""<meta property="og:image" content="https://cards.example/media/share/abc123wxyz.jpg">""");
        html.ShouldContain("""<meta property="og:url" content="https://cards.example/s/abc123wxyz">""");
        html.ShouldContain("""<meta name="twitter:card" content="summary_large_image">""");
        html.ShouldContain("""content="0;url=https://cards.example/i/abc123wxyz""");
        html.ShouldContain("""<meta name="robots" content="noindex">""");
        html.ShouldNotContain("<script>"); // everything user-written is encoded
    }

    [Fact]
    public void Without_an_image_no_og_image_tag_is_emitted_and_an_absolute_image_is_kept_as_is()
    {
        SharePages.Og(Preview with { ImageUrl = null }, "https://cards.example").ShouldNotContain("og:image");
        SharePages.Og(Preview with { ImageUrl = "https://cdn.example/x.jpg" }, "https://cards.example")
            .ShouldContain("""content="https://cdn.example/x.jpg""");
    }

    [Fact]
    public void The_dead_link_page_explains_and_invites()
    {
        var html = SharePages.NotFound();
        html.ShouldContain("no longer active");
        html.ShouldContain("Create your own");
    }
}
