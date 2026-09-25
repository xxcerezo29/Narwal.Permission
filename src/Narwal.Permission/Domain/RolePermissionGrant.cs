namespace Narwal.Permission.Domain;

public sealed class RolePermissionGrant
{
    private RolePermissionGrant()
    {
    }

    internal RolePermissionGrant(string roleCode, string permissionCode)
    {
        RoleCode = RolePermissionCode.Normalize(roleCode);
        PermissionCode = RolePermissionCode.Normalize(permissionCode);
    }

    public string RoleCode { get; private set; } = null!;

    public string PermissionCode { get; private set; } = null!;
}
