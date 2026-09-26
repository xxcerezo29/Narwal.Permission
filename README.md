# Narwal.Permission

Narwal.Permission is an ASP.NET Core and EF Core role based access control package for .NET 10. It provides encapsulated role and permission models, management APIs, direct and role inherited permission checks, and ASP.NET Core authorization policies. It does not require ASP.NET Core Identity. V1 roles are global; tenant or organization scoped roles are not included.

Role and permission `Code` values are stable string primary keys. Display `Name` values can be changed without changing those keys. The application owns the DbContext, migrations, transactions, and calls to `SaveChangesAsync`.

## Install from GitHub Packages

Packages are published to GitHub Packages, not to nuget.org. GitHub Packages packages are private by default. Outside GitHub Actions, authenticate to the NuGet registry with a classic GitHub personal access token using the `read:packages` scope and an account that can read the package. GitHub Actions can use `GITHUB_TOKEN` when its repository has access to the package. See [GitHub's NuGet registry documentation](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry) for authentication details.

Add a NuGet source for the package owner's account or organization:

```sh
dotnet nuget add source "https://nuget.pkg.github.com/OWNER/index.json" --name github --username YOUR_GITHUB_USERNAME --password YOUR_GITHUB_TOKEN --store-password-in-clear-text
```

Use a token with package read access. This command saves the source to the user's NuGet configuration by default; do not commit credentials or tokens to the repository. Keep your regular NuGet.org source enabled too, since the package's dependencies are restored from their configured sources. Then install the package without restricting restore to only GitHub Packages:

```sh
dotnet add package Narwal.Permission
```

Replace `OWNER` with the owner of the GitHub repository that publishes the package.

## Configure EF Core

Add the model to the application's existing `DbContext`. Use the application's user ID type; this example uses `Guid` and has no Identity dependency.

```csharp
using Microsoft.EntityFrameworkCore;
using Narwal.Permission.EntityFrameworkCore;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigureRolePermissionModel<Guid>();
    }
}
```

The default mapping stores user IDs on assignment rows and does not add a foreign key to the application's user table. Keep the app user model free of role or permission assignment collection navigations in this mode; EF Core conventions can discover those navigations. The app remains responsible for cleaning up stale assignments when it deletes a user.

Table names are configurable in both mapping modes. Any names you do not override keep their defaults:

```csharp
modelBuilder.ConfigureRolePermissionModel<Guid>(new RolePermissionTableNames
{
    Roles = "AppRoles",
    Permissions = "AppPermissions",
    RolePermissions = "AppRolePermissions",
    UserRoles = "AppUserRoles",
    UserPermissions = "AppUserPermissions"
});
```

The same `RolePermissionTableNames` argument can be passed as the third argument to the optional user relationship overload.

The application uses its regular EF Core migrations. For example:

```sh
dotnet ef migrations add AddRolePermission
dotnet ef database update
```

### Optional user relationships

To have EF Core enforce user foreign keys and expose assignment collections on the user entity, use the relationship overload. Collections can remain read only to application code:

```csharp
using Narwal.Permission.Domain;

public sealed class AppUser
{
    private readonly List<UserRole<Guid>> _roleAssignments = [];
    private readonly List<UserPermission<Guid>> _permissionAssignments = [];

    public Guid Id { get; private set; }
    public IEnumerable<UserRole<Guid>> RoleAssignments => _roleAssignments.AsReadOnly();
    public IEnumerable<UserPermission<Guid>> PermissionAssignments => _permissionAssignments.AsReadOnly();
}
```

Configure those collections on the same model:

```csharp
modelBuilder.ConfigureRolePermissionModel<AppUser, Guid>(
    user => user.RoleAssignments,
    user => user.PermissionAssignments);
```

The relationship mapping requires the user entity's primary key type to match `TUserId`. Deleting an app user cascades to its assignments in this mode.

## Register services

Register the manager and checker with the application's scoped DbContext. For a non-string key, supply a resolver when using ASP.NET Core permission policies. The resolver returns a success flag and the parsed user ID:

```csharp
using System.Security.Claims;
using Narwal.Permission.DependencyInjection;

// Register AppDbContext above with the provider already used by the application.
builder.Services.AddRolePermission<AppDbContext, Guid>(options =>
    options.UserIdResolver = principal =>
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId)
            ? (true, userId)
            : (false, default);
    });
```

For string user IDs, the default resolver reads `ClaimTypes.NameIdentifier`; no callback is needed. Other key types need a configured `UserIdResolver`. Role and permission services work without the ASP.NET Core policy integration or a resolver.

## Manage roles and permissions

The manager owns assignment changes. Package entities do not expose public setters or public assignment constructors, so applications do not need to update join rows directly.

```csharp
using Narwal.Permission.Services;

public sealed class PermissionSeeder(IRolePermissionManager<Guid> roles, AppDbContext dbContext)
{
    public async Task SeedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await roles.CreateRoleAsync("posts.editor", "Posts editor", cancellationToken);
        await roles.CreatePermissionAsync("posts.edit", "Edit posts", cancellationToken);
        await roles.GrantPermissionToRoleAsync("posts.editor", "posts.edit", cancellationToken);
        await roles.AssignRoleAsync(userId, "posts.editor", cancellationToken);

        // The host saves all changes in its unit of work.
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

Codes are trimmed and normalized to lowercase. They accept ASCII letters, digits, dots, hyphens, and underscores. `Name` is a separate display label and can be renamed while the code remains stable. A duplicate code throws `RolePermissionAlreadyExistsException`; management operations that refer to an unknown role or permission throw `RolePermissionNotFoundException`.

The manager also provides `RenameRoleAsync`, `RenamePermissionAsync`, `DeleteRoleAsync`, `DeletePermissionAsync`, `RevokePermissionFromRoleAsync`, `SyncRolePermissionsAsync`, `RemoveRoleAsync`, `SyncUserRolesAsync`, `GrantPermissionToUserAsync`, `RevokePermissionFromUserAsync`, and `SyncUserPermissionsAsync`. Synchronization replaces the selected user's or role's assignments with the supplied set. Repeated grants and assignments are idempotent. Deleting a role removes its grants and user assignments but leaves permissions intact; deleting a permission removes its role and direct user grants.

The manager never calls `SaveChangesAsync`. Save changes and transaction boundaries remain with the host application, so its other tracked updates can be committed together.

## Optional audit history

Audit history is disabled by default. The ordinary `ConfigureRolePermissionModel` call does not add an audit entity or table. To enable it, explicitly call `ConfigureRolePermissionAudit` from `OnModelCreating`; the application owns the audit table's migrations and schema.

The audit table defaults to `NarwalPermissionAuditEntries`. Pass the same table-name configuration to both model calls when overriding table names:

```csharp
var tableNames = new RolePermissionTableNames
{
    Roles = "AppRoles",
    Permissions = "AppPermissions",
    AuditEntries = "AppPermissionHistory"
};

modelBuilder.ConfigureRolePermissionModel<Guid>(tableNames);
modelBuilder.ConfigureRolePermissionAudit<Guid>(tableNames);
```

The ID-only mapping stores user ID snapshots without a foreign key to the app's user table:

```csharp
modelBuilder.ConfigureRolePermissionAudit<Guid>();
```

EF Core caches models by DbContext type. Configure a stable table-name set for each model; applications that vary table names by context instance need to include those values in their model cache key.

### Actor IDs

Register an actor provider to attach the current actor ID to manager-generated events. It is optional; if no actor is available, return `false`. The `Try` shape preserves a valid `Guid.Empty` ID separately from the absence of an actor:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Narwal.Permission.Services;

public sealed class RequestAuditActorProvider(IHttpContextAccessor accessor)
    : IRolePermissionAuditActorProvider<Guid>
{
    public bool TryGetActorUserId([MaybeNullWhen(false)] out Guid actorUserId)
    {
        var value = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(value, out actorUserId))
        {
            return true;
        }

        actorUserId = default;
        return false;
    }
}
```

Register the app provider with dependency injection. `AddRolePermission` keeps a provider registered by the application:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRolePermissionAuditActorProvider<Guid>, RequestAuditActorProvider>();
builder.Services.AddRolePermission<AppDbContext, Guid>();
```

### Optional user relationships

Audit records can also expose read-only collections on the app user. Add the two collections to the user model:

```csharp
using Narwal.Permission.Domain;

public sealed class AppUser
{
    private readonly List<UserRole<Guid>> _roleAssignments = [];
    private readonly List<UserPermission<Guid>> _permissionAssignments = [];
    private readonly List<RolePermissionAuditEntry<Guid>> _auditEventsAsActor = [];
    private readonly List<RolePermissionAuditEntry<Guid>> _auditEventsAbout = [];

    public Guid Id { get; private set; }
    public IEnumerable<UserRole<Guid>> RoleAssignments => _roleAssignments.AsReadOnly();
    public IEnumerable<UserPermission<Guid>> PermissionAssignments => _permissionAssignments.AsReadOnly();
    public IEnumerable<RolePermissionAuditEntry<Guid>> AuditEventsAsActor => _auditEventsAsActor.AsReadOnly();
    public IEnumerable<RolePermissionAuditEntry<Guid>> AuditEventsAbout => _auditEventsAbout.AsReadOnly();
}
```

Configure those collections after the normal RBAC model:

```csharp
modelBuilder.ConfigureRolePermissionModel<AppUser, Guid>(
    user => user.RoleAssignments,
    user => user.PermissionAssignments);
modelBuilder.ConfigureRolePermissionAudit<AppUser, Guid>(
    user => user.AuditEventsAsActor,
    user => user.AuditEventsAbout);
```

These relationships use separate nullable shadow foreign keys with client-side nulling. The mapping does not add database cascade actions, so it is compatible with SQL Server's cascade-path limits. Before deleting a user, load both audit collections so EF Core can clear their tracked user links while keeping the audit rows and original ID snapshots:

```csharp
var user = await dbContext.Users
    .Include(user => user.AuditEventsAsActor)
    .Include(user => user.AuditEventsAbout)
    .SingleAsync(user => user.Id == userId, cancellationToken);

dbContext.Users.Remove(user);
await dbContext.SaveChangesAsync(cancellationToken);
```

If the history is too large to load for user deletion, use the ID-only audit mapping; the actor and affected-user IDs remain available as snapshots without user foreign keys. When relationship mode is enabled, supplied actor and affected-user IDs must match existing user rows.

### Reading audit history

The public `RolePermissionAuditEntry<TUserId>` is immutable to application code. Entries record stable action codes such as `role.created`, `permission.renamed`, `role.permission_granted`, and `user.role_assigned`, along with applicable role or permission codes, name snapshots, actor ID, affected-user ID, and UTC time.

For any `TUserId`, check `HasActorUserId` and `HasAffectedUserId` before interpreting those ID properties. C# generic nullable annotations do not make a value type such as `Guid` nullable at runtime, so a missing `Guid` appears as `Guid.Empty` with its `Has...` flag set to `false`. A valid `Guid.Empty` is distinguishable because its flag is `true`.

```csharp
var history = await dbContext.Set<RolePermissionAuditEntry<Guid>>()
    .Where(entry => entry.HasAffectedUserId && entry.AffectedUserId == userId)
    .OrderByDescending(entry => entry.Id)
    .ToListAsync(cancellationToken);
```

Ordering by the generated audit ID works with SQLite, whose EF provider does not support ordering by `DateTimeOffset` values. For sequential writes, these IDs provide insertion ordering; they are not timestamps.

The manager queues an audit entry only when its operation changes package-managed state. The application still calls `SaveChangesAsync`; RBAC changes and audit rows persist together in the same unit of work. Direct EF changes, SQL, and user deletions performed outside the manager are not recorded.

## Check permissions

Use `IPermissionChecker<TUserId>` for application level checks. A permission is effective when granted directly to the user or through any assigned role.

```csharp
using Narwal.Permission.Services;

public sealed class PostAuthorizationService(IPermissionChecker<Guid> checker)
{
    public Task<bool> CanEditAsync(Guid userId) =>
        checker.HasPermissionAsync(userId, "posts.edit");

    public Task<bool> CanEditOrPublishAsync(Guid userId) =>
        checker.HasAnyPermissionAsync(userId, ["posts.edit", "posts.publish"]);
}
```

The checker also provides `HasRoleAsync`, `HasAnyRoleAsync`, `HasAllRolesAsync`, and `HasAllPermissionsAsync`. Empty any/all requests, invalid codes, and unknown single codes return `false`. Checks query the database, so changes take effect after the host saves them.

## Protect ASP.NET Core endpoints

Register authorization and the package's dynamic policy provider after `AddRolePermission`:

```csharp
builder.Services.AddAuthorization();
builder.Services.AddRolePermissionAuthorization<Guid>();
```

Use the `Permission:<code>` policy name:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("posts")]
public sealed class PostsController : ControllerBase
{
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "Permission:posts.edit")]
    public IActionResult EditPost(Guid id) => Ok();
}
```

The policy requires an authenticated user and denies access when the user ID cannot be resolved or the permission is not granted. Policies with other names are handled by ASP.NET Core's default policy provider.

## Contributing

See `CONTRIBUTING.md` for local build and pull request instructions. Pull requests run the Release build and test suite. Publishing runs only after a GitHub Release is published; maintainers can follow `docs/RELEASING.md`.
