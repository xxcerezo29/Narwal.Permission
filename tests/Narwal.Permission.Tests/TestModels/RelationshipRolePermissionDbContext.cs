using Microsoft.EntityFrameworkCore;
using Narwal.Permission.EntityFrameworkCore;

namespace Narwal.Permission.Tests.TestModels;

public sealed class RelationshipRolePermissionDbContext(
    DbContextOptions<RelationshipRolePermissionDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureRolePermissionModel<ApplicationUser, Guid>(
            user => user.RoleAssignments,
            user => user.PermissionAssignments);
    }
}
