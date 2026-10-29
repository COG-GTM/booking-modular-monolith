# Step 2 — Identity + Passenger together

## Pre-checks and data separation

Flight must have completed its soak. Apply [common gates](README.md). Identity and Passenger are
one change unit because UserCreated crosses their boundary (ADR 0007).
Copy identity_modular_monolith to identity_service (role identity_service), including users,
password hashes, claims, grants, clients/resources and operational IdentityServer tables;
shared persist_message Identity rows go to identity_service_persist_message.
Copy passenger_modular_monolith to passenger_service (role passenger_service), retaining user/passenger
IDs and passport constraints; its message state goes to passenger_service_persist_message.
Drain outboxes/inboxes and broker queues, retain deduplication IDs and stop old dispatchers/consumers.
Audit user-to-passenger mappings and missing/duplicate UserCreated processing before enabling writes.
Rebuild passenger_service_read from Passenger authoritative rows with an audited backfill; verify
known user/passenger IDs via GET /api/v1/passenger/{id}. No general historical backfill tool is shipped.

Set PostgresOptions__ConnectionString__Identity/Passenger and
PersistMessageOptions__ConnectionString__Identity/Passenger to these databases and matching roles.
Set MongoOptions__Passenger__DatabaseName=passenger_service_read.
Preserve AuthOptions__IssuerUri, Jwt__Authority, Jwt__Audience, Jwt__MetadataAddress and signing keys:
Compose issuer is http://localhost:5000 and audience booking-modular-monolith.
AuthOptions__SigningKeyPath=/app/keys/tempkey.jwk persists in identity-signing-keys.
Legacy Identity also needs the same signing keys if issuing tokens during rollback; otherwise newly
issued tokens may not validate against the service JWKS. Test both old and new tokens.

## Flip / verification

```bash
scripts/cutover/flip-route.sh identity service
scripts/cutover/validate-rollback.sh passenger
```

Either identity or passenger argument always moves BOTH clusters. The validator is a local drill.
Production: obtain token at /connect/token, check discovery/JWKS issuer, register a fresh user at
POST /api/v1/identity/register-user with authentication, wait for UserCreated delivery, and verify
the Passenger read model and Booking gRPC lookup. Require no duplicate passenger and drained inbox.
Record separate upstream proofs for identity-service and passenger-service.
Do not keep the Compose monolith running during registration sanity: it also consumes UserCreated.

## Rollback

```bash
scripts/cutover/flip-route.sh passenger monolith
```

Trigger for token/issuer failures, lost or duplicate UserCreated, missing passenger projections,
wrong destinations or agreed error/latency/lag breach. Both routes reverse together.
First ensure monolith shares the newly written Identity/Passenger and persist-message databases,
read models and compatible signing material; otherwise pause and reconcile before reverse routing.
HTTP rollback does not change Booking's Grpc__PassengerAddress; verify that separately.
Transfer consumer ownership without two active legacy/service consumers.
Canary only read lookups/discovery with consistent signing keys; never shadow registration/token
issuance. Use the common trusted-header canary guidance; no weighted rollout is built into the script.
