-- One database and one role per service. A service only ever connects with its own role, so it
-- cannot read or write another service's tables (including its outbox/inbox persist_message table).
-- Runs once, on first start of an empty postgres-data volume.

CREATE ROLE flight LOGIN PASSWORD 'flight';
CREATE ROLE identity LOGIN PASSWORD 'identity';
CREATE ROLE passenger LOGIN PASSWORD 'passenger';
CREATE ROLE booking LOGIN PASSWORD 'booking';

CREATE DATABASE flight_modular_monolith OWNER flight;
CREATE DATABASE flight_service OWNER flight;
CREATE DATABASE identity_modular_monolith OWNER identity;
CREATE DATABASE passenger_modular_monolith OWNER passenger;
CREATE DATABASE booking_modular_monolith OWNER booking;

REVOKE CONNECT ON DATABASE flight_modular_monolith FROM PUBLIC;
REVOKE CONNECT ON DATABASE flight_service FROM PUBLIC;
REVOKE CONNECT ON DATABASE identity_modular_monolith FROM PUBLIC;
REVOKE CONNECT ON DATABASE passenger_modular_monolith FROM PUBLIC;
REVOKE CONNECT ON DATABASE booking_modular_monolith FROM PUBLIC;
