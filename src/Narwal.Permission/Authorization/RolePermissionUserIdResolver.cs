using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace Narwal.Permission.Authorization;

internal sealed class RolePermissionUserIdResolver<TUserId> : IRolePermissionUserIdResolver<TUserId>
    where TUserId : notnull
{
    private readonly RolePermissionOptions<TUserId> _options;

    public RolePermissionUserIdResolver(IOptions<RolePermissionOptions<TUserId>> options)
    {
        _options = options.Value;
    }

    public bool TryResolve(ClaimsPrincipal principal, out TUserId userId)
    {
        userId = default!;
        if (principal is null)
        {
            return false;
        }

        try
        {
            if (_options.UserIdResolver is not null)
            {
                var (success, resolvedUserId) = _options.UserIdResolver(principal);
                if (!success || resolvedUserId is null)
                {
                    return false;
                }

                userId = resolvedUserId;
                return true;
            }

            if (typeof(TUserId) != typeof(string))
            {
                return false;
            }

            var claimValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(claimValue))
            {
                return false;
            }

            userId = (TUserId)(object)claimValue;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or FormatException
            or InvalidCastException
            or OverflowException)
        {
            userId = default!;
            return false;
        }
    }
}
