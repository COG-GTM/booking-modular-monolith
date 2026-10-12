# 0016. Per-service CI/CD pipelines, image publishing and shared NuGet packages

- **Status:** Proposed
- **Date:** 2026-10-12
- **Authors:** Platform (via Devin, ticket [AB-245](https://cog-gtm.atlassian.net/browse/AB-245), epic [AB-231](https://cog-gtm.atlassian.net/browse/AB-231))
- **Owning team:** Platform team (owners of `.github/`); each service team owns its `service-<name>.yml` path list
- **Related ADRs:** 0001 (service boundaries), 0005 (contract versioning), 0014 (per-service container images)

## Context

`ci.yml` built and tested every module, host and the gateway in one sequential job on every change,
and `docker-images.yml` built all six images (never pushing). ADR 0014 gave each deployable its own
Dockerfile, but nothing published images, and the shared libraries (`src/BuildingBlocks/*`,
`src/Contracts.Grpc`, `src/Contracts.Messages`) were only consumable as project references. To
release services independently, a change to one service must build, test and publish only that
service, and shared contracts must be consumable as versioned packages.

## Decision

- One reusable workflow, `.github/workflows/_service-pipeline.yml` (`workflow_call`), runs a
  `build-test` matrix over the service's module and host projects using the existing
  `.github/actions/build-test` composite action, then builds the image with the existing
  `.github/actions/docker-build-publish` action (now Buildx-based, accepting full tag lists, an
  explicit push flag, registry login and GHA layer cache).
- Five thin callers (`service-{gateway,identity,flight,passenger,booking}.yml`) own the triggers:
  path filters on the service's host directory, its module, the shared libraries, the root build
  inputs (`Directory.*.props`, `global.json`, `.editorconfig`) and the pipeline files.
- Images are pushed to GHCR (`ghcr.io/<owner>/booking-<service>`) with `GITHUB_TOKEN` only for pushes
  to `main` (`sha-<short>`, `main`) and release tags (`v1.2.3` for all services, `<service>-v1.2.3`
  for one); pull requests build without pushing.
- `.github/workflows/packages.yml` publishes the shared libraries to GitHub Packages only when the
  package version is not yet in the feed. BuildingBlocks are versioned in lockstep
  (`src/BuildingBlocks/Directory.Build.props`), contracts independently (`<Version>` in each csproj,
  per ADR 0005).
- `ci.yml` keeps the monolith coverage (modules, contract tests, cross-service E2E, release
  drafter) and ignores changes confined to the gateway/service hosts; `docker-images.yml` keeps the
  monolith image and Compose validation, path-filtered.

Details, tag matrix and the versioning procedure: [docs/ci-cd.md](../ci-cd.md).

## Alternatives considered

| Alternative | Why rejected |
| --- | --- |
| Keep a single `ci.yml` with `dorny/paths-filter` jobs | One workflow, one status; harder to give each service its own required check, triggers and release tags |
| Five fully independent workflows (copy/paste) | Drift between services; every fix has to be made five times |
| Exact `ProjectReference` closure per service in path filters | Fewer rebuilds, but a new transitive reference silently skips a rebuild; shared libraries change rarely |
| Publish packages on every merge with a computed pre-release version | Noisy feed; consumers would float on unreviewed versions; ADR 0005 requires deliberate SemVer bumps |
| Docker Hub as registry | Needs extra secrets; GHCR works with `GITHUB_TOKEN` and links images to the repo |

## Consequences

- A change confined to one service runs one pipeline (plus repo-wide PR hygiene workflows); a shared
  library change fans out to all five.
- Each service has its own status checks (`Service - <Name> / pipeline / ...`) that can be made
  required per service.
- Shared library changes do not ship as packages until someone bumps the version; the PR dry run
  warns when sources changed without a bump.
- The cross-service E2E suite no longer runs for host-only changes (follow-up: add it as an optional
  job of the reusable pipeline once it is fast enough).
- No deployment stage yet; the optional K8s/Helm deploy from AB-245 is left for a follow-up.
