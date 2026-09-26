using Microsoft.EntityFrameworkCore;
using Narwal.Permission.EntityFrameworkCore;

namespace Narwal.Permission.Tests.TestModels;

public sealed class RelationshipAuditedRolePermissionDbContext(
    DbContextOptions<RelationshipAuditedRolePermissionDbContext> options) : DbContext(options)
{
    public DbSet<AuditedApplicationUser> Users => Set<AuditedApplicationUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureRolePermissionModel<AuditedApplicationUser, Guid>(
            user => user.RoleAssignments,
            user => user.PermissionAssignments);
        modelBuilder.ConfigureRolePermissionAudit<AuditedApplicationUser, Guid>(
            user => user.AuditEventsAsActor,
            user => user.AuditEventsAbout);
    }
}
