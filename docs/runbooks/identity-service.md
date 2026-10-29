# Identity service runbook

`Identity.Host` runs the Identity module independently on port 5103 and owns the `identity_service` PostgreSQL database, including its outbox. The monolith can continue serving the other modules while Identity requests are routed to this host.

## Run side by side with the monolith

Start PostgreSQL and RabbitMQ, then start `Identity.Host` and the monolith. Configure the monolith to stop polling Identity's outbox and point the gateway's Identity cluster at the standalone host:

```sh
Modules__Identity__BackgroundProcessingEnabled=false
ReverseProxy__Clusters__identity__Destinations__primary__Address=http://localhost:5103/
```

For local gateway validation, keep the gateway as the public issuer while fetching IdentityServer metadata from the standalone host:

```sh
AuthOptions__IssuerUri=http://localhost:5000
Jwt__Authority=http://localhost:5000
Jwt__MetadataAddress=http://localhost:5103/.well-known/openid-configuration
```

The values above are gateway settings. Configure the Identity host's `AuthOptions:IssuerUri` and `Jwt:Authority` to its externally visible identity URL when deployed behind a gateway or reverse proxy.

## Signing keys and tokens

IdentityServer uses a developer signing credential by default. The monolith and standalone host generate different development keys, so tokens issued by one are not interchangeable with tokens issued by the other. This is expected in local development; deployments that need issuer continuity must provision and securely share a signing key through the approved secret-management mechanism.

## Availability and data

- Run one Identity host replica while using the module-owned outbox worker. The current worker has no distributed lease or leader election, so multiple replicas can poll and publish the same outbox concurrently.
- The host connects only to the `identity_service` database. Do not point it at the monolith's `identity_modular_monolith` database for side-by-side operation.
- Moving existing Identity data from `identity_modular_monolith` into the service-owned database is tracked by AB-505. Complete and verify that migration before switching production traffic.
- Check `/alive` for process liveness and `/health` for PostgreSQL and RabbitMQ readiness. The discovery document is `/.well-known/openid-configuration`.

## Troubleshooting

- **Database connection failures:** verify `PostgresOptions:ConnectionString:Identity` resolves to `identity_service` and that the service login can connect.
- **Outbox backlog:** verify RabbitMQ reachability and `Modules:Identity:BackgroundProcessingEnabled=true` on the standalone host. When running side by side, confirm the monolith setting is `false`.
- **Gateway token validation failures:** check the gateway's issuer/authority and metadata address. A mismatch between issuer and discovery metadata, or the other host's developer signing key, prevents validation.
