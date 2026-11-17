using InviteCraft.Application.Features.Invitations;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;

namespace InviteCraft.UnitTests.Invitations;

public class FieldValueRulesTests
{
    private static TemplateEditableField Field(EditableFieldType type, int? maxLength = null, string[]? options = null, decimal fontSize = 24) => new()
    {
        FieldKey = "f",
        FieldType = type,
        MaxLength = maxLength,
        Options = options is null ? null : TemplateJson.Serialize(options),
        X = 0.1m, Y = 0.4m, Width = 0.8m, Height = 0.1m,
        Style = TemplateJson.Serialize(new TextStyle(null, fontSize, "#112233")),
    };

    [Theory]
    [InlineData(EditableFieldType.Date, "2027-02-14", true)]
    [InlineData(EditableFieldType.Date, "14/02/2027", false)]
    [InlineData(EditableFieldType.Date, "2027-02-30", false)]
    [InlineData(EditableFieldType.Date, "1850-01-01", false)]
    [InlineData(EditableFieldType.Time, "18:30", true)]
    [InlineData(EditableFieldType.Time, "25:00", false)]
    [InlineData(EditableFieldType.Time, "6:30 PM", false)]
    [InlineData(EditableFieldType.Phone, "+91 98765 43210", true)]
    [InlineData(EditableFieldType.Phone, "98765-43210", true)]
    [InlineData(EditableFieldType.Phone, "call me", false)]
    [InlineData(EditableFieldType.Email, "priya@example.com", true)]
    [InlineData(EditableFieldType.Email, "priya@", false)]
    [InlineData(EditableFieldType.Number, "5", true)]
    [InlineData(EditableFieldType.Number, "5.5", true)]
    [InlineData(EditableFieldType.Number, "five", false)]
    public void Typed_fields_accept_only_well_formed_values(EditableFieldType type, string input, bool valid)
    {
        var (value, error) = FieldValueRules.CheckValue(Field(type), input);

        (error is null).ShouldBe(valid, error);
        if (valid) value.ShouldBe(input);
    }

    [Fact]
    public void Empty_input_is_valid_and_becomes_null()
    {
        FieldValueRules.CheckValue(Field(EditableFieldType.Date), "   ").ShouldBe((null, null));
        FieldValueRules.CheckValue(Field(EditableFieldType.Text), null).ShouldBe((null, null));
    }

    [Fact]
    public void Select_values_must_be_one_of_the_options()
    {
        var field = Field(EditableFieldType.Select, options: ["Vegetarian", "Non-vegetarian"]);

        FieldValueRules.CheckValue(field, "Vegetarian").Error.ShouldBeNull();
        FieldValueRules.CheckValue(field, "Vegan").Error.ShouldNotBeNull();
    }

    [Fact]
    public void Max_length_is_enforced_and_capped_at_the_absolute_limit()
    {
        FieldValueRules.CheckValue(Field(EditableFieldType.Text, maxLength: 5), "abcde").Error.ShouldBeNull();
        FieldValueRules.CheckValue(Field(EditableFieldType.Text, maxLength: 5), "abcdef").Error!.ShouldContain("at most 5");
        FieldValueRules.CheckValue(Field(EditableFieldType.MultilineText, maxLength: 100_000), new string('x', 4001)).Error.ShouldNotBeNull();
    }

    [Fact]
    public void Single_line_fields_flatten_newlines_but_multiline_fields_keep_them()
    {
        FieldValueRules.CheckValue(Field(EditableFieldType.Text), "Ananya\r\nSharma").Value.ShouldBe("Ananya Sharma");
        FieldValueRules.CheckValue(Field(EditableFieldType.MultilineText), "Line 1\r\nLine 2\rLine 3").Value.ShouldBe("Line 1\nLine 2\nLine 3");
    }

    [Fact]
    public void Control_characters_are_rejected()
    {
        FieldValueRules.CheckValue(Field(EditableFieldType.Text), "bad\u0007bell").Error.ShouldNotBeNull();
        FieldValueRules.CheckValue(Field(EditableFieldType.Text), "tab\there").Error.ShouldNotBeNull();
    }

    [Fact]
    public void Unicode_is_normalised_to_NFC_so_equivalent_sequences_store_identically()
    {
        var decomposed = "é";   // e + combining acute
        var composed = "é";      // é

        FieldValueRules.CheckValue(Field(EditableFieldType.Text), decomposed).Value.ShouldBe(composed);
        // Devanagari, Tamil and Urdu pass through untouched.
        FieldValueRules.CheckValue(Field(EditableFieldType.Text), "दुल्हन").Value.ShouldBe("दुल्हन");
        FieldValueRules.CheckValue(Field(EditableFieldType.Text), "ஆனந்த்").Value.ShouldBe("ஆனந்த்");
        FieldValueRules.CheckValue(Field(EditableFieldType.Text), "زارا").Value.ShouldBe("زارا");
    }

    [Fact]
    public void Image_value_must_be_an_asset_id()
    {
        var id = Guid.NewGuid().ToString();

        FieldValueRules.CheckValue(Field(EditableFieldType.Image), id).Value.ShouldBe(id);
        FieldValueRules.CheckValue(Field(EditableFieldType.Image), "http://evil.example/x.png").Error.ShouldNotBeNull();
    }

    // ------------------------------------------------------------------ style permissions

    private static readonly Guid FontA = Guid.NewGuid();
    private static readonly IReadOnlySet<Guid> TemplateFonts = new HashSet<Guid> { FontA };

    [Fact]
    public void Allowed_style_changes_pass_and_are_normalised()
    {
        var style = new FieldStyleOverride(FontA, 30.04m, "#AABBCC", "left");

        FieldValueRules.CheckStyle(style, Field(EditableFieldType.Text), FieldPermissions.Default, TemplateFonts).ShouldBeNull();
        FieldValueRules.NormaliseStyle(style).ShouldBe(new FieldStyleOverride(FontA, 30.0m, "#aabbcc", "left"));
    }

    [Theory]
    [InlineData(false, true, true, true, "font")]
    [InlineData(true, false, true, true, "size")]
    [InlineData(true, true, false, true, "colour")]
    [InlineData(true, true, true, false, "alignment")]
    public void Each_style_key_is_blocked_when_its_permission_is_off(bool font, bool size, bool color, bool align, string expected)
    {
        var permissions = new FieldPermissions(font, size, color, align, false);
        var style = new FieldStyleOverride(FontA, 24, "#112233", "center");

        FieldValueRules.CheckStyle(style, Field(EditableFieldType.Text), permissions, TemplateFonts)!.ShouldContain(expected);
    }

    [Fact]
    public void Fonts_outside_the_template_list_and_out_of_range_sizes_and_bad_colours_are_rejected()
    {
        var field = Field(EditableFieldType.Text, fontSize: 24);

        FieldValueRules.CheckStyle(new FieldStyleOverride(Guid.NewGuid(), null, null, null), field, FieldPermissions.Default, TemplateFonts).ShouldNotBeNull();
        FieldValueRules.CheckStyle(new FieldStyleOverride(null, 5, null, null), field, FieldPermissions.Default, TemplateFonts).ShouldNotBeNull();   // < 50 % of 24
        FieldValueRules.CheckStyle(new FieldStyleOverride(null, 60, null, null), field, FieldPermissions.Default, TemplateFonts).ShouldNotBeNull();  // > 200 % of 24
        FieldValueRules.CheckStyle(new FieldStyleOverride(null, 36, null, null), field, FieldPermissions.Default, TemplateFonts).ShouldBeNull();
        FieldValueRules.CheckStyle(new FieldStyleOverride(null, null, "red", null), field, FieldPermissions.Default, TemplateFonts).ShouldNotBeNull();
        FieldValueRules.CheckStyle(new FieldStyleOverride(null, null, null, "diagonal"), field, FieldPermissions.Default, TemplateFonts).ShouldNotBeNull();
    }

    [Fact]
    public void Image_fields_cannot_be_restyled_and_empty_overrides_are_ignored()
    {
        FieldValueRules.CheckStyle(new FieldStyleOverride(null, null, "#112233", null), Field(EditableFieldType.Image), FieldPermissions.Default, TemplateFonts).ShouldNotBeNull();
        FieldValueRules.CheckStyle(new FieldStyleOverride(null, null, null, null), Field(EditableFieldType.Image), FieldPermissions.Default, TemplateFonts).ShouldBeNull();
        FieldValueRules.NormaliseStyle(new FieldStyleOverride(null, null, null, null)).ShouldBeNull();
    }

    [Fact]
    public void Position_needs_the_move_permission_and_must_stay_inside_the_card()
    {
        var field = Field(EditableFieldType.Text);   // frame 0.1, 0.4, 0.8 × 0.1
        var movable = new FieldPermissions(true, true, true, true, true);

        FieldValueRules.CheckPosition(new FieldPosition(0.1m, 0.5m), field, FieldPermissions.Default).ShouldNotBeNull();
        FieldValueRules.CheckPosition(new FieldPosition(0.1m, 0.5m), field, movable).ShouldBeNull();
        FieldValueRules.CheckPosition(new FieldPosition(0.3m, 0.5m), field, movable).ShouldNotBeNull();   // 0.3 + 0.8 > 1
        FieldValueRules.CheckPosition(new FieldPosition(0.1m, 0.95m), field, movable).ShouldNotBeNull();  // 0.95 + 0.1 > 1
        FieldValueRules.CheckPosition(new FieldPosition(-0.1m, 0.5m), field, movable).ShouldNotBeNull();
        FieldValueRules.CheckPosition(null, field, FieldPermissions.Default).ShouldBeNull();
    }
}
