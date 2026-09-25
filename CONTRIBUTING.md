# Contributing

Thanks for helping improve Narwal.Permission.

## Before opening a pull request

1. Open an issue for a substantial behavior change so the expected API and scope are clear.
2. Create a focused branch from `main` and keep changes related to one concern.
3. Add or update tests for changed behavior.
4. Run the Release test suite from the repository root:

   ```sh
   dotnet test RolePermission.sln -c Release
   ```

5. Update the README or release documentation when a public API or workflow changes.

## Pull requests

Use the pull request template. Describe the user visible change, relevant design choices, and the verification you ran. Keep commits reviewable and do not include generated `bin`, `obj`, test result, package, or credential files.

Every pull request runs the GitHub Actions validation workflow, which restores, builds in Release, and runs the solution tests. The workflow runs for pull requests; ordinary branch pushes do not trigger it.

## Code conventions

- Keep role and permission codes stable; use display names for labels that may change.
- Keep package model state encapsulated and route assignment writes through `IRolePermissionManager<TUserId>`.
- Do not make the package save the host application's DbContext.
- Keep tests provider backed with SQLite in-memory when behavior depends on EF Core persistence.
- Do not add NuGet.org publishing credentials or endpoints; releases publish to GitHub Packages.
