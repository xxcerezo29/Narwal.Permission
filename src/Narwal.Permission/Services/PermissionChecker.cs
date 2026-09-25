using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Narwal.Permission.Domain;

namespace Narwal.Permission.Services;

internal sealed class PermissionChecker<TContext, TUserId> : IPermissionChecker<TUserId>
    where TContext : DbContext
    where TUserId : notnull
{
    private readonly TContext _context;

    public PermissionChecker(TContext context)
    {
        _context = context;
    }

    public async Task<bool> HasRoleAsync(
        TUserId userId,
        string roleCode,
        CancellationToken cancellationToken = default)
    {
        if (userId is null || !TryNormalize(roleCode, out var normalizedCode))
        {
            return false;
        }

        var userIdPredicate = UserIdEquals<UserRole<TUserId>>(assignment => assignment.UserId, userId);
        return await _context.Set<UserRole<TUserId>>()
            .Where(userIdPredicate)
            .AnyAsync(assignment => assignment.RoleCode == normalizedCode, cancellationToken);
    }

    public async Task<bool> HasAnyRoleAsync(
        TUserId userId,
        IEnumerable<string> roleCodes,
        CancellationToken cancellationToken = default)
    {
        if (userId is null || !TryNormalizeDistinct(roleCodes, out var normalizedCodes))
        {
            return false;
        }

        var userIdPredicate = UserIdEquals<UserRole<TUserId>>(assignment => assignment.UserId, userId);
        return await _context.Set<UserRole<TUserId>>()
            .Where(userIdPredicate)
            .AnyAsync(assignment => normalizedCodes.Contains(assignment.RoleCode), cancellationToken);
    }

    public async Task<bool> HasAllRolesAsync(
        TUserId userId,
        IEnumerable<string> roleCodes,
        CancellationToken cancellationToken = default)
    {
        if (userId is null || !TryNormalizeDistinct(roleCodes, out var normalizedCodes))
        {
            return false;
        }

        var userIdPredicate = UserIdEquals<UserRole<TUserId>>(assignment => assignment.UserId, userId);
        var matchedCount = await _context.Set<UserRole<TUserId>>()
            .Where(userIdPredicate)
            .CountAsync(assignment => normalizedCodes.Contains(assignment.RoleCode), cancellationToken);
        return matchedCount == normalizedCodes.Count;
    }

    public async Task<bool> HasPermissionAsync(
        TUserId userId,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        if (userId is null || !TryNormalize(permissionCode, out var normalizedCode))
        {
            return false;
        }

        var codes = new[] { normalizedCode };
        return await HasAnyPermissionCoreAsync(userId, codes, cancellationToken);
    }

    public async Task<bool> HasAnyPermissionAsync(
        TUserId userId,
        IEnumerable<string> permissionCodes,
        CancellationToken cancellationToken = default)
    {
        if (userId is null || !TryNormalizeDistinct(permissionCodes, out var normalizedCodes))
        {
            return false;
        }

        return await HasAnyPermissionCoreAsync(userId, normalizedCodes, cancellationToken);
    }

    public async Task<bool> HasAllPermissionsAsync(
        TUserId userId,
        IEnumerable<string> permissionCodes,
        CancellationToken cancellationToken = default)
    {
        if (userId is null || !TryNormalizeDistinct(permissionCodes, out var normalizedCodes))
        {
            return false;
        }

        var effectiveCodes = GetEffectivePermissionCodes(userId, normalizedCodes);
        var matchedCount = await effectiveCodes.CountAsync(cancellationToken);
        return matchedCount == normalizedCodes.Count;
    }

    private async Task<bool> HasAnyPermissionCoreAsync(
        TUserId userId,
        IReadOnlyCollection<string> permissionCodes,
        CancellationToken cancellationToken)
    {
        return await GetEffectivePermissionCodes(userId, permissionCodes).AnyAsync(cancellationToken);
    }

    private IQueryable<string> GetEffectivePermissionCodes(
        TUserId userId,
        IReadOnlyCollection<string> permissionCodes)
    {
        var userRolePredicate = UserIdEquals<UserRole<TUserId>>(assignment => assignment.UserId, userId);
        var directPermissionPredicate = UserIdEquals<UserPermission<TUserId>>(
            assignment => assignment.UserId,
            userId);

        var directCodes = _context.Set<UserPermission<TUserId>>()
            .Where(directPermissionPredicate)
            .Where(assignment => permissionCodes.Contains(assignment.PermissionCode))
            .Select(assignment => assignment.PermissionCode);

        var inheritedCodes =
            from assignment in _context.Set<UserRole<TUserId>>().Where(userRolePredicate)
            join grant in _context.Set<RolePermissionGrant>()
                on assignment.RoleCode equals grant.RoleCode
            where permissionCodes.Contains(grant.PermissionCode)
            select grant.PermissionCode;

        return directCodes.Union(inheritedCodes);
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

    private static bool TryNormalize(string code, out string normalizedCode)
    {
        try
        {
            normalizedCode = RolePermissionCode.Normalize(code);
            return true;
        }
        catch (ArgumentException)
        {
            normalizedCode = string.Empty;
            return false;
        }
    }

    private static bool TryNormalizeDistinct(
        IEnumerable<string> codes,
        out List<string> normalizedCodes)
    {
        normalizedCodes = [];
        if (codes is null)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var code in codes)
            {
                var normalizedCode = RolePermissionCode.Normalize(code);
                if (seen.Add(normalizedCode))
                {
                    normalizedCodes.Add(normalizedCode);
                }
            }
        }
        catch (ArgumentException)
        {
            normalizedCodes.Clear();
            return false;
        }

        return normalizedCodes.Count > 0;
    }
}
