# 0017. Cut-over order, per-context route flips with instant rollback, and monolith decommission

- **Status:** Proposed
- **Date:** 2026-10-12
- **ARB ticket:** TO BE CREATED
- **Authors:** Devin (for AB-248)
- **Owning team:** Booking platform team (owners of `booking-modular-monolith`)
- **Related ADRs:** 0002 Strangler-fig migration, 0006 Per-service database and outbox isolation, 0008 API gateway single ingress, 0009–0012 Standalone Identity/Flight/Passenger/Booking hosts, 0013 Per-module background-processing switch, 0014 Multi-service Docker Compose topology, 0015 Aspire AppHost multi-service topology

## Context

With the standalone hosts (ADRs 0009–0012), the YARP gateway (0008) and the compose/Aspire topologies (0014,
0015) in place, the remaining migration work is operational: moving production traffic from `src/Api` to the
services, being able to undo each step, and eventually deleting the monolith. ADR 0002 suggested the order
Flight → Passenger → Booking → Identity ("lowest coupling first") before the services existed. Since then:

- every service validates tokens against the standalone Identity host (`Jwt:MetadataAddress`), and every smoke
  check of a later flip needs a token;
- Identity publishes `UserCreated` over RabbitMQ to Passenger;
- Booking calls Flight and Passenger synchronously over gRPC;
- Identity, Passenger and Booking services currently share their module's monolith stores, while Flight has its
  own `flight_service` database.

There was no runbook, no tooling for the flip itself, and no rehearsed rollback.

ARB triggers: T1 (deployment topology: compose overlay adding the monolith to the services topology, gateway
deployment env changed per flip, shared `identity-signing-keys` volume mounted into both Identity hosts), T6
(routing of authenticated traffic and of the token endpoint moves between processes; one signing key shared by
two token issuers; `X-Gateway-Upstream` restricted to authenticated callers). The heuristic detector reported no triggers (no IaC files); the triggers above come from reading the diff.

## Decision

1. **Order:** Identity → Flight → Passenger → Booking. Identity first so the token issuer is the same process for
   every later flip and the publisher of Passenger's input moves before its consumer; Flight is a leaf (no outbound
   gRPC); Passenger before Booking; Booking last because it depends on both over gRPC. This supersedes the order in
   ADR 0002.
2. **A flip is one setting:** `ReverseProxy__Clusters__<ctx>__Destinations__monolith__Address` on the gateway,
   applied by `deployments/scripts/route-flip.sh` (compose: the project's git-ignored `.env`, so plain
   `docker compose up` preserves the flipped routes, + recreate only the gateway; Kubernetes:
   `kubectl set env deploy/gateway` + `rollout status`), with `--dry-run`, a post-flip `/health` check and an
   authenticated smoke request that fails unless `X-Gateway-Upstream` names the expected destination.
   **Rollback is the same command with `monolith`.**
3. **Two-phase data move:** flip routes while the service still uses the monolith's module store (instant, lossless
   rollback; ADR 0013 switch keeps exactly one background processor) — `docker-compose.phase-a.yaml` configures all
   four contexts this way, Flight included, because the monolith's Booking module reads Flight over its in-process gRPC
   and must see the rows flight-service writes — then separate data per context after burn-in (Flight only after
   Booking has moved), draining the outbox/inbox first and copying in a write freeze.
4. **Observability for the window:** opt-in `X-Gateway-Upstream` response header (`UpstreamHeaderOptions:Enabled`,
   default off because it leaks internal host names; when on, emitted only to authenticated callers,
   `AuthenticatedOnly` default true) plus YARP's per-request `Proxying to` log line.
5. **Token continuity:** both Identity hosts sign with one key during the window — `AuthOptions:SigningKeyPath`
   points them at a shared key file/volume (compose) or secret (Kubernetes); `validate-rollback.sh identity`
   fails when their JWKS differ. A production key store replaces the developer credential before the identity flip.
6. **Decommission** in two steps: `deployments/scripts/decommission-monolith.sh` removes the running monolith host
   once every cluster is on its service and a gateway log covering the whole quiet period shows nothing proxied to
   the monolith (fails closed if the log is unavailable or too young), recreates the gateway without the cut-over
   overlay but with the Phase A file (so a redeploy cannot restart the monolith and does not move Flight/Identity off
   their shared store/key before Phase B), then verifies all four contexts through the gateway without it; deleting `src/Api`, its CI/compose/Aspire wiring
   and the gateway `monolith` cluster follows as the checklist in `docs/migration/cutover-runbook.md`, after
   production burn-in. The code removal is not done in AB-248.

## Consequences

- Each flip is reversible in one gateway restart; rehearsed locally with `deployments/scripts/validate-rollback.sh`
  against `docker-compose.services.yaml` + `docker-compose.phase-a.yaml` + `docker-compose.cutover.yaml`.
- The monolith must keep running (and stay deployable) until decommission; the cut-over overlay runs it with Flight
  background processing on and Identity/Passenger/Booking off.
- flight-service's default configuration (own `flight_service` databases) must not be used while the monolith still
  serves Booking; `docker-compose.phase-a.yaml` overrides it to the shared store and stays in the deployment after
  decommission until Phase B for Flight (which follows the Booking flip) and the Identity key-store move retire it.
- Identity rollback requires both identity processes to share signing keys; enforced by `AuthOptions:SigningKeyPath`
  in the cut-over overlay and checked by `validate-rollback.sh identity` (pre-flight 3.3).
- YARP's built-in load balancers are not weighted, so weighted canaries use repeated destinations or a custom policy;
  header-based dark launch is the recommended canary.
