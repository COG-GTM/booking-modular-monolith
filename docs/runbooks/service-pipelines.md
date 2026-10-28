# Service CI and package publishing

The monolith's existing `ci.yml` is unchanged. Five independent
`service-<service>.yml` entry workflows call `service-pipeline.yml` on PRs
and pushes to `main` and `devin/ab-488-v2`. Manual runs are also supported.
No deployment is performed; deployment credentials/environments remain out of scope.

## Host interface

| Service | Project | Dockerfile | Tests |
| --- | --- | --- | --- |
| gateway | src/Gateway/Gateway.csproj | src/Gateway/Dockerfile | src/Gateway/tests |
| identity | src/Services/Identity.Host/Identity.Host.csproj | src/Services/Identity.Host/Dockerfile | src/Modules/Identity/tests |
| flight | src/Services/Flight.Host/Flight.Host.csproj | src/Services/Flight.Host/Dockerfile | src/Modules/Flight/tests |
| passenger | src/Services/Passenger.Host/Passenger.Host.csproj | src/Services/Passenger.Host/Dockerfile | src/Modules/Passenger/tests |
| booking | src/Services/Booking.Host/Booking.Host.csproj | src/Services/Booking.Host/Dockerfile | src/Modules/Booking/tests |

Both project and Dockerfile must exist. Until then, the pipeline emits a notice
and succeeds without building or publishing. Tests are recursively discovered
below the listed directory (including Host.Test); an absent test directory emits
a notice. Dockerfiles must use repository-root build context.

A Flight-only change selects Flight, not the other four pipelines. A host,
module or its tests selects its pipeline. Shared Contracts, BuildingBlocks,
ServiceDefaults, global build/NuGet settings and reusable actions select all
services: any service may depend on those shared inputs. Changes to an entry
workflow select only that service; reusable service workflow changes select all.
Root npm/tool/format settings also select all because the existing build action
checks formatting. Solution-only edits do not select services.

Each available service restores/builds only its host dependency graph and runs
only its service tests. The build/test action remains usable by monolith CI.
Test failures (including failures before TRX creation) fail the pipeline.
Container builds also run on PRs without registry credentials.

## Images and permissions

After successful build/tests, trusted branch pushes publish:

```text
ghcr.io/cog-gtm/booking-<service>:sha-<full GitHub commit SHA>
```

There is no mutable `latest` tag. Select an earlier SHA tag to roll back.
Images carry OCI source/revision labels linking to this repository.
Only publishing jobs receive `packages: write`; build/test jobs are read-only.
PRs, including fork PRs, never authenticate to the registry or push images.
Manual dispatch only publishes when the selected ref is main or the v2 base.
The existing docker-build-publish action accepts `registry`, a namespaced
`image-name`, and explicit `push: 'true'`; the safe default is build-only.

GitHub's repository-scoped GITHUB_TOKEN is sufficient. No new long-lived secret
is needed. Org settings must allow this repository to create GHCR/NuGet packages.
If a package already exists, grant this repository Actions access to it.
New package visibility follows GitHub's default (private); an owner must review
any request to make it public. A denied publish fails; it is not silently skipped.

## Shared NuGet packages

Feed: `https://nuget.pkg.github.com/COG-GTM/index.json`.

`shared-packages.yml` discovers `src/Contracts/**/Contracts.*.csproj`,
`src/BuildingBlocks/**/BuildingBlocks.*.csproj`, and the compatibility
`src/BuildingBlocks/BuildingBlocks.csproj`. It excludes tests/bin/obj and
honors `IsPackable=false`. Use `PackageId` and `PackageVersion` (or `Version`)
in the project or imported props; MSBuild evaluates these, rather than parsing
only project XML. Keep referenced package versions compatible when bumping a
dependency (especially the BuildingBlocks aggregate).

- New packable projects publish their initial version.
- Existing package IDs publish only when their evaluated version changes.
- Source-only changes with no version bump do not republish an existing package.
- PRs pack selected packages for validation but cannot publish.
- Pushes compare against `github.event.before` (including multi-commit pushes);
  PRs compare against their base SHA. Manual runs accept a `base` commit,
  defaulting to the previous commit. Reruns use `--skip-duplicate`.
- No packages selected is a successful, noticed no-op.

Local selection: `python3 .github/scripts/shared_packages.py --base origin/devin/ab-488-v2 --output packages.json`.
This evaluates the baseline in an isolated git archive; it never changes the
checkout. Do not commit the generated package list or artifacts.

## Validation and operations

Run `python3 -m unittest discover -s .github/scripts/tests -v` and
`actionlint` for workflow regression checks (tests need PyYAML).
Run `dotnet build booking-modular-monolith.sln` plus the affected module's
tests before merging. Testcontainers tests require Docker.
Workflow runs and job summaries are the audit trail; the publishing jobs'
logs identify image tags and NuGet responses. On failures, inspect the failed
build/test/publish step; retry a trusted push run after fixing registry access.
Do not disable test/policy gates or increase token scope to bypass a failure.
