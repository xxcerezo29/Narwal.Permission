using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Narwal.Permission.Domain;
using Narwal.Permission.EntityFrameworkCore;
using Narwal.Permission.Tests.TestModels;

namespace Narwal.Permission.Tests.EntityFrameworkCore;

public sealed class RolePermissionAuditModelTests
{
    [Fact]
    public void Existing_role_permission_model_does_not_include_the_audit_entity()
    {
        var options = new DbContextOptionsBuilder<RolePermissionDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var context = new RolePermissionDbContext(options);

        Assert.Null(context.Model.FindEntityType(typeof(RolePermissionAuditEntry<Guid>)));
    }

    [Fact]
    public async Task Existing_role_permission_model_does_not_create_an_audit_table()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RolePermissionDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new RolePermissionDbContext(options);

        await context.Database.EnsureCreatedAsync();
        var tableCount = await context.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'NarwalPermissionAuditEntries'")
            .SingleAsync();

        Assert.Equal(0, tableCount);
    }

    [Fact]
    public async Task Audit_navigations_do_not_enable_audit_without_explicit_mapping()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<UnauditedNavigationRolePermissionDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new UnauditedNavigationRolePermissionDbContext(options);

        Assert.Null(context.Model.FindEntityType(typeof(RolePermissionAuditEntry<Guid>)));

        await context.Database.EnsureCreatedAsync();
        var auditTables = await context.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table' AND name LIKE '%RolePermissionAudit%'")
            .ToListAsync();

        Assert.Empty(auditTables);
    }

    [Fact]
    public async Task Audit_mapping_uses_custom_table_name_and_maps_snapshot_columns()
    {
        var tableNames = new RolePermissionTableNames { AuditEntries = "AppPermissionHistory" };
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CustomTableAuditContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new CustomTableAuditContext(options, tableNames);

        var auditType = context.Model.FindEntityType(typeof(RolePermissionAuditEntry<Guid>));

        Assert.NotNull(auditType);
        Assert.Equal("AppPermissionHistory", auditType.GetTableName());
        Assert.Equal(typeof(long), Assert.Single(auditType.FindPrimaryKey()!.Properties).ClrType);
        Assert.Equal(ValueGenerated.OnAdd, auditType.FindProperty(nameof(RolePermissionAuditEntry<Guid>.Id))!.ValueGenerated);
        Assert.Equal(64, auditType.FindProperty(nameof(RolePermissionAuditEntry<Guid>.Action))!.GetMaxLength());
        Assert.Equal(RolePermissionCode.MaximumLength,
            auditType.FindProperty(nameof(RolePermissionAuditEntry<Guid>.RoleCode))!.GetMaxLength());
        Assert.Equal(RolePermissionCode.MaximumLength,
            auditType.FindProperty(nameof(RolePermissionAuditEntry<Guid>.PermissionCode))!.GetMaxLength());
        Assert.Equal(256, auditType.FindProperty(nameof(RolePermissionAuditEntry<Guid>.PreviousName))!.GetMaxLength());
        Assert.Equal(256, auditType.FindProperty(nameof(RolePermissionAuditEntry<Guid>.NewName))!.GetMaxLength());
        Assert.True(auditType.FindProperty(nameof(RolePermissionAuditEntry<Guid>.HasActorUserId))!.IsNullable == false);
        Assert.Empty(auditType.GetForeignKeys());

        await context.Database.EnsureCreatedAsync();
        var tableCount = await context.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'AppPermissionHistory'")
            .SingleAsync();
        Assert.Equal(1, tableCount);
    }

    [Fact]
    public void Id_only_audit_mapping_supports_guid_and_string_user_ids()
    {
        using var guidContext = CreateContext<Guid>();
        using var stringContext = CreateContext<string>();

        Assert.NotNull(guidContext.Model.FindEntityType(typeof(RolePermissionAuditEntry<Guid>)));
        var stringAuditType = stringContext.Model.FindEntityType(typeof(RolePermissionAuditEntry<string>));
        Assert.NotNull(stringAuditType);
        Assert.True(stringAuditType.FindProperty(nameof(RolePermissionAuditEntry<string>.ActorUserId))!.IsNullable);
        Assert.True(stringAuditType.FindProperty(nameof(RolePermissionAuditEntry<string>.AffectedUserId))!.IsNullable);
    }

    [Fact]
    public async Task Relationship_mapping_uses_nullable_shadow_keys_and_creates_schema()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RelationshipAuditContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new RelationshipAuditContext(options);

        var userType = context.Model.FindEntityType(typeof(AuditUser))!;
        var actorForeignKey = userType.FindNavigation(nameof(AuditUser.AuditEventsAsActor))!.ForeignKey;
        var affectedForeignKey = userType.FindNavigation(nameof(AuditUser.AuditEventsAbout))!.ForeignKey;

        Assert.Equal("ActorRelationshipUserId", Assert.Single(actorForeignKey.Properties).Name);
        Assert.Equal(typeof(Guid?), actorForeignKey.Properties[0].ClrType);
        Assert.True(actorForeignKey.Properties[0].IsNullable);
        Assert.Equal(DeleteBehavior.ClientSetNull, actorForeignKey.DeleteBehavior);
        Assert.Equal(PropertyAccessMode.Field,
            userType.FindNavigation(nameof(AuditUser.AuditEventsAsActor))!.GetPropertyAccessMode());

        Assert.Equal("AffectedRelationshipUserId", Assert.Single(affectedForeignKey.Properties).Name);
        Assert.Equal(typeof(Guid?), affectedForeignKey.Properties[0].ClrType);
        Assert.True(affectedForeignKey.Properties[0].IsNullable);
        Assert.Equal(DeleteBehavior.ClientSetNull, affectedForeignKey.DeleteBehavior);

        await context.Database.EnsureCreatedAsync();
    }

    [Fact]
    public void Relationship_mapping_rejects_a_user_key_type_that_differs_from_audit_user_id()
    {
        var options = new DbContextOptionsBuilder<MismatchedRelationshipAuditContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var context = new MismatchedRelationshipAuditContext(options);

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("primary key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static AuditDbContext<TUserId> CreateContext<TUserId>()
        where TUserId : notnull
    {
        var options = new DbContextOptionsBuilder<AuditDbContext<TUserId>>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        return new AuditDbContext<TUserId>(options, new RolePermissionTableNames());
    }

    private sealed class AuditDbContext<TUserId>(
        DbContextOptions<AuditDbContext<TUserId>> options,
        RolePermissionTableNames tableNames) : DbContext(options)
        where TUserId : notnull
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureRolePermissionModel<TUserId>();
            modelBuilder.ConfigureRolePermissionAudit<TUserId>(tableNames);
        }
    }

    private sealed class RelationshipAuditContext(DbContextOptions<RelationshipAuditContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AuditUser>().HasKey(user => user.Id);
            modelBuilder.ConfigureRolePermissionModel<Guid>();
            modelBuilder.ConfigureRolePermissionAudit<AuditUser, Guid>(
                user => user.AuditEventsAsActor,
                user => user.AuditEventsAbout);
        }
    }

    private sealed class CustomTableAuditContext(
        DbContextOptions<CustomTableAuditContext> options,
        RolePermissionTableNames tableNames) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureRolePermissionModel<Guid>();
            modelBuilder.ConfigureRolePermissionAudit<Guid>(tableNames);
        }
    }

    private sealed class MismatchedRelationshipAuditContext(
        DbContextOptions<MismatchedRelationshipAuditContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MismatchedUser>().HasKey(user => user.Id);
            modelBuilder.ConfigureRolePermissionAudit<MismatchedUser, string>(
                user => user.AuditEventsAsActor,
                user => user.AuditEventsAbout);
        }
    }

    private sealed class AuditUser
    {
        private readonly List<RolePermissionAuditEntry<Guid>> _auditEventsAsActor = [];
        private readonly List<RolePermissionAuditEntry<Guid>> _auditEventsAbout = [];

        public Guid Id { get; set; }

        public IReadOnlyCollection<RolePermissionAuditEntry<Guid>> AuditEventsAsActor => _auditEventsAsActor;

        public IReadOnlyCollection<RolePermissionAuditEntry<Guid>> AuditEventsAbout => _auditEventsAbout;
    }

    private sealed class MismatchedUser
    {
        private readonly List<RolePermissionAuditEntry<string>> _auditEventsAsActor = [];
        private readonly List<RolePermissionAuditEntry<string>> _auditEventsAbout = [];

        public Guid Id { get; set; }

        public IReadOnlyCollection<RolePermissionAuditEntry<string>> AuditEventsAsActor => _auditEventsAsActor;

        public IReadOnlyCollection<RolePermissionAuditEntry<string>> AuditEventsAbout => _auditEventsAbout;
    }
}
