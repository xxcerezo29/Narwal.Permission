using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Narwal.Permission.Domain;
using PermissionEntity = Narwal.Permission.Domain.Permission;

namespace Narwal.Permission.EntityFrameworkCore;

public static class RolePermissionModelBuilderExtensions
{
    public static ModelBuilder ConfigureRolePermissionModel<TUserId>(this ModelBuilder modelBuilder)
        where TUserId : notnull
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("NarwalRoles");
            entity.HasKey(role => role.Code);
            entity.Property(role => role.Code)
                .HasMaxLength(RolePermissionCode.MaximumLength)
                .IsRequired()
                .ValueGeneratedNever();
            entity.Property(role => role.Name)
                .HasMaxLength(256)
                .IsRequired();
        });

        modelBuilder.Entity<PermissionEntity>(entity =>
        {
            entity.ToTable("NarwalPermissions");
            entity.HasKey(permission => permission.Code);
            entity.Property(permission => permission.Code)
                .HasMaxLength(RolePermissionCode.MaximumLength)
                .IsRequired()
                .ValueGeneratedNever();
            entity.Property(permission => permission.Name)
                .HasMaxLength(256)
                .IsRequired();
        });

        modelBuilder.Entity<RolePermissionGrant>(entity =>
        {
            entity.ToTable("NarwalRolePermissions");
            entity.HasKey(grant => new { grant.RoleCode, grant.PermissionCode });
            entity.Property(grant => grant.RoleCode)
                .HasMaxLength(RolePermissionCode.MaximumLength)
                .IsRequired();
            entity.Property(grant => grant.PermissionCode)
                .HasMaxLength(RolePermissionCode.MaximumLength)
                .IsRequired();
            entity.HasOne<Role>()
                .WithMany()
                .HasForeignKey(grant => grant.RoleCode)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<PermissionEntity>()
                .WithMany()
                .HasForeignKey(grant => grant.PermissionCode)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserRole<TUserId>>(entity =>
        {
            entity.ToTable("NarwalUserRoles");
            entity.HasKey(assignment => new { assignment.UserId, assignment.RoleCode });
            entity.Property(assignment => assignment.UserId).IsRequired();
            entity.Property(assignment => assignment.RoleCode)
                .HasMaxLength(RolePermissionCode.MaximumLength)
                .IsRequired();
            entity.HasOne<Role>()
                .WithMany()
                .HasForeignKey(assignment => assignment.RoleCode)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserPermission<TUserId>>(entity =>
        {
            entity.ToTable("NarwalUserPermissions");
            entity.HasKey(assignment => new { assignment.UserId, assignment.PermissionCode });
            entity.Property(assignment => assignment.UserId).IsRequired();
            entity.Property(assignment => assignment.PermissionCode)
                .HasMaxLength(RolePermissionCode.MaximumLength)
                .IsRequired();
            entity.HasOne<PermissionEntity>()
                .WithMany()
                .HasForeignKey(assignment => assignment.PermissionCode)
                .OnDelete(DeleteBehavior.Cascade);
        });

        return modelBuilder;
    }

    public static ModelBuilder ConfigureRolePermissionModel<TUserEntity, TUserId>(
        this ModelBuilder modelBuilder,
        Expression<Func<TUserEntity, IEnumerable<UserRole<TUserId>>?>> roleAssignments,
        Expression<Func<TUserEntity, IEnumerable<UserPermission<TUserId>>?>> permissionAssignments)
        where TUserEntity : class
        where TUserId : notnull
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(roleAssignments);
        ArgumentNullException.ThrowIfNull(permissionAssignments);

        ConfigureRolePermissionModel<TUserId>(modelBuilder);

        var userEntity = modelBuilder.Entity<TUserEntity>();
        userEntity.HasMany(roleAssignments)
            .WithOne()
            .HasForeignKey(assignment => assignment.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        userEntity.Navigation(roleAssignments)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        userEntity.HasMany(permissionAssignments)
            .WithOne()
            .HasForeignKey(assignment => assignment.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        userEntity.Navigation(permissionAssignments)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        return modelBuilder;
    }
}
