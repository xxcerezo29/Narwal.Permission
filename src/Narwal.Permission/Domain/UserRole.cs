namespace Narwal.Permission.Domain;

public sealed class UserRole<TUserId>
    where TUserId : notnull
{
    private UserRole()
    {
    }

    internal UserRole(TUserId userId, string roleCode)
    {
        UserId = userId ?? throw new ArgumentNullException(nameof(userId));
        RoleCode = RolePermissionCode.Normalize(roleCode);
    }

    public TUserId UserId { get; private set; } = default!;

    public string RoleCode { get; private set; } = null!;
}
