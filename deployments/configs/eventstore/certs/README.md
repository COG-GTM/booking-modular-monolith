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
# Keep private keys owner-only; the EventStoreDB container runs as uid 1000 and must own node.key.
chmod 644 ca/ca.crt node/node.crt
chmod 600 node/node.key
sudo chown 1000:1000 node/node.key
# The CA key is only needed to sign node certificates: move it out of this (mounted) directory.
mv ca/ca.key "$HOME/.eventstore-ca.key" && chmod 600 "$HOME/.eventstore-ca.key"
```

The published API container gets only `ca/ca.crt` mounted (read-only) and connects with
`tls=true&tlsCaFile=/etc/eventstore/certs/ca/ca.crt`, so certificate validation stays enabled.

Local `aspire run` keeps the insecure, certificate-less configuration and does not use this directory.
