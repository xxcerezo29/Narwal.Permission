using Microsoft.EntityFrameworkCore;
using Narwal.Permission.EntityFrameworkCore;

namespace Narwal.Permission.Tests.TestModels;

public sealed class RolePermissionDbContext(DbContextOptions<RolePermissionDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureRolePermissionModel<Guid>();
    }
}
