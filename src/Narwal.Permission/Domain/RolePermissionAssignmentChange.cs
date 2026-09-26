using System.ComponentModel.DataAnnotations.Schema;

namespace Narwal.Permission.Domain;

/// <summary>Describes a user role or direct-permission assignment change emitted by an application aggregate.</summary>
[NotMapped]
public sealed class RolePermissionAssignmentChange<TUserId>
    where TUserId : notnull
{
    private RolePermissionAssignmentChange(
        string action,
        DateTimeOffset occurredAtUtc,
        bool hasActorUserId,
        TUserId? actorUserId,
        TUserId affectedUserId,
        string? roleCode,
        string? permissionCode)
    {
        Action = action;
        OccurredAtUtc = occurredAtUtc;
        HasActorUserId = hasActorUserId;
        ActorUserId = actorUserId;
        AffectedUserId = affectedUserId;
        RoleCode = roleCode;
        PermissionCode = permissionCode;
    }

    /// <summary>Gets the stable audit action code for this assignment change.</summary>
    public string Action { get; }

    /// <summary>Gets the UTC time at which the application aggregate made the change.</summary>
    public DateTimeOffset OccurredAtUtc { get; }

    /// <summary>Gets whether <see cref="ActorUserId"/> contains an actor ID.</summary>
    public bool HasActorUserId { get; }

    /// <summary>Gets the optional ID of the user who made the change.</summary>
    public TUserId? ActorUserId { get; }

    /// <summary>Gets the ID of the user whose assignments changed.</summary>
    public TUserId AffectedUserId { get; }

    /// <summary>Gets the role code for a role assignment change, when applicable.</summary>
    public string? RoleCode { get; }

    /// <summary>Gets the permission code for a direct-permission change, when applicable.</summary>
    public string? PermissionCode { get; }

    /// <summary>Creates an event for assigning a role to a user.</summary>
    public static RolePermissionAssignmentChange<TUserId> UserRoleAssigned(
        TUserId affectedUserId,
        string roleCode,
        DateTimeOffset occurredAtUtc,
        bool hasActorUserId = false,
        TUserId? actorUserId = default) =>
        Create(RolePermissionAuditActions.UserRoleAssigned, affectedUserId, roleCode, null,
            occurredAtUtc, hasActorUserId, actorUserId);

    /// <summary>Creates an event for removing a role from a user.</summary>
    public static RolePermissionAssignmentChange<TUserId> UserRoleRemoved(
        TUserId affectedUserId,
        string roleCode,
        DateTimeOffset occurredAtUtc,
        bool hasActorUserId = false,
        TUserId? actorUserId = default) =>
        Create(RolePermissionAuditActions.UserRoleRemoved, affectedUserId, roleCode, null,
            occurredAtUtc, hasActorUserId, actorUserId);

    /// <summary>Creates an event for granting a direct permission to a user.</summary>
    public static RolePermissionAssignmentChange<TUserId> UserPermissionGranted(
        TUserId affectedUserId,
        string permissionCode,
        DateTimeOffset occurredAtUtc,
        bool hasActorUserId = false,
        TUserId? actorUserId = default) =>
        Create(RolePermissionAuditActions.UserPermissionGranted, affectedUserId, null, permissionCode,
            occurredAtUtc, hasActorUserId, actorUserId);

    /// <summary>Creates an event for revoking a direct permission from a user.</summary>
    public static RolePermissionAssignmentChange<TUserId> UserPermissionRevoked(
        TUserId affectedUserId,
        string permissionCode,
        DateTimeOffset occurredAtUtc,
        bool hasActorUserId = false,
        TUserId? actorUserId = default) =>
        Create(RolePermissionAuditActions.UserPermissionRevoked, affectedUserId, null, permissionCode,
            occurredAtUtc, hasActorUserId, actorUserId);

    private static RolePermissionAssignmentChange<TUserId> Create(
        string action,
        TUserId affectedUserId,
        string? roleCode,
        string? permissionCode,
        DateTimeOffset occurredAtUtc,
        bool hasActorUserId,
        TUserId? actorUserId)
    {
        ArgumentNullException.ThrowIfNull(affectedUserId);
        if (hasActorUserId && actorUserId is null)
        {
            throw new ArgumentNullException(nameof(actorUserId));
        }

        return new RolePermissionAssignmentChange<TUserId>(
            action,
            occurredAtUtc.ToUniversalTime(),
            hasActorUserId,
            hasActorUserId ? actorUserId : default,
            affectedUserId,
            roleCode is null ? null : RolePermissionCode.Normalize(roleCode),
            permissionCode is null ? null : RolePermissionCode.Normalize(permissionCode));
    }
}
