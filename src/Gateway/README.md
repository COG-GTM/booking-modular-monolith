# Gateway

YARP is the public ingress on HTTP port 5000 (container port 8080).
Compose exposes only the gateway, not the monolith. Use TLS termination in
front of the gateway in production.

| Path | Cluster | Gateway authorization |
| --- | --- | --- |
| /api/v{version}/identity/** | identity | authenticated |
| /connect/**, /.well-known/** | identity | anonymous |
| /api/v{version}/flight/** | flight | authenticated |
| /api/v{version}/passenger/** | passenger | authenticated |
| /api/v{version}/booking/** | booking | authenticated |
| Everything else | monolith | anonymous (backend retains authorization) |

All clusters initially target the monolith at http://localhost:3001/.
The stable destination name is **primary**. Repoint one module without code changes:

```sh
export ReverseProxy__Clusters__flight__Destinations__primary__Address=http://flight-service/
dotnet run --project src/Gateway
```

Routes/clusters are loaded from the ReverseProxy configuration section.
JWT validation reuses BuildingBlocks and requires audience booking-modular-monolith.
Set Jwt__Authority to the token issuer and optionally Jwt__MetadataAddress to an
internally reachable discovery URL. The Authorization header remains intact for
backend authorization. Set the monolith's AuthOptions__IssuerUri to the same
issuer as Jwt__Authority (Aspire and Compose set both to https://localhost:3000).
Discovery also advertises a signing-key URL, which must
be reachable by the gateway.

Cors__AllowedOrigins__0 (and subsequent indices) allow explicit browser origins.
RateLimiting__PermitLimit (100) and RateLimiting__WindowSeconds (60) configure a
fixed window per authenticated subject, otherwise per direct client IP. Untrusted
incoming forwarded headers are not used for rate-limit identity. YARP adds
X-Forwarded headers and propagates tracing and X-Correlation-ID; missing correlation
IDs are generated. Request logs never include tokens or request bodies.
ObservabilityOptions sets the OpenTelemetry service name to gateway.

/health/live and /health/ready are anonymous self checks, not dependency checks.
Run gateway tests with dotnet test src/Gateway/tests/Unit.Test.
