namespace InviteCraft.Domain.Common;

/// <summary>
/// Entity that is written once and never edited (tokens, logs, events, downloads).
/// <see cref="CreatedAt"/> is set by the EF Core SaveChanges interceptor.
/// </summary>
public abstract class CreationAuditedEntity : Entity
{
    public DateTimeOffset CreatedAt { get; set; }
}
