using InviteCraft.Api.Contracts;
using InviteCraft.Application.Common.Settings;
using InviteCraft.Application.Features.Auth;

namespace InviteCraft.Api.Infrastructure;

/// <summary>
/// The refresh token lives only in this HttpOnly cookie, scoped to the auth endpoints, so page scripts
/// (and any XSS) can never read it. SameSite=Strict blocks cross-site requests from sending it.
/// </summary>
internal static class RefreshTokenCookie
{
    public const string Name = "ic_refresh";
    public const string Path = "/" + ApiRoutes.V1 + "/auth";

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    public static void Write(HttpResponse response, AuthResult session, AuthSettings settings) =>
        response.Cookies.Append(Name, session.RefreshToken, Options(settings, session.RefreshTokenExpiresAt));

    public static void Delete(HttpResponse response, AuthSettings settings) =>
        response.Cookies.Delete(Name, Options(settings, expires: null));

    private static CookieOptions Options(AuthSettings settings, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = settings.RefreshCookieSecure,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expires,
        IsEssential = true,
    };
}
