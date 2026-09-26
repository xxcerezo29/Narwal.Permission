namespace Narwal.Permission.Domain;

/// <summary>Represents an immutable snapshot of a package-managed RBAC change.</summary>
public sealed class RolePermissionAuditEntry<TUserId>
    where TUserId : notnull
{
    private RolePermissionAuditEntry()
    {
    }

    internal RolePermissionAuditEntry(
        string action,
        DateTimeOffset occurredAtUtc,
        bool hasActorUserId,
        TUserId? actorUserId,
        bool hasAffectedUserId,
        TUserId? affectedUserId,
        string? roleCode,
        string? permissionCode,
        string? previousName,
        string? newName)
    {
        Action = action;
        OccurredAtUtc = occurredAtUtc;
        HasActorUserId = hasActorUserId;
        ActorUserId = actorUserId;
        HasAffectedUserId = hasAffectedUserId;
        AffectedUserId = affectedUserId;
        RoleCode = roleCode;
        PermissionCode = permissionCode;
        PreviousName = previousName;
        NewName = newName;
    }

    /// <summary>Gets the database-generated audit entry identifier.</summary>
    public long Id { get; private set; }

    /// <summary>Gets the stable action code for this change.</summary>
    public string Action { get; private set; } = null!;

    /// <summary>Gets the UTC timestamp at which the manager or application aggregate made the change.</summary>
    public DateTimeOffset OccurredAtUtc { get; private set; }

    /// <summary>Gets the optional user ID of the actor that initiated the change.</summary>
    public TUserId? ActorUserId { get; private set; }

    /// <summary>Gets whether <see cref="ActorUserId"/> contains an actor ID.</summary>
    public bool HasActorUserId { get; private set; }

    /// <summary>Gets the optional user ID affected by the change.</summary>
    public TUserId? AffectedUserId { get; private set; }

    /// <summary>Gets whether <see cref="AffectedUserId"/> contains an affected user ID.</summary>
    public bool HasAffectedUserId { get; private set; }

    /// <summary>Gets the role code snapshot for the change, when applicable.</summary>
    public string? RoleCode { get; private set; }

    /// <summary>Gets the permission code snapshot for the change, when applicable.</summary>
    public string? PermissionCode { get; private set; }

    /// <summary>Gets the previous display name snapshot, when applicable.</summary>
    public string? PreviousName { get; private set; }

    /// <summary>Gets the new display name snapshot, when applicable.</summary>
    public string? NewName { get; private set; }
}
