using FluentValidation;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Common.Validation;
using InviteCraft.Application.Features.Auth;
using InviteCraft.Domain.Entities.Identity;

namespace InviteCraft.Application.Features.Account;

public sealed record UpdateProfileRequest(string FullName, string? Phone);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

internal sealed class UpdateProfileRequestValidator : AppValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FullName).PersonName();
        RuleFor(x => x.Phone).OptionalPhone();
    }
}

internal sealed class ChangePasswordRequestValidator : AppValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Current password is required.");
        RuleFor(x => x.NewPassword).StrongPassword()
            .NotEqual(x => x.CurrentPassword).WithMessage("New password must be different from the current password.");
    }
}

public interface IAccountService
{
    Task<Result<AuthUserDto>> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result<AuthUserDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>Changes the password, signs out every other session and returns a fresh session for this one.</summary>
    Task<Result<AuthResult>> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
}

internal sealed class AccountService(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    AuthSessionFactory sessions,
    IAuditLogger audit) : IAccountService
{
    public async Task<Result<AuthUserDto>> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdWithRolesAsync(userId, cancellationToken);
        return user is null ? AuthErrors.UserNotFound : user.ToAuthUserDto();
    }

    public async Task<Result<AuthUserDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdWithRolesAsync(userId, cancellationToken);
        if (user is null)
            return AuthErrors.UserNotFound;

        user.FullName = request.FullName.Trim();
        user.Phone = AuthMappings.NormalizePhone(request.Phone);
        audit.Log("ProfileUpdated", nameof(User), user.Id.ToString(), user.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return user.ToAuthUserDto();
    }

    public async Task<Result<AuthResult>> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdWithRolesAsync(userId, cancellationToken);
        if (user is null)
            return AuthErrors.UserNotFound;

        if (passwordHasher.Verify(user, user.PasswordHash, request.CurrentPassword) == PasswordCheck.Failed)
            return AuthErrors.WrongCurrentPassword;

        user.PasswordHash = passwordHasher.Hash(user, request.NewPassword);
        await sessions.RevokeAllAsync(user.Id, "PasswordChanged", cancellationToken);
        var (session, _) = await sessions.CreateAsync(user, cancellationToken: cancellationToken);

        audit.Log("PasswordChanged", nameof(User), user.Id.ToString(), user.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return session;
    }
}
