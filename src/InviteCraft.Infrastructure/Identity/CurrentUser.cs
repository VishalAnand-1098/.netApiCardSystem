using System.Security.Claims;
using InviteCraft.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace InviteCraft.Infrastructure.Identity;

internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            // "sub" when inbound claim mapping is disabled (Phase 3), NameIdentifier otherwise.
            var raw = Principal?.FindFirst("sub")?.Value ?? Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public string? Email => Principal?.FindFirst("email")?.Value ?? Principal?.FindFirst(ClaimTypes.Email)?.Value;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString();
}
