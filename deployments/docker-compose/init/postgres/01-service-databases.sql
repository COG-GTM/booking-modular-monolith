-- Per-service databases for the standalone service hosts (AB-488). Runs once, on first start of an
-- empty postgres data volume. Each service gets its own role and owns only its own databases
-- (write DB + outbox/inbox DB); CONNECT is revoked from PUBLIC so a service role cannot open another
-- service's databases. Dev-only credentials; real environments inject them from a secret store.
-- The monolith keeps using its existing *_modular_monolith databases until cut-over.

CREATE ROLE identity_service LOGIN PASSWORD 'identity_service';
CREATE ROLE flight_service LOGIN PASSWORD 'flight_service';
CREATE ROLE passenger_service LOGIN PASSWORD 'passenger_service';
CREATE ROLE booking_service LOGIN PASSWORD 'booking_service';

CREATE DATABASE identity_service OWNER identity_service;
CREATE DATABASE identity_service_persist_message OWNER identity_service;
CREATE DATABASE flight_service OWNER flight_service;
CREATE DATABASE flight_service_persist_message OWNER flight_service;
CREATE DATABASE passenger_service OWNER passenger_service;
CREATE DATABASE passenger_service_persist_message OWNER passenger_service;
-- Booking's write model lives in EventStoreDB; it only needs an outbox/inbox database.
CREATE DATABASE booking_service_persist_message OWNER booking_service;

REVOKE CONNECT ON DATABASE identity_service FROM PUBLIC;
REVOKE CONNECT ON DATABASE identity_service_persist_message FROM PUBLIC;
REVOKE CONNECT ON DATABASE flight_service FROM PUBLIC;
REVOKE CONNECT ON DATABASE flight_service_persist_message FROM PUBLIC;
REVOKE CONNECT ON DATABASE passenger_service FROM PUBLIC;
REVOKE CONNECT ON DATABASE passenger_service_persist_message FROM PUBLIC;
REVOKE CONNECT ON DATABASE booking_service_persist_message FROM PUBLIC;
