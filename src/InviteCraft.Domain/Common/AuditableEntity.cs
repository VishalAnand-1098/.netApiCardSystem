namespace InviteCraft.Domain.Common;

/// <summary>
/// Entity with creation and modification metadata. Values are set by the EF Core
/// SaveChanges interceptor, never by application code.
/// </summary>
public abstract class AuditableEntity : CreationAuditedEntity
{
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>
/// Auditable entity that is hidden instead of physically deleted. Calling
/// <c>DbContext.Remove()</c> on it is converted to a soft delete by the interceptor,
/// and a global query filter excludes deleted rows from every query.
/// </summary>
public abstract class SoftDeletableEntity : AuditableEntity, ISoftDeletable
{
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }

    public void Restore()
    {
        IsDeleted = false;
        DeletedAt = null;
    }
}
