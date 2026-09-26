using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Narwal.Permission.DependencyInjection;
using Narwal.Permission.Domain;
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

    [Fact]
    public async Task AddRolePermission_registers_a_scoped_recorder()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<RolePermissionDbContext>(options => options.UseSqlite(connection));
        services.AddRolePermission<RolePermissionDbContext, Guid>();

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var contract = typeof(RolePermissionAssignmentChange<Guid>).Assembly.GetType(
            "Narwal.Permission.Services.IRolePermissionAuditRecorder`1");
        Assert.NotNull(contract);
        var closedContract = contract!.MakeGenericType(typeof(Guid));
        var first = scope.ServiceProvider.GetService(closedContract);
        var second = scope.ServiceProvider.GetService(closedContract);

        Assert.NotNull(first);
        Assert.Same(first, second);
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
