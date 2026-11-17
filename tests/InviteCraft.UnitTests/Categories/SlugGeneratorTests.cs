using InviteCraft.Application.Common.Text;

namespace InviteCraft.UnitTests.Categories;

public class SlugGeneratorTests
{
    [Theory]
    [InlineData("Griha Pravesh", "griha-pravesh")]
    [InlineData("  Save the Date!  ", "save-the-date")]
    [InlineData("Haldi & Mehndi", "haldi-mehndi")]
    [InlineData("Café Noël", "cafe-noel")]
    [InlineData("New Year 2027", "new-year-2027")]
    [InlineData("Kids' Birthday -- Party", "kids-birthday-party")]
    public void Generates_clean_ascii_slugs(string input, string expected) =>
        SlugGenerator.Generate(input).ShouldBe(expected);

    [Theory]
    [InlineData("दिवाली")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Returns_empty_when_no_latin_letters_or_digits(string? input) =>
        SlugGenerator.Generate(input).ShouldBeEmpty();

    [Fact]
    public void Truncates_to_max_length_without_trailing_hyphen()
    {
        var slug = SlugGenerator.Generate(string.Join(' ', Enumerable.Repeat("celebration", 20)));

        slug.Length.ShouldBeLessThanOrEqualTo(SlugGenerator.MaxLength);
        slug.ShouldNotEndWith("-");
        SlugGenerator.IsValid(slug).ShouldBeTrue();
    }

    [Theory]
    [InlineData("diwali", true)]
    [InlineData("save-the-date", true)]
    [InlineData("Diwali", false)]
    [InlineData("-diwali", false)]
    [InlineData("diwali--2026", false)]
    [InlineData("diwali_2026", false)]
    [InlineData("", false)]
    public void Validates_slug_format(string slug, bool valid) =>
        SlugGenerator.IsValid(slug).ShouldBe(valid);
}
