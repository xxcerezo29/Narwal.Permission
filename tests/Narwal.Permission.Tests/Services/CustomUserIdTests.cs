using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Narwal.Permission.DependencyInjection;
using Narwal.Permission.Tests.TestModels;
using UserPermissionEntity = Narwal.Permission.Domain.UserPermission<Narwal.Permission.Tests.TestModels.OpaqueUserId>;

namespace Narwal.Permission.Tests.Services;

public sealed class CustomUserIdTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;
    private OpaqueUserIdDbContext _context = null!;
    private Narwal.Permission.Services.IRolePermissionManager<OpaqueUserId> _manager = null!;
    private Narwal.Permission.Services.IPermissionChecker<OpaqueUserId> _checker = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OpaqueUserIdDbContext>(options => options.UseSqlite(_connection));
        services.AddRolePermission<OpaqueUserIdDbContext, OpaqueUserId>();
        _serviceProvider = services.BuildServiceProvider(validateScopes: true);
        _scope = _serviceProvider.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<OpaqueUserIdDbContext>();
        _manager = _scope.ServiceProvider
            .GetRequiredService<Narwal.Permission.Services.IRolePermissionManager<OpaqueUserId>>();
        _checker = _scope.ServiceProvider
            .GetRequiredService<Narwal.Permission.Services.IPermissionChecker<OpaqueUserId>>();

        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Manager_and_checker_support_converter_backed_user_ids_without_equality_operators()
    {
        var userId = new OpaqueUserId("user-42");
        await _manager.CreateRoleAsync("report-reader", "Report reader");
        await _manager.CreatePermissionAsync("reports.view", "View reports");
        await _manager.CreatePermissionAsync("reports.export", "Export reports");
        await _manager.GrantPermissionToRoleAsync("report-reader", "reports.export");
        await _manager.AssignRoleAsync(userId, "report-reader");
        await _manager.GrantPermissionToUserAsync(userId, "reports.view");
        await _context.SaveChangesAsync();

        Assert.True(await _checker.HasRoleAsync(userId, "report-reader"));
        Assert.True(await _checker.HasPermissionAsync(userId, "reports.view"));
        Assert.True(await _checker.HasPermissionAsync(userId, "reports.export"));

        await _manager.RevokePermissionFromUserAsync(userId, "reports.view");
        await _context.SaveChangesAsync();

        Assert.False(await _checker.HasPermissionAsync(userId, "reports.view"));
        Assert.True(await _checker.HasPermissionAsync(userId, "reports.export"));
        Assert.Empty(await _context.Set<UserPermissionEntity>().ToListAsync());

        await _manager.RemoveRoleAsync(userId, "report-reader");
        await _context.SaveChangesAsync();

        Assert.False(await _checker.HasRoleAsync(userId, "report-reader"));
        Assert.False(await _checker.HasPermissionAsync(userId, "reports.export"));
    }
}
