using Narwal.Permission.Domain;

namespace Narwal.Permission.Tests.Domain;

public sealed class RolePermissionAuditEntryTests
{
    [Fact]
    public void Guid_actor_id_uses_an_explicit_presence_flag_and_action_codes_are_stable()
    {
        var actorProperty = typeof(RolePermissionAuditEntry<Guid>)
            .GetProperty(nameof(RolePermissionAuditEntry<Guid>.ActorUserId));
        var hasActorProperty = typeof(RolePermissionAuditEntry<Guid>)
            .GetProperty(nameof(RolePermissionAuditEntry<Guid>.HasActorUserId));

        Assert.Equal(typeof(Guid), actorProperty!.PropertyType);
        Assert.Equal(typeof(bool), hasActorProperty!.PropertyType);
        Assert.Equal(
            new[]
            {
                "role.created", "role.renamed", "role.deleted",
                "permission.created", "permission.renamed", "permission.deleted",
                "role.permission_granted", "role.permission_revoked",
                "user.role_assigned", "user.role_removed",
                "user.permission_granted", "user.permission_revoked"
            },
            new[]
            {
                RolePermissionAuditActions.RoleCreated,
                RolePermissionAuditActions.RoleRenamed,
                RolePermissionAuditActions.RoleDeleted,
                RolePermissionAuditActions.PermissionCreated,
                RolePermissionAuditActions.PermissionRenamed,
                RolePermissionAuditActions.PermissionDeleted,
                RolePermissionAuditActions.RolePermissionGranted,
                RolePermissionAuditActions.RolePermissionRevoked,
                RolePermissionAuditActions.UserRoleAssigned,
                RolePermissionAuditActions.UserRoleRemoved,
                RolePermissionAuditActions.UserPermissionGranted,
                RolePermissionAuditActions.UserPermissionRevoked
            });
        Assert.Null(typeof(RolePermissionAuditEntry<Guid>).GetConstructor(Type.EmptyTypes));
        Assert.False(actorProperty.GetSetMethod(nonPublic: true)!.IsPublic);
        Assert.False(hasActorProperty.GetSetMethod(nonPublic: true)!.IsPublic);
    }
}
