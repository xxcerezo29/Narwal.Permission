using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Narwal.Permission.DependencyInjection;
using Narwal.Permission.Domain;
using Narwal.Permission.Services;
using Narwal.Permission.Tests.TestModels;
using RoleEntity = Narwal.Permission.Domain.Role;
using UserPermissionEntity = Narwal.Permission.Domain.UserPermission<System.Guid>;
using UserRoleEntity = Narwal.Permission.Domain.UserRole<System.Guid>;

namespace Narwal.Permission.Tests.Services;

public sealed class RolePermissionAuditAssignmentTests : IAsyncLifetime
{
    private readonly MutableActorProvider _actorProvider = new(Guid.Empty);
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
    public async Task User_assignment_and_sync_methods_record_each_real_change()
    {
        var userId = Guid.NewGuid();
        await _manager.CreateRoleAsync("reader", "Reader");
        await _manager.CreateRoleAsync("writer", "Writer");
        await _manager.CreateRoleAsync("admin", "Administrator");
        await _manager.CreatePermissionAsync("posts.edit", "Edit posts");
        await _manager.CreatePermissionAsync("posts.view", "View posts");
        await _manager.CreatePermissionAsync("posts.delete", "Delete posts");

        await _manager.AssignRoleAsync(userId, "reader");
        await _manager.AssignRoleAsync(userId, "reader");
        await _manager.AssignRoleAsync(userId, "writer");
        await _manager.RemoveRoleAsync(userId, "admin");
        await _manager.SyncUserRolesAsync(userId, ["writer", "admin"]);
        await _manager.SyncUserRolesAsync(userId, ["writer", "admin"]);

        await _manager.GrantPermissionToUserAsync(userId, "posts.edit");
        await _manager.GrantPermissionToUserAsync(userId, "posts.edit");
        await _manager.GrantPermissionToUserAsync(userId, "posts.view");
        await _manager.SyncUserPermissionsAsync(userId, ["posts.view", "posts.delete"]);
        await _manager.SyncUserPermissionsAsync(userId, ["posts.view", "posts.delete"]);
        await _manager.RevokePermissionFromUserAsync(userId, "posts.view");
        await _manager.RevokePermissionFromUserAsync(userId, "posts.view");

        var events = _context.Set<RolePermissionAuditEntry<Guid>>().Local.ToArray();
        var assignmentEvents = events.Where(entry =>
            entry.Action is RolePermissionAuditActions.UserRoleAssigned
                or RolePermissionAuditActions.UserRoleRemoved
                or RolePermissionAuditActions.UserPermissionGranted
                or RolePermissionAuditActions.UserPermissionRevoked).ToArray();

        Assert.Equal(9, assignmentEvents.Length);
        Assert.Equal(3, assignmentEvents.Count(entry => entry.Action == RolePermissionAuditActions.UserRoleAssigned));
        Assert.Equal(1, assignmentEvents.Count(entry => entry.Action == RolePermissionAuditActions.UserRoleRemoved));
        Assert.Equal(3, assignmentEvents.Count(entry => entry.Action == RolePermissionAuditActions.UserPermissionGranted));
        Assert.Equal(2, assignmentEvents.Count(entry => entry.Action == RolePermissionAuditActions.UserPermissionRevoked));
        Assert.Contains(assignmentEvents, entry =>
            entry.Action == RolePermissionAuditActions.UserRoleAssigned && entry.RoleCode == "admin");
        Assert.Contains(assignmentEvents, entry =>
            entry.Action == RolePermissionAuditActions.UserRoleRemoved && entry.RoleCode == "reader");
        Assert.Contains(assignmentEvents, entry =>
            entry.Action == RolePermissionAuditActions.UserPermissionGranted
            && entry.PermissionCode == "posts.delete");
        Assert.Contains(assignmentEvents, entry =>
            entry.Action == RolePermissionAuditActions.UserPermissionRevoked
            && entry.PermissionCode == "posts.edit");
        Assert.All(assignmentEvents, entry =>
        {
            Assert.True(entry.HasActorUserId);
            Assert.Equal(Guid.Empty, entry.ActorUserId);
            Assert.True(entry.HasAffectedUserId);
            Assert.Equal(userId, entry.AffectedUserId);
        });
    }

    [Fact]
    public async Task Reassigning_links_after_removal_records_restoration_events()
    {
        var userId = Guid.NewGuid();
        await _manager.CreateRoleAsync("reader", "Reader");
        await _manager.CreatePermissionAsync("posts.view", "View posts");
        await _manager.GrantPermissionToRoleAsync("reader", "posts.view");
        await _manager.AssignRoleAsync(userId, "reader");
        await _manager.GrantPermissionToUserAsync(userId, "posts.view");
        await _context.SaveChangesAsync();

        await _manager.RevokePermissionFromRoleAsync("reader", "posts.view");
        await _manager.GrantPermissionToRoleAsync("reader", "posts.view");
        await _manager.RemoveRoleAsync(userId, "reader");
        await _manager.AssignRoleAsync(userId, "reader");
        await _manager.RevokePermissionFromUserAsync(userId, "posts.view");
        await _manager.GrantPermissionToUserAsync(userId, "posts.view");

        Assert.Equal(2, Count(RolePermissionAuditActions.RolePermissionGranted));
        Assert.Equal(1, Count(RolePermissionAuditActions.RolePermissionRevoked));
        Assert.Equal(2, Count(RolePermissionAuditActions.UserRoleAssigned));
        Assert.Equal(1, Count(RolePermissionAuditActions.UserRoleRemoved));
        Assert.Equal(2, Count(RolePermissionAuditActions.UserPermissionGranted));
        Assert.Equal(1, Count(RolePermissionAuditActions.UserPermissionRevoked));

        await _context.SaveChangesAsync();

        Assert.Equal(1, await _context.Set<Narwal.Permission.Domain.RolePermissionGrant>().CountAsync());
        Assert.Equal(1, await _context.Set<UserRoleEntity>().CountAsync());
        Assert.Equal(1, await _context.Set<UserPermissionEntity>().CountAsync());
    }

    [Fact]
    public async Task Sync_methods_record_restoration_events_after_pending_removals()
    {
        var userId = Guid.NewGuid();
        await _manager.CreateRoleAsync("reader", "Reader");
        await _manager.CreatePermissionAsync("posts.view", "View posts");
        await _manager.GrantPermissionToRoleAsync("reader", "posts.view");
        await _manager.AssignRoleAsync(userId, "reader");
        await _manager.GrantPermissionToUserAsync(userId, "posts.view");
        await _context.SaveChangesAsync();

        await _manager.RevokePermissionFromRoleAsync("reader", "posts.view");
        await _manager.SyncRolePermissionsAsync("reader", ["posts.view"]);
        await _manager.RemoveRoleAsync(userId, "reader");
        await _manager.SyncUserRolesAsync(userId, ["reader"]);
        await _manager.RevokePermissionFromUserAsync(userId, "posts.view");
        await _manager.SyncUserPermissionsAsync(userId, ["posts.view"]);

        Assert.Equal(2, Count(RolePermissionAuditActions.RolePermissionGranted));
        Assert.Equal(1, Count(RolePermissionAuditActions.RolePermissionRevoked));
        Assert.Equal(2, Count(RolePermissionAuditActions.UserRoleAssigned));
        Assert.Equal(1, Count(RolePermissionAuditActions.UserRoleRemoved));
        Assert.Equal(2, Count(RolePermissionAuditActions.UserPermissionGranted));
        Assert.Equal(1, Count(RolePermissionAuditActions.UserPermissionRevoked));

        await _context.SaveChangesAsync();

        Assert.Equal(1, await _context.Set<Narwal.Permission.Domain.RolePermissionGrant>().CountAsync());
        Assert.Equal(1, await _context.Set<UserRoleEntity>().CountAsync());
        Assert.Equal(1, await _context.Set<UserPermissionEntity>().CountAsync());
    }

    [Fact]
    public async Task Audit_history_query_orders_by_entry_id_in_sqlite()
    {
        var userId = Guid.NewGuid();
        await _manager.CreateRoleAsync("reader", "Reader");
        await _manager.AssignRoleAsync(userId, "reader");
        await _manager.CreateRoleAsync("writer", "Writer");
        await _manager.AssignRoleAsync(userId, "writer");
        await _context.SaveChangesAsync();

        var history = await _context.Set<RolePermissionAuditEntry<Guid>>()
            .Where(entry => entry.HasAffectedUserId && entry.AffectedUserId == userId)
            .OrderByDescending(entry => entry.Id)
            .ToListAsync();

        Assert.Equal(2, history.Count);
        Assert.True(history[0].Id > history[1].Id);
    }

    [Fact]
    public async Task Missing_actor_is_distinct_from_guid_empty()
    {
        _actorProvider.HasActor = false;
        var userId = Guid.NewGuid();
        await _manager.CreateRoleAsync("reader", "Reader");
        await _manager.AssignRoleAsync(userId, "reader");

        var events = _context.Set<RolePermissionAuditEntry<Guid>>().Local.ToArray();
        var assignment = Assert.Single(events, entry => entry.Action == RolePermissionAuditActions.UserRoleAssigned);

        Assert.All(events, entry => Assert.False(entry.HasActorUserId));
        Assert.Equal(Guid.Empty, assignment.ActorUserId);
        Assert.True(assignment.HasAffectedUserId);
        Assert.Equal(userId, assignment.AffectedUserId);
    }

    [Fact]
    public async Task Host_save_persists_rbac_and_audit_rows_together()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var unsavedServices = CreateServices(connection, new MutableActorProvider(Guid.Empty));
        await using (unsavedServices)
        {
            await using var unsavedScope = unsavedServices.CreateAsyncScope();
            var unsavedContext = unsavedScope.ServiceProvider.GetRequiredService<AuditedRolePermissionDbContext>();
            var unsavedManager = unsavedScope.ServiceProvider.GetRequiredService<IRolePermissionManager<Guid>>();
            await unsavedContext.Database.EnsureCreatedAsync();
            await unsavedManager.CreateRoleAsync("reader", "Reader");
            await unsavedManager.AssignRoleAsync(Guid.NewGuid(), "reader");
        }

        var services = CreateServices(connection, new MutableActorProvider(Guid.Empty));
        await using (services)
        {
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AuditedRolePermissionDbContext>();
            var manager = scope.ServiceProvider.GetRequiredService<IRolePermissionManager<Guid>>();

            Assert.Equal(0, await context.Set<RolePermissionAuditEntry<Guid>>().CountAsync());
            Assert.Equal(0, await context.Set<RoleEntity>().CountAsync());
            Assert.Equal(0, await context.Set<UserRoleEntity>().CountAsync());

            await manager.CreateRoleAsync("reader", "Reader");
            await manager.AssignRoleAsync(Guid.NewGuid(), "reader");
            Assert.Contains(context.ChangeTracker.Entries<RoleEntity>(), entry => entry.State == EntityState.Added);
            Assert.Contains(context.ChangeTracker.Entries<UserRoleEntity>(), entry => entry.State == EntityState.Added);
            Assert.Equal(2, context.ChangeTracker.Entries<RolePermissionAuditEntry<Guid>>()
                .Count(entry => entry.State == EntityState.Added));

            await context.SaveChangesAsync();

            Assert.Equal(1, await context.Set<RoleEntity>().CountAsync());
            Assert.Equal(1, await context.Set<UserRoleEntity>().CountAsync());
            Assert.Equal(2, await context.Set<RolePermissionAuditEntry<Guid>>().CountAsync());
        }
    }

    [Fact]
    public async Task User_deletion_preserves_audit_snapshots_and_clears_optional_user_links()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var actorId = Guid.NewGuid();
        var affectedUserId = Guid.NewGuid();
        var actorProvider = new MutableActorProvider(actorId);
        var services = CreateRelationshipServices(connection, actorProvider);
        await using (services)
        {
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<RelationshipAuditedRolePermissionDbContext>();
            var manager = scope.ServiceProvider.GetRequiredService<IRolePermissionManager<Guid>>();
            await context.Database.EnsureCreatedAsync();
            var actor = new AuditedApplicationUser(actorId);
            var affectedUser = new AuditedApplicationUser(affectedUserId);
            context.Users.AddRange(actor, affectedUser);
            await context.SaveChangesAsync();

            await manager.CreateRoleAsync("reader", "Reader");
            await manager.AssignRoleAsync(affectedUserId, "reader");
            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();
            var users = await context.Users
                .Include(user => user.AuditEventsAsActor)
                .Include(user => user.AuditEventsAbout)
                .ToListAsync();
            actor = users.Single(user => user.Id == actorId);
            affectedUser = users.Single(user => user.Id == affectedUserId);

            var audit = await context.Set<RolePermissionAuditEntry<Guid>>()
                .SingleAsync(entry => entry.Action == RolePermissionAuditActions.UserRoleAssigned);
            var trackedAudit = context.Entry(audit);
            Assert.Equal(actorId, trackedAudit.Property<Guid?>("ActorRelationshipUserId").CurrentValue);
            Assert.Equal(affectedUserId, trackedAudit.Property<Guid?>("AffectedRelationshipUserId").CurrentValue);
            Assert.True(audit.HasActorUserId);
            Assert.Equal(actorId, audit.ActorUserId);
            Assert.True(audit.HasAffectedUserId);
            Assert.Equal(affectedUserId, audit.AffectedUserId);

            context.Users.RemoveRange(actor, affectedUser);
            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();
            var retainedAudits = await context.Set<RolePermissionAuditEntry<Guid>>().ToListAsync();
            Assert.Equal(2, retainedAudits.Count);
            var retainedAssignment = Assert.Single(retainedAudits,
                entry => entry.Action == RolePermissionAuditActions.UserRoleAssigned);
            Assert.Equal(actorId, retainedAssignment.ActorUserId);
            Assert.Equal(affectedUserId, retainedAssignment.AffectedUserId);
            Assert.Null(context.Entry(retainedAssignment)
                .Property<Guid?>("ActorRelationshipUserId").CurrentValue);
            Assert.Null(context.Entry(retainedAssignment)
                .Property<Guid?>("AffectedRelationshipUserId").CurrentValue);
            Assert.All(retainedAudits, retainedAudit =>
            {
                Assert.Null(context.Entry(retainedAudit)
                    .Property<Guid?>("ActorRelationshipUserId").CurrentValue);
                Assert.Null(context.Entry(retainedAudit)
                    .Property<Guid?>("AffectedRelationshipUserId").CurrentValue);
            });
        }
    }

    private static ServiceProvider CreateServices(
        SqliteConnection connection,
        IRolePermissionAuditActorProvider<Guid> actorProvider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AuditedRolePermissionDbContext>(options => options.UseSqlite(connection));
        services.AddScoped<IRolePermissionAuditActorProvider<Guid>>(_ => actorProvider);
        services.AddRolePermission<AuditedRolePermissionDbContext, Guid>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static ServiceProvider CreateRelationshipServices(
        SqliteConnection connection,
        IRolePermissionAuditActorProvider<Guid> actorProvider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<RelationshipAuditedRolePermissionDbContext>(options => options.UseSqlite(connection));
        services.AddScoped<IRolePermissionAuditActorProvider<Guid>>(_ => actorProvider);
        services.AddRolePermission<RelationshipAuditedRolePermissionDbContext, Guid>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private int Count(string action) => _context.Set<RolePermissionAuditEntry<Guid>>().Local
        .Count(entry => entry.Action == action);

    private sealed class MutableActorProvider(Guid actorId) : IRolePermissionAuditActorProvider<Guid>
    {
        public bool HasActor { get; set; } = true;

        public bool TryGetActorUserId(out Guid userId)
        {
            userId = actorId;
            return HasActor;
        }
    }
}
