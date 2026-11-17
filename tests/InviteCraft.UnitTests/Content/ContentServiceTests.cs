using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Features.Content;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Enums;
using NSubstitute;

namespace InviteCraft.UnitTests.Content;

public class ContentServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly IContentRepository _repo = Substitute.For<IContentRepository>();
    private readonly ContentService _sut;

    public ContentServiceTests()
    {
        var storage = Substitute.For<IFileStorage>();
        storage.GetPublicUrl(Arg.Any<string>()).Returns(ci => "/media/" + ci.Arg<string>()[7..]);
        _sut = new ContentService(_repo, new TemplateMapper(storage), TimeProvider.System);
    }

    [Theory]
    [InlineData("Priya Sharma", "Priya S.")]
    [InlineData("  anil   kumar  verma ", "anil V.")]
    [InlineData("Madonna", "Madonna")]
    [InlineData("   ", "Customer")]
    public void Author_names_are_abbreviated_to_protect_privacy(string full, string expected) =>
        ContentService.DisplayName(full).ShouldBe(expected);

    [Fact]
    public async Task Testimonials_never_expose_the_full_name_and_limit_is_clamped()
    {
        _repo.GetFeaturedTestimonialsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new TestimonialRow(Guid.NewGuid(), "Priya Sharma", 5, "Lovely", "Beautiful cards", DateTimeOffset.UtcNow)]);

        var result = await _sut.GetTestimonialsAsync(500, Ct);

        result.Single().AuthorName.ShouldBe("Priya S.");
        await _repo.Received(1).GetFeaturedTestimonialsAsync(12, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Collections_without_visible_templates_are_hidden_from_the_list()
    {
        var empty = new CollectionRow(Guid.NewGuid(), "Empty", "empty", null, null, null, null, null, TemplateCount: 0);
        var full = new CollectionRow(Guid.NewGuid(), "Diwali", "diwali", null, null, "#14213D", null, null, TemplateCount: 1);
        _repo.GetActiveCollectionsAsync(Arg.Any<DateTimeOffset>(), 4, Arg.Any<CancellationToken>()).Returns([
            new CollectionWithTemplates(empty, []),
            new CollectionWithTemplates(full, [Card("golden-diya")]),
        ]);

        var result = await _sut.GetCollectionsAsync(Ct);

        result.Select(c => c.Slug).ShouldBe(["diwali"]);
        result[0].Templates.Single().ThumbnailUrl.ShouldNotBeNull();
    }

    [Fact]
    public async Task Unknown_collection_is_not_found_and_slug_is_normalised()
    {
        _repo.GetActiveCollectionAsync("nope", Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((CollectionWithTemplates?)null);

        var result = await _sut.GetCollectionAsync("  NOPE ", Ct);

        result.FirstError.ShouldBe(ContentErrors.CollectionNotFound);
    }

    [Fact]
    public async Task Contact_settings_are_parsed_and_malformed_json_falls_back_to_empty()
    {
        _repo.GetSettingJsonAsync("contact", Arg.Any<CancellationToken>())
            .Returns("""{"email":"hello@example.com","phone":null,"whatsapp":"+919876543210"}""", "{not json");

        var good = await _sut.GetContactAsync(Ct);
        var bad = await _sut.GetContactAsync(Ct);

        good.Email.ShouldBe("hello@example.com");
        good.WhatsApp.ShouldBe("+919876543210");
        bad.ShouldBe(new SiteContactDto(null, null, null));
    }

    private static TemplateCardRow Card(string slug) => new(
        Guid.NewGuid(), "IC-1", slug, slug, TemplateStatus.Published, TemplateTier.Premium, TemplateOrientation.Landscape, 99m,
        Guid.NewGuid(), null, Guid.NewGuid(), "Festivals", "festivals", null, null, null, ["en"], 0m, 0, false, 3, 0, 0, null, DateTimeOffset.UtcNow);
}
