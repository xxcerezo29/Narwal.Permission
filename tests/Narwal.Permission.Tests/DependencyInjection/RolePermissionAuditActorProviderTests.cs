using Microsoft.Extensions.DependencyInjection;
using Narwal.Permission.DependencyInjection;
using Narwal.Permission.Services;
using Narwal.Permission.Tests.TestModels;

namespace Narwal.Permission.Tests.DependencyInjection;

public sealed class RolePermissionAuditActorProviderTests
{
    [Fact]
    public void AddRolePermission_registers_a_null_actor_provider_by_default()
    {
        var services = new ServiceCollection();
        services.AddRolePermission<RolePermissionDbContext, Guid>();

        using var provider = services.BuildServiceProvider();
        Assert.False(provider.GetRequiredService<IRolePermissionAuditActorProvider<Guid>>()
            .TryGetActorUserId(out _));
    }

    [Fact]
    public void AddRolePermission_preserves_an_application_actor_provider_and_guid_empty()
    {
        var services = new ServiceCollection();
        services.AddScoped<IRolePermissionAuditActorProvider<Guid>>(
            _ => new FixedActorProvider(Guid.Empty));
        services.AddRolePermission<RolePermissionDbContext, Guid>();

        using var provider = services.BuildServiceProvider();
        Assert.True(provider.GetRequiredService<IRolePermissionAuditActorProvider<Guid>>()
            .TryGetActorUserId(out var actorId));
        Assert.Equal(Guid.Empty, actorId);
    }

    private sealed class FixedActorProvider(Guid actorId) : IRolePermissionAuditActorProvider<Guid>
    {
        public bool TryGetActorUserId(out Guid userId)
        {
            userId = actorId;
            return true;
        }
    }
}
