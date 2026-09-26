using Microsoft.EntityFrameworkCore;
using Narwal.Permission.EntityFrameworkCore;

namespace Narwal.Permission.Tests.TestModels;

public sealed class AuditedRolePermissionDbContext(DbContextOptions<AuditedRolePermissionDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureRolePermissionModel<Guid>();
        modelBuilder.ConfigureRolePermissionAudit<Guid>();
    }
}
