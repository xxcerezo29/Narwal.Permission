namespace Narwal.Permission.Domain;

public sealed class UserPermission<TUserId>
    where TUserId : notnull
{
    private UserPermission()
    {
    }

    internal UserPermission(TUserId userId, string permissionCode)
    {
        UserId = userId ?? throw new ArgumentNullException(nameof(userId));
        PermissionCode = RolePermissionCode.Normalize(permissionCode);
    }

    public TUserId UserId { get; private set; } = default!;

    public string PermissionCode { get; private set; } = null!;
}
