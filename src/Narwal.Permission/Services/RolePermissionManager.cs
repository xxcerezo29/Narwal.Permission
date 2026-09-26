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
    private readonly RolePermissionAuditWriter<TContext, TUserId> _auditWriter;

    public RolePermissionManager(
        TContext context,
        RolePermissionAuditWriter<TContext, TUserId> auditWriter)
    {
        _context = context;
        _auditWriter = auditWriter;
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
        var actor = _auditWriter.GetActor();
        _context.Set<Role>().Add(role);
        _auditWriter.Add(
            RolePermissionAuditActions.RoleCreated,
            role.Code,
            null,
            actor,
            newName: role.Name);
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
        var actor = _auditWriter.GetActor();
        _context.Set<PermissionEntity>().Add(permission);
        _auditWriter.Add(
            RolePermissionAuditActions.PermissionCreated,
            null,
            permission.Code,
            actor,
            newName: permission.Name);
        return permission;
    }

    public async Task RenameRoleAsync(string code, string name, CancellationToken cancellationToken = default)
    {
        var role = await GetRoleAsync(code, cancellationToken);
        var newName = RolePermissionCode.NormalizeName(name);
        if (StringComparer.Ordinal.Equals(role.Name, newName))
        {
            return;
        }

        var previousName = role.Name;
        var actor = _auditWriter.GetActor();
        role.Rename(newName);
        _auditWriter.Add(
            RolePermissionAuditActions.RoleRenamed,
            role.Code,
            null,
            actor,
            previousName: previousName,
            newName: role.Name);
    }

    public async Task RenamePermissionAsync(string code, string name, CancellationToken cancellationToken = default)
    {
        var permission = await GetPermissionAsync(code, cancellationToken);
        var newName = RolePermissionCode.NormalizeName(name);
        if (StringComparer.Ordinal.Equals(permission.Name, newName))
        {
            return;
        }

        var previousName = permission.Name;
        var actor = _auditWriter.GetActor();
        permission.Rename(newName);
        _auditWriter.Add(
            RolePermissionAuditActions.PermissionRenamed,
            null,
            permission.Code,
            actor,
            previousName: previousName,
            newName: permission.Name);
    }

    public async Task DeleteRoleAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalizedCode = RolePermissionCode.Normalize(code);
        var role = await GetRoleAsync(normalizedCode, cancellationToken);

        var grants = await GetRoleGrantsAsync(normalizedCode, cancellationToken);
        var assignments = await GetRoleUserAssignmentsAsync(normalizedCode, cancellationToken);
        var actor = _auditWriter.GetActor();
        _context.Set<RolePermissionGrant>().RemoveRange(grants);
        _context.Set<UserRole<TUserId>>().RemoveRange(assignments);
        _context.Set<Role>().Remove(role);

        foreach (var grant in grants)
        {
            _auditWriter.Add(
                RolePermissionAuditActions.RolePermissionRevoked,
                grant.RoleCode,
                grant.PermissionCode,
                actor);
        }

        foreach (var assignment in assignments)
        {
            _auditWriter.Add(
                RolePermissionAuditActions.UserRoleRemoved,
                assignment.RoleCode,
                null,
                actor,
                hasAffectedUserId: true,
                affectedUserId: assignment.UserId);
        }

        _auditWriter.Add(
            RolePermissionAuditActions.RoleDeleted,
            role.Code,
            null,
            actor,
            previousName: role.Name);
    }

    public async Task DeletePermissionAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalizedCode = RolePermissionCode.Normalize(code);
        var permission = await GetPermissionAsync(normalizedCode, cancellationToken);

        var grants = await GetPermissionGrantsAsync(normalizedCode, cancellationToken);
        var assignments = await GetPermissionUserAssignmentsAsync(normalizedCode, cancellationToken);
        var actor = _auditWriter.GetActor();
        _context.Set<RolePermissionGrant>().RemoveRange(grants);
        _context.Set<UserPermission<TUserId>>().RemoveRange(assignments);
        _context.Set<PermissionEntity>().Remove(permission);

        foreach (var grant in grants)
        {
            _auditWriter.Add(
                RolePermissionAuditActions.RolePermissionRevoked,
                grant.RoleCode,
                grant.PermissionCode,
                actor);
        }

        foreach (var assignment in assignments)
        {
            _auditWriter.Add(
                RolePermissionAuditActions.UserPermissionRevoked,
                null,
                assignment.PermissionCode,
                actor,
                hasAffectedUserId: true,
                affectedUserId: assignment.UserId);
        }

        _auditWriter.Add(
            RolePermissionAuditActions.PermissionDeleted,
            null,
            permission.Code,
            actor,
            previousName: permission.Name);
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

        var actor = _auditWriter.GetActor();
        if (RestoreDeletedRoleGrant(role.Code, permission.Code))
        {
            _auditWriter.Add(
                RolePermissionAuditActions.RolePermissionGranted,
                role.Code,
                permission.Code,
                actor);
            return;
        }

        _context.Set<RolePermissionGrant>().Add(new RolePermissionGrant(role.Code, permission.Code));
        _auditWriter.Add(
            RolePermissionAuditActions.RolePermissionGranted,
            role.Code,
            permission.Code,
            actor);
    }

    public async Task RevokePermissionFromRoleAsync(
        string roleCode,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        var role = await GetRoleAsync(roleCode, cancellationToken);
        var permission = await GetPermissionAsync(permissionCode, cancellationToken);
        var grants = await GetRoleGrantsAsync(role.Code, cancellationToken);
        var matchingGrants = grants.Where(grant => grant.PermissionCode == permission.Code).ToArray();
        if (matchingGrants.Length == 0)
        {
            return;
        }

        var actor = _auditWriter.GetActor();
        _context.Set<RolePermissionGrant>().RemoveRange(matchingGrants);
        foreach (var grant in matchingGrants)
        {
            _auditWriter.Add(
                RolePermissionAuditActions.RolePermissionRevoked,
                grant.RoleCode,
                grant.PermissionCode,
                actor);
        }
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
        var removals = current.Where(grant => !desiredCodes.Contains(grant.PermissionCode)).ToArray();
        var currentCodes = current.Select(grant => grant.PermissionCode).ToHashSet(StringComparer.Ordinal);
        var missing = permissions.Where(permission => !currentCodes.Contains(permission.Code)).ToArray();
        var restorations = missing
            .Where(permission => HasDeletedRoleGrant(role.Code, permission.Code))
            .ToArray();
        var additions = missing.Except(restorations).ToArray();

        if (removals.Length == 0 && additions.Length == 0 && restorations.Length == 0)
        {
            return;
        }

        var actor = _auditWriter.GetActor();
        _context.Set<RolePermissionGrant>().RemoveRange(removals);
        foreach (var permission in restorations)
        {
            if (RestoreDeletedRoleGrant(role.Code, permission.Code))
            {
                _auditWriter.Add(
                    RolePermissionAuditActions.RolePermissionGranted,
                    role.Code,
                    permission.Code,
                    actor);
            }
        }

        foreach (var grant in removals)
        {
            _auditWriter.Add(
                RolePermissionAuditActions.RolePermissionRevoked,
                grant.RoleCode,
                grant.PermissionCode,
                actor);
        }

        foreach (var permission in additions)
        {
            _context.Set<RolePermissionGrant>().Add(new RolePermissionGrant(role.Code, permission.Code));
            _auditWriter.Add(
                RolePermissionAuditActions.RolePermissionGranted,
                role.Code,
                permission.Code,
                actor);
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

        var actor = _auditWriter.GetActor();
        if (RestoreDeletedUserRole(userId, role.Code))
        {
            _auditWriter.Add(
                RolePermissionAuditActions.UserRoleAssigned,
                role.Code,
                null,
                actor,
                hasAffectedUserId: true,
                affectedUserId: userId);
            return;
        }

        _context.Set<UserRole<TUserId>>().Add(new UserRole<TUserId>(userId, role.Code));
        _auditWriter.Add(
            RolePermissionAuditActions.UserRoleAssigned,
            role.Code,
            null,
            actor,
            hasAffectedUserId: true,
            affectedUserId: userId);
    }

    public async Task RemoveRoleAsync(
        TUserId userId,
        string roleCode,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullUserId(userId);
        var role = await GetRoleAsync(roleCode, cancellationToken);
        var assignments = await GetUserRolesAsync(userId, cancellationToken);
        var matchingAssignments = assignments.Where(assignment => assignment.RoleCode == role.Code).ToArray();
        if (matchingAssignments.Length == 0)
        {
            return;
        }

        var actor = _auditWriter.GetActor();
        _context.Set<UserRole<TUserId>>().RemoveRange(matchingAssignments);
        foreach (var assignment in matchingAssignments)
        {
            _auditWriter.Add(
                RolePermissionAuditActions.UserRoleRemoved,
                assignment.RoleCode,
                null,
                actor,
                hasAffectedUserId: true,
                affectedUserId: assignment.UserId);
        }
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
        var removals = current.Where(assignment => !desiredCodes.Contains(assignment.RoleCode)).ToArray();
        var currentCodes = current.Select(assignment => assignment.RoleCode).ToHashSet(StringComparer.Ordinal);
        var missing = roles.Where(role => !currentCodes.Contains(role.Code)).ToArray();
        var restorations = missing
            .Where(role => HasDeletedUserRole(userId, role.Code))
            .ToArray();
        var additions = missing.Except(restorations).ToArray();

        if (removals.Length == 0 && additions.Length == 0 && restorations.Length == 0)
        {
            return;
        }

        var actor = _auditWriter.GetActor();
        _context.Set<UserRole<TUserId>>().RemoveRange(removals);
        foreach (var role in restorations)
        {
            if (RestoreDeletedUserRole(userId, role.Code))
            {
                _auditWriter.Add(
                    RolePermissionAuditActions.UserRoleAssigned,
                    role.Code,
                    null,
                    actor,
                    hasAffectedUserId: true,
                    affectedUserId: userId);
            }
        }

        foreach (var assignment in removals)
        {
            _auditWriter.Add(
                RolePermissionAuditActions.UserRoleRemoved,
                assignment.RoleCode,
                null,
                actor,
                hasAffectedUserId: true,
                affectedUserId: assignment.UserId);
        }

        foreach (var role in additions)
        {
            _context.Set<UserRole<TUserId>>().Add(new UserRole<TUserId>(userId, role.Code));
            _auditWriter.Add(
                RolePermissionAuditActions.UserRoleAssigned,
                role.Code,
                null,
                actor,
                hasAffectedUserId: true,
                affectedUserId: userId);
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

        var actor = _auditWriter.GetActor();
        if (RestoreDeletedUserPermission(userId, permission.Code))
        {
            _auditWriter.Add(
                RolePermissionAuditActions.UserPermissionGranted,
                null,
                permission.Code,
                actor,
                hasAffectedUserId: true,
                affectedUserId: userId);
            return;
        }

        _context.Set<UserPermission<TUserId>>().Add(new UserPermission<TUserId>(userId, permission.Code));
        _auditWriter.Add(
            RolePermissionAuditActions.UserPermissionGranted,
            null,
            permission.Code,
            actor,
            hasAffectedUserId: true,
            affectedUserId: userId);
    }

    public async Task RevokePermissionFromUserAsync(
        TUserId userId,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullUserId(userId);
        var permission = await GetPermissionAsync(permissionCode, cancellationToken);
        var assignments = await GetUserPermissionsAsync(userId, cancellationToken);
        var matchingAssignments = assignments.Where(assignment => assignment.PermissionCode == permission.Code).ToArray();
        if (matchingAssignments.Length == 0)
        {
            return;
        }

        var actor = _auditWriter.GetActor();
        _context.Set<UserPermission<TUserId>>().RemoveRange(matchingAssignments);
        foreach (var assignment in matchingAssignments)
        {
            _auditWriter.Add(
                RolePermissionAuditActions.UserPermissionRevoked,
                null,
                assignment.PermissionCode,
                actor,
                hasAffectedUserId: true,
                affectedUserId: assignment.UserId);
        }
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
        var removals = current.Where(assignment => !desiredCodes.Contains(assignment.PermissionCode)).ToArray();
        var currentCodes = current.Select(assignment => assignment.PermissionCode).ToHashSet(StringComparer.Ordinal);
        var missing = permissions.Where(permission => !currentCodes.Contains(permission.Code)).ToArray();
        var restorations = missing
            .Where(permission => HasDeletedUserPermission(userId, permission.Code))
            .ToArray();
        var additions = missing.Except(restorations).ToArray();

        if (removals.Length == 0 && additions.Length == 0 && restorations.Length == 0)
        {
            return;
        }

        var actor = _auditWriter.GetActor();
        _context.Set<UserPermission<TUserId>>().RemoveRange(removals);
        foreach (var permission in restorations)
        {
            if (RestoreDeletedUserPermission(userId, permission.Code))
            {
                _auditWriter.Add(
                    RolePermissionAuditActions.UserPermissionGranted,
                    null,
                    permission.Code,
                    actor,
                    hasAffectedUserId: true,
                    affectedUserId: userId);
            }
        }

        foreach (var assignment in removals)
        {
            _auditWriter.Add(
                RolePermissionAuditActions.UserPermissionRevoked,
                null,
                assignment.PermissionCode,
                actor,
                hasAffectedUserId: true,
                affectedUserId: assignment.UserId);
        }

        foreach (var permission in additions)
        {
            _context.Set<UserPermission<TUserId>>().Add(new UserPermission<TUserId>(userId, permission.Code));
            _auditWriter.Add(
                RolePermissionAuditActions.UserPermissionGranted,
                null,
                permission.Code,
                actor,
                hasAffectedUserId: true,
                affectedUserId: userId);
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

    private bool HasDeletedRoleGrant(string roleCode, string permissionCode)
    {
        return _context.ChangeTracker.Entries<RolePermissionGrant>().Any(candidate =>
            candidate.State == EntityState.Deleted
            && candidate.Entity.RoleCode == roleCode
            && candidate.Entity.PermissionCode == permissionCode);
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

    private bool HasDeletedUserRole(TUserId userId, string roleCode)
    {
        return _context.ChangeTracker.Entries<UserRole<TUserId>>().Any(candidate =>
            candidate.State == EntityState.Deleted
            && EqualityComparer<TUserId>.Default.Equals(candidate.Entity.UserId, userId)
            && candidate.Entity.RoleCode == roleCode);
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

    private bool HasDeletedUserPermission(TUserId userId, string permissionCode)
    {
        return _context.ChangeTracker.Entries<UserPermission<TUserId>>().Any(candidate =>
            candidate.State == EntityState.Deleted
            && EqualityComparer<TUserId>.Default.Equals(candidate.Entity.UserId, userId)
            && candidate.Entity.PermissionCode == permissionCode);
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
