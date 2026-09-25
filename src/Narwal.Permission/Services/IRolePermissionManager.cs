using Narwal.Permission.Domain;
using PermissionEntity = Narwal.Permission.Domain.Permission;

namespace Narwal.Permission.Services;

/// <summary>Manages roles, permissions, and their assignments in the host application's unit of work.</summary>
public interface IRolePermissionManager<TUserId>
    where TUserId : notnull
{
    Task<Role> CreateRoleAsync(string code, string name, CancellationToken cancellationToken = default);

    Task<PermissionEntity> CreatePermissionAsync(string code, string name, CancellationToken cancellationToken = default);

    Task RenameRoleAsync(string code, string name, CancellationToken cancellationToken = default);

    Task RenamePermissionAsync(string code, string name, CancellationToken cancellationToken = default);

    Task DeleteRoleAsync(string code, CancellationToken cancellationToken = default);

    Task DeletePermissionAsync(string code, CancellationToken cancellationToken = default);

    Task GrantPermissionToRoleAsync(string roleCode, string permissionCode, CancellationToken cancellationToken = default);

    Task RevokePermissionFromRoleAsync(string roleCode, string permissionCode, CancellationToken cancellationToken = default);

    Task SyncRolePermissionsAsync(
        string roleCode,
        IEnumerable<string> permissionCodes,
        CancellationToken cancellationToken = default);

    Task AssignRoleAsync(TUserId userId, string roleCode, CancellationToken cancellationToken = default);

    Task RemoveRoleAsync(TUserId userId, string roleCode, CancellationToken cancellationToken = default);

    Task SyncUserRolesAsync(
        TUserId userId,
        IEnumerable<string> roleCodes,
        CancellationToken cancellationToken = default);

    Task GrantPermissionToUserAsync(TUserId userId, string permissionCode, CancellationToken cancellationToken = default);

    Task RevokePermissionFromUserAsync(TUserId userId, string permissionCode, CancellationToken cancellationToken = default);

    Task SyncUserPermissionsAsync(
        TUserId userId,
        IEnumerable<string> permissionCodes,
        CancellationToken cancellationToken = default);
}
