using FluentValidation;

namespace InviteCraft.Application.Common.Validation;

/// <summary>
/// Base for all request validators: stops at the first failing rule of each property,
/// so a field shows one clear message ("Name is required.") instead of a list.
/// </summary>
public abstract class AppValidator<T> : AbstractValidator<T>
{
    protected AppValidator() => RuleLevelCascadeMode = CascadeMode.Stop;
}
