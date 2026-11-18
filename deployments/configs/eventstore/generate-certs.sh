#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cert_dir="$script_dir/certs"
ca_dir="$cert_dir/ca"
node_dir="$cert_dir/node"
ca_key="$ca_dir/ca.key"
ca_cert="$ca_dir/ca.crt"
node_key="$node_dir/node.key"
node_cert="$node_dir/node.crt"
force=false

if [[ "${1:-}" == "--force" ]]; then
    force=true
elif [[ "$#" -ne 0 ]]; then
    printf 'Usage: %s [--force]\n' "$0" >&2
    exit 2
fi

if [[ "$force" == false && -f "$ca_key" && -f "$ca_cert" && -f "$node_key" && -f "$node_cert" ]]; then
    printf 'EventStoreDB certificates already exist; use --force to regenerate.\n'
    exit 0
fi

mkdir -p "$ca_dir" "$node_dir"

openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:4096 -out "$ca_key"
openssl req -x509 -new -sha256 -key "$ca_key" -days 365 \
    -subj "/CN=EventStoreDB Local Development CA" \
    -addext "basicConstraints=critical,CA:TRUE" \
    -addext "keyUsage=critical,keyCertSign,cRLSign" \
    -out "$ca_cert"
chmod 0600 "$ca_key"

openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$node_key"
openssl req -new -sha256 -key "$node_key" \
    -subj "/CN=eventstore" \
    -out "$node_dir/node.csr"
openssl x509 -req -sha256 -in "$node_dir/node.csr" \
    -CA "$ca_cert" -CAkey "$ca_key" -CAcreateserial -days 365 \
    -extfile <(printf '%s\n' \
        "subjectAltName=DNS:localhost,DNS:eventstore,IP:127.0.0.1" \
        "extendedKeyUsage=serverAuth,clientAuth" \
        "keyUsage=digitalSignature,keyEncipherment") \
    -out "$node_cert"
rm "$node_dir/node.csr"
# These are local development certificates; the container's non-root user needs to read the key.
chmod 0644 "$node_key"

printf 'Generated EventStoreDB local development certificates in %s\n' "$cert_dir"
