using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Narwal.Permission.DependencyInjection;
using Narwal.Permission.Domain;
using Narwal.Permission.Services;
using Narwal.Permission.Tests.TestModels;
using RoleEntity = Narwal.Permission.Domain.Role;

namespace Narwal.Permission.Tests.Services;

public sealed class RolePermissionAuditLifecycleTests : IAsyncLifetime
{
    private readonly FixedActorProvider _actorProvider = new(Guid.Empty);
    private SqliteConnection _connection = null!;
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;
    private AuditedRolePermissionDbContext _context = null!;
    private IRolePermissionManager<Guid> _manager = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AuditedRolePermissionDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<IRolePermissionAuditActorProvider<Guid>>(_ => _actorProvider);
        services.AddRolePermission<AuditedRolePermissionDbContext, Guid>();
        _serviceProvider = services.BuildServiceProvider(validateScopes: true);
        _scope = _serviceProvider.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<AuditedRolePermissionDbContext>();
        _manager = _scope.ServiceProvider.GetRequiredService<IRolePermissionManager<Guid>>();

        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Create_rename_and_role_permission_sync_record_expected_snapshots()
    {
        await _manager.CreateRoleAsync("editor", "Editor");
        await _manager.RenameRoleAsync("editor", "Content editor");
        await _manager.CreatePermissionAsync("posts.edit", "Edit posts");
        await _manager.RenamePermissionAsync("posts.edit", "Update posts");
        await _manager.CreatePermissionAsync("posts.view", "View posts");
        await _manager.CreatePermissionAsync("posts.delete", "Delete posts");
        await _manager.GrantPermissionToRoleAsync("editor", "posts.edit");
        await _manager.GrantPermissionToRoleAsync("editor", "posts.view");
        await _manager.GrantPermissionToRoleAsync("editor", "posts.edit");
        await _manager.SyncRolePermissionsAsync("editor", ["posts.edit", "posts.delete"]);
        await _manager.SyncRolePermissionsAsync("editor", ["posts.edit", "posts.delete"]);
        await _manager.RevokePermissionFromRoleAsync("editor", "posts.edit");
        await _manager.RevokePermissionFromRoleAsync("editor", "posts.edit");

        var events = _context.Set<RolePermissionAuditEntry<Guid>>().Local.ToArray();

        Assert.Equal(11, events.Length);
        var roleCreated = Assert.Single(events, entry => entry.Action == RolePermissionAuditActions.RoleCreated);
        Assert.Equal("editor", roleCreated.RoleCode);
        Assert.Equal("Editor", roleCreated.NewName);

        var roleRenamed = Assert.Single(events, entry => entry.Action == RolePermissionAuditActions.RoleRenamed);
        Assert.Equal("Editor", roleRenamed.PreviousName);
        Assert.Equal("Content editor", roleRenamed.NewName);

        var permissionCreated = Assert.Single(events, entry =>
            entry.Action == RolePermissionAuditActions.PermissionCreated && entry.PermissionCode == "posts.edit");
        Assert.Equal("Edit posts", permissionCreated.NewName);
        var permissionRenamed = Assert.Single(events, entry => entry.Action == RolePermissionAuditActions.PermissionRenamed);
        Assert.Equal("Edit posts", permissionRenamed.PreviousName);
        Assert.Equal("Update posts", permissionRenamed.NewName);

        Assert.Equal(3, events.Count(entry => entry.Action == RolePermissionAuditActions.RolePermissionGranted));
        Assert.Equal(2, events.Count(entry => entry.Action == RolePermissionAuditActions.RolePermissionRevoked));
        Assert.Contains(events, entry =>
            entry.Action == RolePermissionAuditActions.RolePermissionGranted
            && entry.RoleCode == "editor"
            && entry.PermissionCode == "posts.delete");
        Assert.Contains(events, entry =>
            entry.Action == RolePermissionAuditActions.RolePermissionRevoked
            && entry.RoleCode == "editor"
            && entry.PermissionCode == "posts.view");

        Assert.All(events, entry =>
        {
            Assert.True(entry.HasActorUserId);
            Assert.Equal(Guid.Empty, entry.ActorUserId);
            Assert.False(entry.HasAffectedUserId);
            Assert.Equal(TimeSpan.Zero, entry.OccurredAtUtc.Offset);
        });
    }

    [Fact]
    public async Task Deleting_a_role_records_each_removed_grant_and_user_assignment()
    {
        var userId = Guid.NewGuid();
        await _manager.CreateRoleAsync("editor", "Editor");
        await _manager.CreatePermissionAsync("posts.edit", "Edit posts");
        await _manager.GrantPermissionToRoleAsync("editor", "posts.edit");
        await _manager.AssignRoleAsync(userId, "editor");
        await _context.SaveChangesAsync();
        var existingEvents = _context.Set<RolePermissionAuditEntry<Guid>>().Local.ToHashSet();

        await _manager.DeleteRoleAsync("editor");

        var events = _context.Set<RolePermissionAuditEntry<Guid>>().Local
            .Where(entry => !existingEvents.Contains(entry))
            .ToArray();
        Assert.Equal(3, events.Length);
        Assert.Contains(events, entry =>
            entry.Action == RolePermissionAuditActions.RolePermissionRevoked
            && entry.RoleCode == "editor"
            && entry.PermissionCode == "posts.edit");
        Assert.Contains(events, entry =>
            entry.Action == RolePermissionAuditActions.UserRoleRemoved
            && entry.RoleCode == "editor"
            && entry.HasAffectedUserId
            && entry.AffectedUserId == userId);
        var deleted = Assert.Single(events, entry => entry.Action == RolePermissionAuditActions.RoleDeleted);
        Assert.Equal("editor", deleted.RoleCode);
        Assert.Equal("Editor", deleted.PreviousName);
    }

    [Fact]
    public async Task Deleting_a_permission_records_removed_grants_and_direct_assignments()
    {
        var userId = Guid.NewGuid();
        await _manager.CreateRoleAsync("editor", "Editor");
        await _manager.CreatePermissionAsync("posts.edit", "Edit posts");
        await _manager.GrantPermissionToRoleAsync("editor", "posts.edit");
        await _manager.GrantPermissionToUserAsync(userId, "posts.edit");
        await _context.SaveChangesAsync();
        var existingEvents = _context.Set<RolePermissionAuditEntry<Guid>>().Local.ToHashSet();

        await _manager.DeletePermissionAsync("posts.edit");

        var events = _context.Set<RolePermissionAuditEntry<Guid>>().Local
            .Where(entry => !existingEvents.Contains(entry))
            .ToArray();
        Assert.Equal(3, events.Length);
        Assert.Contains(events, entry =>
            entry.Action == RolePermissionAuditActions.RolePermissionRevoked
            && entry.RoleCode == "editor"
            && entry.PermissionCode == "posts.edit");
        Assert.Contains(events, entry =>
            entry.Action == RolePermissionAuditActions.UserPermissionRevoked
            && entry.PermissionCode == "posts.edit"
            && entry.HasAffectedUserId
            && entry.AffectedUserId == userId);
        var deleted = Assert.Single(events, entry => entry.Action == RolePermissionAuditActions.PermissionDeleted);
        Assert.Equal("posts.edit", deleted.PermissionCode);
        Assert.Equal("Edit posts", deleted.PreviousName);
    }

    [Fact]
    public async Task Failed_and_no_op_lifecycle_methods_do_not_create_audit_entries()
    {
        await _manager.CreateRoleAsync("editor", "Editor");
        await _manager.CreatePermissionAsync("posts.edit", "Edit posts");
        await _manager.CreatePermissionAsync("posts.view", "View posts");
        await _manager.GrantPermissionToRoleAsync("editor", "posts.edit");
        var eventCount = _context.Set<RolePermissionAuditEntry<Guid>>().Local.Count;
        _actorProvider.ThrowOnGet = true;

        await Assert.ThrowsAsync<RolePermissionAlreadyExistsException>(
            () => _manager.CreateRoleAsync("EDITOR", "Another editor"));
        await Assert.ThrowsAsync<RolePermissionAlreadyExistsException>(
            () => _manager.CreatePermissionAsync("POSTS.EDIT", "Another label"));
        await Assert.ThrowsAsync<RolePermissionNotFoundException>(
            () => _manager.RenameRoleAsync("missing", "Missing"));
        await Assert.ThrowsAsync<RolePermissionNotFoundException>(
            () => _manager.RenamePermissionAsync("missing", "Missing"));
        await Assert.ThrowsAsync<RolePermissionNotFoundException>(
            () => _manager.GrantPermissionToRoleAsync("missing", "posts.edit"));
        await Assert.ThrowsAsync<RolePermissionNotFoundException>(
            () => _manager.GrantPermissionToRoleAsync("editor", "missing"));

        await _manager.GrantPermissionToRoleAsync("editor", "posts.edit");
        await _manager.RevokePermissionFromRoleAsync("editor", "posts.view");
        await _manager.SyncRolePermissionsAsync("editor", ["posts.edit"]);

        Assert.Equal(eventCount, _context.Set<RolePermissionAuditEntry<Guid>>().Local.Count);
    }

    [Fact]
    public async Task Actor_provider_failure_leaves_manager_state_unchanged()
    {
        _actorProvider.ThrowOnGet = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.CreateRoleAsync("editor", "Editor"));

        Assert.Empty(_context.Set<RoleEntity>().Local);
        Assert.Empty(_context.Set<RolePermissionAuditEntry<Guid>>().Local);
    }

    private sealed class FixedActorProvider(Guid actorId) : IRolePermissionAuditActorProvider<Guid>
    {
        public bool ThrowOnGet { get; set; }

        public bool TryGetActorUserId(out Guid userId)
        {
            if (ThrowOnGet)
            {
                throw new InvalidOperationException("Actor resolution failed.");
            }

            userId = actorId;
            return true;
        }
    }
}
