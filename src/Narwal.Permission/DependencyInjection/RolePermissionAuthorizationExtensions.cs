using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Narwal.Permission.Authorization;

namespace Narwal.Permission.DependencyInjection;

public static class RolePermissionAuthorizationExtensions
{
    public static IServiceCollection AddRolePermissionAuthorization<TUserId>(this IServiceCollection services)
        where TUserId : notnull
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler<TUserId>>();
        services.TryAddScoped<IRolePermissionUserIdResolver<TUserId>, RolePermissionUserIdResolver<TUserId>>();
        return services;
    }
}
