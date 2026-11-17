namespace InviteCraft.Domain.Common;

/// <summary>
/// Marks entities that are hidden instead of physically deleted.
/// A global EF Core query filter excludes rows where <see cref="IsDeleted"/> is true.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }
    DateTimeOffset? DeletedAt { get; }
    void MarkDeleted(DateTimeOffset at);
    void Restore();
}
