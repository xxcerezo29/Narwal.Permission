using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Narwal.Permission.DependencyInjection;
using Narwal.Permission.Domain;
using Narwal.Permission.Tests.TestModels;

namespace Narwal.Permission.Tests.Services;

public sealed class RolePermissionAuditRecorderTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;
    private AuditedRolePermissionDbContext _context = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AuditedRolePermissionDbContext>(options => options.UseSqlite(_connection));
        services.AddRolePermission<AuditedRolePermissionDbContext, Guid>();
        _serviceProvider = services.BuildServiceProvider(validateScopes: true);
        _scope = _serviceProvider.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<AuditedRolePermissionDbContext>();
        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Record_queues_each_change_with_its_actor_and_event_time_without_saving()
    {
        var actorId = Guid.NewGuid();
        var affectedUserId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 9, 26, 12, 30, 0, TimeSpan.FromHours(-4));
        var recorder = ResolveRecorder<AuditedRolePermissionDbContext, Guid>(_scope.ServiceProvider);
        var changes = new[]
        {
            RolePermissionAssignmentChange<Guid>.UserRoleAssigned(
                affectedUserId, "reader", occurredAt, true, actorId),
            RolePermissionAssignmentChange<Guid>.UserRoleRemoved(
                affectedUserId, "writer", occurredAt, false, default),
            RolePermissionAssignmentChange<Guid>.UserPermissionGranted(
                affectedUserId, "posts.read", occurredAt, true, Guid.Empty),
            RolePermissionAssignmentChange<Guid>.UserPermissionRevoked(
                affectedUserId, "posts.edit", occurredAt, true, actorId)
        };

        foreach (var change in changes)
        {
            Record(recorder, change);
        }

        var entries = _context.Set<RolePermissionAuditEntry<Guid>>().Local.ToArray();
        Assert.Equal(4, entries.Length);
        Assert.All(entries, entry =>
        {
            Assert.Equal(EntityState.Added, _context.Entry(entry).State);
            Assert.Equal(occurredAt.ToUniversalTime(), entry.OccurredAtUtc);
            Assert.Equal(affectedUserId, entry.AffectedUserId);
            Assert.True(entry.HasAffectedUserId);
        });
        Assert.Contains(entries, entry =>
            entry.Action == "user.role_assigned" && entry.RoleCode == "reader"
            && entry.HasActorUserId && entry.ActorUserId == actorId);
        Assert.Contains(entries, entry =>
            entry.Action == "user.role_removed" && entry.RoleCode == "writer"
            && !entry.HasActorUserId && entry.ActorUserId == Guid.Empty);
        Assert.Contains(entries, entry =>
            entry.Action == "user.permission_granted" && entry.PermissionCode == "posts.read"
            && entry.HasActorUserId && entry.ActorUserId == Guid.Empty);
        Assert.Contains(entries, entry =>
            entry.Action == "user.permission_revoked" && entry.PermissionCode == "posts.edit"
            && entry.HasActorUserId && entry.ActorUserId == actorId);
        Assert.Equal(0, await _context.Set<RolePermissionAuditEntry<Guid>>().CountAsync());
    }

    [Fact]
    public async Task Record_is_a_no_op_when_the_context_does_not_map_audit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<UnauditedNavigationRolePermissionDbContext>(options => options.UseSqlite(connection));
        services.AddRolePermission<UnauditedNavigationRolePermissionDbContext, Guid>();

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<UnauditedNavigationRolePermissionDbContext>();
        await context.Database.EnsureCreatedAsync();
        Assert.Null(context.Model.FindEntityType(typeof(RolePermissionAuditEntry<Guid>)));

        var recorder = ResolveRecorder<UnauditedNavigationRolePermissionDbContext, Guid>(scope.ServiceProvider);
        Record(recorder, RolePermissionAssignmentChange<Guid>.UserRoleAssigned(
            Guid.NewGuid(), "reader", DateTimeOffset.UnixEpoch));

        Assert.DoesNotContain(context.ChangeTracker.Entries(),
            entry => entry.Entity is RolePermissionAuditEntry<Guid>);
    }

    [Fact]
    public async Task Record_sets_optional_user_relationship_keys_and_keeps_id_snapshots()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<RelationshipAuditedRolePermissionDbContext>(options => options.UseSqlite(connection));
        services.AddRolePermission<RelationshipAuditedRolePermissionDbContext, Guid>();

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<RelationshipAuditedRolePermissionDbContext>();
        await context.Database.EnsureCreatedAsync();
        var actorId = Guid.NewGuid();
        var affectedUserId = Guid.NewGuid();
        context.Users.AddRange(new AuditedApplicationUser(actorId), new AuditedApplicationUser(affectedUserId));
        await context.SaveChangesAsync();

        var recorder = ResolveRecorder<RelationshipAuditedRolePermissionDbContext, Guid>(scope.ServiceProvider);
        Record(recorder, RolePermissionAssignmentChange<Guid>.UserRoleAssigned(
            affectedUserId, "reader", DateTimeOffset.UnixEpoch, true, actorId));

        var audit = Assert.Single(context.Set<RolePermissionAuditEntry<Guid>>().Local);
        Assert.Equal(actorId,
            context.Entry(audit).Property<Guid?>("ActorRelationshipUserId").CurrentValue);
        Assert.Equal(affectedUserId,
            context.Entry(audit).Property<Guid?>("AffectedRelationshipUserId").CurrentValue);
        Assert.Equal(actorId, audit.ActorUserId);
        Assert.Equal(affectedUserId, audit.AffectedUserId);

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var persisted = await context.Set<RolePermissionAuditEntry<Guid>>()
            .SingleAsync(entry => entry.Action == "user.role_assigned");
        Assert.Equal(actorId, persisted.ActorUserId);
        Assert.Equal(affectedUserId, persisted.AffectedUserId);
        Assert.Equal(actorId,
            context.Entry(persisted).Property<Guid?>("ActorRelationshipUserId").CurrentValue);
        Assert.Equal(affectedUserId,
            context.Entry(persisted).Property<Guid?>("AffectedRelationshipUserId").CurrentValue);
    }

    private static object ResolveRecorder<TContext, TUserId>(IServiceProvider services)
        where TContext : DbContext
        where TUserId : notnull
    {
        var contract = GetRecorderContract<TUserId>();
        var recorder = services.GetService(contract);
        Assert.NotNull(recorder);
        return recorder!;
    }

    private static void Record<TUserId>(object recorder, RolePermissionAssignmentChange<TUserId> change)
        where TUserId : notnull
    {
        var contract = GetRecorderContract<TUserId>();
        var recordMethod = contract.GetMethod("Record");
        Assert.NotNull(recordMethod);

        try
        {
            recordMethod!.Invoke(recorder, [change]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static Type GetRecorderContract<TUserId>()
        where TUserId : notnull
    {
        var openContract = typeof(RolePermissionAssignmentChange<TUserId>).Assembly.GetType(
            "Narwal.Permission.Services.IRolePermissionAuditRecorder`1");
        Assert.NotNull(openContract);
        return openContract!.MakeGenericType(typeof(TUserId));
    }
}
