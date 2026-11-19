-- Local development only: passwords equal role names. Never use these credentials in production.
\set ON_ERROR_STOP on

CREATE ROLE identity_service LOGIN PASSWORD 'identity_service' NOSUPERUSER NOCREATEDB NOCREATEROLE;
CREATE DATABASE identity_modular_monolith OWNER identity_service;
REVOKE CONNECT ON DATABASE identity_modular_monolith FROM PUBLIC;
GRANT CONNECT ON DATABASE identity_modular_monolith TO identity_service;
CREATE DATABASE identity_service OWNER identity_service;
REVOKE CONNECT ON DATABASE identity_service FROM PUBLIC;
GRANT CONNECT ON DATABASE identity_service TO identity_service;

CREATE ROLE flight_service LOGIN PASSWORD 'flight_service' NOSUPERUSER NOCREATEDB NOCREATEROLE;
CREATE DATABASE flight_modular_monolith OWNER flight_service;
REVOKE CONNECT ON DATABASE flight_modular_monolith FROM PUBLIC;
GRANT CONNECT ON DATABASE flight_modular_monolith TO flight_service;
CREATE DATABASE flight_service OWNER flight_service;
REVOKE CONNECT ON DATABASE flight_service FROM PUBLIC;
GRANT CONNECT ON DATABASE flight_service TO flight_service;

CREATE ROLE passenger_service LOGIN PASSWORD 'passenger_service' NOSUPERUSER NOCREATEDB NOCREATEROLE;
CREATE DATABASE passenger_modular_monolith OWNER passenger_service;
REVOKE CONNECT ON DATABASE passenger_modular_monolith FROM PUBLIC;
GRANT CONNECT ON DATABASE passenger_modular_monolith TO passenger_service;
CREATE DATABASE passenger_service OWNER passenger_service;
REVOKE CONNECT ON DATABASE passenger_service FROM PUBLIC;
GRANT CONNECT ON DATABASE passenger_service TO passenger_service;

CREATE ROLE booking_service LOGIN PASSWORD 'booking_service' NOSUPERUSER NOCREATEDB NOCREATEROLE;
CREATE DATABASE booking_modular_monolith OWNER booking_service;
REVOKE CONNECT ON DATABASE booking_modular_monolith FROM PUBLIC;
GRANT CONNECT ON DATABASE booking_modular_monolith TO booking_service;
CREATE DATABASE booking_service OWNER booking_service;
REVOKE CONNECT ON DATABASE booking_service FROM PUBLIC;
GRANT CONNECT ON DATABASE booking_service TO booking_service;
