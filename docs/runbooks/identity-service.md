# Identity service runbook

`Identity.Host` runs the Identity module independently on port 5103 and owns the `identity_service` PostgreSQL database, including its outbox.

## Local operation

Start PostgreSQL and RabbitMQ, then start `Identity.Host`:

```sh
dotnet run --project src/Services/Identity.Host/Identity.Host.csproj
```

For local gateway validation, keep the gateway as the public issuer while fetching IdentityServer metadata from the standalone host:

```sh
AuthOptions__IssuerUri=http://localhost:5000
Jwt__Authority=http://localhost:5000
Jwt__MetadataAddress=http://localhost:5103/.well-known/openid-configuration
```

The values above are gateway settings. Configure the Identity host's `AuthOptions:IssuerUri` and `Jwt:Authority` to its externally visible identity URL when deployed behind a gateway or reverse proxy.

## Signing keys and tokens

The cutover Compose topology mounts the same named signing-key and Data Protection
volumes into Identity.Host and the prebuilt legacy image. Keep those volumes with the
legacy image to preserve token and protected-data continuity during rollback. Ordinary
local processes use their own developer key; production keys must use the approved
secret-management mechanism.

## Availability and data

- Run one Identity host replica while using the module-owned outbox worker. The current worker has no distributed lease or leader election, so multiple replicas can poll and publish the same outbox concurrently.
- The host connects only to the `identity_service` database.
- Check `/alive` for process liveness and `/health` for PostgreSQL and RabbitMQ readiness. The discovery document is `/.well-known/openid-configuration`.

## Troubleshooting

- **Database connection failures:** verify `PostgresOptions:ConnectionString:Identity` resolves to `identity_service` and that the service login can connect.
- **Outbox backlog:** verify RabbitMQ reachability and `Modules:Identity:BackgroundProcessingEnabled=true` on the active host.
- **Gateway token validation failures:** check the gateway's issuer/authority and metadata address. A mismatch between issuer and discovery metadata, or the other host's developer signing key, prevents validation.
