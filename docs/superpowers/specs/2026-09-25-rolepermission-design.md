# Narwal.Permission NuGet Package Design

**Date:** 2026-09-25  
**Status:** Design for user review

## Purpose

Build a reusable role-based access control package for .NET 10 applications. The package should let application teams manage named permissions through roles and check those permissions in application code without repeatedly writing role and permission storage, assignment, and authorization logic.

The intended model follows Spatie Laravel Permission's core distinction: permissions represent capabilities the application checks, and roles group permissions for assignment to users. V1 is an ASP.NET Core and EF Core package that can be added to an existing application without requiring ASP.NET Core Identity.

## Goals

- Publish one NuGet package with the ID and root namespace `Narwal.Permission` and MIT license.
- Target .NET 10 and EF Core 10.
- Integrate RBAC entities into an application's existing EF Core `DbContext` and migrations.
- Use immutable string `Code` values as role and permission primary keys, so seed data and references are deterministic without generated `Guid` or integer keys.
- Support any EF Core compatible, non-null user ID type without owning authentication or the user table, with optional relationships to an application's user entity.
- Provide role and permission management APIs, direct user permission grants, effective permission checks, and ASP.NET Core policy integration.
- Initialize a Git repository with contribution guidance, pull request build/test checks, and release-triggered NuGet publishing.

## V1 scope

V1 provides globally scoped roles and permissions, with no organization or tenant dimension. Role and permission codes are immutable normalized string primary keys; display names can change independently. It includes:

- Create and delete roles and permissions; rename their display names while codes remain immutable.
- Grant, revoke, and synchronize permissions on a role.
- Assign, remove, and synchronize roles for a user ID.
- Grant, revoke, and synchronize direct permissions for a user ID.
- Check whether a user has a role and whether a permission is granted directly or through any assigned role. Permission checks support one, any, and all semantics.
- Protect ASP.NET Core endpoints with permission policies such as `[Authorize(Policy = "Permission:posts.edit")]`.

V1 does not include an authentication system, Identity-specific user model, tenant/team-scoped permissions, guards, wildcard permissions, nested role-to-role inheritance, admin UI, or database-provider-specific code. Permission codes are exact string capabilities; a dotted convention such as `posts.edit` is recommended but not required. Wildcard characters do not have special semantics.

## Package architecture

The package is one NuGet artifact organized internally into three areas. Its NuGet ID, assembly name, and root namespace are `Narwal.Permission`.

1. **RBAC management and evaluation** exposes services for role/permission assignment and effective checks.
2. **EF Core integration** supplies RBAC entities and a model-builder extension. The application calls `modelBuilder.ConfigureRolePermissionModel<TUserId>()` from its existing `DbContext`, uses its own migration workflow, and supplies its normal EF Core database provider.
3. **ASP.NET Core integration** supplies a permission authorization requirement/handler and a policy provider for the `Permission:<name>` policy format.

Roles and permissions use their immutable `Code` string as the primary key; there is no generated `Guid` or integer ID. Codes are trimmed, normalized to lowercase invariant form, and limited to lowercase ASCII letters, digits, dots, hyphens, and underscores. `Name` is a separate display label and can be renamed without changing foreign keys or seed references. `RolePermissionGrant` links a `RoleCode` to a `PermissionCode`; `UserRole<TUserId>` and `UserPermission<TUserId>` store the application's chosen user key together with a `RoleCode` or `PermissionCode`. The default model-builder overload configures only these scalar user IDs and does not add a foreign key or navigation to the application's user entity. This keeps the package independent of Identity and lets it work with applications that do not want to change their user model.

An opt-in model-builder overload accepts the application's user entity type and expressions for its role-assignment and direct-permission-assignment collections. It configures foreign keys from the assignment rows to the user table and exposes those collections as EF navigations. The user class does not need to inherit a package type or implement a package interface; it adds private backing collections exposed through read-only `IEnumerable<UserRole<TUserId>>` and `IEnumerable<UserPermission<TUserId>>` properties. The package does not add a navigation from the user-assignment row back to the application user unless a future API explicitly requests that direction. Both modes continue to use the same `TUserId` and the same authorization services.

Package-owned domain entities encapsulate their state. `Code` and `Name` have no public setters; creation, display-name changes, permission grants, and removals go through constructors/factories and named domain methods. Collection navigations use private backing fields and read-only views. EF Core is configured to materialize through private constructors and access backing fields. `IRolePermissionManager<TUserId>` is the supported write API for role and user assignments; consumers may query the optional user-side navigations without mutating assignment collections. This follows EF Core's documented backing-field pattern for read-only collection navigations ([navigations](https://learn.microsoft.com/en-us/ef/core/modeling/relationships/navigations), [backing fields](https://learn.microsoft.com/en-us/ef/core/modeling/backing-field)).

The application registers the package against its context and key type with `AddRolePermission<TContext, TUserId>()`. ASP.NET Core integration maps the authenticated `ClaimsPrincipal` to `TUserId` through an `IRolePermissionUserIdResolver<TUserId>`. The default resolver reads `ClaimTypes.NameIdentifier` for string keys; applications with another key type or claim convention configure a resolver.

The authorization handler asks the permission evaluator to check the current user against the application `DbContext`. A missing/unresolvable user ID or missing permission denies access. Direct user permissions and permissions inherited through roles are both effective. Checks query the database; V1 has no cross-request permission cache, so assignments take effect on subsequent checks after the application saves them.

Management methods operate on the application's scoped `DbContext` and update its change tracker. The application remains responsible for `SaveChangesAsync`, keeping transaction and unit-of-work ownership with the host application and avoiding package code saving unrelated tracked entities.

Role and permission codes are normalized before lookup and persistence, and the primary key enforces code uniqueness. This lets seeders use stable codes instead of generating a different key on each run. Display names are trimmed but need not be unique. Assignment tables use composite keys (`RoleCode` + `PermissionCode`, `UserId` + `RoleCode`, and `UserId` + `PermissionCode`) to prevent duplicate grants. Deleting a role removes its assignment rows but does not delete its permissions. Deleting a permission removes its links from roles and users.

## Public API shape

These signatures describe the intended public usage model:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    // Guid is the application's user ID type; role and permission keys are strings.
    modelBuilder.ConfigureRolePermissionModel<Guid>();
}
```

Applications that want user-side EF navigations add assignment collections to their user model and select them in the opt-in overload:

```csharp
public sealed class ApplicationUser
{
    private readonly List<UserRole<Guid>> _roleAssignments = [];
    private readonly List<UserPermission<Guid>> _permissionAssignments = [];

    public IEnumerable<UserRole<Guid>> RoleAssignments => _roleAssignments.AsReadOnly();
    public IEnumerable<UserPermission<Guid>> PermissionAssignments => _permissionAssignments.AsReadOnly();
}
```

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ConfigureRolePermissionModel<ApplicationUser, Guid>(
        user => user.RoleAssignments,
        user => user.PermissionAssignments);
}
```

```csharp
builder.Services.AddRolePermission<AppDbContext, Guid>(options =>
    options.UserIdResolver = principal =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null);

builder.Services.AddAuthorization();
builder.Services.AddRolePermissionAuthorization();
```

Consumers use an `IRolePermissionManager<TUserId>` for management operations and an `IPermissionChecker<TUserId>` for application checks. Management operations are asynchronous and accept `CancellationToken`. They take role and permission codes as identifiers plus display names when creating or renaming. Missing codes in management operations produce typed not-found errors; permission checks return `false` for unknown codes or users without a matching grant.

The manager includes `CreateRoleAsync(code, name)`, `CreatePermissionAsync(code, name)`, `RenameRoleAsync(code, name)`, `RenamePermissionAsync(code, name)`, `DeleteRoleAsync(code)`, `DeletePermissionAsync(code)`, `GrantPermissionToRoleAsync(roleCode, permissionCode)`, `RevokePermissionFromRoleAsync(roleCode, permissionCode)`, `SyncRolePermissionsAsync(roleCode, permissionCodes)`, `AssignRoleAsync(userId, roleCode)`, `RemoveRoleAsync(userId, roleCode)`, `SyncUserRolesAsync(userId, roleCodes)`, `GrantPermissionToUserAsync(userId, permissionCode)`, `RevokePermissionFromUserAsync(userId, permissionCode)`, and `SyncUserPermissionsAsync(userId, permissionCodes)`. The checker includes `HasRoleAsync(userId, roleCode)`, `HasAnyRoleAsync(userId, roleCodes)`, `HasAllRolesAsync(userId, roleCodes)`, `HasPermissionAsync(userId, permissionCode)`, `HasAnyPermissionAsync(userId, permissionCodes)`, and `HasAllPermissionsAsync(userId, permissionCodes)`.

## Repository and contribution workflow

The repository uses `main` as its initial branch, a .NET `.gitignore`, a root solution, one library project under `src/Narwal.Permission`, and test projects under `tests/`. The root contains `README.md`, `LICENSE` (MIT), `CONTRIBUTING.md`, a pull request template, and release instructions. The README includes installation, setup, management, and authorization examples, including `dotnet add package Narwal.Permission`.

The GitHub Actions pull request workflow triggers on `pull_request` only. It installs the .NET 10 SDK and runs restore, Release build, and tests for the solution. It does not run on ordinary branch pushes. Tests cover management behavior, direct and inherited permission evaluation, user ID resolution, authorization policies, and EF Core model/persistence behavior using SQLite in-memory as a relational test provider.

A separate GitHub Actions workflow triggers only when a GitHub Release is published. The release tag is the package version and must use `vMAJOR.MINOR.PATCH` or a NuGet-compatible prerelease suffix. The workflow packs the tagged commit and publishes to nuget.org; it does not rerun the pull request test suite. Publishing uses NuGet Trusted Publishing with GitHub OIDC and a short-lived token. Release instructions document the one-time NuGet.org trusted-publisher policy setup, version-tag procedure, and required GitHub release action.

## Risks and constraints

- The NuGet package ID must be available to claim on nuget.org before the first publication.
- Role and permission codes become persistent primary keys, so changing a code requires a deliberate data migration and updates to seeders/policy references; display names are the supported renameable labels.
- User ID key types must be supported by the selected EF Core database provider. Non-string IDs require a configured principal resolver for ASP.NET Core policy checks.
- In ID-only mode, the database cannot enforce that an assignment references an existing user, so the application must remove stale assignments when it deletes a user. Relationship mode adds user foreign keys and lets EF/database constraints manage assignment lifetime.
- Because the application owns `SaveChangesAsync`, permission/role changes are not effective until the host saves the `DbContext`.
- Permission checks hit the database in V1. Caching can be added later if measured workloads require it, with explicit invalidation behavior for assignment changes.
- NuGet Trusted Publishing requires a one-time policy configured on the NuGet.org account for the eventual GitHub owner, repository, and release workflow file.

## References

- [Spatie Laravel Permission introduction](https://spatie.be/docs/laravel-permission/v8/introduction)
- [Spatie roles vs permissions guidance](https://spatie.be/docs/laravel-permission/v8/best-practices/roles-vs-permissions)
- [EF Core relationship configuration](https://learn.microsoft.com/en-us/ef/core/modeling/relationships/one-to-many)
- [ASP.NET Core policy-based authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0)
- [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
