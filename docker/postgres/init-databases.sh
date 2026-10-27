#!/bin/bash
# Creates one database and one login role per service (database-per-service, no shared access).
# Runs only when the data volume is empty (postgres image behaviour), so restarts never re-run it;
# the IF NOT EXISTS guards make a manual re-run safe too.
set -euo pipefail

create_service_database() {
  local service="$1" password="$2"
  : "${password:?password for ${service} is not set}"
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
    -v role="$service" -v db="${service}_db" -v pw="$password" <<'SQL'
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'role', :'pw')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'role') \gexec
SELECT format('CREATE DATABASE %I OWNER %I', :'db', :'role')
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = :'db') \gexec
SELECT format('REVOKE ALL ON DATABASE %I FROM PUBLIC', :'db') \gexec
SQL
}

create_service_database identity     "${IDENTITY_DB_PASSWORD:-}"
create_service_database claims       "${CLAIMS_DB_PASSWORD:-}"
create_service_database reporting    "${REPORTING_DB_PASSWORD:-}"
create_service_database notification "${NOTIFICATION_DB_PASSWORD:-}"
