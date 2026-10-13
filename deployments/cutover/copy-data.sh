#!/usr/bin/env bash
# Copy one bounded context's data between the monolith stores and the service-owned stores.
#
#   copy-data.sh <identity|flight|passenger|booking> to-service|to-monolith [--verify-only]
#
# Preconditions (enforced by flip-route.sh, assumed here): the context's gateway route is frozen,
# neither side runs background processing for the context, and the source outbox is drained.
#
# What is copied:
#   Postgres write DB   <m>_modular_monolith  <->  <m>_service        (identity, flight, passenger)
#   Mongo read DB       <m>_modular_monolith_read <-> <m>_service_read (flight, passenger, booking)
#   Outbox/inbox DB     target <m>_*_persist_message is truncated (anything in it was produced
#                       against data that is about to be replaced); the drained source is not copied.
#   EventStoreDB        nothing: Booking's event store is shared by both hosts, including the
#                       subscription checkpoint stream, so the copied read model resumes in place.
#
# Schema is owned by each host's EF migrations; the copy is data-only and refuses to run if the two
# databases are on different migrations.
set -euo pipefail
# shellcheck source=deployments/cutover/lib.sh
source "$(dirname "$0")/lib.sh"

module="${1:-}"
direction="${2:-}"
verify_only="${3:-}"
require_module "${module}"

case "${direction}" in
  to-service)
    src_db="$(monolith_db "${module}")"
    dst_db="$(service_db "${module}")"
    src_read="$(monolith_read_db "${module}")"
    dst_read="$(service_read_db "${module}")"
    dst_persist="$(service_persist_db "${module}")"
    ;;
  to-monolith)
    src_db="$(service_db "${module}")"
    dst_db="$(monolith_db "${module}")"
    src_read="$(service_read_db "${module}")"
    dst_read="$(monolith_read_db "${module}")"
    dst_persist="$(monolith_persist_db "${module}")"
    ;;
  *) die "usage: $0 <module> to-service|to-monolith [--verify-only]" ;;
esac

user_tables() {
  psql_admin "$1" -Atc "select format('%I.%I', schemaname, tablename) from pg_tables
    where schemaname not in ('pg_catalog','information_schema') and tablename <> '__EFMigrationsHistory'
    order by 1"
}

row_counts() {
  local db="$1" t sql=""
  for t in $(user_tables "${db}"); do
    sql+="select '${t}' as t, count(*) as n from ${t} union all "
  done
  [[ -n "${sql}" ]] || return 0
  psql_admin "${db}" -AtF ' ' -c "${sql% union all } order by 1"
}

# EF history column is "MigrationId", or migration_id under the snake-case naming convention.
last_migration() {
  local column
  column="$(psql_admin "$1" -Atc "select column_name from information_schema.columns
    where table_name = '__EFMigrationsHistory' and column_name in ('MigrationId', 'migration_id')")"
  [[ -n "${column}" ]] || die "$1 has no __EFMigrationsHistory table"
  psql_admin "$1" -Atc "select coalesce(max(\"${column}\"), '<none>') from \"__EFMigrationsHistory\""
}

copy_postgres() {
  pg_db_exists "${src_db}" || die "source database ${src_db} does not exist"
  pg_db_exists "${dst_db}" || die "target database ${dst_db} does not exist (start its host once so EF migrates it)"

  local src_mig dst_mig
  src_mig="$(last_migration "${src_db}")"
  dst_mig="$(last_migration "${dst_db}")"
  [[ "${src_mig}" == "${dst_mig}" ]] ||
    die "schema mismatch: ${src_db} is on ${src_mig}, ${dst_db} is on ${dst_mig}"

  if [[ "${verify_only}" != --verify-only ]]; then
    local tables
    tables="$(user_tables "${dst_db}" | paste -sd, -)"
    log "postgres: truncating ${dst_db} (${tables:-no tables})"
    [[ -z "${tables}" ]] || psql_admin "${dst_db}" -qc "TRUNCATE ${tables} RESTART IDENTITY CASCADE"
    log "postgres: copying ${src_db} -> ${dst_db} (data only, migration ${src_mig})"
    docker exec "${POSTGRES_CONTAINER}" pg_dump -U "${PGUSER_ADMIN}" -d "${src_db}" --data-only \
      --disable-triggers --no-owner --no-privileges --exclude-table='"__EFMigrationsHistory"' |
      psql_admin "${dst_db}" -q >/dev/null
  fi

  local src_counts dst_counts
  src_counts="$(row_counts "${src_db}")"
  dst_counts="$(row_counts "${dst_db}")"
  printf '%s\n' "postgres row counts (${src_db} | ${dst_db}):" >&2
  diff --side-by-side --width=110 <(printf '%s\n' "${src_counts}") <(printf '%s\n' "${dst_counts}") >&2 ||
    die "row counts differ between ${src_db} and ${dst_db}"
}

reset_persist() {
  pg_db_exists "${dst_persist}" || return 0
  local tables
  tables="$(user_tables "${dst_persist}" | paste -sd, -)"
  [[ -n "${tables}" ]] || return 0
  log "postgres: truncating stale outbox/inbox rows in ${dst_persist}"
  psql_admin "${dst_persist}" -qc "TRUNCATE ${tables}"
}

mongo_counts() {
  mongo_eval "mongodb://localhost:27017/$1" --eval \
    'db.getCollectionNames().sort().forEach(c => print(c + " " + db.getCollection(c).countDocuments()))'
}

copy_mongo() {
  if [[ "${verify_only}" != --verify-only ]]; then
    log "mongo: replacing ${dst_read} with ${src_read}"
    mongo_eval "mongodb://localhost:27017/${dst_read}" --eval 'db.dropDatabase()' >/dev/null
    docker exec "${MONGO_CONTAINER}" sh -c \
      "mongodump --quiet --archive --db='${src_read}' | mongorestore --quiet --archive --drop \
       --nsFrom='${src_read}.*' --nsTo='${dst_read}.*'"
  fi
  local src_counts dst_counts
  src_counts="$(mongo_counts "${src_read}")"
  dst_counts="$(mongo_counts "${dst_read}")"
  printf '%s\n' "mongo document counts (${src_read} | ${dst_read}):" >&2
  diff --side-by-side --width=110 <(printf '%s\n' "${src_counts}") <(printf '%s\n' "${dst_counts}") >&2 ||
    die "document counts differ between ${src_read} and ${dst_read}"
}

if [[ "${DRY_RUN}" == 1 ]]; then
  log "dry-run: would copy ${module} ${direction}: pg ${src_db:-<none>}->${dst_db:-<none>}," \
    "mongo ${src_read:-<none>}->${dst_read:-<none>}, reset ${dst_persist}"
  exit 0
fi

[[ -z "${dst_db}" ]] || copy_postgres
[[ -z "${dst_read}" ]] || copy_mongo
[[ "${verify_only}" == --verify-only ]] || reset_persist
log "copy ${module} ${direction}: done"
