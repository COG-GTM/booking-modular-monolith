#!/usr/bin/env bash

set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/cutover/lib.sh
source "$SCRIPT_DIR/lib.sh"

context="${1:-}"
direction="${2:-}"
case "$context:$direction" in
  identity:to-service|identity:to-monolith|flight:to-service|flight:to-monolith|passenger:to-service|passenger:to-monolith|booking:to-service|booking:to-monolith) ;;
  *) printf 'Usage: %s <identity|flight|passenger|booking> <to-service|to-monolith>\n' "$0" >&2; exit 2 ;;
esac

database_context="$context"
source_suffix=modular_monolith
destination_suffix=service
if [[ "$direction" == to-monolith ]]; then
  source_suffix=service
  destination_suffix=modular_monolith
fi
source_db="${database_context}_${source_suffix}"
destination_db="${database_context}_${destination_suffix}"
role="${context}_service"
password_variable="${context^^}_DB_PASSWORD"
password="${!password_variable:-$role}"

docker exec -e "PGPASSWORD=$password" postgres pg_dump -Fc -h 127.0.0.1 -U "$role" "$source_db" |
  docker exec -i -e "PGPASSWORD=$password" postgres pg_restore \
    --clean --if-exists --no-owner --single-transaction \
    -h 127.0.0.1 -U "$role" -d "$destination_db"

postgres_counts() {
  local database="$1" table
  while IFS= read -r table; do
    [[ -n "$table" ]] || continue
    printf '%s:%s\n' "$table" \
      "$(docker exec -e "PGPASSWORD=$password" postgres psql -h 127.0.0.1 -U "$role" -d "$database" -Atc "SELECT count(*) FROM $table")"
  done < <(
    docker exec -e "PGPASSWORD=$password" postgres psql -h 127.0.0.1 -U "$role" -d "$database" -Atc \
      "SELECT format('%I.%I', schemaname, tablename) FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename"
  )
}

if ! diff -u <(postgres_counts "$source_db") <(postgres_counts "$destination_db"); then
  printf 'PostgreSQL row-count verification failed for %s.\n' "$context" >&2
  exit 1
fi

if [[ "$context" == flight || "$context" == passenger ]]; then
  source_mongo="${context}_${source_suffix}_read"
  destination_mongo="${context}_${destination_suffix}_read"
  if ! docker exec mongo sh -lc 'command -v mongodump >/dev/null && command -v mongorestore >/dev/null && command -v mongosh >/dev/null'; then
    printf 'Mongo image does not include mongodump, mongorestore, and mongosh.\n' >&2
    exit 1
  fi
  docker exec mongo mongodump --uri "mongodb://127.0.0.1:27017/$source_mongo" --archive |
    docker exec -i mongo mongorestore --uri "mongodb://127.0.0.1:27017/$destination_mongo" \
      --archive --drop --nsInclude="$source_mongo.*" \
      --nsFrom="$source_mongo.*" --nsTo="$destination_mongo.*"
  if [[ "$context" == flight ]]; then
    while IFS=$'\t' read -r seat_id flight_id seat_number; do
      [[ -n "$seat_id" ]] || continue
      seat_json="$(jq -cn \
        --arg seatId "$seat_id" \
        --arg flightId "$flight_id" \
        --arg seatNumber "$seat_number" \
        '{seatId: $seatId, flightId: $flightId, seatNumber: $seatNumber}')"
      docker exec mongo mongosh --quiet --eval "
        const seat = $seat_json;
        const result = db.getSiblingDB('$destination_mongo').seat.updateOne(
          {flightId: seat.flightId, seatNumber: seat.seatNumber},
          {\$set: {seatId: seat.seatId}}
        );
        if (result.matchedCount !== 1) quit(1);
      "
    done < <(
      docker exec -e "PGPASSWORD=$password" postgres psql -h 127.0.0.1 -U "$role" \
        -d "$destination_db" -AtF $'\t' -c \
        "SELECT id, flight_id, seat_number FROM seat ORDER BY flight_id, seat_number"
    )
  fi
  mongo_counts() {
    docker exec mongo mongosh --quiet --eval \
      "db.getSiblingDB('$1').getCollectionNames().sort().forEach(name => print(name + ':' + db.getSiblingDB('$1').getCollection(name).countDocuments()))"
  }
  if ! diff -u <(mongo_counts "$source_mongo") <(mongo_counts "$destination_mongo"); then
    printf 'Mongo collection-count verification failed for %s.\n' "$context" >&2
    exit 1
  fi
fi

printf 'Verified %s data copy %s.\n' "$context" "$direction"
