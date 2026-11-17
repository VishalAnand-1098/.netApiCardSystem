using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Settings;
using InviteCraft.Application.Features.Auth;
using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace InviteCraft.UnitTests.Auth;

public class AuthServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IUserTokenRepository _userTokens = Substitute.For<IUserTokenRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ISecureTokenGenerator _tokens = Substitute.For<ISecureTokenGenerator>();
    private readonly IAccessTokenService _accessTokens = Substitute.For<IAccessTokenService>();
    private readonly AuthSettings _settings = new() { MaxFailedLoginAttempts = 3, LockoutMinutes = 15, RefreshReuseGraceSeconds = 30 };
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        var time = new FixedTimeProvider(Now);
        _tokens.Generate().Returns(("raw-token", "hashed-token"));
        _tokens.Hash(Arg.Any<string>()).Returns(ci => "hash:" + ci.Arg<string>());
        _accessTokens.Create(Arg.Any<User>(), Arg.Any<IReadOnlyCollection<string>>()).Returns(new AccessToken("jwt", Now.AddMinutes(15)));

        var sessions = new AuthSessionFactory(_accessTokens, _tokens, _refreshTokens, Substitute.For<ICurrentUser>(), time, Options.Create(_settings));
        _sut = new AuthService(_users, _refreshTokens, _userTokens, _unitOfWork, _hasher, _tokens, sessions,
            Substitute.For<IEmailSender>(), Substitute.For<IAuditLogger>(), time, Options.Create(_settings), NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task Login_with_unknown_email_returns_generic_invalid_credentials()
    {
        _users.GetByEmailWithRolesAsync("nobody@example.com", Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _sut.LoginAsync(new LoginRequest("Nobody@Example.com ", "whatever"), TestContext.Current.CancellationToken);

        result.FirstError.ShouldBe(AuthErrors.InvalidCredentials);
        _hasher.Received(1).Verify(Arg.Any<User>(), Arg.Any<string>(), "whatever"); // constant-time path
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account()
    {
        var user = CustomerUser();
        _users.GetByEmailWithRolesAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify(user, user.PasswordHash, "wrong").Returns(PasswordCheck.Failed);
        var ct = TestContext.Current.CancellationToken;

        (await _sut.LoginAsync(new LoginRequest(user.Email, "wrong"), ct)).FirstError.ShouldBe(AuthErrors.InvalidCredentials);
        (await _sut.LoginAsync(new LoginRequest(user.Email, "wrong"), ct)).FirstError.ShouldBe(AuthErrors.InvalidCredentials);
        var third = await _sut.LoginAsync(new LoginRequest(user.Email, "wrong"), ct);

        third.FirstError!.Code.ShouldBe("Auth.LockedOut");
        user.LockoutEnd.ShouldBe(Now.AddMinutes(15));

        // Even the correct password is refused while locked.
        _hasher.Verify(user, user.PasswordHash, "Correct1").Returns(PasswordCheck.Success);
        (await _sut.LoginAsync(new LoginRequest(user.Email, "Correct1"), ct)).FirstError!.Code.ShouldBe("Auth.LockedOut");
    }

    [Fact]
    public async Task Successful_login_resets_failures_and_issues_a_session()
    {
        var user = CustomerUser();
        user.FailedLoginCount = 2;
        _users.GetByEmailWithRolesAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify(user, user.PasswordHash, "Correct1").Returns(PasswordCheck.Success);

        var result = await _sut.LoginAsync(new LoginRequest(user.Email, "Correct1"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AccessToken.ShouldBe("jwt");
        result.Value.RefreshToken.ShouldBe("raw-token");
        result.Value.User.Roles.ShouldBe([RoleNames.Customer]);
        user.FailedLoginCount.ShouldBe(0);
        user.LastLoginAt.ShouldBe(Now);
        await _refreshTokens.Received(1).AddAsync(Arg.Is<RefreshToken>(t => t.TokenHash == "hashed-token"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_rotates_the_token_within_the_same_family()
    {
        var stored = ActiveToken(CustomerUser());
        _refreshTokens.GetByHashWithUserAsync("hash:old", Arg.Any<CancellationToken>()).Returns(stored);

        var result = await _sut.RefreshAsync("old", TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        stored.RevokedReason.ShouldBe("Rotated");
        stored.ReplacedByTokenId.ShouldNotBeNull();
        await _refreshTokens.Received(1).AddAsync(Arg.Is<RefreshToken>(t => t.FamilyId == stored.FamilyId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reusing_a_rotated_token_revokes_the_whole_family()
    {
        var stored = ActiveToken(CustomerUser());
        stored.RevokedAt = Now.AddMinutes(-5);          // rotated 5 minutes ago — outside the grace window
        stored.ReplacedByTokenId = Guid.NewGuid();
        var sibling = ActiveToken(stored.User, stored.FamilyId);
        _refreshTokens.GetByHashWithUserAsync("hash:stolen", Arg.Any<CancellationToken>()).Returns(stored);
        _refreshTokens.GetActiveByFamilyAsync(stored.FamilyId, Now, Arg.Any<CancellationToken>()).Returns([sibling]);

        var result = await _sut.RefreshAsync("stolen", TestContext.Current.CancellationToken);

        result.FirstError.ShouldBe(AuthErrors.InvalidRefreshToken);
        sibling.RevokedAt.ShouldBe(Now);
        sibling.RevokedReason.ShouldBe("ReuseDetected");
    }

    [Fact]
    public async Task Reuse_within_grace_window_is_treated_as_a_benign_race()
    {
        var stored = ActiveToken(CustomerUser());
        stored.RevokedAt = Now.AddSeconds(-5);           // two tabs refreshing at the same time
        stored.ReplacedByTokenId = Guid.NewGuid();
        _refreshTokens.GetByHashWithUserAsync("hash:old", Arg.Any<CancellationToken>()).Returns(stored);

        var result = await _sut.RefreshAsync("old", TestContext.Current.CancellationToken);

        result.FirstError.ShouldBe(AuthErrors.InvalidRefreshToken);
        await _refreshTokens.DidNotReceive().GetActiveByFamilyAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Forgot_password_for_unknown_email_still_succeeds()
    {
        _users.GetByEmailWithRolesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _sut.ForgotPasswordAsync(new ForgotPasswordRequest("ghost@example.com"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static User CustomerUser()
    {
        var role = new Role { Name = RoleNames.Customer };
        var user = new User { Email = "priya@example.com", FullName = "Priya Sharma", PasswordHash = "hash", IsActive = true };
        user.UserRoles.Add(new UserRole { User = user, Role = role, RoleId = role.Id, UserId = user.Id });
        return user;
    }

    private static RefreshToken ActiveToken(User user, Guid? familyId = null) => new()
    {
        User = user,
        UserId = user.Id,
        TokenHash = "hash",
        FamilyId = familyId ?? Guid.NewGuid(),
        ExpiresAt = Now.AddDays(7),
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
