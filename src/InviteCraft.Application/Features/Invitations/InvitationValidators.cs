using FluentValidation;
using InviteCraft.Application.Common.Validation;
using InviteCraft.Application.Features.Templates;

namespace InviteCraft.Application.Features.Invitations;

/// <summary>Shape-level checks only. Whether a value fits its template field is decided by <see cref="FieldValueRules"/>.</summary>
internal sealed class CreateInvitationRequestValidator : AppValidator<CreateInvitationRequest>
{
    public CreateInvitationRequestValidator()
    {
        RuleFor(x => x.TemplateSlug).NotEmpty().WithMessage("Choose a template.").MaximumLength(220);
        RuleFor(x => x.Title).MaximumLength(150);
        RuleFor(x => x.Language).Matches(DesignRules.LanguageCode()).When(x => !string.IsNullOrWhiteSpace(x.Language))
            .WithMessage("Language must be an ISO code such as \"en\" or \"hi\".");
    }
}

internal sealed class SaveInvitationContentRequestValidator : AppValidator<SaveInvitationContentRequest>
{
    public const int MaxValues = 100;

    public SaveInvitationContentRequestValidator()
    {
        RuleFor(x => x.Title).MaximumLength(150);
        RuleFor(x => x.Language).Matches(DesignRules.LanguageCode()).When(x => !string.IsNullOrWhiteSpace(x.Language))
            .WithMessage("Language must be an ISO code such as \"en\" or \"hi\".");
        RuleFor(x => x.Values).NotNull()
            .Must(v => v.Count <= MaxValues).WithMessage($"At most {MaxValues} values per save.");

        RuleForEach(x => x.Values).Cascade(CascadeMode.Continue).ChildRules(value =>
        {
            value.RuleFor(v => v.FieldKey).Matches(DesignRules.FieldKey()).WithMessage("Invalid field key.");
            value.RuleFor(v => v.Value).MaximumLength(FieldValueRules.AbsoluteMaxLength + 100);
        });
    }
}
