using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Narwal.Permission.Authorization;
using Narwal.Permission.Services;

namespace Narwal.Permission.DependencyInjection;

public static class RolePermissionServiceCollectionExtensions
{
    /// <summary>Registers role management and permission checking against the application's scoped DbContext.</summary>
    public static IServiceCollection AddRolePermission<TContext, TUserId>(
        this IServiceCollection services,
        Action<RolePermissionOptions<TUserId>>? configure = null)
        where TContext : DbContext
        where TUserId : notnull
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IRolePermissionManager<TUserId>, RolePermissionManager<TContext, TUserId>>();
        services.AddScoped<IPermissionChecker<TUserId>, PermissionChecker<TContext, TUserId>>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services;
    }
}
