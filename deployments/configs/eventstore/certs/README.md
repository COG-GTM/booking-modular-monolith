# EventStoreDB certificates (publish mode only)

When the Aspire AppHost is published (`aspire publish`), EventStoreDB runs **secured**
(`EVENTSTORE_INSECURE=False`, AtomPub disabled, admin password taken from the
`eventstore-admin-password` parameter) and expects TLS certificates mounted from this directory:

```
certs/
  ca/ca.crt
  node/node.crt
  node/node.key
```

Generate a development/staging set with the official tool (never commit the output):

```bash
docker run --rm -v "$PWD:/tmp" eventstore/es-gencert-cli create-ca -out /tmp/ca
docker run --rm -v "$PWD:/tmp" eventstore/es-gencert-cli create-node \
  -ca-certificate /tmp/ca/ca.crt -ca-key /tmp/ca/ca.key \
  -out /tmp/node -ip-addresses 127.0.0.1 -dns-names localhost,eventstore
chmod -R 644 node ca
```

Local `aspire run` keeps the insecure, certificate-less configuration and does not use this directory.
