using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Narwal.Permission.Domain;
using PermissionEntity = Narwal.Permission.Domain.Permission;

namespace Narwal.Permission.Services;

internal sealed class RolePermissionManager<TContext, TUserId> : IRolePermissionManager<TUserId>
    where TContext : DbContext
    where TUserId : notnull
{
    private readonly TContext _context;

    public RolePermissionManager(TContext context)
    {
        _context = context;
    }

    public async Task<Role> CreateRoleAsync(
        string code,
        string name,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = RolePermissionCode.Normalize(code);
        if (_context.Set<Role>().Local.Any(role => IsActive(role) && role.Code == normalizedCode)
            || await _context.Set<Role>().AnyAsync(role => role.Code == normalizedCode, cancellationToken))
        {
            throw new RolePermissionAlreadyExistsException("Role", normalizedCode);
        }

        var role = new Role(normalizedCode, name);
        _context.Set<Role>().Add(role);
        return role;
    }

    public async Task<PermissionEntity> CreatePermissionAsync(
        string code,
        string name,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = RolePermissionCode.Normalize(code);
        if (_context.Set<PermissionEntity>().Local.Any(permission => IsActive(permission) && permission.Code == normalizedCode)
            || await _context.Set<PermissionEntity>().AnyAsync(permission => permission.Code == normalizedCode, cancellationToken))
        {
            throw new RolePermissionAlreadyExistsException("Permission", normalizedCode);
        }

        var permission = new PermissionEntity(normalizedCode, name);
        _context.Set<PermissionEntity>().Add(permission);
        return permission;
    }

    public async Task RenameRoleAsync(string code, string name, CancellationToken cancellationToken = default)
    {
        var role = await GetRoleAsync(code, cancellationToken);
        role.Rename(name);
    }

    public async Task RenamePermissionAsync(string code, string name, CancellationToken cancellationToken = default)
    {
        var permission = await GetPermissionAsync(code, cancellationToken);
        permission.Rename(name);
    }

    public async Task DeleteRoleAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalizedCode = RolePermissionCode.Normalize(code);
        var role = await GetRoleAsync(normalizedCode, cancellationToken);

        var grants = await GetRoleGrantsAsync(normalizedCode, cancellationToken);
        var assignments = await GetRoleUserAssignmentsAsync(normalizedCode, cancellationToken);
        _context.Set<RolePermissionGrant>().RemoveRange(grants);
        _context.Set<UserRole<TUserId>>().RemoveRange(assignments);
        _context.Set<Role>().Remove(role);
    }

    public async Task DeletePermissionAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalizedCode = RolePermissionCode.Normalize(code);
        var permission = await GetPermissionAsync(normalizedCode, cancellationToken);

        var grants = await GetPermissionGrantsAsync(normalizedCode, cancellationToken);
        var assignments = await GetPermissionUserAssignmentsAsync(normalizedCode, cancellationToken);
        _context.Set<RolePermissionGrant>().RemoveRange(grants);
        _context.Set<UserPermission<TUserId>>().RemoveRange(assignments);
        _context.Set<PermissionEntity>().Remove(permission);
    }

    public async Task GrantPermissionToRoleAsync(
        string roleCode,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        var role = await GetRoleAsync(roleCode, cancellationToken);
        var permission = await GetPermissionAsync(permissionCode, cancellationToken);
        var grants = await GetRoleGrantsAsync(role.Code, cancellationToken);
        if (grants.Any(grant => grant.PermissionCode == permission.Code))
        {
            return;
        }

        if (RestoreDeletedRoleGrant(role.Code, permission.Code))
        {
            return;
        }

        _context.Set<RolePermissionGrant>().Add(new RolePermissionGrant(role.Code, permission.Code));
    }

    public async Task RevokePermissionFromRoleAsync(
        string roleCode,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        var role = await GetRoleAsync(roleCode, cancellationToken);
        var permission = await GetPermissionAsync(permissionCode, cancellationToken);
        var grants = await GetRoleGrantsAsync(role.Code, cancellationToken);
        _context.Set<RolePermissionGrant>().RemoveRange(
            grants.Where(grant => grant.PermissionCode == permission.Code));
    }

    public async Task SyncRolePermissionsAsync(
        string roleCode,
        IEnumerable<string> permissionCodes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissionCodes);
        var role = await GetRoleAsync(roleCode, cancellationToken);
        var normalizedCodes = NormalizeDistinct(permissionCodes);
        var permissions = new List<PermissionEntity>(normalizedCodes.Count);
        foreach (var permissionCode in normalizedCodes)
        {
            permissions.Add(await GetPermissionAsync(permissionCode, cancellationToken));
        }

        var desiredCodes = permissions.Select(permission => permission.Code).ToHashSet(StringComparer.Ordinal);
        var current = await GetRoleGrantsAsync(role.Code, cancellationToken);
        _context.Set<RolePermissionGrant>().RemoveRange(
            current.Where(grant => !desiredCodes.Contains(grant.PermissionCode)));

        var currentCodes = current.Select(grant => grant.PermissionCode).ToHashSet(StringComparer.Ordinal);
        foreach (var permission in permissions)
        {
            if (!currentCodes.Contains(permission.Code)
                && !RestoreDeletedRoleGrant(role.Code, permission.Code))
            {
                _context.Set<RolePermissionGrant>().Add(new RolePermissionGrant(role.Code, permission.Code));
            }
        }
    }

    public async Task AssignRoleAsync(
        TUserId userId,
        string roleCode,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullUserId(userId);
        var role = await GetRoleAsync(roleCode, cancellationToken);
        var assignments = await GetUserRolesAsync(userId, cancellationToken);
        if (assignments.Any(assignment => assignment.RoleCode == role.Code))
        {
            return;
        }

        if (RestoreDeletedUserRole(userId, role.Code))
        {
            return;
        }

        _context.Set<UserRole<TUserId>>().Add(new UserRole<TUserId>(userId, role.Code));
    }

    public async Task RemoveRoleAsync(
        TUserId userId,
        string roleCode,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullUserId(userId);
        var role = await GetRoleAsync(roleCode, cancellationToken);
        var assignments = await GetUserRolesAsync(userId, cancellationToken);
        _context.Set<UserRole<TUserId>>().RemoveRange(
            assignments.Where(assignment => assignment.RoleCode == role.Code));
    }

    public async Task SyncUserRolesAsync(
        TUserId userId,
        IEnumerable<string> roleCodes,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullUserId(userId);
        ArgumentNullException.ThrowIfNull(roleCodes);
        var normalizedCodes = NormalizeDistinct(roleCodes);
        var roles = new List<Role>(normalizedCodes.Count);
        foreach (var roleCode in normalizedCodes)
        {
            roles.Add(await GetRoleAsync(roleCode, cancellationToken));
        }

        var desiredCodes = roles.Select(role => role.Code).ToHashSet(StringComparer.Ordinal);
        var current = await GetUserRolesAsync(userId, cancellationToken);
        _context.Set<UserRole<TUserId>>().RemoveRange(
            current.Where(assignment => !desiredCodes.Contains(assignment.RoleCode)));

        var currentCodes = current.Select(assignment => assignment.RoleCode).ToHashSet(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            if (!currentCodes.Contains(role.Code)
                && !RestoreDeletedUserRole(userId, role.Code))
            {
                _context.Set<UserRole<TUserId>>().Add(new UserRole<TUserId>(userId, role.Code));
            }
        }
    }

    public async Task GrantPermissionToUserAsync(
        TUserId userId,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullUserId(userId);
        var permission = await GetPermissionAsync(permissionCode, cancellationToken);
        var assignments = await GetUserPermissionsAsync(userId, cancellationToken);
        if (assignments.Any(assignment => assignment.PermissionCode == permission.Code))
        {
            return;
        }

        if (RestoreDeletedUserPermission(userId, permission.Code))
        {
            return;
        }

        _context.Set<UserPermission<TUserId>>().Add(new UserPermission<TUserId>(userId, permission.Code));
    }

    public async Task RevokePermissionFromUserAsync(
        TUserId userId,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullUserId(userId);
        var permission = await GetPermissionAsync(permissionCode, cancellationToken);
        var assignments = await GetUserPermissionsAsync(userId, cancellationToken);
        _context.Set<UserPermission<TUserId>>().RemoveRange(
            assignments.Where(assignment => assignment.PermissionCode == permission.Code));
    }

    public async Task SyncUserPermissionsAsync(
        TUserId userId,
        IEnumerable<string> permissionCodes,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullUserId(userId);
        ArgumentNullException.ThrowIfNull(permissionCodes);
        var normalizedCodes = NormalizeDistinct(permissionCodes);
        var permissions = new List<PermissionEntity>(normalizedCodes.Count);
        foreach (var permissionCode in normalizedCodes)
        {
            permissions.Add(await GetPermissionAsync(permissionCode, cancellationToken));
        }

        var desiredCodes = permissions.Select(permission => permission.Code).ToHashSet(StringComparer.Ordinal);
        var current = await GetUserPermissionsAsync(userId, cancellationToken);
        _context.Set<UserPermission<TUserId>>().RemoveRange(
            current.Where(assignment => !desiredCodes.Contains(assignment.PermissionCode)));

        var currentCodes = current.Select(assignment => assignment.PermissionCode).ToHashSet(StringComparer.Ordinal);
        foreach (var permission in permissions)
        {
            if (!currentCodes.Contains(permission.Code)
                && !RestoreDeletedUserPermission(userId, permission.Code))
            {
                _context.Set<UserPermission<TUserId>>().Add(new UserPermission<TUserId>(userId, permission.Code));
            }
        }
    }

    private async Task<Role> GetRoleAsync(string code, CancellationToken cancellationToken)
    {
        var normalizedCode = RolePermissionCode.Normalize(code);
        var local = _context.Set<Role>().Local.FirstOrDefault(
            role => IsActive(role) && role.Code == normalizedCode);
        if (local is not null)
        {
            return local;
        }

        var role = await _context.Set<Role>()
            .SingleOrDefaultAsync(candidate => candidate.Code == normalizedCode, cancellationToken);
        if (role is null || !IsActive(role))
        {
            throw new RolePermissionNotFoundException("Role", normalizedCode);
        }

        return role;
    }

    private async Task<PermissionEntity> GetPermissionAsync(string code, CancellationToken cancellationToken)
    {
        var normalizedCode = RolePermissionCode.Normalize(code);
        var local = _context.Set<PermissionEntity>().Local.FirstOrDefault(
            permission => IsActive(permission) && permission.Code == normalizedCode);
        if (local is not null)
        {
            return local;
        }

        var permission = await _context.Set<PermissionEntity>()
            .SingleOrDefaultAsync(candidate => candidate.Code == normalizedCode, cancellationToken);
        if (permission is null || !IsActive(permission))
        {
            throw new RolePermissionNotFoundException("Permission", normalizedCode);
        }

        return permission;
    }

    private async Task<List<RolePermissionGrant>> GetRoleGrantsAsync(
        string roleCode,
        CancellationToken cancellationToken)
    {
        var fromDatabase = await _context.Set<RolePermissionGrant>()
            .Where(grant => grant.RoleCode == roleCode)
            .ToListAsync(cancellationToken);
        return MergeActive(fromDatabase, _context.Set<RolePermissionGrant>().Local
            .Where(grant => grant.RoleCode == roleCode));
    }

    private async Task<List<RolePermissionGrant>> GetPermissionGrantsAsync(
        string permissionCode,
        CancellationToken cancellationToken)
    {
        var fromDatabase = await _context.Set<RolePermissionGrant>()
            .Where(grant => grant.PermissionCode == permissionCode)
            .ToListAsync(cancellationToken);
        return MergeActive(fromDatabase, _context.Set<RolePermissionGrant>().Local
            .Where(grant => grant.PermissionCode == permissionCode));
    }

    private async Task<List<UserRole<TUserId>>> GetUserRolesAsync(
        TUserId userId,
        CancellationToken cancellationToken)
    {
        var predicate = UserIdEquals<UserRole<TUserId>>(assignment => assignment.UserId, userId);
        var fromDatabase = await _context.Set<UserRole<TUserId>>()
            .Where(predicate)
            .ToListAsync(cancellationToken);
        return MergeActive(fromDatabase, _context.Set<UserRole<TUserId>>().Local
            .Where(assignment => EqualityComparer<TUserId>.Default.Equals(assignment.UserId, userId)));
    }

    private async Task<List<UserRole<TUserId>>> GetRoleUserAssignmentsAsync(
        string roleCode,
        CancellationToken cancellationToken)
    {
        var fromDatabase = await _context.Set<UserRole<TUserId>>()
            .Where(assignment => assignment.RoleCode == roleCode)
            .ToListAsync(cancellationToken);
        return MergeActive(fromDatabase, _context.Set<UserRole<TUserId>>().Local
            .Where(assignment => assignment.RoleCode == roleCode));
    }

    private async Task<List<UserPermission<TUserId>>> GetUserPermissionsAsync(
        TUserId userId,
        CancellationToken cancellationToken)
    {
        var predicate = UserIdEquals<UserPermission<TUserId>>(assignment => assignment.UserId, userId);
        var fromDatabase = await _context.Set<UserPermission<TUserId>>()
            .Where(predicate)
            .ToListAsync(cancellationToken);
        return MergeActive(fromDatabase, _context.Set<UserPermission<TUserId>>().Local
            .Where(assignment => EqualityComparer<TUserId>.Default.Equals(assignment.UserId, userId)));
    }

    private async Task<List<UserPermission<TUserId>>> GetPermissionUserAssignmentsAsync(
        string permissionCode,
        CancellationToken cancellationToken)
    {
        var fromDatabase = await _context.Set<UserPermission<TUserId>>()
            .Where(assignment => assignment.PermissionCode == permissionCode)
            .ToListAsync(cancellationToken);
        return MergeActive(fromDatabase, _context.Set<UserPermission<TUserId>>().Local
            .Where(assignment => assignment.PermissionCode == permissionCode));
    }

    private bool RestoreDeletedRoleGrant(string roleCode, string permissionCode)
    {
        var entry = _context.ChangeTracker.Entries<RolePermissionGrant>().FirstOrDefault(candidate =>
            candidate.State == EntityState.Deleted
            && candidate.Entity.RoleCode == roleCode
            && candidate.Entity.PermissionCode == permissionCode);
        if (entry is null)
        {
            return false;
        }

        entry.State = EntityState.Unchanged;
        return true;
    }

    private bool RestoreDeletedUserRole(TUserId userId, string roleCode)
    {
        var entry = _context.ChangeTracker.Entries<UserRole<TUserId>>().FirstOrDefault(candidate =>
            candidate.State == EntityState.Deleted
            && EqualityComparer<TUserId>.Default.Equals(candidate.Entity.UserId, userId)
            && candidate.Entity.RoleCode == roleCode);
        if (entry is null)
        {
            return false;
        }

        entry.State = EntityState.Unchanged;
        return true;
    }

    private bool RestoreDeletedUserPermission(TUserId userId, string permissionCode)
    {
        var entry = _context.ChangeTracker.Entries<UserPermission<TUserId>>().FirstOrDefault(candidate =>
            candidate.State == EntityState.Deleted
            && EqualityComparer<TUserId>.Default.Equals(candidate.Entity.UserId, userId)
            && candidate.Entity.PermissionCode == permissionCode);
        if (entry is null)
        {
            return false;
        }

        entry.State = EntityState.Unchanged;
        return true;
    }

    private static Expression<Func<TEntity, bool>> UserIdEquals<TEntity>(
        Expression<Func<TEntity, TUserId>> userIdSelector,
        TUserId userId)
    {
        var equalsMethod = typeof(object).GetMethod(
            nameof(object.Equals),
            [typeof(object), typeof(object)])!;
        var body = Expression.Call(
            equalsMethod,
            Expression.Convert(userIdSelector.Body, typeof(object)),
            Expression.Convert(Expression.Constant(userId, typeof(TUserId)), typeof(object)));
        return Expression.Lambda<Func<TEntity, bool>>(body, userIdSelector.Parameters);
    }

    private List<TEntity> MergeActive<TEntity>(IEnumerable<TEntity> fromDatabase, IEnumerable<TEntity> fromLocal)
        where TEntity : class
    {
        var merged = new HashSet<TEntity>(ReferenceEqualityComparer.Instance);
        foreach (var entity in fromDatabase.Concat(fromLocal))
        {
            if (IsActive(entity))
            {
                merged.Add(entity);
            }
        }

        return merged.ToList();
    }

    private bool IsActive<TEntity>(TEntity entity)
        where TEntity : class => _context.Entry(entity).State != EntityState.Deleted;

    private static List<string> NormalizeDistinct(IEnumerable<string> codes)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            var normalizedCode = RolePermissionCode.Normalize(code);
            if (seen.Add(normalizedCode))
            {
                normalized.Add(normalizedCode);
            }
        }

        return normalized;
    }

    private static void ThrowIfNullUserId(TUserId userId)
    {
        if (userId is null)
        {
            throw new ArgumentNullException(nameof(userId));
        }
    }
}
