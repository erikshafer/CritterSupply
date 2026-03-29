#!/bin/bash
# Creates one database per bounded context in the shared Postgres server.
# This script runs automatically on first container start via /docker-entrypoint-initdb.d/.
# Each BC connects to its own database (e.g. orders, payments) rather than the default
# 'postgres' database, providing database-level isolation while sharing a single server.
#
# NOTE: This file must use Unix (LF) line endings. Windows line endings (CRLF) corrupt
# the shebang line and cause Docker to fail with "cannot execute: required file not found".
# This is enforced by .gitattributes (*.sh text eol=lf). If you experience this error,
# run: git add --renormalize . && git checkout -- docker/postgres/create-databases.sh

set -euo pipefail

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "postgres" <<-EOSQL
    CREATE DATABASE orders;
    CREATE DATABASE payments;
    CREATE DATABASE inventory;
    CREATE DATABASE fulfillment;
    CREATE DATABASE customeridentity;
    CREATE DATABASE shopping;
    CREATE DATABASE productcatalog;
    CREATE DATABASE storefront;
    CREATE DATABASE pricing;
    CREATE DATABASE vendoridentity;
    CREATE DATABASE vendorportal;
    CREATE DATABASE returns;
    CREATE DATABASE backofficeidentity;
    CREATE DATABASE listings;
EOSQL

echo "CritterSupply: all bounded context databases created successfully."
