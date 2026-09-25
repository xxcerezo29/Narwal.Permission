using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Narwal.Permission.DependencyInjection;
using Narwal.Permission.Tests.TestModels;
using PermissionEntity = Narwal.Permission.Domain.Permission;
using RoleEntity = Narwal.Permission.Domain.Role;
using RolePermissionAlreadyExistsException = Narwal.Permission.Domain.RolePermissionAlreadyExistsException;
using RolePermissionNotFoundException = Narwal.Permission.Domain.RolePermissionNotFoundException;
using UserPermissionEntity = Narwal.Permission.Domain.UserPermission<System.Guid>;
using UserRoleEntity = Narwal.Permission.Domain.UserRole<System.Guid>;
using RolePermissionGrantEntity = Narwal.Permission.Domain.RolePermissionGrant;

namespace Narwal.Permission.Tests.Services;

public sealed class RolePermissionManagerTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;
    private RolePermissionDbContext _context = null!;
    private Narwal.Permission.Services.IRolePermissionManager<Guid> _manager = null!;

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
        _manager = _scope.ServiceProvider
            .GetRequiredService<Narwal.Permission.Services.IRolePermissionManager<Guid>>();

        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Create_and_rename_preserve_code_and_leave_saving_to_the_host()
    {
        var role = await _manager.CreateRoleAsync(" EDITOR ", "  Editor  ");
        var permission = await _manager.CreatePermissionAsync(" Posts.Edit ", "  Edit posts  ");
        await _manager.RenameRoleAsync("EDITOR", "Content editor");
        await _manager.RenamePermissionAsync("posts.edit", "Modify posts");

        Assert.Equal("editor", role.Code);
        Assert.Equal("Content editor", role.Name);
        Assert.Equal("posts.edit", permission.Code);
        Assert.Equal("Modify posts", permission.Name);
        Assert.Equal(EntityState.Added, _context.Entry(role).State);
        Assert.Equal(EntityState.Added, _context.Entry(permission).State);
        Assert.Equal(0, await _context.Set<RoleEntity>().CountAsync());
        Assert.Equal(0, await _context.Set<PermissionEntity>().CountAsync());

        await _context.SaveChangesAsync();

        Assert.Equal("editor", (await _context.Set<RoleEntity>().SingleAsync()).Code);
        Assert.Equal("posts.edit", (await _context.Set<PermissionEntity>().SingleAsync()).Code);
    }

    [Fact]
    public async Task Duplicate_codes_are_rejected_case_insensitively()
    {
        await _manager.CreateRoleAsync("editor", "Editor");
        await _manager.CreatePermissionAsync("posts.edit", "Edit posts");

        var roleException = await Assert.ThrowsAsync<RolePermissionAlreadyExistsException>(
            () => _manager.CreateRoleAsync("EDITOR", "Another editor"));
        var permissionException = await Assert.ThrowsAsync<RolePermissionAlreadyExistsException>(
            () => _manager.CreatePermissionAsync(" Posts.Edit ", "Another label"));

        Assert.Equal("editor", roleException.Code);
        Assert.Equal("posts.edit", permissionException.Code);
    }

    [Fact]
    public async Task Operations_requiring_missing_entities_throw_typed_errors()
    {
        var missingRole = await Assert.ThrowsAsync<RolePermissionNotFoundException>(
            () => _manager.GrantPermissionToRoleAsync("unknown", "posts.edit"));
        await _manager.CreateRoleAsync("editor", "Editor");
        var missingPermission = await Assert.ThrowsAsync<RolePermissionNotFoundException>(
            () => _manager.GrantPermissionToRoleAsync("editor", "posts.edit"));

        Assert.Equal("Role", missingRole.EntityType);
        Assert.Equal("unknown", missingRole.Code);
        Assert.Equal("Permission", missingPermission.EntityType);
        Assert.Equal("posts.edit", missingPermission.Code);
    }

    [Fact]
    public async Task Grants_and_sync_methods_are_idempotent_before_one_save()
    {
        var userId = Guid.NewGuid();
        await _manager.CreateRoleAsync("writer", "Writer");
        await _manager.CreateRoleAsync("reader", "Reader");
        await _manager.CreatePermissionAsync("posts.edit", "Edit posts");
        await _manager.CreatePermissionAsync("posts.view", "View posts");
        await _manager.GrantPermissionToRoleAsync("writer", "posts.edit");
        await _manager.GrantPermissionToRoleAsync("writer", "POSTS.EDIT");
        await _manager.GrantPermissionToRoleAsync("writer", "posts.view");
        await _manager.SyncRolePermissionsAsync("writer", ["posts.view"]);
        await _manager.AssignRoleAsync(userId, "writer");
        await _manager.AssignRoleAsync(userId, "WRITER");
        await _manager.SyncUserRolesAsync(userId, ["reader"]);
        await _manager.GrantPermissionToUserAsync(userId, "posts.edit");
        await _manager.GrantPermissionToUserAsync(userId, "POSTS.EDIT");
        await _manager.GrantPermissionToUserAsync(userId, "posts.view");
        await _manager.SyncUserPermissionsAsync(userId, ["posts.view"]);

        await _context.SaveChangesAsync();

        Assert.Equal(
            new[] { "posts.view" },
            await _context.Set<RolePermissionGrantEntity>()
                .Where(grant => grant.RoleCode == "writer")
                .Select(grant => grant.PermissionCode)
                .ToArrayAsync());
        Assert.Equal(
            new[] { "reader" },
            await _context.Set<UserRoleEntity>()
                .Where(assignment => assignment.UserId == userId)
                .Select(assignment => assignment.RoleCode)
                .ToArrayAsync());
        Assert.Equal(
            new[] { "posts.view" },
            await _context.Set<UserPermissionEntity>()
                .Where(assignment => assignment.UserId == userId)
                .Select(assignment => assignment.PermissionCode)
                .ToArrayAsync());
    }

    [Fact]
    public async Task Deleting_a_role_preserves_permissions_and_deleting_permission_removes_its_links()
    {
        var userId = Guid.NewGuid();
        await _manager.CreateRoleAsync("editor", "Editor");
        await _manager.CreatePermissionAsync("posts.edit", "Edit posts");
        await _manager.GrantPermissionToRoleAsync("editor", "posts.edit");
        await _manager.AssignRoleAsync(userId, "editor");
        await _manager.GrantPermissionToUserAsync(userId, "posts.edit");
        await _context.SaveChangesAsync();

        await _manager.DeleteRoleAsync("editor");
        await _context.SaveChangesAsync();

        Assert.Equal("posts.edit", (await _context.Set<PermissionEntity>().SingleAsync()).Code);
        Assert.Empty(await _context.Set<RolePermissionGrantEntity>().ToListAsync());
        Assert.Empty(await _context.Set<UserRoleEntity>().ToListAsync());
        Assert.Single(await _context.Set<UserPermissionEntity>().ToListAsync());

        await _manager.DeletePermissionAsync("posts.edit");
        await _context.SaveChangesAsync();

        Assert.Empty(await _context.Set<PermissionEntity>().ToListAsync());
        Assert.Empty(await _context.Set<UserPermissionEntity>().ToListAsync());
    }

    [Fact]
    public async Task Removed_links_can_be_restored_before_the_host_saves()
    {
        var userId = Guid.NewGuid();
        await _manager.CreateRoleAsync("editor", "Editor");
        await _manager.CreatePermissionAsync("posts.edit", "Edit posts");
        await _manager.GrantPermissionToRoleAsync("editor", "posts.edit");
        await _manager.AssignRoleAsync(userId, "editor");
        await _manager.GrantPermissionToUserAsync(userId, "posts.edit");
        await _context.SaveChangesAsync();

        await _manager.RevokePermissionFromRoleAsync("editor", "posts.edit");
        await _manager.RemoveRoleAsync(userId, "editor");
        await _manager.RevokePermissionFromUserAsync(userId, "posts.edit");
        await _manager.GrantPermissionToRoleAsync("editor", "posts.edit");
        await _manager.AssignRoleAsync(userId, "editor");
        await _manager.SyncUserPermissionsAsync(userId, ["posts.edit"]);
        await _context.SaveChangesAsync();

        Assert.Single(await _context.Set<RolePermissionGrantEntity>().ToListAsync());
        Assert.Single(await _context.Set<UserRoleEntity>().ToListAsync());
        Assert.Single(await _context.Set<UserPermissionEntity>().ToListAsync());
    }
}
