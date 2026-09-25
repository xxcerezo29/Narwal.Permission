namespace Narwal.Permission.Services;

/// <summary>Checks role membership and direct or role-inherited permission grants.</summary>
public interface IPermissionChecker<TUserId>
    where TUserId : notnull
{
    Task<bool> HasRoleAsync(TUserId userId, string roleCode, CancellationToken cancellationToken = default);

    Task<bool> HasAnyRoleAsync(
        TUserId userId,
        IEnumerable<string> roleCodes,
        CancellationToken cancellationToken = default);

    Task<bool> HasAllRolesAsync(
        TUserId userId,
        IEnumerable<string> roleCodes,
        CancellationToken cancellationToken = default);

    Task<bool> HasPermissionAsync(
        TUserId userId,
        string permissionCode,
        CancellationToken cancellationToken = default);

    Task<bool> HasAnyPermissionAsync(
        TUserId userId,
        IEnumerable<string> permissionCodes,
        CancellationToken cancellationToken = default);

    Task<bool> HasAllPermissionsAsync(
        TUserId userId,
        IEnumerable<string> permissionCodes,
        CancellationToken cancellationToken = default);
}
