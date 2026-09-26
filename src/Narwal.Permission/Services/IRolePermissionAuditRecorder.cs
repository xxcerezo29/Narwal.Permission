using Narwal.Permission.Domain;

namespace Narwal.Permission.Services;

/// <summary>Queues audit entries for assignment changes emitted by an application-owned user aggregate.</summary>
public interface IRolePermissionAuditRecorder<TUserId>
    where TUserId : notnull
{
    /// <summary>Adds the audit entry to the current application's DbContext without saving it.</summary>
    void Record(RolePermissionAssignmentChange<TUserId> change);
}
