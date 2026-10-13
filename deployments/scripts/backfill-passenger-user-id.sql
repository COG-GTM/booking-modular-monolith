-- One-off backfill for passengers created before passenger.user_id existed
-- (migration AddPassengerUserId). Binds each legacy passenger to the Identity
-- user that registered with the same passport number. Until this runs, those
-- passengers can only be completed by an admin (the handler fails closed).
--
-- Identity and Passenger live in separate databases on the same server, so the
-- lookup goes through dblink. Run against passenger_modular_monolith, e.g.:
--   psql "Server=localhost;Port=5432;Database=passenger_modular_monolith;..." \
--        -v identity_conninfo='dbname=identity_modular_monolith user=postgres password=...' \
--        -f deployments/scripts/backfill-passenger-user-id.sql
--
-- Only passports that map to exactly one Identity user are backfilled; ambiguous
-- passports are left NULL and listed at the end for manual review.

CREATE EXTENSION IF NOT EXISTS dblink;

WITH identity_users AS (
    SELECT id, passport_number
    FROM dblink(:'identity_conninfo',
                'SELECT id, pass_port_number FROM asp_net_users WHERE pass_port_number IS NOT NULL')
         AS u(id uuid, passport_number text)
),
unique_owners AS (
    SELECT passport_number, min(id) AS user_id
    FROM identity_users
    GROUP BY passport_number
    HAVING count(*) = 1
)
UPDATE passenger p
SET user_id = o.user_id
FROM unique_owners o
WHERE p.user_id IS NULL
  AND p.passport_number = o.passport_number;

SELECT p.id, p.passport_number
FROM passenger p
WHERE p.user_id IS NULL
ORDER BY p.passport_number;
