#!/bin/sh
# Sets the built-in `kibana_system` user's password so Kibana can connect to a secured Elasticsearch.
set -eu

: "${ELASTIC_PASSWORD:?ELASTIC_PASSWORD is required}"
: "${KIBANA_SYSTEM_PASSWORD:?KIBANA_SYSTEM_PASSWORD is required}"
ELASTICSEARCH_URL="${ELASTICSEARCH_URL:-http://elasticsearch:9200}"

until curl -fsS -o /dev/null -u "elastic:${ELASTIC_PASSWORD}" \
    -H 'Content-Type: application/json' \
    -X POST "${ELASTICSEARCH_URL}/_security/user/kibana_system/_password" \
    -d "{\"password\":\"${KIBANA_SYSTEM_PASSWORD}\"}"; do
    echo "Waiting for Elasticsearch to accept the kibana_system password..."
    sleep 5
done

echo "kibana_system password set."
