using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Narwal.Permission.DependencyInjection;
using Narwal.Permission.Tests.TestModels;

namespace Narwal.Permission.Tests.Services;

public sealed class PermissionCheckerTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;
    private RolePermissionDbContext _context = null!;
    private Narwal.Permission.Services.IRolePermissionManager<Guid> _manager = null!;
    private Narwal.Permission.Services.IPermissionChecker<Guid> _checker = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<RolePermissionDbContext>(options => options.UseSqlite(_connection));
        services.AddRolePermission<RolePermissionDbContext, Guid>();
        _serviceProvider = services.BuildServiceProvider(validateScopes: true);
        _scope = _serviceProvider.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<RolePermissionDbContext>();
        _manager = _scope.ServiceProvider.GetRequiredService<Narwal.Permission.Services.IRolePermissionManager<Guid>>();
        _checker = _scope.ServiceProvider.GetRequiredService<Narwal.Permission.Services.IPermissionChecker<Guid>>();

        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Checks_direct_and_role_permissions_with_any_and_all_semantics()
    {
        var assignedUserId = Guid.NewGuid();
        var unassignedUserId = Guid.NewGuid();
        await _manager.CreateRoleAsync("editor", "Editor");
        await _manager.CreateRoleAsync("reviewer", "Reviewer");
        await _manager.CreatePermissionAsync("posts.edit", "Edit posts");
        await _manager.CreatePermissionAsync("posts.view", "View posts");
        await _manager.CreatePermissionAsync("users.manage", "Manage users");
        await _manager.GrantPermissionToRoleAsync("editor", "posts.edit");
        await _manager.GrantPermissionToUserAsync(assignedUserId, "users.manage");
        await _manager.AssignRoleAsync(assignedUserId, "editor");
        await _manager.AssignRoleAsync(assignedUserId, "reviewer");
        await _context.SaveChangesAsync();

        Assert.True(await _checker.HasRoleAsync(assignedUserId, "EDITOR"));
        Assert.False(await _checker.HasRoleAsync(unassignedUserId, "editor"));
        Assert.True(await _checker.HasAnyRoleAsync(assignedUserId, ["editor", "missing"]));
        Assert.True(await _checker.HasAllRolesAsync(assignedUserId, ["editor", "reviewer"]));
        Assert.False(await _checker.HasAllRolesAsync(assignedUserId, ["editor", "missing"]));
        Assert.False(await _checker.HasAnyRoleAsync(assignedUserId, Array.Empty<string>()));
        Assert.False(await _checker.HasAllRolesAsync(assignedUserId, Array.Empty<string>()));

        Assert.True(await _checker.HasPermissionAsync(assignedUserId, "posts.edit"));
        Assert.True(await _checker.HasPermissionAsync(assignedUserId, "users.manage"));
        Assert.False(await _checker.HasPermissionAsync(unassignedUserId, "posts.edit"));
        Assert.True(await _checker.HasAnyPermissionAsync(assignedUserId, ["posts.view", "POSTS.EDIT"]));
        Assert.True(await _checker.HasAllPermissionsAsync(assignedUserId, ["posts.edit", "users.manage"]));
        Assert.False(await _checker.HasAllPermissionsAsync(assignedUserId, ["posts.edit", "posts.view"]));
        Assert.False(await _checker.HasAnyPermissionAsync(assignedUserId, Array.Empty<string>()));
        Assert.False(await _checker.HasAllPermissionsAsync(assignedUserId, Array.Empty<string>()));
        Assert.False(await _checker.HasPermissionAsync(assignedUserId, "posts.*"));
        Assert.False(await _checker.HasPermissionAsync(assignedUserId, "unknown.permission"));
    }
}
