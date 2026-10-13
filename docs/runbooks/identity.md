# Identity cut-over (step 1 of 4)

| | Monolith | Service |
| - | - | - |
| Upstream | `http://booking_modular_monolith:80` | `http://identity-service:80` (host 5100) |
| Postgres | `identity_modular_monolith` | `identity_service` |
| Outbox/inbox | `identity_modular_monolith_persist_message` | `identity_service_persist_message` |
| Gateway routes | `/api/{v}/identity/**`, `/connect/**`, `/.well-known/**` | same |
| Async edges | publishes `UserCreated` -> Passenger (RabbitMQ) | same |

## Before the window

- Token continuity: both hosts must sign with the **same key** and advertise the **same issuer**.
  - Issuer: `AuthOptions__IssuerUri` = the public gateway URL on both hosts; `Jwt__Authority` on
    every host = gateway URL, `Jwt__MetadataAddress` = Identity's discovery document.
  - Key: `AddDeveloperSigningCredential()` persists `tempkey.jwk` in the working directory; the
    compose overlay mounts one `identity-signing-key` volume as the working directory of both
    Identity hosts. In production provide the key from the secret store to both hosts instead.
  - Check: `curl $GATEWAY/.well-known/openid-configuration/jwks` and
    `curl http://identity-service/.well-known/openid-configuration/jwks` return the same `kid`.
- Both hosts publish `UserCreated` on RabbitMQ (`MessageBroker__TransportType=RabbitMq`), so
  Passenger receives it regardless of which side owns Identity.
- `identity_service` is migrated (start identity-service once) and on the same EF migration.

## Flip

```bash
deployments/cutover/flip-route.sh identity service
```

Data: `identity_modular_monolith` -> `identity_service` (users, roles, claims, tokens), outbox
drained first so no `UserCreated` is lost.

## Verify

1. `flip-route.sh status` shows `identity service ... service`.
2. `POST /connect/token` through the gateway returns 200; decode `iss` = gateway URL.
3. A token issued **before** the flip is still accepted by `GET /api/v1.0/flight/get-available-flights`.
4. `POST /api/v1.0/identity/register-user` -> 200, then `POST /api/v1.0/passenger/complete-registration`
   for the same passport eventually returns 200 (UserCreated crossed RabbitMQ).

## Rollback

```bash
deployments/cutover/flip-route.sh identity monolith
```

Users registered on the service are copied back into `identity_modular_monolith`; tokens stay
valid. Rollback triggers: see [README section 6](README.md#6-monitoring-and-rollback-triggers),
plus any spike in `401` responses across all contexts.
