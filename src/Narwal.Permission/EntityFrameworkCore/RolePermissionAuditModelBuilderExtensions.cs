using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Narwal.Permission.Domain;

namespace Narwal.Permission.EntityFrameworkCore;

/// <summary>Configures opt-in EF Core mapping for RBAC audit history.</summary>
public static class RolePermissionAuditModelBuilderExtensions
{
    /// <summary>Maps audit history without a foreign key to the application's user entity.</summary>
    public static ModelBuilder ConfigureRolePermissionAudit<TUserId>(
        this ModelBuilder modelBuilder,
        RolePermissionTableNames? tableNames = null)
        where TUserId : notnull
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        tableNames ??= new RolePermissionTableNames();
        tableNames.Validate();

        modelBuilder.Entity<RolePermissionAuditEntry<TUserId>>(entity =>
        {
            entity.ToTable(tableNames.AuditEntries);
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.Id).ValueGeneratedOnAdd();
            entity.Property(entry => entry.Action).HasMaxLength(64).IsRequired();
            entity.Property(entry => entry.OccurredAtUtc).IsRequired();
            entity.Property(entry => entry.ActorUserId).IsRequired(typeof(TUserId).IsValueType);
            entity.Property(entry => entry.HasActorUserId).IsRequired();
            entity.Property(entry => entry.AffectedUserId).IsRequired(typeof(TUserId).IsValueType);
            entity.Property(entry => entry.HasAffectedUserId).IsRequired();
            entity.Property(entry => entry.RoleCode).HasMaxLength(RolePermissionCode.MaximumLength);
            entity.Property(entry => entry.PermissionCode).HasMaxLength(RolePermissionCode.MaximumLength);
            entity.Property(entry => entry.PreviousName).HasMaxLength(256);
            entity.Property(entry => entry.NewName).HasMaxLength(256);
        });

        return modelBuilder;
    }

    /// <summary>Maps audit history with optional user-side actor and affected-user collections.</summary>
    /// <remarks>
    /// The relationships use client-side nulling to avoid database cascade paths. Load both collections
    /// before deleting a user so EF Core can clear tracked links while retaining audit ID snapshots.
    /// </remarks>
    public static ModelBuilder ConfigureRolePermissionAudit<TUserEntity, TUserId>(
        this ModelBuilder modelBuilder,
        Expression<Func<TUserEntity, IEnumerable<RolePermissionAuditEntry<TUserId>>?>> actorEvents,
        Expression<Func<TUserEntity, IEnumerable<RolePermissionAuditEntry<TUserId>>?>> affectedEvents,
        RolePermissionTableNames? tableNames = null)
        where TUserEntity : class
        where TUserId : notnull
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(actorEvents);
        ArgumentNullException.ThrowIfNull(affectedEvents);

        ConfigureRolePermissionAudit<TUserId>(modelBuilder, tableNames);

        var user = modelBuilder.Entity<TUserEntity>();
        var primaryKey = user.Metadata.FindPrimaryKey();
        if (primaryKey is null
            || primaryKey.Properties.Count != 1
            || primaryKey.Properties[0].ClrType != typeof(TUserId))
        {
            throw new InvalidOperationException(
                $"The {typeof(TUserEntity).Name} primary key must be a single property of type {typeof(TUserId).Name} to configure role-permission audit relationships.");
        }

        user.HasMany(actorEvents).WithOne()
            .HasForeignKey("ActorRelationshipUserId")
            .IsRequired(false)
            .OnDelete(DeleteBehavior.ClientSetNull);
        user.Navigation(actorEvents).UsePropertyAccessMode(PropertyAccessMode.Field);

        user.HasMany(affectedEvents).WithOne()
            .HasForeignKey("AffectedRelationshipUserId")
            .IsRequired(false)
            .OnDelete(DeleteBehavior.ClientSetNull);
        user.Navigation(affectedEvents).UsePropertyAccessMode(PropertyAccessMode.Field);

        return modelBuilder;
    }
}
