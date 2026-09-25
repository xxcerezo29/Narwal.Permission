using Narwal.Permission.Domain;

namespace Narwal.Permission.Tests.TestModels;

public sealed class ApplicationUser
{
    private readonly List<UserRole<Guid>> _roleAssignments = [];
    private readonly List<UserPermission<Guid>> _permissionAssignments = [];

    private ApplicationUser()
    {
    }

    public ApplicationUser(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; private set; }

    public IEnumerable<UserRole<Guid>> RoleAssignments => _roleAssignments.AsReadOnly();

    public IEnumerable<UserPermission<Guid>> PermissionAssignments => _permissionAssignments.AsReadOnly();
}
