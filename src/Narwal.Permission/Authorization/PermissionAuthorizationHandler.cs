using Microsoft.AspNetCore.Authorization;
using Narwal.Permission.Services;

namespace Narwal.Permission.Authorization;

public sealed class PermissionAuthorizationHandler<TUserId> : AuthorizationHandler<PermissionRequirement>
    where TUserId : notnull
{
    private readonly IRolePermissionUserIdResolver<TUserId> _userIdResolver;
    private readonly IPermissionChecker<TUserId> _permissionChecker;

    public PermissionAuthorizationHandler(
        IRolePermissionUserIdResolver<TUserId> userIdResolver,
        IPermissionChecker<TUserId> permissionChecker)
    {
        _userIdResolver = userIdResolver;
        _permissionChecker = permissionChecker;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (!_userIdResolver.TryResolve(context.User, out var userId))
        {
            return;
        }

        if (await _permissionChecker.HasPermissionAsync(userId, requirement.PermissionCode))
        {
            context.Succeed(requirement);
        }
    }
}
