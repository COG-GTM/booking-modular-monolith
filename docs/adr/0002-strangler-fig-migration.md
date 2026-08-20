# ADR 0002: Strangler-fig migration behind an API gateway

## Status

Accepted

## Context

We must migrate to microservices without a big-bang rewrite or downtime. The monolith
(`src/Api`) currently exposes all module endpoints from a single host.

## Decision

Use the **strangler-fig pattern behind an API gateway**:

1. Introduce an API gateway (YARP, see AB-234) as the single ingress. Initially it routes
   100% of traffic to the monolith.
2. Peel off one service at a time (recommended order: Flight → Passenger → Booking →
   Identity, lowest-coupling first). For each service, flip the gateway route for that
   module's path prefix (e.g. `/api/v*/flight/**`) from the monolith to the new service.
3. The monolith keeps running throughout; a module's code is deleted from the monolith
   only after its service has taken 100% of traffic and burned in.
4. Routing flips are configuration-only and instantly reversible (roll back = point the
   route back at the monolith).

## Consequences

- Clients see a single stable origin; no client changes are needed as services move.
- The gateway becomes a critical component and must be deployed highly available.
- During the transition both the monolith and extracted services run, temporarily
  increasing infrastructure footprint.
- Each extraction is independently shippable and reversible, which keeps risk per step
  small.
