using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Narwal.Permission.Domain;

namespace Narwal.Permission.Services;

internal sealed class RolePermissionAuditWriter<TContext, TUserId>(
    TContext context,
    IRolePermissionAuditActorProvider<TUserId> actorProvider)
    where TContext : DbContext
    where TUserId : notnull
{
    private readonly TContext _context = context;
    private readonly IRolePermissionAuditActorProvider<TUserId> _actorProvider = actorProvider;

    internal bool IsEnabled => _context.Model.FindEntityType(typeof(RolePermissionAuditEntry<TUserId>)) is not null;

    internal AuditActor GetActor()
    {
        if (!IsEnabled || !_actorProvider.TryGetActorUserId(out var actorUserId))
        {
            return default;
        }

        if (actorUserId is null)
        {
            throw new InvalidOperationException(
                "The actor provider returned true without supplying a user ID.");
        }

        return new AuditActor(true, actorUserId);
    }

    internal void Add(
        string action,
        string? roleCode,
        string? permissionCode,
        AuditActor actor,
        bool hasAffectedUserId = false,
        TUserId? affectedUserId = default,
        string? previousName = null,
        string? newName = null)
    {
        if (!IsEnabled)
        {
            return;
        }

        var auditEntry = new RolePermissionAuditEntry<TUserId>(
            action,
            DateTimeOffset.UtcNow,
            actor.HasActorUserId,
            actor.ActorUserId,
            hasAffectedUserId,
            affectedUserId,
            roleCode,
            permissionCode,
            previousName,
            newName);
        var tracked = _context.Set<RolePermissionAuditEntry<TUserId>>().Add(auditEntry);
        var auditType = _context.Model.FindEntityType(typeof(RolePermissionAuditEntry<TUserId>))!;
        SetOptionalRelationshipKey(tracked, auditType, "ActorRelationshipUserId",
            actor.HasActorUserId ? (object?)actor.ActorUserId : null);
        SetOptionalRelationshipKey(tracked, auditType, "AffectedRelationshipUserId",
            hasAffectedUserId ? (object?)affectedUserId : null);
    }

    private static void SetOptionalRelationshipKey(
        EntityEntry<RolePermissionAuditEntry<TUserId>> tracked,
        Microsoft.EntityFrameworkCore.Metadata.IEntityType auditType,
        string propertyName,
        object? value)
    {
        if (auditType.FindProperty(propertyName) is not null)
        {
            tracked.Property(propertyName).CurrentValue = value;
        }
    }

    internal readonly record struct AuditActor(bool HasActorUserId, TUserId? ActorUserId);
}
