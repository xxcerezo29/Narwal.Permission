using System.Security.Claims;

namespace Narwal.Permission.Authorization;

/// <summary>Options for mapping claims to an application's user key.</summary>
public sealed class RolePermissionOptions<TUserId>
    where TUserId : notnull
{
    public Func<ClaimsPrincipal, (bool Success, TUserId UserId)>? UserIdResolver { get; set; }
}
