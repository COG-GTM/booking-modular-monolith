#!/usr/bin/env bash
set -euo pipefail
umask 077

output="${1:-tempkey.jwk}"
temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT

openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$temporary_directory/private.pem"
openssl rsa -in "$temporary_directory/private.pem" -traditional -outform DER -out "$temporary_directory/private.der" 2>/dev/null

python3 - "$temporary_directory/private.der" "$output" <<'PY'
import base64
import hashlib
import json
import pathlib
import sys


def read_length(data, offset):
    length = data[offset]
    offset += 1
    if length & 0x80:
        count = length & 0x7F
        length = int.from_bytes(data[offset : offset + count], "big")
        offset += count
    return length, offset


def read_integer(data, offset):
    if data[offset] != 0x02:
        raise ValueError("expected a DER INTEGER")
    length, offset = read_length(data, offset + 1)
    value = data[offset : offset + length]
    return int.from_bytes(value, "big"), offset + length


def base64url(number):
    encoded = base64.urlsafe_b64encode(number.to_bytes((number.bit_length() + 7) // 8, "big"))
    return encoded.rstrip(b"=").decode("ascii")


der = pathlib.Path(sys.argv[1]).read_bytes()
if der[0] != 0x30:
    raise ValueError("expected a DER SEQUENCE")
sequence_length, offset = read_length(der, 1)
end = offset + sequence_length
integers = []
while offset < end:
    value, offset = read_integer(der, offset)
    integers.append(value)
if len(integers) != 9:
    raise ValueError("expected a PKCS#1 RSA private key with nine INTEGER fields")

_, modulus, public_exponent, private_exponent, prime1, prime2, exponent1, exponent2, coefficient = integers
key = {
    "kty": "RSA",
    "n": base64url(modulus),
    "e": base64url(public_exponent),
    "d": base64url(private_exponent),
    "p": base64url(prime1),
    "q": base64url(prime2),
    "dp": base64url(exponent1),
    "dq": base64url(exponent2),
    "qi": base64url(coefficient),
}
thumbprint = json.dumps(
    {"e": key["e"], "kty": key["kty"], "n": key["n"]},
    separators=(",", ":"),
    sort_keys=True,
).encode("utf-8")
key["kid"] = base64.urlsafe_b64encode(hashlib.sha256(thumbprint).digest()).rstrip(b"=").decode("ascii")
key["use"] = "sig"
key["alg"] = "RS256"

path = pathlib.Path(sys.argv[2])
path.write_text(json.dumps(key, separators=(",", ":")) + "\n", encoding="utf-8")
path.chmod(0o600)
PY

printf 'Generated Duende-compatible RSA JWK at %s\n' "$output"
