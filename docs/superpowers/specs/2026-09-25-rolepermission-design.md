# RolePermission NuGet Package Design

**Date:** 2026-09-25  
**Status:** Design for user review

## Purpose

Build a reusable role-based access control package for .NET 10 applications. The package should let application teams manage named permissions through roles and check those permissions in application code without repeatedly writing role and permission storage, assignment, and authorization logic.

The intended model follows Spatie Laravel Permission's core distinction: permissions represent capabilities the application checks, and roles group permissions for assignment to users. V1 is an ASP.NET Core and EF Core package that can be added to an existing application without requiring ASP.NET Core Identity.

## Goals

- Publish one NuGet package with the ID `RolePermission` and MIT license.
- Target .NET 10 and EF Core 10.
- Integrate RBAC entities into an application's existing EF Core `DbContext` and migrations.
- Support any EF Core compatible, non-null user ID type without owning authentication or the user table.
- Provide role and permission management APIs, direct user permission grants, effective permission checks, and ASP.NET Core policy integration.
- Initialize a Git repository with contribution guidance, pull request build/test checks, and release-triggered NuGet publishing.

## V1 scope

V1 provides globally scoped roles and permissions, with no organization or tenant dimension. It includes:

- Create, rename, and delete roles and permissions.
- Grant, revoke, and synchronize permissions on a role.
- Assign, remove, and synchronize roles for a user ID.
- Grant, revoke, and synchronize direct permissions for a user ID.
- Check whether a user has a role and whether a permission is granted directly or through any assigned role. Permission checks support one, any, and all semantics.
- Protect ASP.NET Core endpoints with permission policies such as `[Authorize(Policy = "Permission:posts.edit")]`.

V1 does not include an authentication system, Identity-specific user model, tenant/team-scoped permissions, guards, wildcard permissions, role inheritance, admin UI, or database-provider-specific code. Permission names are exact string capabilities; a dotted convention such as `posts.edit` is recommended but not required. Wildcard characters do not have special semantics.

## Package architecture

The package is one NuGet artifact organized internally into three areas:

1. **RBAC management and evaluation** exposes services for role/permission assignment and effective checks.
2. **EF Core integration** supplies RBAC entities and a model-builder extension. The application calls `modelBuilder.ConfigureRolePermissionModel<TUserId>()` from its existing `DbContext`, uses its own migration workflow, and supplies its normal EF Core database provider.
3. **ASP.NET Core integration** supplies a permission authorization requirement/handler and a policy provider for the `Permission:<name>` policy format.

Roles and permissions use package-owned `Guid` keys. `UserRole<TUserId>` and `UserPermission<TUserId>` store the application's chosen user key. These assignment entities do not have a navigation or foreign key to the application's user entity, so the package does not depend on Identity or require changing the user model.

The application registers the package against its context and key type with `AddRolePermission<TContext, TUserId>()`. ASP.NET Core integration maps the authenticated `ClaimsPrincipal` to `TUserId` through an `IRolePermissionUserIdResolver<TUserId>`. The default resolver reads `ClaimTypes.NameIdentifier` for string keys; applications with another key type or claim convention configure a resolver.

The authorization handler asks the permission evaluator to check the current user against the application `DbContext`. A missing/unresolvable user ID or missing permission denies access. Direct user permissions and permissions inherited through roles are both effective. Checks query the database; V1 has no cross-request permission cache, so assignments take effect on subsequent checks after the application saves them.

Management methods operate on the application's scoped `DbContext` and update its change tracker. The application remains responsible for `SaveChangesAsync`, keeping transaction and unit-of-work ownership with the host application and avoiding package code saving unrelated tracked entities.

Role names and permission names are trimmed and unique without regard to case. The model stores a normalized name for a unique index while retaining the supplied name for display. Assignment tables have composite keys or unique indexes to prevent duplicate grants. Deleting a role removes its assignment rows but does not delete its permissions.

## Public API shape

The exact names may be refined during implementation, while keeping this usage model:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ConfigureRolePermissionModel<Guid>();
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

Consumers use an `IRolePermissionManager<TUserId>` for management operations and an `IPermissionChecker<TUserId>` for application checks. Management operations are asynchronous and accept `CancellationToken`. Missing names in management operations produce typed not-found errors; permission checks return `false` for unknown permissions or users without a matching grant.

The manager includes `CreateRoleAsync`, `CreatePermissionAsync`, `RenameRoleAsync`, `RenamePermissionAsync`, `DeleteRoleAsync`, `DeletePermissionAsync`, `GrantPermissionToRoleAsync`, `RevokePermissionFromRoleAsync`, `SyncRolePermissionsAsync`, `AssignRoleAsync`, `RemoveRoleAsync`, `SyncUserRolesAsync`, `GrantPermissionToUserAsync`, `RevokePermissionFromUserAsync`, and `SyncUserPermissionsAsync`. The checker includes `HasRoleAsync`, `HasAnyRoleAsync`, `HasAllRolesAsync`, `HasPermissionAsync`, `HasAnyPermissionAsync`, and `HasAllPermissionsAsync`.

## Repository and contribution workflow

The repository uses `main` as its initial branch, a .NET `.gitignore`, a root solution, one library project under `src/RolePermission`, and test projects under `tests/`. The root contains `README.md`, `LICENSE` (MIT), `CONTRIBUTING.md`, a pull request template, and release instructions. The README includes installation, setup, management, and authorization examples.

The GitHub Actions pull request workflow triggers on `pull_request` only. It installs the .NET 10 SDK and runs restore, Release build, and tests for the solution. It does not run on ordinary branch pushes. Tests cover management behavior, direct and inherited permission evaluation, user ID resolution, authorization policies, and EF Core model/persistence behavior using a relational test provider.

A separate GitHub Actions workflow triggers only when a GitHub Release is published. The release tag is the package version and must use `vMAJOR.MINOR.PATCH` or a NuGet-compatible prerelease suffix. The workflow packs the tagged commit and publishes to nuget.org; it does not rerun the pull request test suite. Publishing uses NuGet Trusted Publishing with GitHub OIDC and a short-lived token. Release instructions document the one-time NuGet.org trusted-publisher policy setup, version-tag procedure, and required GitHub release action.

## Risks and constraints

- The NuGet package ID must be available to claim on nuget.org before the first publication.
- User ID key types must be supported by the selected EF Core database provider. Non-string IDs require a configured principal resolver for ASP.NET Core policy checks.
- Because the application owns `SaveChangesAsync`, permission/role changes are not effective until the host saves the `DbContext`.
- Permission checks hit the database in V1. Caching can be added later if measured workloads require it, with explicit invalidation behavior for assignment changes.
- NuGet Trusted Publishing requires a one-time policy configured on the NuGet.org account for the eventual GitHub owner, repository, and release workflow file.
