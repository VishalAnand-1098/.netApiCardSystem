namespace InviteCraft.Domain.Common;

/// <summary>
/// Base type for every entity with a surrogate key. Ids are UUID v7 (time-ordered) generated in the
/// application, which keeps PostgreSQL B-tree indexes compact and avoids a database round-trip on insert.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public override bool Equals(object? obj) =>
        obj is Entity other && other.GetType() == GetType() && other.Id == Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
