using Microsoft.EntityFrameworkCore;
using Narwal.Permission.EntityFrameworkCore;

namespace Narwal.Permission.Tests.TestModels;

public sealed class StringRolePermissionDbContext(DbContextOptions<StringRolePermissionDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureRolePermissionModel<string>();
    }
}
