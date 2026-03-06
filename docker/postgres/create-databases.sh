#!/bin/bash
# Creates one database per bounded context in the shared Postgres server.
# This script runs automatically on first container start via /docker-entrypoint-initdb.d/.
# Each BC connects to its own database (e.g. orders, payments) rather than the default
# 'postgres' database, providing database-level isolation while sharing a single server.

set -e

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "postgres" <<-EOSQL
    CREATE DATABASE orders;
    CREATE DATABASE payments;
    CREATE DATABASE inventory;
    CREATE DATABASE fulfillment;
    CREATE DATABASE customeridentity;
    CREATE DATABASE shopping;
    CREATE DATABASE productcatalog;
    CREATE DATABASE storefront;
EOSQL

echo "CritterSupply: all bounded context databases created successfully."
