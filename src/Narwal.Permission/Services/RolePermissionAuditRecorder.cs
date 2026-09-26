using Microsoft.EntityFrameworkCore;
using Narwal.Permission.Domain;

namespace Narwal.Permission.Services;

internal sealed class RolePermissionAuditRecorder<TContext, TUserId>(
    RolePermissionAuditWriter<TContext, TUserId> auditWriter)
    : IRolePermissionAuditRecorder<TUserId>
    where TContext : DbContext
    where TUserId : notnull
{
    private readonly RolePermissionAuditWriter<TContext, TUserId> _auditWriter = auditWriter;

    public void Record(RolePermissionAssignmentChange<TUserId> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        _auditWriter.Add(
            change.Action,
            change.RoleCode,
            change.PermissionCode,
            new RolePermissionAuditWriter<TContext, TUserId>.AuditActor(
                change.HasActorUserId, change.ActorUserId),
            hasAffectedUserId: true,
            affectedUserId: change.AffectedUserId,
            occurredAtUtc: change.OccurredAtUtc);
    }
}
