CREATE ROLE flight_service LOGIN PASSWORD 'flight_service';
CREATE DATABASE flight_service OWNER flight_service;
REVOKE CONNECT ON DATABASE flight_service FROM PUBLIC;
GRANT CONNECT ON DATABASE flight_service TO flight_service;
CREATE DATABASE flight_service_persist_message OWNER flight_service;
REVOKE CONNECT ON DATABASE flight_service_persist_message FROM PUBLIC;
GRANT CONNECT ON DATABASE flight_service_persist_message TO flight_service;

CREATE ROLE identity_service LOGIN PASSWORD 'identity_service';
CREATE DATABASE identity_service OWNER identity_service;
REVOKE CONNECT ON DATABASE identity_service FROM PUBLIC;
GRANT CONNECT ON DATABASE identity_service TO identity_service;
CREATE DATABASE identity_service_persist_message OWNER identity_service;
REVOKE CONNECT ON DATABASE identity_service_persist_message FROM PUBLIC;
GRANT CONNECT ON DATABASE identity_service_persist_message TO identity_service;

CREATE ROLE passenger_service LOGIN PASSWORD 'passenger_service';
CREATE DATABASE passenger_service OWNER passenger_service;
REVOKE CONNECT ON DATABASE passenger_service FROM PUBLIC;
GRANT CONNECT ON DATABASE passenger_service TO passenger_service;
CREATE DATABASE passenger_service_persist_message OWNER passenger_service;
REVOKE CONNECT ON DATABASE passenger_service_persist_message FROM PUBLIC;
GRANT CONNECT ON DATABASE passenger_service_persist_message TO passenger_service;

CREATE ROLE booking_service LOGIN PASSWORD 'booking_service';
CREATE DATABASE booking_service_persist_message OWNER booking_service;
REVOKE CONNECT ON DATABASE booking_service_persist_message FROM PUBLIC;
GRANT CONNECT ON DATABASE booking_service_persist_message TO booking_service;
