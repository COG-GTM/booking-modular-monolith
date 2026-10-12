# CI/CD pipeline layout

Each deployable (Gateway, Identity, Flight, Passenger, Booking) has its own GitHub Actions pipeline
that builds, tests and images only that service, triggered by path filters. The shared libraries
are published as NuGet packages by a separate workflow when their version is bumped. Decision
record: [ADR 0016](adr/0016-per-service-ci-cd-pipelines.md). Ticket:
[AB-245](https://cog-gtm.atlassian.net/browse/AB-245).

## Workflows

| Workflow | Purpose | Triggers |
| --- | --- | --- |
| `.github/workflows/_service-pipeline.yml` | Reusable (`workflow_call`) pipeline: `build-test` matrix (one job per project/test pair, via `.github/actions/build-test`), then `image` (via `.github/actions/docker-build-publish`) | Called by the `service-*.yml` workflows |
| `.github/workflows/service-gateway.yml` | Gateway → `booking-gateway` image | PR / push to `main` touching its paths; tags `v*.*.*`, `gateway-v*.*.*` |
| `.github/workflows/service-identity.yml` | Identity module + host → `booking-identity` | same, tags `identity-v*.*.*` |
| `.github/workflows/service-flight.yml` | Flight module + host → `booking-flight` | same, tags `flight-v*.*.*` |
| `.github/workflows/service-passenger.yml` | Passenger module + host → `booking-passenger` | same, tags `passenger-v*.*.*` |
| `.github/workflows/service-booking.yml` | Booking module + host → `booking-booking` | same, tags `booking-v*.*.*` |
| `.github/workflows/packages.yml` | Pack + publish bumped shared NuGet packages (script: `.github/scripts/publish-packages.sh`) | PR (dry run) / push to `main` touching shared libs; manual dispatch |
| `.github/workflows/ci.yml` | Monolith CI: the four modules + contract tests + cross-service E2E, release drafter | PR / push to `main`, except changes confined to `src/Gateway/**`, `src/Services/**`, docs or the pipelines above |
| `.github/workflows/docker-images.yml` | Monolith image (`src/Api/Dockerfile`) + Compose validation | PR / push to `main` touching the monolith, shared libs or `deployments/docker-compose/**` |

## Path filters

A service pipeline runs when any of these change:

| Service | Own paths | Shared inputs (all services) |
| --- | --- | --- |
| gateway | `src/Gateway/**` (includes `src/Gateway/Dockerfile`) | `src/BuildingBlocks/**`, `src/Contracts.Grpc/**`, `src/Contracts.Messages/**`, `src/Aspire/src/ServiceDefaults/**`, `Directory.*.props`, `global.json`, `.dockerignore`, `.editorconfig`, `.github/actions/**`, `.github/workflows/_service-pipeline.yml`, its own `service-<name>.yml` |
| identity | `src/Services/Identity/**`, `src/Modules/Identity/**` | ↑ |
| flight | `src/Services/Flight/**`, `src/Modules/Flight/**` | ↑ |
| passenger | `src/Services/Passenger.Host/**`, `src/Modules/Passenger/**` | ↑ |
| booking | `src/Services/Booking/**`, `src/Modules/Booking/**` | ↑ |

So a commit touching only `src/Services/Flight/**` runs only `Service - Flight`; a change to
`src/Contracts.Messages/**` runs all five service pipelines plus `Shared NuGet packages`, `CI` and
`Docker images`. Every service gets the full shared-library set (rather than its exact
`ProjectReference` closure) so a new transitive reference can never silently skip a rebuild.

GitHub evaluates `pull_request` path filters against the whole PR diff (merge base → head), and
`push` filters against the pushed commits. Path filters are not evaluated for tag pushes.

## Images and tags

Images go to GitHub Container Registry as `ghcr.io/<owner>/booking-<service>` (owner lower-cased,
e.g. `ghcr.io/cog-gtm/booking-flight`), authenticated with `GITHUB_TOKEN` (`packages: write`).
Tags come from `docker/metadata-action`:

| Event | Tags | Pushed |
| --- | --- | --- |
| Pull request | `pr-<number>` | No (image is built only) |
| Push to `main` | `sha-<short sha>`, `main` | Yes |
| Tag `v1.2.3` (releases every service) | `1.2.3`, `1.2`, `latest` | Yes |
| Tag `<service>-v1.2.3`, e.g. `flight-v1.2.3` (releases one service) | `1.2.3`, `latest` | Yes |

Buildx layers are cached in the GitHub Actions cache (scope `booking-<service>`).

## Shared NuGet packages and versioning

Packages: every packable project under `src/BuildingBlocks/*` (`BuildingBlocks.Core`, `.EFCore`,
`.EventStoreDB`, `.Grpc`, `.HealthCheck`, `.Jwt`, `.Mapster`, `.MassTransit`, `.Mongo`,
`.OpenTelemetryCollector`, `.PersistMessageProcessor`, `.ProblemDetails`, `.TestBase`,
`.Validation`, `.Web`), plus `Contracts.Grpc` and `Contracts.Messages`. Feed: GitHub Packages,
`https://nuget.pkg.github.com/<owner>/index.json` (`COG-GTM`), pushed with `GITHUB_TOKEN`.

Versioning scheme (SemVer):

- **BuildingBlocks.\*** are versioned in lockstep by `<VersionPrefix>` in
  `src/BuildingBlocks/Directory.Build.props`. Bump it once for any BuildingBlocks change you want to
  ship; all BuildingBlocks packages are published at the new version together (they reference each
  other, so mixing versions is not supported).
- **Contracts.Grpc** and **Contracts.Messages** are versioned independently by `<Version>` in their
  own `.csproj`, following the compatibility policy in
  [ADR 0005](adr/0005-contract-versioning.md): additive change → minor, fix → patch, breaking change
  → new proto package / message version and a major bump.

Publishing rule: on every push to `main` that touches the shared libraries, the workflow reads each
project's `PackageVersion` (`dotnet msbuild -getProperty`), asks the feed (NuGet v3
`PackageBaseAddress`) which versions exist, and packs + pushes only packages whose version is not
there yet. Unchanged versions are skipped (and `--skip-duplicate` guards against races), so a
version bump is the only way to publish. On pull requests the workflow is a dry run: it packs every
package (to catch packaging errors), pushes nothing, uploads the `.nupkg` files as an artifact, and
emits a warning for any package whose sources changed in the PR without a version bump (a change to its project directory or to any `Directory.*.props` between it and the repo root counts).

Consuming the feed requires a token with `read:packages`, e.g.:

```bash
dotnet nuget add source https://nuget.pkg.github.com/COG-GTM/index.json \
  -n cog-gtm -u <github-user> -p <token-with-read:packages> --store-password-in-clear-text
```

Run the publish logic locally against any NuGet v3 feed (e.g. a throwaway BaGet container):

```bash
NUGET_SOURCE=http://localhost:5555/v3/index.json NUGET_API_KEY=<key> PUBLISH=true \
  .github/scripts/publish-packages.sh
```

## Known gaps

- No deployment step yet (ticket marks it optional); images are published for a later K8s/Helm stage.
- The cross-service E2E suite runs in `ci.yml`, which ignores host-only changes; a change confined to
  one service host is covered by that service's build, host tests and image build only.
- `ci.yml` and `docker-images.yml` keep their original `main`-only `pull_request` / push branch
  scopes; the service and package pipelines run for pull requests against any base branch.
