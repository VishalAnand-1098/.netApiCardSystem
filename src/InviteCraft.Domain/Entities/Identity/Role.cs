using InviteCraft.Domain.Common;

namespace InviteCraft.Domain.Entities.Identity;

public class Role : Entity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = [];
}

/// <summary>Join entity between users and roles (composite key).</summary>
public class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }

    public User User { get; set; } = null!;
    public Role Role { get; set; } = null!;
}
