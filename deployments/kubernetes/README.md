# Kubernetes / Helm

Helm chart [`deployments/helm/booking`](../helm/booking) deploys the split topology from
[`docker-compose.services.yaml`](../docker-compose/docker-compose.services.yaml): `gateway`, `identity-service`,
`flight-service`, `passenger-service`, `booking-service`. Service names match the compose hostnames, so in-cluster DNS
(`http://flight-service:80`, gRPC `flight-service:81`, `passenger-service:5202`, ...) works unchanged. Only `gateway`
is exposed, through a single Ingress; everything else is `ClusterIP`.

Per service the chart renders a Deployment (startup + liveness probes on `/alive`, readiness on `/ready`), Service,
ConfigMap (non-secret env), Secret (connection strings / RabbitMQ creds, skipped when `existingSecret` is set) and HPA.

| File | Purpose |
| --- | --- |
| `values.yaml` | defaults (dev-only credentials, clearly labelled) |
| `values-kind.yaml` | local kind: `imagePullPolicy: Never`, `infrastructure.enabled: true` (Postgres/Mongo/RabbitMQ/EventStoreDB in-cluster), issuer `http://localhost:8080` |
| `values-production.yaml` | managed infra, `existingSecret` for every service (enforced by `global.requireExistingSecrets`), TLS Ingress |

## Local kind

Requires docker, [kind](https://kind.sigs.k8s.io/), kubectl and helm 3.

```bash
deployments/kubernetes/kind-up.sh      # cluster + build/load 5 images + ingress-nginx + metrics-server + signing key + helm install
deployments/kubernetes/smoke-test.sh   # token -> flight seats -> register user -> passenger created via RabbitMQ
kind delete cluster --name booking
```

`kind-config.yaml` maps host `8080` -> ingress-nginx `80` and sets pod CIDR `10.244.0.0/16` (trusted by the gateway via
`TrustedProxyOptions__KnownNetworks__0`). `SKIP_BUILD=1` reuses existing `booking/*:dev` images.

Manual install (the namespace is created by Helm):

```bash
helm upgrade --install booking deployments/helm/booking \
  --namespace booking --create-namespace \
  -f deployments/helm/booking/values-kind.yaml
```

## Secrets

- **Service secrets**: set `services.<name>.existingSecret` to a Secret holding the same keys as `secretEnv` in
  `values.yaml` (e.g. `PostgresOptions__ConnectionString__Flight`, `RabbitMqOptions__UserName`/`RabbitMqOptions__Password`, `MongoOptions__ConnectionString`).
- **IdentityServer signing key**: identity uses `AddDeveloperSigningCredential()`, which reads/creates `/app/tempkey.jwk`.
  Set `services.identity-service.signingKey.existingSecret` and the key is mounted there (subPath), so every replica signs
  with the same key and restarts don't invalidate tokens. Rendering fails if identity has `replicaCount > 1` or
  autoscaling enabled without it. `kind-up.sh` generates one by briefly running the identity image and `docker cp`-ing
  the file, then `kubectl create secret generic identity-signing-key --from-file=tempkey.jwk=...` — it is never committed.
- **JWT**: `Jwt__Authority` and identity `AuthOptions__IssuerUri` = `global.publicUrl` (the public gateway URL);
  `Jwt__MetadataAddress` = `http://identity-service/.well-known/openid-configuration` (in-cluster discovery).

## Production

```bash
helm upgrade --install booking deployments/helm/booking \
  --namespace booking --create-namespace \
  -f deployments/helm/booking/values-production.yaml --set global.image.tag=<git-sha>
```

Pre-create `gateway-secret`, `identity-service-secret`, `identity-signing-key`, `flight-service-secret`,
`passenger-service-secret`, `booking-service-secret` (names configurable) pointing at managed Postgres/Mongo/RabbitMQ/
EventStoreDB. TLS uses `booking-gateway-tls` (cert-manager annotation included). Set `TrustedProxyOptions__KnownNetworks__0`
to your cluster's pod CIDR and `global.publicUrl` to the public host.
