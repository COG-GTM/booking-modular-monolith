# 0007. Unauthenticated service-to-service gRPC (accepted risk, ARB follow-up)

- **Status:** Proposed (accepted risk, time-boxed)
- **Date:** 2026-10-13
- **ARB ticket:** TO BE CREATED (follow-up: service-to-service authentication)
- **Related ADRs:** [0003](0003-inter-service-communication.md)

## Context

In the monolith, Booking's gRPC calls to Flight and Passenger never leave the process. After the
split they cross the network. External callers are authenticated at the gateway and by every
service's JWT bearer validation, but the internal gRPC endpoints (Flight 5201, Passenger 5301 /
container port 81) currently have no caller authentication. Adding token propagation, client
credentials or mTLS now would widen every service-host ticket and couple them to an
identity design that has not been reviewed.

## Decision

- **For the migration, service-to-service gRPC stays unauthenticated.** Service-host tickets must
  not add gRPC authentication.
- Compensating controls:
  - gRPC ports are **not routed by the gateway** and not published outside the container network /
    cluster (compose network, Kubernetes `ClusterIP` + NetworkPolicy allowing only Booking, AB-491);
  - gRPC runs on dedicated ports so they can be firewalled separately from HTTP;
  - calls are traced (AB-495), so unexpected callers are visible.
- This is an **accepted risk** to be revisited by ARB before production use. Options for the
  follow-up: propagate the caller's JWT, OAuth2 client-credentials tokens issued by the Identity
  service, or mTLS via a service mesh.

## Alternatives considered

| Alternative | Pros | Cons | Why rejected (for now) |
| --- | --- | --- | --- |
| Forward the user JWT on gRPC calls | Reuses existing auth | Background/system calls have no user; audience design needed | Needs design + ARB |
| Client-credentials tokens from Identity | Standard, per-service identity | New clients/scopes, Identity becomes critical path for internal calls | Needs design + ARB |
| mTLS / service mesh | Transparent, strong | New infra (mesh, cert rotation) | New infrastructure, out of scope |

## Consequences

- Anyone with network access to the internal gRPC ports can call Flight/Passenger gRPC methods
  (including `ReserveSeat`) - mitigated only by network isolation.
- Must be resolved (new ADR superseding this one) before any internet-facing production rollout.
