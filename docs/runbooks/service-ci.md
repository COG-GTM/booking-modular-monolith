# Service CI and publishing (AB-490)

## Pipeline contract

`CI` runs for PRs and pushes to `main` and `devin/ab-488-v3`. The `changes` job
selects independent matrix entries using `.github/scripts/ci.py`. The reusable
`service.yml` runs the existing `build-test` action, validates the Docker build,
and publishes **only after tests pass on a trusted branch push**. A failing
service does not cancel siblings. `CI result` is the stable aggregate check to
require in branch protection; it fails if selection, any service, or packing fails.

| Change | Selected services |
| --- | --- |
| `src/Modules/Flight/**` or `src/Services/Flight.Host/**` | Flight only (same rule for Identity, Passenger, Booking) |
| `src/Gateway/**` | Gateway only |
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
`src/Gateway/*.csproj` exists. Module tests remain under `src/Modules/<Module>/tests`;
gateway tests, if provided, belong under `src/Gateway/tests`.

## Registry, credentials and versions

Images: `ghcr.io/<lowercase-owner>/booking-modular-monolith/<service>:sha-<full-commit>`.
No mutable `latest` tag is pushed. Use the exact SHA tag or registry digest for
Helm values/rollback; pushing a commit never automatically deploys it.

NuGet feed: `https://nuget.pkg.github.com/<owner>/index.json`. Shared solution
projects under `src/BuildingBlocks` and `src/Contracts` are packed on shared changes.
Only active solution projects are packed (the abandoned net6 Mongo project is
deliberately excluded). Test projects are never published. Every shared project
must be listed in the solution, including split libraries and proto packages.

`Directory.Build.props` defines `SharedPackageVersion` and defaults shared package
IDs to `Booking.<project-name>`. Bump this SemVer value in the same PR as releasable
library/contract changes. All shared packages use that version together so project
references generate consistent NuGet dependencies. A version change (including the
initial version) publishes on the next trusted push; unchanged versions only pack
and validate. `--skip-duplicate` makes reruns idempotent. Never replace an existing
version: bump it instead. Project files must not override `PackageVersion` or
`PackageId` without updating the package-consumer contract. Proto projects must
include their `.proto` files as package content themselves; CI does not rename or
rewrite wire contracts.

Both registries use the run's short-lived `GITHUB_TOKEN`; no PAT or stored registry
password is required. Repository contents are read-only. Only image publishing and
the shared-package job receive `packages: write`. New GitHub packages default to
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
release, bump `SharedPackageVersion`, confirm `shared-packages` uploads `.nupkg`
artifacts and publishes the new version, then restore a consumer against the feed.
Publishing cannot be proven by a PR-only run; inspect the trusted push run too.

Rollback: restore the prior image SHA in the target environment's values using its
deployment process; pin the previous NuGet version in consumers. Do not delete old
packages or image tags as part of rollback. There is no automatic cluster deployment
in this ticket; environment approvals and credentials remain a deployment concern.
