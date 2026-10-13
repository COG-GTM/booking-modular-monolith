# Kubernetes / Helm deployment

Helm chart for the microservices topology (AB-491): the YARP **gateway** plus the **identity**,
**flight**, **passenger** and **booking** service hosts, with optional in-chart
Postgres / MongoDB / RabbitMQ / EventStoreDB for local clusters.

```
deployments/k8s/
├── helm/booking/              # the chart (values.yaml documents every knob)
├── environments/kind.yaml     # local kind: locally built images, in-chart infra, localhost:8080
├── environments/production.yaml  # example: published images, managed infra, existing Secrets, TLS
├── kind/cluster.yaml          # kind cluster with ingress-nginx's port published on the host
└── kind-smoke.sh              # create cluster -> build/load images -> install -> smoke test
```

## Topology

| Object | gateway | identity | flight | passenger | booking |
|---|---|---|---|---|---|
| Service (ClusterIP) | `gateway:80` | `identity:80` | `flight:80`, `flight:81` (`grpc`, h2c) | `passenger:80`, `passenger:81` (`grpc`, h2c) | `booking:80` |
| HPA (CPU) | 1–3 | off (see signing key) | 1–3 | 1–3 | **never** — pinned to 1 replica, `Recreate` |
| Probes | startup + liveness `/health/live`, readiness `/health/ready` (AB-495) on every service |

- The **Ingress** (class `nginx`) → `gateway` Service is the only external entry. The gateway's YARP
  clusters are repointed with `ReverseProxy__Clusters__<module>__Destinations__monolith__Address=http://<service>:80`.
- Booking calls Flight/Passenger over gRPC at `Grpc__FlightAddress=http://flight:81` /
  `Grpc__PassengerAddress=http://passenger:81` (named port `grpc`, `appProtocol: kubernetes.io/h2c`).
- Tokens are issued with `global.publicUrl` (the URL clients use to reach the ingress) as issuer;
  every service validates `iss` against it and fetches discovery/JWKS from `http://identity`.
- Booking's EventStoreDB subscription is single-consumer (ADR 0496): the chart **fails to render**
  if `services.booking.replicas > 1` or its HPA is enabled while `backgroundProcessingEnabled=true`.
- An init container waits for each service's TCP dependencies; `infra.enabled=true` adds
  single-replica StatefulSets (PVC-backed) for Postgres 16 (`max_prepared_transactions=100`, one role
  + databases per service), MongoDB 7, RabbitMQ 3 and EventStoreDB 24.10. Dev only — no HA/backups.

## Configuration and secrets

Each service gets a `<service>-config` ConfigMap (non-secret env) and a `<service>-secrets` Secret
(connection strings and credentials) consumed with `envFrom`. The chart's credentials are
**local-dev placeholders**; real environments must supply their own:

| What | How |
|---|---|
| Postgres write/outbox DB credentials | `services.<svc>.database.{user,password}`, or `services.<svc>.existingSecret` with keys `PostgresOptions__ConnectionString__<Module>`, `PersistMessageOptions__ConnectionString__<Module>` (+ `MongoOptions__ConnectionString`, Booking: `EventStoreOptions__ConnectionString`) |
| RabbitMQ | `rabbitmq.{username,password}` or `rabbitmq.existingSecret` (keys `username`, `password`) |
| IdentityServer signing key | `identity.signingKey.existingSecret` (key `tempkey.jwk`, an RSA private JWK) mounted at `/app/tempkey.jwk` |
| Extra env | `services.<svc>.env` (ConfigMap) / `services.<svc>.secretEnv` (Secret) |

Identity signs tokens with Duende's `AddDeveloperSigningCredential`, which loads `tempkey.jwk` if it
exists and otherwise generates a key per pod. Without a shared key identity must stay at one
replica (the chart enforces it); with `identity.signingKey.existingSecret` you may scale it.
Generate the key once, outside the repo, and never commit it:

```bash
python3 - <<'PY' > tempkey.jwk   # pip install jwcrypto; standard RSA JWK with private parameters
from jwcrypto import jwk; import uuid
print(jwk.JWK.generate(kty="RSA", size=2048, kid=uuid.uuid4().hex, alg="RS256", use="sig").export(private_key=True))
PY
kubectl -n booking create secret generic booking-identity-signing-key --from-file=tempkey.jwk && rm tempkey.jwk
```

## Install

```bash
# Local kind cluster, everything scripted (images built from the Dockerfiles, then the smoke flow):
deployments/k8s/kind-smoke.sh                       # KIND_HTTP_PORT=8088 if 8080 is taken

# Any cluster with ingress-nginx, published images:
helm upgrade --install booking deployments/k8s/helm/booking -n booking --create-namespace \
  -f deployments/k8s/environments/production.yaml --set global.imageTag=<tag>

# Plain manifests (e.g. for kubectl/kustomize/GitOps), including the Namespace object:
helm template booking deployments/k8s/helm/booking -n booking --set namespace.create=true > booking.yaml
```

`kind-smoke.sh` waits for every pod to be Ready, resolves every Service by DNS from a test pod
(and probes `/health/ready` on :80 and the gRPC port :81), then drives the booking flow through
the ingress: token → register user → complete passenger registration (RabbitMQ event) → create
flight + seat → create booking (Booking → Flight/Passenger gRPC) → seat reserved.

CI (`.github/workflows/k8s-manifests.yml`) runs `helm lint`, renders the chart with every overlay
through `kubeconform`, checks the replica guards and shellchecks the smoke script.
