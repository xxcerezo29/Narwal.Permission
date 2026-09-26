namespace Narwal.Permission.Domain;

/// <summary>Stable action codes recorded for package-managed role-permission changes.</summary>
public static class RolePermissionAuditActions
{
    public const string RoleCreated = "role.created";
    public const string RoleRenamed = "role.renamed";
    public const string RoleDeleted = "role.deleted";
    public const string PermissionCreated = "permission.created";
    public const string PermissionRenamed = "permission.renamed";
    public const string PermissionDeleted = "permission.deleted";
    public const string RolePermissionGranted = "role.permission_granted";
    public const string RolePermissionRevoked = "role.permission_revoked";
    public const string UserRoleAssigned = "user.role_assigned";
    public const string UserRoleRemoved = "user.role_removed";
    public const string UserPermissionGranted = "user.permission_granted";
    public const string UserPermissionRevoked = "user.permission_revoked";
}
