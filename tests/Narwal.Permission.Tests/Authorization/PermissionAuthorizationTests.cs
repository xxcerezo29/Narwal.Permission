using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Narwal.Permission.DependencyInjection;
using Narwal.Permission.Tests.TestModels;

namespace Narwal.Permission.Tests.Authorization;

public sealed class PermissionAuthorizationTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;
    private RolePermissionDbContext _context = null!;
    private Narwal.Permission.Services.IRolePermissionManager<Guid> _manager = null!;
    private IAuthorizationService _authorization = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<RolePermissionDbContext>(options => options.UseSqlite(_connection));
        services.AddRolePermission<RolePermissionDbContext, Guid>(options =>
            options.UserIdResolver = principal =>
            {
                var claimValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                return Guid.TryParse(claimValue, out var userId)
                    ? (true, userId)
                    : (false, default);
            });
        services.AddAuthorization();
        services.AddRolePermissionAuthorization<Guid>();
        _serviceProvider = services.BuildServiceProvider(validateScopes: true);
        _scope = _serviceProvider.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<RolePermissionDbContext>();
        _manager = _scope.ServiceProvider.GetRequiredService<Narwal.Permission.Services.IRolePermissionManager<Guid>>();
        _authorization = _scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Dynamic_permission_policies_allow_grants_and_fail_closed_for_bad_identities()
    {
        var userId = Guid.NewGuid();
        await _manager.CreateRoleAsync("editor", "Editor");
        await _manager.CreatePermissionAsync("posts.edit", "Edit posts");
        await _manager.CreatePermissionAsync("users.manage", "Manage users");
        await _manager.GrantPermissionToRoleAsync("editor", "posts.edit");
        await _manager.AssignRoleAsync(userId, "editor");
        await _manager.GrantPermissionToUserAsync(userId, "users.manage");
        await _context.SaveChangesAsync();

        var principal = AuthenticatedPrincipal((ClaimTypes.NameIdentifier, userId.ToString()));
        var inheritedAllowed = await _authorization.AuthorizeAsync(principal, resource: null, "Permission:posts.edit");
        var directAllowed = await _authorization.AuthorizeAsync(principal, resource: null, "Permission:users.manage");
        var unknownDenied = await _authorization.AuthorizeAsync(principal, resource: null, "Permission:users.delete");
        var anonymousDenied = await _authorization.AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            resource: null,
            "Permission:posts.edit");
        var missingIdDenied = await _authorization.AuthorizeAsync(
            AuthenticatedPrincipal(),
            resource: null,
            "Permission:posts.edit");
        var malformedIdDenied = await _authorization.AuthorizeAsync(
            AuthenticatedPrincipal((ClaimTypes.NameIdentifier, "not-a-guid")),
            resource: null,
            "Permission:posts.edit");

        Assert.True(inheritedAllowed.Succeeded);
        Assert.True(directAllowed.Succeeded);
        Assert.False(unknownDenied.Succeeded);
        Assert.False(anonymousDenied.Succeeded);
        Assert.False(missingIdDenied.Succeeded);
        Assert.False(malformedIdDenied.Succeeded);
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), "test");
        return new ClaimsPrincipal(identity);
    }
}

public sealed class StringPermissionAuthorizationTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;
    private StringRolePermissionDbContext _context = null!;
    private Narwal.Permission.Services.IRolePermissionManager<string> _manager = null!;
    private IAuthorizationService _authorization = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<StringRolePermissionDbContext>(options => options.UseSqlite(_connection));
        services.AddRolePermission<StringRolePermissionDbContext, string>();
        services.AddAuthorization(options =>
            options.AddPolicy("NamedPolicy", policy => policy.RequireClaim("scope", "read")));
        services.AddRolePermissionAuthorization<string>();
        _serviceProvider = services.BuildServiceProvider(validateScopes: true);
        _scope = _serviceProvider.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<StringRolePermissionDbContext>();
        _manager = _scope.ServiceProvider.GetRequiredService<Narwal.Permission.Services.IRolePermissionManager<string>>();
        _authorization = _scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task String_ids_use_name_identifier_and_other_policies_use_the_default_provider()
    {
        await _manager.CreatePermissionAsync("reports.view", "View reports");
        await _manager.GrantPermissionToUserAsync("user-123", "reports.view");
        await _context.SaveChangesAsync();

        var permitted = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user-123"), new Claim("scope", "read")],
            "test"));
        var permissionResult = await _authorization.AuthorizeAsync(
            permitted,
            resource: null,
            "Permission:reports.view");
        var namedPolicyResult = await _authorization.AuthorizeAsync(
            permitted,
            resource: null,
            "NamedPolicy");

        Assert.True(permissionResult.Succeeded);
        Assert.True(namedPolicyResult.Succeeded);
    }
}
