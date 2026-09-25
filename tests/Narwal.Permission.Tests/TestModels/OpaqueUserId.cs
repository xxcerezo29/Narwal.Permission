using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Narwal.Permission.Domain;
using Narwal.Permission.EntityFrameworkCore;

namespace Narwal.Permission.Tests.TestModels;

public readonly struct OpaqueUserId : IEquatable<OpaqueUserId>
{
    public OpaqueUserId(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string Value { get; }

    public bool Equals(OpaqueUserId other) => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is OpaqueUserId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
}

public sealed class OpaqueUserIdDbContext(DbContextOptions<OpaqueUserIdDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureRolePermissionModel<OpaqueUserId>();

        var converter = new ValueConverter<OpaqueUserId, string>(
            userId => userId.Value,
            value => new OpaqueUserId(value));
        modelBuilder.Entity<UserRole<OpaqueUserId>>()
            .Property(assignment => assignment.UserId)
            .HasConversion(converter);
        modelBuilder.Entity<UserPermission<OpaqueUserId>>()
            .Property(assignment => assignment.UserId)
            .HasConversion(converter);
    }
}
