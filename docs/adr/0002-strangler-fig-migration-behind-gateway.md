# 0002. Strangler-fig migration behind a YARP gateway

- **Status:** Proposed
- **Date:** 2026-10-13
- **ARB ticket:** TO BE CREATED
- **Related ADRs:** [0001](0001-service-boundaries.md), [0005](0005-side-by-side-background-processing-switch.md),
  [0008](0008-service-host-layout-and-ports.md); implemented by AB-489, cut over by AB-505

## Context

Clients call the monolith directly (`src/Api`, `https://localhost:3000` / `http://localhost:3001`)
on `/api/v1.0/{module}/...` routes. Moving every module at once ("big bang") would require the
four services, their data and all contracts to be ready and correct simultaneously, with no
incremental rollback.

## Decision

We will migrate with the **strangler-fig pattern behind an API gateway**:

1. Introduce a YARP gateway (`src/Gateway`, port **5000**) as the single ingress (AB-489). Initially
   it proxies everything to the monolith, so clients switch to the gateway with no behaviour change.
2. The gateway has **one YARP cluster per module** (`identity`, `flight`, `passenger`, `booking`).
   Each cluster has a single destination named **`monolith`**. Routes match the module's path
   prefix `/api/{version}/<module>/...`; IdentityServer's `/connect/...` and `/.well-known/...`
   endpoints also go to `identity`.
3. A module is peeled off by repointing its cluster's destination address via configuration only:
   `ReverseProxy__Clusters__<module>__Destinations__monolith__Address=http://<module>-service`.
   The destination keeps the name `monolith` so the switch and rollback are a single config value.
4. The monolith `src/Api` keeps building and running **unchanged** on its existing databases and
   in-memory bus for the whole transition. It is decommissioned only after every cluster points at
   a service and the cutover runbook (AB-505) has been executed.
5. Suggested extraction order (lowest coupling first): Identity, Flight, Passenger, Booking.

## Alternatives considered

| Alternative | Pros | Cons | Why rejected |
| --- | --- | --- | --- |
| Big-bang rewrite/cutover | One switch | All-or-nothing risk, no incremental rollback | Too risky |
| Client-side routing (clients call each service) | No gateway | Every client changes per step; exposes internal topology | Breaks clients repeatedly |
| Ingress controller / service mesh routing only | Infra-native | Not available in local/Aspire/compose dev loops; no .NET-level tests | YARP runs everywhere the app runs and is testable |

## Consequences

- Each module can be moved (and moved back) independently with a config change.
- The gateway is a new hop and a single point of failure; it must be stateless and horizontally
  scaled, with health checks per destination.
- For a while two hosts can run the same module code - background work must run in exactly one of
  them ([0005](0005-side-by-side-background-processing-switch.md)).
