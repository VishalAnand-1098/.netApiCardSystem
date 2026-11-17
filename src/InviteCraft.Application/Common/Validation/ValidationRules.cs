using FluentValidation;

namespace InviteCraft.Application.Common.Validation;

public static class ValidationRules
{
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;

    /// <summary>At least 8 characters with an uppercase letter, a lowercase letter and a digit.</summary>
    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(PasswordMinLength).WithMessage($"Password must be at least {PasswordMinLength} characters.")
            .MaximumLength(PasswordMaxLength).WithMessage($"Password must be at most {PasswordMaxLength} characters.")
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a number.");

    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(256)
            .EmailAddress().WithMessage("Enter a valid email address.");

    public static IRuleBuilderOptions<T, string> PersonName<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithMessage("Full name is required.")
            .Must(n => n.Trim().Length >= 2).WithMessage("Full name must be at least 2 characters.")
            .MaximumLength(150);

    /// <summary>Optional phone: digits with optional leading +, 10–15 digits (e.g. +919876543210).</summary>
    public static IRuleBuilderOptions<T, string?> OptionalPhone<T>(this IRuleBuilder<T, string?> rule) =>
        rule
            .Must(phone => string.IsNullOrWhiteSpace(phone) || PhonePattern.IsMatch(phone.Trim()))
            .WithMessage("Enter a valid phone number, e.g. +919876543210.");

    private static readonly System.Text.RegularExpressions.Regex PhonePattern =
        new(@"^\+?[0-9]{10,15}$", System.Text.RegularExpressions.RegexOptions.Compiled);
}
