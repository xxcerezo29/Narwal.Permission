# Releasing to GitHub Packages

The release workflow publishes the package to GitHub Packages' NuGet registry when a GitHub Release is published. It does not publish on branch pushes or pull requests.

## Before the first release

- Confirm the repository owner and package namespace are the intended GitHub account or organization. The workflow uses `https://nuget.pkg.github.com/<owner>/index.json`.
- Confirm the repository allows Actions to create and write packages. The workflow uses the built in `GITHUB_TOKEN` with `contents: read` and `packages: write`; no long lived publishing secret is needed.
- Confirm the package is associated with this repository. The workflow passes `https://github.com/<owner>/<repository>` as the package `RepositoryUrl` during pack.
- After the first publish, review the package's GitHub settings and repository access. GitHub Packages packages are private by default. Keep it private for restricted distribution, or change visibility to public for public consumption. Consumers installing outside GitHub Actions need package read access and a classic GitHub personal access token with `read:packages`; workflows can use `GITHUB_TOKEN` when their repository has package access. See [GitHub's package access guide](https://docs.github.com/en/packages/learn-github-packages/configuring-a-packages-access-control-and-visibility) for visibility and access settings.

## Publish a version

1. Merge the release changes to `main` and confirm the pull request validation workflow succeeded.
2. Create and push a version tag in `vMAJOR.MINOR.PATCH` form, optionally with a NuGet compatible prerelease suffix, for example `v0.1.0` or `v0.2.0-rc.1`.
3. Create a GitHub Release for that tag and publish it.
4. Check the `Publish GitHub Packages release` workflow and confirm `Narwal.Permission.<version>.nupkg` appears in the repository owner's GitHub Packages registry.

The workflow removes the leading `v` from the release tag for the NuGet package version. It packs the release source and pushes only to the GitHub Packages NuGet source. If a package version already exists, NuGet package versions cannot be overwritten; publish a new version instead.
