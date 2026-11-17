namespace InviteCraft.Application.Common.Interfaces;

/// <summary>
/// The caller of the current request. Implemented in Infrastructure from the HTTP context,
/// so application services never depend on ASP.NET Core types.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    string? IpAddress { get; }
    string? UserAgent { get; }
}
