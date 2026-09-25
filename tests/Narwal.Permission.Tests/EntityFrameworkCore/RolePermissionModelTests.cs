using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Narwal.Permission.EntityFrameworkCore;
using Narwal.Permission.Tests.TestModels;
using PermissionEntity = Narwal.Permission.Domain.Permission;
using RoleEntity = Narwal.Permission.Domain.Role;
using UserPermissionEntity = Narwal.Permission.Domain.UserPermission<System.Guid>;
using UserRoleEntity = Narwal.Permission.Domain.UserRole<System.Guid>;

namespace Narwal.Permission.Tests.EntityFrameworkCore;

public sealed class RolePermissionModelTests
{
    [Fact]
    public async Task Id_only_model_uses_string_keys_and_has_no_user_foreign_keys()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<IdOnlyDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new IdOnlyDbContext(options);

        await context.Database.EnsureCreatedAsync();

        var role = context.Model.FindEntityType(typeof(RoleEntity))!;
        var permission = context.Model.FindEntityType(typeof(PermissionEntity))!;
        var roleGrant = context.Model.FindEntityType(typeof(Narwal.Permission.Domain.RolePermissionGrant))!;
        var userRole = context.Model.FindEntityType(typeof(UserRoleEntity))!;
        var userPermission = context.Model.FindEntityType(typeof(UserPermissionEntity))!;

        Assert.Equal(typeof(string), Assert.Single(role.FindPrimaryKey()!.Properties).ClrType);
        Assert.Equal(typeof(string), Assert.Single(permission.FindPrimaryKey()!.Properties).ClrType);
        Assert.Equal(
            new[] { "RoleCode", "PermissionCode" },
            roleGrant.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(
            new[] { "UserId", "RoleCode" },
            userRole.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(
            new[] { "UserId", "PermissionCode" },
            userPermission.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.DoesNotContain(
            userRole.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(IdOnlyApplicationUser));
    }

    [Fact]
    public void Table_names_can_be_customized_in_id_only_mode()
    {
        var options = new DbContextOptionsBuilder<CustomTableNamesDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var context = new CustomTableNamesDbContext(options);

        AssertTableNames(context);
    }

    [Fact]
    public void Table_names_can_be_customized_in_relationship_mode()
    {
        var options = new DbContextOptionsBuilder<CustomRelationshipTableNamesDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var context = new CustomRelationshipTableNamesDbContext(options);

        AssertTableNames(context);
        var userType = context.Model.FindEntityType(typeof(ApplicationUser))!;
        Assert.NotNull(userType.FindNavigation(nameof(ApplicationUser.RoleAssignments)));
        Assert.NotNull(userType.FindNavigation(nameof(ApplicationUser.PermissionAssignments)));
    }

    private static void AssertTableNames(DbContext context)
    {
        Assert.Equal("AppRoles", context.Model.FindEntityType(typeof(RoleEntity))!.GetTableName());
        Assert.Equal("AppPermissions", context.Model.FindEntityType(typeof(PermissionEntity))!.GetTableName());
        Assert.Equal(
            "AppRolePermissions",
            context.Model.FindEntityType(typeof(Narwal.Permission.Domain.RolePermissionGrant))!.GetTableName());
        Assert.Equal("AppUserRoles", context.Model.FindEntityType(typeof(UserRoleEntity))!.GetTableName());
        Assert.Equal(
            "AppUserPermissions",
            context.Model.FindEntityType(typeof(UserPermissionEntity))!.GetTableName());
    }

    [Fact]
    public async Task Relationship_mode_adds_user_foreign_keys_and_cascades_assignment_deletes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RelationshipDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new RelationshipDbContext(options);

        await context.Database.EnsureCreatedAsync();
        var user = new ApplicationUser(Guid.NewGuid());
        context.Users.Add(user);
        await context.SaveChangesAsync();

        const string roleCode = "editor";
        const string permissionCode = "posts.edit";
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO NarwalRoles (Code, Name) VALUES ({roleCode}, {"Editor"})");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO NarwalPermissions (Code, Name) VALUES ({permissionCode}, {"Edit posts"})");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO NarwalUserRoles (UserId, RoleCode) VALUES ({user.Id}, {roleCode})");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO NarwalUserPermissions (UserId, PermissionCode) VALUES ({user.Id}, {permissionCode})");

        var userType = context.Model.FindEntityType(typeof(ApplicationUser))!;
        Assert.NotNull(userType.FindNavigation(nameof(ApplicationUser.RoleAssignments)));
        Assert.NotNull(userType.FindNavigation(nameof(ApplicationUser.PermissionAssignments)));

        context.Users.Remove(user);
        await context.SaveChangesAsync();

        Assert.Equal(0, await context.Set<UserRoleEntity>().CountAsync());
        Assert.Equal(0, await context.Set<UserPermissionEntity>().CountAsync());
    }

    [Fact]
    public async Task Relationship_mode_rejects_assignment_for_a_missing_user()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<StringRelationshipDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new StringRelationshipDbContext(options);

        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO NarwalRoles (Code, Name) VALUES ({"reader"}, {"Reader"})");

        await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO NarwalUserRoles (UserId, RoleCode) VALUES ({"missing-user"}, {"reader"})"));
    }

    [Fact]
    public void Relationship_mode_rejects_a_user_key_type_that_does_not_match_the_user_primary_key()
    {
        var options = new DbContextOptionsBuilder<MismatchedUserKeyDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var context = new MismatchedUserKeyDbContext(options);

        Assert.Throws<InvalidOperationException>(() => _ = context.Model);
    }

    private sealed class IdOnlyDbContext(DbContextOptions<IdOnlyDbContext> options) : DbContext(options)
    {
        public DbSet<IdOnlyApplicationUser> Users => Set<IdOnlyApplicationUser>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureRolePermissionModel<Guid>();
        }
    }

    private sealed class CustomTableNamesDbContext(
        DbContextOptions<CustomTableNamesDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureRolePermissionModel<Guid>(new RolePermissionTableNames
            {
                Roles = "AppRoles",
                Permissions = "AppPermissions",
                RolePermissions = "AppRolePermissions",
                UserRoles = "AppUserRoles",
                UserPermissions = "AppUserPermissions"
            });
        }
    }

    private sealed class CustomRelationshipTableNamesDbContext(
        DbContextOptions<CustomRelationshipTableNamesDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureRolePermissionModel<ApplicationUser, Guid>(
                user => user.RoleAssignments,
                user => user.PermissionAssignments,
                new RolePermissionTableNames
                {
                    Roles = "AppRoles",
                    Permissions = "AppPermissions",
                    RolePermissions = "AppRolePermissions",
                    UserRoles = "AppUserRoles",
                    UserPermissions = "AppUserPermissions"
                });
        }
    }

    private sealed class RelationshipDbContext(DbContextOptions<RelationshipDbContext> options) : DbContext(options)
    {
        public DbSet<ApplicationUser> Users => Set<ApplicationUser>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureRolePermissionModel<ApplicationUser, Guid>(
                user => user.RoleAssignments,
                user => user.PermissionAssignments);
        }
    }

    private sealed class StringRelationshipDbContext(
        DbContextOptions<StringRelationshipDbContext> options) : DbContext(options)
    {
        public DbSet<StringApplicationUser> Users => Set<StringApplicationUser>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureRolePermissionModel<StringApplicationUser, string>(
                user => user.RoleAssignments,
                user => user.PermissionAssignments);
        }
    }

    private sealed class MismatchedUserKeyDbContext(
        DbContextOptions<MismatchedUserKeyDbContext> options) : DbContext(options)
    {
        public DbSet<MismatchedApplicationUser> Users => Set<MismatchedApplicationUser>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureRolePermissionModel<MismatchedApplicationUser, string>(
                user => user.RoleAssignments,
                user => user.PermissionAssignments);
        }
    }

    private sealed class IdOnlyApplicationUser
    {
        public Guid Id { get; private set; }
    }

    private sealed class StringApplicationUser
    {
        private readonly List<Narwal.Permission.Domain.UserRole<string>> _roleAssignments = [];
        private readonly List<Narwal.Permission.Domain.UserPermission<string>> _permissionAssignments = [];

        public string Id { get; private set; } = null!;

        public IEnumerable<Narwal.Permission.Domain.UserRole<string>> RoleAssignments =>
            _roleAssignments.AsReadOnly();

        public IEnumerable<Narwal.Permission.Domain.UserPermission<string>> PermissionAssignments =>
            _permissionAssignments.AsReadOnly();
    }

    private sealed class MismatchedApplicationUser
    {
        private readonly List<Narwal.Permission.Domain.UserRole<string>> _roleAssignments = [];
        private readonly List<Narwal.Permission.Domain.UserPermission<string>> _permissionAssignments = [];

        public Guid Id { get; private set; }

        public IEnumerable<Narwal.Permission.Domain.UserRole<string>> RoleAssignments =>
            _roleAssignments.AsReadOnly();

        public IEnumerable<Narwal.Permission.Domain.UserPermission<string>> PermissionAssignments =>
            _permissionAssignments.AsReadOnly();
    }
}
