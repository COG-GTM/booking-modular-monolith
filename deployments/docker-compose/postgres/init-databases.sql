-- Runs once on a fresh postgres-data volume (docker-entrypoint-initdb.d).
-- One write database per owning service plus one outbox/inbox database per service host.
CREATE DATABASE identity_service;
CREATE DATABASE flight_service;
CREATE DATABASE passenger_service;
CREATE DATABASE identity_persist_message;
CREATE DATABASE flight_persist_message;
CREATE DATABASE passenger_persist_message;
CREATE DATABASE booking_persist_message;
