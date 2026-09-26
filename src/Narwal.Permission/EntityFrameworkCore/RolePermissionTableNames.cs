namespace Narwal.Permission.EntityFrameworkCore;

/// <summary>
/// Configures the table names used by the role and permission entities.
/// </summary>
public sealed class RolePermissionTableNames
{
    /// <summary>Gets or sets the roles table name.</summary>
    public string Roles { get; init; } = "NarwalRoles";

    /// <summary>Gets or sets the permissions table name.</summary>
    public string Permissions { get; init; } = "NarwalPermissions";

    /// <summary>Gets or sets the role-permissions join table name.</summary>
    public string RolePermissions { get; init; } = "NarwalRolePermissions";

    /// <summary>Gets or sets the user-roles assignment table name.</summary>
    public string UserRoles { get; init; } = "NarwalUserRoles";

    /// <summary>Gets or sets the user-permissions assignment table name.</summary>
    public string UserPermissions { get; init; } = "NarwalUserPermissions";

    /// <summary>Gets or sets the RBAC audit history table name.</summary>
    public string AuditEntries { get; init; } = "NarwalPermissionAuditEntries";

    internal void Validate()
    {
        ValidateName(Roles, nameof(Roles));
        ValidateName(Permissions, nameof(Permissions));
        ValidateName(RolePermissions, nameof(RolePermissions));
        ValidateName(UserRoles, nameof(UserRoles));
        ValidateName(UserPermissions, nameof(UserPermissions));
        ValidateName(AuditEntries, nameof(AuditEntries));
    }

    private static void ValidateName(string? name, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                $"The {propertyName} table name cannot be null, empty, or whitespace.",
                "tableNames");
        }
    }
}
