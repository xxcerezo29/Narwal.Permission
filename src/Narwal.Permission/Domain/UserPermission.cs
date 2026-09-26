namespace Narwal.Permission.Domain;

public sealed class UserPermission<TUserId>
    where TUserId : notnull
{
    private UserPermission()
    {
    }

    /// <summary>Creates an encapsulated direct-permission assignment for an application-owned user aggregate.</summary>
    public static UserPermission<TUserId> Create(TUserId userId, string permissionCode) =>
        new(userId, permissionCode);

    internal UserPermission(TUserId userId, string permissionCode)
    {
        UserId = userId ?? throw new ArgumentNullException(nameof(userId));
        PermissionCode = RolePermissionCode.Normalize(permissionCode);
    }

    public TUserId UserId { get; private set; } = default!;

    public string PermissionCode { get; private set; } = null!;
}
