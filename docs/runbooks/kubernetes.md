# Kubernetes (Helm) runbook

The Helm chart at `deployments/k8s/helm/booking` deploys the gateway and the four service hosts. Only the gateway
is exposed by an Ingress; the application and infrastructure services use ClusterIP DNS names. The kind and
production overlays are in `deployments/k8s/environments`.

## Local kind cluster

Install Docker, kind, kubectl, Helm, jq, curl, and Python 3. The signing-key smoke step requires Python's
`cryptography` package. The default kind cluster maps its Traefik web NodePort to `http://localhost:8088`; override
the host port with `KIND_HTTP_PORT` if needed.

```bash
python3 -m pip install cryptography
bash deployments/k8s/kind/smoke.sh
```

The smoke script builds and loads the five application images, installs pinned Traefik and metrics-server
releases, deploys the kind overlay, checks rollouts and service DNS, verifies the mounted signing-key ID is present
in the IdentityServer JWKS and in a password-grant token header, then runs the same booking flow as Docker Compose
through the ingress. It leaves the `booking` kind cluster running by default. Set `KEEP_CLUSTER=0` to remove it
after the run or `SKIP_BUILD=1` to use already-built app images.

To install the chart manually after building and loading the application images:

```bash
kubectl create namespace booking
helm upgrade --install booking deployments/k8s/helm/booking \
  --namespace booking \
  --values deployments/k8s/environments/kind.yaml \
  --set global.image.tag=local
```

The kind overlay opts into development credentials and uses an ephemeral IdentityServer signing key unless
`services.identity.signingKey.existingSecret` is set. A generated developer key is not stable across replicas or
pod replacement; use one identity replica for local insecure defaults. The smoke test creates and mounts an RSA
JWK secret to verify the persistent-key path. Tokens are signed with this static Secret key, so identity replicas
sharing it would issue tokens with the same signing key. Duende's unlicensed automatic key management still
publishes an additional per-pod key in JWKS (a license warning may appear in logs); disabling it requires an
Identity code change and is a follow-up, not part of this PR. Keep Identity at one replica because its outbox worker
is also single-replica guarded.

## Production and secret management

Use the production overlay with an image registry/tag and pre-provisioned Secrets:

```bash
helm upgrade --install booking deployments/k8s/helm/booking \
  --namespace booking --create-namespace \
  --values deployments/k8s/environments/production.yaml
```

Production values deliberately set `allowInsecureDefaults: false`. Supply the named service Secrets in the
overlay; each `services.<service>.existingSecret` must contain exactly the environment keys listed below for its
service. Do not use the chart-generated local credentials in production.

| Secret | Required keys |
|---|---|
| Identity, Flight, Passenger, Booking service Secrets | `PostgresOptions__ConnectionString__<Module>`, `RabbitMqOptions__UserName`, `RabbitMqOptions__Password` |
| Flight, Passenger, Booking service Secrets | `MongoOptions__ConnectionString` |
| Booking service Secret | `EventStoreOptions__ConnectionString` |
| `services.identity.signingKey.existingSecret` | the key named by `services.identity.signingKey.key` (default `tempkey.jwk`), containing the IdentityServer RSA signing JWK |

The PostgreSQL connection string must use the selected server, port, database, role, password, and SSL mode, for
example `Server=postgres.example.internal;Port=5432;Database=flight_service;User Id=flight_service;Password=<secret>;SSL Mode=Require`.
Mongo read database names are separate (`flight_service_read`, `passenger_service_read`, and
`booking_service_read`). The Booking EventStore endpoint uses `esdb://<host>:2113?tls=false` for the local
insecure development configuration; provide a TLS-capable connection string through the Booking Secret for
production deployments as required by the EventStore configuration.

Identity mounts the signing Secret key read-only at `/app/tempkey.jwk` and leaves `/app/keys` writable for
Duende key management. Do not share the local generated developer key or deploy without a stable signing key.
The generated ConfigMap and chart-generated Secret checksums trigger pod rollouts when their values change.
Kubernetes cannot checksum changes in an externally managed `existingSecret`; restart the affected Deployment
after rotating one:

```bash
kubectl rollout restart deployment/flight-service -n booking
```

The production overlay intentionally disables bundled infrastructure and points at placeholder external hosts.
Set a real registry, external service names, OTLP endpoint, ingress TLS secret, and external Secret values before
deploying. Keep `infrastructure.persistence.enabled` enabled when deploying bundled stateful services outside
ephemeral kind clusters.

## Operational notes

- The app probes use `/health/live` for startup/liveness and `/health/ready` for readiness. Readiness includes
  required dependencies; pods restart naturally while dependencies are unavailable.
- Flight and Passenger expose HTTP on port 80 and h2c gRPC on port 81. Booking's gRPC client addresses and the
  gateway cluster destinations derive from the configured service DNS names.
- Configure gateway CORS with `services.gateway.cors.allowedOrigins`; an empty list leaves the appsettings defaults
  in effect, while the production overlay sets the public HTTPS origin.
- All chart pods set `enableServiceLinks: false`; injected variables such as `EVENTSTORE_SERVICE_PORT` can collide
  with EventStoreDB option parsing. DNS-based service discovery remains available.
- Background-processing services must remain at one replica while their workers are enabled. The chart rejects
  scale-out or HPA when `backgroundProcessing: true`, because the outbox has no row locking and Booking uses an
  EventStoreDB single-consumer subscription. These Deployments use Recreate, causing brief downtime during an
  upgrade so two background workers never overlap.
- The bundled PostgreSQL init script creates only the four service databases and roles. It is initialized only
  for a fresh PostgreSQL data volume.
- Run `bash deployments/k8s/tests/chart-render-test.sh` for Helm lint, render guards, and Kubernetes 1.31 schema
  validation.
