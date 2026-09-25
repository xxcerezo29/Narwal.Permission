using System.Security.Claims;

namespace Narwal.Permission.Authorization;

/// <summary>Resolves the application's user key from an authenticated principal.</summary>
public interface IRolePermissionUserIdResolver<TUserId>
    where TUserId : notnull
{
    bool TryResolve(ClaimsPrincipal principal, out TUserId userId);
}
