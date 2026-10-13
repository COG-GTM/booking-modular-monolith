# ADR 0490: Per-service CI/CD pipelines, GHCR images and GitHub Packages NuGet feed

- Status: Proposed
- Ticket: AB-490 (epic AB-488)
- ARB ticket: TO BE CREATED (ARB_LIGHT)

## Context

The monolith is being split into Identity, Flight, Passenger and Booking services behind a YARP
gateway. A single `ci.yml` built and tested every module on every change and the
`docker-build-publish` action only targeted Docker Hub on `main`. Independent deployables need
independent pipelines, versioned images and a way to share BuildingBlocks/contracts as packages.

## Decision

- One thin, path-filtered workflow per deployable (`service-<name>.yml`, monolith stays in `ci.yml`)
  calling a reusable `workflow_call` pipeline (`_service-pipeline.yml`): build/test matrix, then image.
- Images go to **GitHub Container Registry** as `ghcr.io/cog-gtm/booking-<service>`, tagged
  `sha-<short>`, `main`, and SemVer from `vX.Y.Z` (all) / `<service>-vX.Y.Z` (one service) tags,
  pushed with the workflow `GITHUB_TOKEN`. Pull requests (incl. forks) build but never push.
- Shared packages (BuildingBlocks, Contracts) are published to the **GitHub Packages NuGet feed**
  of the org, once per `PackageVersion` (publish-on-bump, idempotent); PRs dry-run.
- Deploy is a manual, environment-gated `helm upgrade` placeholder until the chart (AB-491) exists.

## Alternatives considered

- Single CI with a change-detection job (`dorny/paths-filter`) — fewer files, but one workflow's
  status mixes services and a third-party action gets repo write access to compute filters.
- Docker Hub (existing action default) — needs extra org credentials and has pull rate limits;
  GHCR uses the built-in token and keeps images next to the code.
- Version every package from git tags (MinVer/GitVersion) — deferred; AB-500 owns versioning in
  props, the pipeline only reads `PackageVersion`.

## Consequences

- Module changes run both `ci.yml` (monolith) and that module's service pipeline (duplicate test
  minutes, parallel wall time); shared-code changes fan out to all pipelines by design.
- No new vendor: GHCR and GitHub Packages are part of the existing GitHub platform. Images/packages
  inherit org visibility; consumers outside the repo need a `read:packages` token.
- Security: least-privilege `permissions` per job; `packages: write` only in image/publish jobs;
  push gated on non-PR events on `main`/tags; untrusted PR fields passed via env, not inline.
- Follow-ups: ARB to confirm registry retention/visibility and image signing (cosign/SBOM);
  make the new workflow checks required in branch protection once hosts land.
