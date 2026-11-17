using FluentValidation;
using InviteCraft.Application.Common.Validation;

namespace InviteCraft.Application.Features.Auth;

internal sealed class RegisterRequestValidator : AppValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.FullName).PersonName();
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Password).StrongPassword();
        RuleFor(x => x.Phone).OptionalPhone();
    }
}

internal sealed class LoginRequestValidator : AppValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.").MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.").MaximumLength(ValidationRules.PasswordMaxLength);
    }
}

internal sealed class VerifyEmailRequestValidator : AppValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator() =>
        RuleFor(x => x.Token).NotEmpty().WithMessage("Verification token is required.").MaximumLength(200);
}

internal sealed class ForgotPasswordRequestValidator : AppValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() => RuleFor(x => x.Email).ValidEmail();
}

internal sealed class ResetPasswordRequestValidator : AppValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().WithMessage("Reset token is required.").MaximumLength(200);
        RuleFor(x => x.NewPassword).StrongPassword();
    }
}
