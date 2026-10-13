# CI/CD pipelines

Each deployable gets its own path-filtered GitHub Actions workflow, so a change only rebuilds,
retests and republishes what it affects. All service pipelines call one reusable workflow,
[`_service-pipeline.yml`](../.github/workflows/_service-pipeline.yml).

| Workflow | Deployable | Image | Triggers (besides shared inputs\*) |
|---|---|---|---|
| `ci.yml` | Monolith (`src/Api` + all modules) | `ghcr.io/cog-gtm/booking-monolith` | everything except `src/Gateway/**`, `src/Services/**`, docs, other pipelines' files |
| `service-gateway.yml` | YARP gateway (`src/Gateway`) | `ghcr.io/cog-gtm/booking-gateway` | `src/Gateway/**` |
| `service-identity.yml` | Identity host | `ghcr.io/cog-gtm/booking-identity` | `src/Services/Identity.Host/**`, `src/Modules/Identity/**` |
| `service-flight.yml` | Flight host | `ghcr.io/cog-gtm/booking-flight` | `src/Services/Flight.Host/**`, `src/Modules/Flight/**` |
| `service-passenger.yml` | Passenger host | `ghcr.io/cog-gtm/booking-passenger` | `src/Services/Passenger.Host/**`, `src/Modules/Passenger/**` |
| `service-booking.yml` | Booking host | `ghcr.io/cog-gtm/booking-booking` | `src/Services/Booking.Host/**`, `src/Modules/Booking/**` |
| `packages.yml` | Shared NuGet packages | GitHub Packages NuGet feed | `src/BuildingBlocks/**`, `src/Contracts/**` |
| `deploy.yml` | One service to an environment | — | manual (`workflow_dispatch`) |

\* Shared inputs that trigger every module service pipeline: `src/BuildingBlocks/**`, `src/Contracts/**`,
`src/Aspire/src/ServiceDefaults/**`, `Directory.*.props`, `global.json`, `nuget.config`, `.editorconfig`,
`.dockerignore`, `.config/dotnet-tools.json` and the shared actions/reusable workflow. The gateway only
depends on ServiceDefaults, so BuildingBlocks/Contracts changes don't rebuild it.

## Service pipeline

`_service-pipeline.yml` (`workflow_call`) takes `service`, `dockerfile` and a JSON `targets` list of
`{name, project, tests}`:

1. **build-test** — one matrix job per target through `.github/actions/build-test` (restore, CSharpier
   check, Release build, `dotnet test` incl. Testcontainers suites, TRX report). Targets whose project
   directory doesn't exist yet are skipped with a notice, so pipelines for hosts that haven't landed
   stay green. `targets: "[]"` skips the job (used by `ci.yml`, which tests the monolith itself).
2. **image** — runs after build-test succeeds. Builds `dockerfile` (context = repo root) through
   `.github/actions/docker-build-publish` (Buildx + GHA layer cache). Skipped with a notice when the
   Dockerfile doesn't exist yet.

Conventions for new hosts: project `src/Services/<Module>.Host/src/`, tests
`src/Services/<Module>.Host/tests/`, Dockerfile `src/Services/<Module>.Host/Dockerfile`.

### When images are pushed

| Event | Build | Push |
|---|---|---|
| `pull_request` (incl. forks) | yes | **never** |
| push to `main` / manual run on `main` | yes | yes |
| tag `vX.Y.Z` | every pipeline | yes |
| tag `<service>-vX.Y.Z` | that service only | yes |

Tags: `sha-<short>` always; `X.Y.Z` and `X.Y` for release tags; `main` for the default branch.
Images are pushed with the workflow's `GITHUB_TOKEN` (`packages: write`).

## Shared packages

`packages.yml` packs every `IsPackable` project of the solution under `src/BuildingBlocks/` and
`src/Contracts/` (logic in `.github/actions/nuget-pack-publish`). A package is pushed to
`https://nuget.pkg.github.com/cog-gtm/index.json` only when its `PackageVersion` isn't in the feed yet,
so **bumping the version publishes it** and re-runs are idempotent. Pull requests do a dry run: pack
everything, push nothing, and warn when a package's sources changed but its version wasn't bumped.
Versioning is SemVer; bump the version (e.g. `<Version>` in the project or a `Directory.Build.props`)
with any change you want consumers to get, and follow the additive-only rule for contracts.

Consumers outside this repo add the feed with a PAT that has `read:packages`.

## Deploy

`deploy.yml` (manual) takes `service`, `environment` (`dev`/`staging`/`production` GitHub
environments, so protection rules/approvals apply) and `image-tag`. It verifies the image exists in
GHCR, then — once the Helm chart exists at `deployments/helm/booking` — lints/renders it and runs
`helm upgrade --install booking` into namespace `booking-<environment>` when the environment has a
`KUBE_CONFIG` secret. The chart is expected to expose `<service>.image.repository` and
`<service>.image.tag` values (and optional `values-<environment>.yaml`).
