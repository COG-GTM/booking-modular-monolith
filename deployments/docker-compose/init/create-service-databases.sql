-- Executed once by the postgres image on a fresh data volume (/docker-entrypoint-initdb.d).
-- Each standalone service owns its own write database and its own outbox/inbox (persist_message) database.
-- The hosts also create missing databases on start-up (EF Core Migrate / EnsureCreated), so this is a convenience for tooling.

CREATE DATABASE identity_service;
CREATE DATABASE identity_service_persist_message;

CREATE DATABASE flight_service;
CREATE DATABASE flight_service_persist_message;

CREATE DATABASE passenger_service;
CREATE DATABASE passenger_service_persist_message;

CREATE DATABASE booking_service_persist_message;
