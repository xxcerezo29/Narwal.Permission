using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Narwal.Permission.DependencyInjection;
using Narwal.Permission.Domain;
using Narwal.Permission.Services;
using Narwal.Permission.Tests.TestModels;

namespace Narwal.Permission.Tests.Services;

public sealed class RolePermissionAggregateAuditTests
{
    [Fact]
    public void Ef_model_does_not_persist_assignment_change_events()
    {
        var options = new DbContextOptionsBuilder<EventedRolePermissionDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var context = new EventedRolePermissionDbContext(options);

        var modelException = Record.Exception(() => _ = context.Model);
        Assert.Null(modelException);
        Assert.Null(context.Model.FindEntityType(typeof(RolePermissionAssignmentChange<Guid>)));
    }

    [Fact]
    public void User_aggregate_methods_queue_changes_only_after_successful_mutations()
    {
        var userId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 9, 26, 15, 0, 0, TimeSpan.FromHours(3));
        var user = new EventedApplicationUser(userId);

        user.AssignRole("posts.edit", occurredAt, actorId);
        user.RevokeRole(" POSTS.EDIT ", occurredAt.AddMinutes(1), actorId);
        user.GrantPermission("posts.read", occurredAt.AddMinutes(2), actorId);
        user.RevokePermission(" POSTS.READ ", occurredAt.AddMinutes(3), actorId);

        Assert.Empty(user.RoleAssignments);
        Assert.Empty(user.PermissionAssignments);
        var changes = user.PendingRolePermissionChanges.ToArray();
        Assert.Equal(4, changes.Length);
        Assert.Equal(
            ["user.role_assigned", "user.role_removed", "user.permission_granted", "user.permission_revoked"],
            changes.Select(change => change.Action).ToArray());
        Assert.All(changes, change =>
        {
            Assert.Equal(userId, change.AffectedUserId);
            Assert.Equal(actorId, change.ActorUserId);
            Assert.True(change.HasActorUserId);
            Assert.Equal(TimeSpan.Zero, change.OccurredAtUtc.Offset);
        });
        Assert.Equal("posts.edit", changes[0].RoleCode);
        Assert.Equal("posts.edit", changes[1].RoleCode);
        Assert.Equal("posts.read", changes[2].PermissionCode);
        Assert.Equal("posts.read", changes[3].PermissionCode);
    }

    [Fact]
    public void Duplicate_and_missing_assignment_operations_leave_the_event_queue_unchanged()
    {
        var user = new EventedApplicationUser(Guid.NewGuid());
        var occurredAt = DateTimeOffset.UnixEpoch;

        user.AssignRole("reader", occurredAt, null);
        Assert.Throws<InvalidOperationException>(() => user.AssignRole(" READER ", occurredAt, null));
        Assert.Throws<InvalidOperationException>(() => user.RevokeRole("writer", occurredAt, null));

        user.GrantPermission("posts.read", occurredAt, null);
        Assert.Throws<InvalidOperationException>(() => user.GrantPermission(" POSTS.READ ", occurredAt, null));
        Assert.Throws<InvalidOperationException>(() => user.RevokePermission("posts.edit", occurredAt, null));

        Assert.Single(user.RoleAssignments);
        Assert.Single(user.PermissionAssignments);
        Assert.Equal(2, user.PendingRolePermissionChanges.Count);
    }

    [Fact]
    public void Invalid_codes_do_not_mutate_assignments_or_queue_events()
    {
        var user = new EventedApplicationUser(Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => user.AssignRole("posts.*", DateTimeOffset.UnixEpoch, null));
        Assert.Throws<ArgumentException>(() => user.GrantPermission("posts.*", DateTimeOffset.UnixEpoch, null));

        Assert.Empty(user.RoleAssignments);
        Assert.Empty(user.PermissionAssignments);
        Assert.Empty(user.PendingRolePermissionChanges);
    }

    [Fact]
    public async Task Dispatch_and_one_host_save_persist_assignments_and_audit_rows_together()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var provider = CreateServices(connection);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EventedRolePermissionDbContext>();
        await context.Database.EnsureCreatedAsync();
        var manager = scope.ServiceProvider.GetRequiredService<IRolePermissionManager<Guid>>();
        var (actorId, user) = await SeedAsync(context, manager);
        var occurredAt = new DateTimeOffset(2026, 9, 26, 8, 30, 0, TimeSpan.FromHours(8));

        user.AssignRole("reader", occurredAt, actorId);
        user.GrantPermission("posts.read", occurredAt, actorId);
        var recorder = scope.ServiceProvider.GetRequiredService<IRolePermissionAuditRecorder<Guid>>();
        foreach (var change in user.PendingRolePermissionChanges)
        {
            recorder.Record(change);
        }

        user.ClearPendingRolePermissionChanges();
        await context.SaveChangesAsync();

        Assert.Equal(1, await context.Set<UserRole<Guid>>().CountAsync());
        Assert.Equal(1, await context.Set<UserPermission<Guid>>().CountAsync());
        var auditEntries = await context.Set<RolePermissionAuditEntry<Guid>>()
            .Where(entry => entry.Action == "user.role_assigned"
                || entry.Action == "user.permission_granted")
            .ToArrayAsync();
        Assert.Equal(2, auditEntries.Length);
        Assert.Contains(auditEntries, entry => entry.Action == "user.role_assigned" && entry.RoleCode == "reader");
        Assert.Contains(auditEntries, entry =>
            entry.Action == "user.permission_granted" && entry.PermissionCode == "posts.read");
        Assert.All(auditEntries, entry =>
        {
            Assert.Equal(actorId, entry.ActorUserId);
            Assert.True(entry.HasActorUserId);
            Assert.Equal(user.Id, entry.AffectedUserId);
            Assert.Equal(occurredAt.ToUniversalTime(), entry.OccurredAtUtc);
            Assert.Equal(actorId,
                context.Entry(entry).Property<Guid?>("ActorRelationshipUserId").CurrentValue);
            Assert.Equal(user.Id,
                context.Entry(entry).Property<Guid?>("AffectedRelationshipUserId").CurrentValue);
        });
    }

    [Fact]
    public async Task Saving_without_dispatch_persists_assignment_without_assignment_audit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var provider = CreateServices(connection);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EventedRolePermissionDbContext>();
        await context.Database.EnsureCreatedAsync();
        var manager = scope.ServiceProvider.GetRequiredService<IRolePermissionManager<Guid>>();
        var (_, user) = await SeedAsync(context, manager);

        user.AssignRole("reader", DateTimeOffset.UnixEpoch, null);
        await context.SaveChangesAsync();

        Assert.Equal(1, await context.Set<UserRole<Guid>>().CountAsync());
        Assert.Equal(0, await context.Set<RolePermissionAuditEntry<Guid>>()
            .CountAsync(entry => entry.Action == "user.role_assigned"));
    }

    [Fact]
    public async Task Dispatch_without_save_does_not_persist_assignment_or_audit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var provider = CreateServices(connection);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EventedRolePermissionDbContext>();
        await context.Database.EnsureCreatedAsync();
        var manager = scope.ServiceProvider.GetRequiredService<IRolePermissionManager<Guid>>();
        var (actorId, user) = await SeedAsync(context, manager);

        user.AssignRole("reader", DateTimeOffset.UnixEpoch, actorId);
        var recorder = scope.ServiceProvider.GetRequiredService<IRolePermissionAuditRecorder<Guid>>();
        foreach (var change in user.PendingRolePermissionChanges)
        {
            recorder.Record(change);
        }

        Assert.Equal(0, await context.Set<UserRole<Guid>>().AsNoTracking().CountAsync());
        Assert.Equal(0, await context.Set<RolePermissionAuditEntry<Guid>>().AsNoTracking()
            .CountAsync(entry => entry.Action == "user.role_assigned"));
    }

    [Fact]
    public async Task Failed_dispatch_does_not_persist_aggregate_changes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var provider = CreateServices(connection);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EventedRolePermissionDbContext>();
        await context.Database.EnsureCreatedAsync();
        var manager = scope.ServiceProvider.GetRequiredService<IRolePermissionManager<Guid>>();
        var (_, user) = await SeedAsync(context, manager);

        user.AssignRole("reader", DateTimeOffset.UnixEpoch, null);
        Assert.Throws<InvalidOperationException>(() => Dispatch(user, new FailingAuditRecorder()));

        Assert.Single(user.PendingRolePermissionChanges);
        Assert.Equal(0, await context.Set<UserRole<Guid>>().AsNoTracking().CountAsync());
        Assert.Equal(0, await context.Set<RolePermissionAuditEntry<Guid>>().AsNoTracking()
            .CountAsync(entry => entry.Action == "user.role_assigned"));
    }

    [Fact]
    public async Task Unknown_role_code_is_rejected_by_the_role_foreign_key_on_save()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var provider = CreateServices(connection);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EventedRolePermissionDbContext>();
        await context.Database.EnsureCreatedAsync();
        var manager = scope.ServiceProvider.GetRequiredService<IRolePermissionManager<Guid>>();
        var (actorId, user) = await SeedAsync(context, manager);

        user.AssignRole("role.does.not.exist", DateTimeOffset.UnixEpoch, actorId);
        var recorder = scope.ServiceProvider.GetRequiredService<IRolePermissionAuditRecorder<Guid>>();
        foreach (var change in user.PendingRolePermissionChanges)
        {
            recorder.Record(change);
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        context.ChangeTracker.Clear();
        Assert.Equal(0, await context.Set<UserRole<Guid>>().AsNoTracking().CountAsync());
        Assert.Equal(0, await context.Set<RolePermissionAuditEntry<Guid>>().AsNoTracking()
            .CountAsync(entry => entry.Action == "user.role_assigned"));
    }

    private static async Task<(Guid ActorId, EventedApplicationUser User)> SeedAsync(
        EventedRolePermissionDbContext context,
        IRolePermissionManager<Guid> manager)
    {
        var actorId = Guid.NewGuid();
        var user = new EventedApplicationUser(Guid.NewGuid());
        context.Users.AddRange(new EventedApplicationUser(actorId), user);
        await context.SaveChangesAsync();
        await manager.CreateRoleAsync("reader", "Reader");
        await manager.CreatePermissionAsync("posts.read", "Read posts");
        await context.SaveChangesAsync();
        return (actorId, user);
    }

    private static ServiceProvider CreateServices(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<EventedRolePermissionDbContext>(options => options.UseSqlite(connection));
        services.AddRolePermission<EventedRolePermissionDbContext, Guid>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static void Dispatch(EventedApplicationUser user, IRolePermissionAuditRecorder<Guid> recorder)
    {
        foreach (var change in user.PendingRolePermissionChanges)
        {
            recorder.Record(change);
        }

        user.ClearPendingRolePermissionChanges();
    }

    private sealed class FailingAuditRecorder : IRolePermissionAuditRecorder<Guid>
    {
        public void Record(RolePermissionAssignmentChange<Guid> change) =>
            throw new InvalidOperationException("Audit dispatch failed.");
    }
}
