# Service CI and publishing (AB-490)

## Pipeline contract

`CI` runs for PRs and pushes to `main` and `devin/ab-488-v3`. The `changes` job
selects independent matrix entries using `.github/scripts/ci.py`. The reusable
`service.yml` runs the existing `build-test` action, validates the Docker build,
and publishes **only after tests pass on a trusted branch push**. A failing
service does not cancel siblings. `CI result` is the stable aggregate check to
require in branch protection; it fails if selection, any service, or packing fails.
AppHost orchestration builds independently when its sources or global build inputs
change. Shared package metadata is checked on Linux and Windows. Main pushes keep
the existing Release Drafter update after the aggregate CI check succeeds.

Module changes also compile AppHost, whose project references build the monolith
API, guarding its calls into module APIs during migration. Only the affected
service's tests/image jobs run; unrelated service pipelines and shared packing
remain skipped. Standalone-host-only changes do not need this compatibility build.

| Change | Selected services |
| --- | --- |
| `src/Modules/Flight/**` or `src/Services/Flight.Host/**` | Flight only (same rule for Identity, Passenger, Booking) |
| `src/Gateway/**` | Gateway only |
| `src/Aspire/src/AppHost/**` | AppHost build only |
| `src/BuildingBlocks/**`, `src/Contracts/**`, ServiceDefaults, root build configuration | All existing services |
| Shared build/test actions, selection scripts, CI/service workflow, monolith composition | All existing services |
| Docs, deployment manifests alone | None |

Deleted and renamed files are considered too. Push selection compares `before`
to `sha`; PR selection compares base SHA to head SHA (including cumulative PR changes).
Manual dispatch rebuilds every service but does **not** publish images or packages.
The workflow never runs with `pull_request_target` or registry secrets on PR code.

During migration, a module without its host still builds/tests as a module, with
no image published. Adding `src/Services/<Module>.Host/<Module>.Host.csproj` and
`Dockerfile` automatically activates its image build/publish. A host missing a
Dockerfile is an error, not a silently skipped image. The gateway is selected once
`src/Gateway/*.csproj` or `src/Gateway/src/*.csproj` exists. Hosts likewise support
root or `src/` projects with a root Dockerfile. Module tests remain under `src/Modules/<Module>/tests`;
gateway tests, if provided, belong under `src/Gateway/tests`.

## Registry, credentials and versions

Images: `ghcr.io/<lowercase-owner>/booking-modular-monolith/<service>:sha-<full-commit>`.
No `latest` tag is pushed. SHA tags identify source, not guaranteed immutable
artifacts: rerunning a build can replace the tag if base images or dependencies
change. Pin the registry digest for immutable Helm values/rollback. Read the
digest from the registry or `docker buildx imagetools inspect <image>:sha-<commit>`.
Pushing a commit never automatically deploys it.

NuGet feed: `https://nuget.pkg.github.com/<owner>/index.json`. Shared solution
projects under `src/BuildingBlocks` and `src/Contracts` are packed on shared changes.
Only active solution projects are packed (the abandoned net6 Mongo project is
deliberately excluded). Test projects are never published. Every shared project
must be listed in the solution, including split libraries and proto packages.

`Directory.Build.props` defines `SharedPackageVersion` for BuildingBlocks;
`BuildingBlocksVersion` aliases it in `src/BuildingBlocks/Directory.Build.props`.
`src/Contracts/Versions.props` independently defines `ContractsMessagesVersion`.
Both preserve AB-500/AB-506's `BookingModularMonolith.<project-name>` IDs, and
each pack retains its own SemVer to keep project and exact-pinned package references
consistent. Bump the applicable version with releasable library or contract changes;
local BuildingBlocks overrides use `-p:SharedPackageVersion` and do not re-version
Contracts.Messages. A version bump (including a newly introduced version) publishes
on the next trusted push; unchanged versions only pack
and validate. `--skip-duplicate` makes reruns idempotent. Never replace an existing
version: bump it instead. Project files must not override `PackageVersion` or
`PackageId` without updating the package-consumer contract. The Contracts override
of root `PackageVersion` is intentional: its independent `ContractsMessagesVersion`
controls the exact pinned package reference. Proto projects must
include their `.proto` files as package content themselves; CI does not rename or
rewrite wire contracts.

Both registries use the run's short-lived `GITHUB_TOKEN`; no PAT or stored registry
password is required. Build/test/pack jobs explicitly use read-only tokens. Only
trusted-push image and package publishing jobs receive `packages: write`; the
shared package publisher downloads the already-validated pack artifact. The
publisher waits for selected service tests, AppHost and both metadata checks.
Shared BuildingBlocks/contracts test projects run before packing; the specialized
BuildingBlocks consumer workflow is path-filtered to shared/build inputs.
The main-only Release Drafter retains its scoped `contents: write` permission.
New GitHub packages default to
private; keep that visibility, link them to this repository and grant Actions access
when reusing an existing package. If publishing returns 403, an owner must enable
package creation for the organization or grant this repository access to the package.
Do not broaden the token to bypass an organization policy.

## Verify and troubleshoot

```bash
python3 -m unittest discover -s .github/scripts -p 'test_*.py' -v
python3 .github/scripts/ci.py select --base origin/devin/ab-488-v3 --head HEAD
python3 .github/scripts/ci.py layout flight
dotnet build booking-modular-monolith.sln -c Release
python3 .github/scripts/ci.py pack --output artifacts/packages
```

Service tests run all `.csproj` files in the selected test directory, preserving any
failure even if later projects pass. TRX reports and packed NuGet files are Actions
artifacts. Build-only PR container checks never log in or push. Testcontainers need
Docker; mirror/tag rate-limited Docker Hub images for local verification. Shared
test fixtures still referencing the monolith can build its transitive dependencies;
path selection controls **pipeline execution**, not that legacy compile-time graph.

For a release, merge a single-service change and confirm only that service appears
under `service` and its `publish` job outputs the SHA image reference. For a shared
release, bump `SharedPackageVersion` or `ContractsMessagesVersion`, confirm `shared-packages` uploads `.nupkg`
artifacts and `publish-packages` publishes the new version, then restore a consumer against the feed.
Publishing cannot be proven by a PR-only run; inspect the trusted push run too.

Rollback: restore the prior image SHA in the target environment's values using its
deployment process; pin the previous NuGet version in consumers. Do not delete old
packages or image tags as part of rollback. There is no automatic cluster deployment
in this ticket; environment approvals and credentials remain a deployment concern.
