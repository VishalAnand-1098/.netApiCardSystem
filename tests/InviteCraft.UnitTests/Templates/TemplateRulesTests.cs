using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Features.Categories;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Entities.Catalog;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;
using NSubstitute;

namespace InviteCraft.UnitTests.Templates;

public class TemplateRulesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TextStyle Style = new(null, 24, "#7A1F2B");

    // ------------------------------------------------------------------ design validation

    private static TemplateDesign Design(params DesignLayer[] layers) =>
        new(1, new DesignCanvas("#FFF8EE"), [new DesignPage(0, new DesignBackground(null, "#FFF8EE"), layers)]);

    [Fact]
    public void Valid_design_passes()
    {
        var design = Design(
            new DesignLayer("title", "text", new LayerFrame(0.1m, 0.1m, 0.8m, 0.1m), 1, Text: "॥ श्री गणेशाय नमः ॥", Style: Style),
            new DesignLayer("divider", "shape", new LayerFrame(0.3m, 0.25m, 0.4m, 0.005m), 1, Shape: "rect", Fill: "#B8925A"));

        new SaveTemplateDesignRequestValidator().Validate(new SaveTemplateDesignRequest(design)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Design_rejects_out_of_range_frames_missing_text_and_duplicate_ids()
    {
        var design = Design(
            new DesignLayer("a", "text", new LayerFrame(1.2m, 0, 0.5m, 0.1m), 1, Style: Style),          // x > 1, no text
            new DesignLayer("a", "image", new LayerFrame(0, 0, 0.5m, 0.1m), 1),                        // duplicate id, no asset
            new DesignLayer("Bad Id", "sparkle", new LayerFrame(0, 0, 0, 0.1m), 1));                     // bad id, type, w = 0

        var errors = new SaveTemplateDesignRequestValidator().Validate(new SaveTemplateDesignRequest(design)).Errors
            .Select(e => e.ErrorMessage).ToList();

        errors.ShouldContain(m => m.Contains("unique"));
        errors.ShouldContain(m => m.Contains("Text layers need text"));
        errors.ShouldContain(m => m.Contains("Image layers need an assetId"));
        errors.ShouldContain(m => m.Contains("Layer type must be"));
        errors.ShouldContain(m => m.Contains("Layer id must be"));
        errors.Count(m => m.Contains("between 0 and 1") || m.Contains("greater than")).ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Design_pages_must_be_numbered_without_gaps()
    {
        var design = new TemplateDesign(1, new DesignCanvas(null), [new DesignPage(0, null, []), new DesignPage(2, null, [])]);

        new SaveTemplateDesignRequestValidator().Validate(new SaveTemplateDesignRequest(design)).Errors
            .ShouldContain(e => e.ErrorMessage.Contains("without gaps"));
    }

    [Fact]
    public void Field_validator_requires_style_for_text_and_options_for_select()
    {
        var fields = new SaveTemplateFieldsRequest([
            Field("bride_name") with { Style = null },
            Field("rsvp_choice") with { FieldType = EditableFieldType.Select, Options = [] },
            Field("Bad-Key"),
        ]);

        var errors = new SaveTemplateFieldsRequestValidator().Validate(fields).Errors.Select(e => e.ErrorMessage).ToList();

        errors.ShouldContain(m => m.Contains("need a style"));
        errors.ShouldContain(m => m.Contains("at least one option"));
        errors.ShouldContain(m => m.Contains("snake_case"));
    }

    // ------------------------------------------------------------------ publish readiness

    [Fact]
    public void Empty_draft_lists_every_missing_piece()
    {
        var template = NewTemplate(TemplateTier.Premium);

        var issues = PublishReadiness.Check(template);

        issues.ShouldContain(i => i.Contains("thumbnail"));
        issues.ShouldContain(i => i.Contains("editable field"));
        issues.ShouldContain(i => i.Contains("font"));
        issues.ShouldContain(i => i.Contains("price"));
    }

    [Fact]
    public void Complete_template_is_ready()
    {
        var template = NewTemplate(TemplateTier.Premium);
        var font = new Font { FamilyName = "Cormorant Garamond", DisplayName = "Cormorant", IsActive = true };
        template.Assets.Add(new TemplateAsset { AssetType = TemplateAssetType.Thumbnail });
        template.FontConfigurations.Add(new TemplateFontConfiguration { FontId = font.Id, Font = font, IsDefault = true });
        template.EditableFields.Add(new TemplateEditableField { FieldKey = "bride_name", IsActive = true, Style = TemplateJson.Serialize(Style) });
        template.Pricing.Add(new TemplatePricing { IsActive = true, Price = 249, Package = new Package { Code = Package.PremiumCode } });

        PublishReadiness.Check(template).ShouldBeEmpty();
    }

    [Fact]
    public void Font_used_by_a_field_must_be_in_the_template_font_list()
    {
        var template = NewTemplate(TemplateTier.Free);
        var listed = new Font { IsActive = true };
        template.Assets.Add(new TemplateAsset { AssetType = TemplateAssetType.Thumbnail });
        template.FontConfigurations.Add(new TemplateFontConfiguration { FontId = listed.Id, Font = listed, IsDefault = true });
        template.EditableFields.Add(new TemplateEditableField
        {
            FieldKey = "name", IsActive = true, Style = TemplateJson.Serialize(Style with { FontId = Guid.NewGuid() }),
        });

        PublishReadiness.Check(template).ShouldContain(i => i.Contains("every font used"));
    }

    // ------------------------------------------------------------------ service rules

    private readonly ITemplateRepository _templates = Substitute.For<ITemplateRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private AdminTemplateService Service() => new(
        _templates, Substitute.For<ICategoryRepository>(), Substitute.For<ISubCategoryRepository>(), _unitOfWork,
        new TemplateMapper(Substitute.For<IFileStorage>()), Substitute.For<IAuditLogger>(), TimeProvider.System);

    [Fact]
    public async Task Premium_template_cannot_be_priced_with_the_free_package()
    {
        var template = NewTemplate(TemplateTier.Premium);
        var free = new Package { Code = Package.FreeCode, IsActive = true };
        _templates.GetAggregateAsync(template.Id, true, Arg.Any<CancellationToken>()).Returns(template);
        _templates.GetActivePackagesAsync(Arg.Any<CancellationToken>()).Returns([free]);

        var result = await Service().SavePricingAsync(template.Id,
            new SaveTemplatePricingRequest([new TemplatePriceInput(free.Id, 0, null)]), Ct);

        result.FirstError.ShouldBe(TemplateErrors.FreePackageOnPremium);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Saving_prices_sets_the_starting_price_to_the_cheapest_active_one()
    {
        var template = NewTemplate(TemplateTier.Premium);
        var basic = new Package { Code = Package.BasicCode, IsActive = true };
        var luxury = new Package { Code = Package.LuxuryCode, IsActive = true };
        _templates.GetAggregateAsync(template.Id, true, Arg.Any<CancellationToken>()).Returns(template);
        _templates.GetActivePackagesAsync(Arg.Any<CancellationToken>()).Returns([basic, luxury]);

        await Service().SavePricingAsync(template.Id, new SaveTemplatePricingRequest([
            new TemplatePriceInput(basic.Id, 149, 199),
            new TemplatePriceInput(luxury.Id, 499, null),
        ]), Ct);

        template.StartingPrice.ShouldBe(149m);
        template.Pricing.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Removing_a_field_used_by_invitations_deactivates_it_instead_of_deleting()
    {
        var template = NewTemplate(TemplateTier.Free);
        var used = new TemplateEditableField { FieldKey = "groom_name", IsActive = true, Style = TemplateJson.Serialize(Style) };
        var unused = new TemplateEditableField { FieldKey = "old_note", IsActive = true, Style = TemplateJson.Serialize(Style) };
        template.EditableFields.Add(used);
        template.EditableFields.Add(unused);
        _templates.GetAggregateAsync(template.Id, true, Arg.Any<CancellationToken>()).Returns(template);
        _templates.GetFieldIdsWithValuesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { used.Id });

        await Service().SaveFieldsAsync(template.Id, new SaveTemplateFieldsRequest([Field("bride_name")]), Ct);

        used.IsActive.ShouldBeFalse();
        template.EditableFields.ShouldContain(used);
        template.EditableFields.ShouldNotContain(unused);
        template.EditableFields.ShouldContain(f => f.FieldKey == "bride_name");
    }

    [Fact]
    public async Task Design_may_only_reference_this_templates_assets()
    {
        var template = NewTemplate(TemplateTier.Free);
        _templates.GetAggregateAsync(template.Id, true, Arg.Any<CancellationToken>()).Returns(template);
        var design = new TemplateDesign(1, new DesignCanvas(null),
            [new DesignPage(0, new DesignBackground(Guid.NewGuid(), null), [])]);

        var result = await Service().SaveDesignAsync(template.Id, new SaveTemplateDesignRequest(design), Ct);

        result.FirstError!.Code.ShouldBe("Template.UnknownAssets");
    }

    // ------------------------------------------------------------------ helpers

    private static TemplateFieldInput Field(string key) => new(
        key, "Label", null, EditableFieldType.Text, "Couple", true, 40, null, null, null, null, 0,
        new LayerFrame(0.1m, 0.4m, 0.8m, 0.07m), 10, Style, null, 0);

    private static Template NewTemplate(TemplateTier tier) => new()
    {
        Name = "Royal Maroon Wedding",
        Tier = tier,
        Design = TemplateJson.Serialize(TemplateDesign.Empty()),
        Category = new Category { Name = "Wedding", Slug = "wedding", IsActive = true },
    };
}
