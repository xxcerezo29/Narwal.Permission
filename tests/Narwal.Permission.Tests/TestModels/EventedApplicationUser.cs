using Narwal.Permission.Domain;

namespace Narwal.Permission.Tests.TestModels;

public sealed class EventedApplicationUser
{
    private readonly List<UserRole<Guid>> _roleAssignments = [];
    private readonly List<UserPermission<Guid>> _permissionAssignments = [];
    private readonly List<RolePermissionAssignmentChange<Guid>> _pendingRolePermissionChanges = [];
    private readonly List<RolePermissionAuditEntry<Guid>> _auditEventsAsActor = [];
    private readonly List<RolePermissionAuditEntry<Guid>> _auditEventsAbout = [];

    private EventedApplicationUser()
    {
    }

    public EventedApplicationUser(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; private set; }

    public IEnumerable<UserRole<Guid>> RoleAssignments => _roleAssignments.AsReadOnly();

    public IEnumerable<UserPermission<Guid>> PermissionAssignments => _permissionAssignments.AsReadOnly();

    public IEnumerable<RolePermissionAuditEntry<Guid>> AuditEventsAsActor => _auditEventsAsActor.AsReadOnly();

    public IEnumerable<RolePermissionAuditEntry<Guid>> AuditEventsAbout => _auditEventsAbout.AsReadOnly();

    public IReadOnlyCollection<RolePermissionAssignmentChange<Guid>> PendingRolePermissionChanges =>
        _pendingRolePermissionChanges.AsReadOnly();

    public void AssignRole(string roleCode, DateTimeOffset occurredAtUtc, Guid? actorId)
    {
        var normalizedCode = RolePermissionCode.Normalize(roleCode);
        if (_roleAssignments.Any(assignment => assignment.RoleCode == normalizedCode))
        {
            throw new InvalidOperationException("User already has this role.");
        }

        var change = RolePermissionAssignmentChange<Guid>.UserRoleAssigned(
            Id, normalizedCode, occurredAtUtc, actorId.HasValue, actorId.GetValueOrDefault());
        _roleAssignments.Add(UserRole<Guid>.Create(Id, normalizedCode));
        _pendingRolePermissionChanges.Add(change);
    }

    public void RevokeRole(string roleCode, DateTimeOffset occurredAtUtc, Guid? actorId)
    {
        var normalizedCode = RolePermissionCode.Normalize(roleCode);
        var index = _roleAssignments.FindIndex(assignment => assignment.RoleCode == normalizedCode);
        if (index < 0)
        {
            throw new InvalidOperationException("User does not have this role.");
        }

        var change = RolePermissionAssignmentChange<Guid>.UserRoleRemoved(
            Id, normalizedCode, occurredAtUtc, actorId.HasValue, actorId.GetValueOrDefault());
        _roleAssignments.RemoveAt(index);
        _pendingRolePermissionChanges.Add(change);
    }

    public void GrantPermission(string permissionCode, DateTimeOffset occurredAtUtc, Guid? actorId)
    {
        var normalizedCode = RolePermissionCode.Normalize(permissionCode);
        if (_permissionAssignments.Any(assignment => assignment.PermissionCode == normalizedCode))
        {
            throw new InvalidOperationException("User already has this permission.");
        }

        var change = RolePermissionAssignmentChange<Guid>.UserPermissionGranted(
            Id, normalizedCode, occurredAtUtc, actorId.HasValue, actorId.GetValueOrDefault());
        _permissionAssignments.Add(UserPermission<Guid>.Create(Id, normalizedCode));
        _pendingRolePermissionChanges.Add(change);
    }

    public void RevokePermission(string permissionCode, DateTimeOffset occurredAtUtc, Guid? actorId)
    {
        var normalizedCode = RolePermissionCode.Normalize(permissionCode);
        var index = _permissionAssignments.FindIndex(assignment =>
            assignment.PermissionCode == normalizedCode);
        if (index < 0)
        {
            throw new InvalidOperationException("User does not have this permission.");
        }

        var change = RolePermissionAssignmentChange<Guid>.UserPermissionRevoked(
            Id, normalizedCode, occurredAtUtc, actorId.HasValue, actorId.GetValueOrDefault());
        _permissionAssignments.RemoveAt(index);
        _pendingRolePermissionChanges.Add(change);
    }

    public void ClearPendingRolePermissionChanges() => _pendingRolePermissionChanges.Clear();
}
