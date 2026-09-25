using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Narwal.Permission.DependencyInjection;
using Narwal.Permission.Tests.TestModels;
using UserPermissionEntity = Narwal.Permission.Domain.UserPermission<System.Guid>;
using UserRoleEntity = Narwal.Permission.Domain.UserRole<System.Guid>;

namespace Narwal.Permission.Tests.Services;

public sealed class RelationshipModeManagerTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;
    private RelationshipRolePermissionDbContext _context = null!;
    private Narwal.Permission.Services.IRolePermissionManager<Guid> _manager = null!;
    private Narwal.Permission.Services.IPermissionChecker<Guid> _checker = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<RelationshipRolePermissionDbContext>(options => options.UseSqlite(_connection));
        services.AddRolePermission<RelationshipRolePermissionDbContext, Guid>();
        _serviceProvider = services.BuildServiceProvider(validateScopes: true);
        _scope = _serviceProvider.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<RelationshipRolePermissionDbContext>();
        _manager = _scope.ServiceProvider
            .GetRequiredService<Narwal.Permission.Services.IRolePermissionManager<Guid>>();
        _checker = _scope.ServiceProvider
            .GetRequiredService<Narwal.Permission.Services.IPermissionChecker<Guid>>();

        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Manager_assignments_populate_read_only_user_relationships_and_cascade_on_delete()
    {
        var user = new ApplicationUser(Guid.NewGuid());
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        await _manager.CreateRoleAsync("editor", "Editor");
        await _manager.CreatePermissionAsync("posts.edit", "Edit posts");
        await _manager.GrantPermissionToRoleAsync("editor", "posts.edit");
        await _manager.AssignRoleAsync(user.Id, "editor");
        await _manager.GrantPermissionToUserAsync(user.Id, "posts.edit");
        await _context.SaveChangesAsync();

        Assert.True(await _checker.HasPermissionAsync(user.Id, "posts.edit"));
        Assert.Equal("editor", Assert.Single(user.RoleAssignments).RoleCode);
        Assert.Equal("posts.edit", Assert.Single(user.PermissionAssignments).PermissionCode);

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        Assert.Empty(await _context.Set<UserRoleEntity>().ToListAsync());
        Assert.Empty(await _context.Set<UserPermissionEntity>().ToListAsync());
    }
}
