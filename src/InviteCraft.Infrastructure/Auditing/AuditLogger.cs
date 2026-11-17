using System.Diagnostics;
using System.Net;
using System.Text.Json;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Domain.Entities.Platform;
using InviteCraft.Infrastructure.Persistence;

namespace InviteCraft.Infrastructure.Auditing;

/// <summary>Adds an audit_logs row to the current unit of work (persisted by the caller's SaveChanges).</summary>
internal sealed class AuditLogger(AppDbContext context, ICurrentUser currentUser) : IAuditLogger
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Log(string action, string entityType, string? entityId = null, Guid? userId = null, object? details = null)
    {
        context.AuditLogs.Add(new AuditLog
        {
            UserId = userId ?? currentUser.UserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            NewValues = details is null ? null : JsonSerializer.Serialize(details, Json),
            IpAddress = IPAddress.TryParse(currentUser.IpAddress, out var ip) ? ip : null,
            UserAgent = Truncate(currentUser.UserAgent, 512),
            CorrelationId = Activity.Current?.TraceId.ToString(),
        });
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
