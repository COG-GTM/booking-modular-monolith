# Kubernetes deployment

The `booking` Helm chart deploys the Identity, Flight, Passenger, and Booking services behind one
public YARP gateway; it does not deploy the legacy monolith. Only the gateway is exposed through
the Ingress. Service HTTP and h2c gRPC endpoints are ClusterIP-only.

## Install

Helm 3 is required. Create the signing-key Secret before installing; the chart intentionally does
not generate or rotate IdentityServer signing material:

```sh
deployments/k8s/scripts/generate-signing-key.sh /secure/path/tempkey.jwk
kubectl create namespace booking
kubectl create secret generic identity-signing-key -n booking \
  --from-file=tempkey.jwk=/secure/path/tempkey.jwk
helm upgrade --install booking deployments/k8s/helm/booking \
  --namespace booking --create-namespace \
  --values deployments/k8s/environments/kind.yaml
```

For production, replace the example hosts and Secret names in `environments/production.yaml` with
environment-specific values before installing. `namespace.create` is optional and defaults to
`false`; when enabled, it creates the configured namespace with Pod Security Admission labels for
the `restricted` profile. Normal installation uses `-n booking --create-namespace`.

## Values reference

| Values | Purpose |
| --- | --- |
| `global.publicUrl` | Public issuer and IdentityServer issuer URI; for kind it is `http://localhost:8088`. |
| `global.audience` | JWT audience required by service hosts. |
| `global.otlpEndpoint` | Optional OTLP/gRPC collector endpoint; no exporter is enabled when empty. |
| `imagePullSecrets` | Optional pod image-pull Secret references; the production overlay expects `ghcr-pull`. |
| `services.<identity\|flight\|passenger\|booking\|gateway>.nameOverride` | Overrides that workload's stable Kubernetes Service name; all inter-service addresses use the helper-derived name. |
| `services.<name>.image.{repository,tag,pullPolicy}` | Image metadata per workload. |
| `services.<name>.replicas`, `services.<name>.autoscaling` | Replica and CPU HPA settings. HPA defaults on only for the gateway. |
| `services.<identity\|flight\|passenger\|booking>.backgroundProcessingEnabled` | Enables that module's background processing; the chart rejects multiple replicas or HPA max above one when enabled. |
| `services.<name>.probes` | Startup, liveness, and readiness paths. Defaults are `/health/live`, `/health/live`, and `/health/ready`. |
| `services.<name>.resources` | Container CPU and memory requests and limits. |
| `services.<name>.database` | PostgreSQL role, database names, password Secret, and Mongo read database. Booking has no primary Postgres database, only its persist-message database. |
| `postgres.{host,port,sslMode}` | PostgreSQL endpoint and connection-string TLS mode. The chart derives the host from the `postgres` infra Service when unset. |
| `rabbitmq.{host,port,vhost,auth}` | RabbitMQ endpoint and Secret contract. |
| `mongodb.{connectionString,existingSecret,connectionStringKey}` | MongoDB connection-string Secret contract. |
| `eventstore.{connectionString,existingSecret,connectionStringKey}` | Booking EventStoreDB connection-string Secret contract. |
| `identity.signingKey.{existingSecret,key}` | Secret mounted read-only at `/app/keys/tempkey.jwk`; Duende key-management files use the writable pod-local `/app/keys` workspace. |
| `gateway.ingress.{enabled,className,host,annotations,tls}` | Public Ingress configuration; optional annotations and TLS entries are rendered onto the Ingress. |
| `infrastructure.enabled` | Enables development-only single-instance PostgreSQL, MongoDB, RabbitMQ, and EventStoreDB. Defaults off. |
| `infrastructure.persistence.{enabled,size,storageClass}` | PVC settings for development infrastructure; disable only for disposable environments. |
| `namespace.create` | Optional namespace resource with Pod Security Admission labels. |

Default app resource requests are 100m CPU / 256Mi memory and limits are 1 CPU / 768Mi. Production
overlay values raise these defaults. `podSecurity` controls the app Pod UID/GID and read-only
filesystem settings; the defaults use UID/GID 1654 (`app` in the .NET images), a `RuntimeDefault`
seccomp profile, dropped capabilities, and a writable `/tmp` `emptyDir`.

Identity, Flight, Passenger, and Booking each have background processing enabled by default and an
outbox poller that does not use row locking or `FOR UPDATE SKIP LOCKED`; more than one replica can
publish the same rows. The chart rejects replicas or HPA maxReplicas above one for any of those
hosts while background processing is enabled. Booking additionally owns the EventStoreDB `$all`
subscription and must remain single-active. Gateway HPA is enabled by default (one to three
replicas); the production example scales from two to six. Horizontal scaling service workers
requires outbox leasing/leader election or row locking first.

## Secrets contract and rotation

Production should set `existingSecret` and provide the listed key in that Secret. If an
`existingSecret` is empty, the chart creates a development Secret using the configured development
value. Do not use generated defaults in production.

| Values field | Secret key |
| --- | --- |
| `services.<service>.database.existingSecret` | `password` by default; override with `passwordKey`. |
| `rabbitmq.auth.existingSecret` | `username` and `password` by default; keys are configurable. |
| `mongodb.existingSecret` | `connectionString` by default; configurable with `connectionStringKey`. |
| `eventstore.existingSecret` | `connectionString` by default; configurable with `connectionStringKey`. |
| `identity.signingKey.existingSecret` | `tempkey.jwk` by default; override with `identity.signingKey.key`. All Identity replicas must share this same key. |
| `infrastructure.postgres.existingSecret` | `password` by default; used only by the development PostgreSQL StatefulSet. |

PostgreSQL connection strings use Npgsql's single-quoted password form so passwords may contain
semicolons. A password must not contain a single quote: Helm rejects it for chart-managed database
passwords, and externally supplied database passwords must follow the same restriction.

Chart-managed configuration and development Secrets trigger pod checksum rollouts. External
`existingSecret` rotation is not included in the checksum; run
`kubectl rollout restart deployment/<workload> -n booking` after rotation. Identity tokens continue
to validate across a restart only when every Identity replica uses the same persisted signing-key
Secret.

The JWK generator uses OpenSSL and Python 3's standard library. Restrict access to the generated
private key, store it in the approved secret manager, and do not add JWKs or evidence output to Git.

## kind quick start

Install Docker, kind, kubectl, Helm, curl, OpenSSL, and Python 3, then run:

```sh
deployments/k8s/kind/smoke.sh
```

The script creates the `booking` kind cluster if needed, maps host port `${KIND_HTTP_PORT:-8088}` to
cluster port 80 (host port 8080 is intentionally not used), installs a pinned ingress-nginx kind
manifest, a pinned local-path provisioner with a default `standard` StorageClass, and metrics-server,
builds and loads the five app images, creates the local signing-key Secret, and installs the chart
with the kind overlay. The local-path provisioner is for disposable kind storage only.
`SKIP_BUILD=1` skips image builds;
`INSTALL_METRICS_SERVER=0` skips optional local CPU metrics; and `DELETE_CLUSTER=1` removes the
cluster at the end. It leaves the cluster running by default.

## Development infrastructure

The bundled PostgreSQL init script creates roles and databases only when PostgreSQL initializes an
empty data directory. Changing a chart-managed database password on an existing kind release does
not update the stored PostgreSQL role password. Delete the PostgreSQL PVC to reinitialize the
disposable development database (this removes its data), or update the role with `ALTER ROLE` and
coordinate the Secret and service restarts manually.

## Production notes

- Set `infrastructure.enabled=false` and point the chart at managed/external data services.
- The production overlay uses `ghcr.io/cog-gtm/booking-{gateway,identity,flight,passenger,booking}`
  images with `sha-REPLACE_WITH_COMMIT_SHA` placeholders. Set each release image tag with
  `--set services.<service>.image.tag=sha-<release-commit-sha>`.
- Provision the `ghcr-pull` image-pull Secret in the release namespace through approved secret
  management before deploying the private GHCR images.
- TLS terminates at the gateway Ingress; provision its certificate Secret out of band.
- Internal REST and Flight/Passenger gRPC use plaintext HTTP/h2c. NetworkPolicies and mTLS are
  follow-up hardening; do not expose ClusterIP service ports publicly.
- Use external secret management and separate credentials per service. Secrets should be delivered
  to the cluster by the platform's approved secret controller.
- Internal h2c gRPC is intentionally unauthenticated per ADR 0008. Restrict reachability with
  NetworkPolicies until service authentication is implemented.
- `existingSecret` rotation needs an explicit rollout restart, including Identity signing-key
  rotation; signing-key replacement also invalidates tokens signed by the previous key.
- The included databases and broker are disposable development examples, not production defaults.

### Production release checklist

- [ ] Replace all image-tag placeholders with the release commit SHA and provision `ghcr-pull`.
- [ ] Keep the Identity seed account `samh` and password-grant client `client` / `secret` from being
      exposed; the current application seeding must be changed in a follow-up before production
      traffic is enabled.
- [ ] Provision production credentials and signing material through the approved secret manager.

See [ADR 0026](../../docs/adr/0026-kubernetes-helm-deployment.md) for the deployment decision,
security model, NFR targets, and follow-up work.
