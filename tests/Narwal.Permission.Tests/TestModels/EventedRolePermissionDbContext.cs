using Microsoft.EntityFrameworkCore;
using Narwal.Permission.EntityFrameworkCore;

namespace Narwal.Permission.Tests.TestModels;

public sealed class EventedRolePermissionDbContext(
    DbContextOptions<EventedRolePermissionDbContext> options) : DbContext(options)
{
    public DbSet<EventedApplicationUser> Users => Set<EventedApplicationUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureRolePermissionModel<EventedApplicationUser, Guid>(
            user => user.RoleAssignments,
            user => user.PermissionAssignments);
        modelBuilder.ConfigureRolePermissionAudit<EventedApplicationUser, Guid>(
            user => user.AuditEventsAsActor,
            user => user.AuditEventsAbout);
    }
}
