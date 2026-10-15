#!/bin/sh
# Corre UNA sola vez, cuando Postgres arranca con el volumen vacío (docker-entrypoint-initdb.d).
# Crea el rol con el que entra la app (hermes_app): SIN privilegios de dueño ni de superusuario, así la Row Level Security
# (segunda barrera multi-tienda) se le aplica. Las tablas las crean las migraciones con el dueño (POSTGRES_USER), y los
# permisos por defecto hacen que hermes_app pueda leer y escribir cada tabla nueva (pero no cambiar el esquema).
# APP_DB_PASSWORD: solo letras y números (openssl rand -hex 32).
set -eu

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<EOSQL
CREATE ROLE hermes_app LOGIN PASSWORD '${APP_DB_PASSWORD}';
CREATE SCHEMA IF NOT EXISTS hermes AUTHORIZATION ${POSTGRES_USER};
GRANT CONNECT ON DATABASE ${POSTGRES_DB} TO hermes_app;
GRANT USAGE ON SCHEMA hermes TO hermes_app;
ALTER DEFAULT PRIVILEGES FOR ROLE ${POSTGRES_USER} IN SCHEMA hermes GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO hermes_app;
ALTER DEFAULT PRIVILEGES FOR ROLE ${POSTGRES_USER} IN SCHEMA hermes GRANT USAGE, SELECT ON SEQUENCES TO hermes_app;
EOSQL
