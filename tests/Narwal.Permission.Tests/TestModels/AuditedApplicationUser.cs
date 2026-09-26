using Narwal.Permission.Domain;

namespace Narwal.Permission.Tests.TestModels;

public sealed class AuditedApplicationUser
{
    private readonly List<UserRole<Guid>> _roleAssignments = [];
    private readonly List<UserPermission<Guid>> _permissionAssignments = [];
    private readonly List<RolePermissionAuditEntry<Guid>> _auditEventsAsActor = [];
    private readonly List<RolePermissionAuditEntry<Guid>> _auditEventsAbout = [];

    private AuditedApplicationUser()
    {
    }

    public AuditedApplicationUser(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; private set; }

    public IEnumerable<UserRole<Guid>> RoleAssignments => _roleAssignments.AsReadOnly();

    public IEnumerable<UserPermission<Guid>> PermissionAssignments => _permissionAssignments.AsReadOnly();

    public IEnumerable<RolePermissionAuditEntry<Guid>> AuditEventsAsActor => _auditEventsAsActor.AsReadOnly();

    public IEnumerable<RolePermissionAuditEntry<Guid>> AuditEventsAbout => _auditEventsAbout.AsReadOnly();
}
