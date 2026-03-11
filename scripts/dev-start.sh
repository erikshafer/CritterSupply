#!/usr/bin/env bash
# Small helper script for common development start/stop workflows
# Usage:
#   ./scripts/dev-start.sh infra       # start infrastructure (Postgres, RabbitMQ, Jaeger)
#   ./scripts/dev-start.sh run <proj>  # run a single service natively (dotnet run --project <proj>)
#   ./scripts/dev-start.sh all         # start full stack in containers (docker-compose --profile all up --build)
#   ./scripts/dev-start.sh down        # stop containers (docker-compose --profile all down)

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
CMD=${1:-}

print_usage() {
  cat <<'USAGE' >&2
Usage: ./scripts/dev-start.sh <command>

Commands:
  infra                      Start infrastructure (Postgres, RabbitMQ, Jaeger)
  run <project-csproj-path>  Run a single service natively (dotnet run --project <path>)
  all                        Start full stack in containers (docker-compose --profile all up --build)
  down                       Stop all containers (docker-compose --profile all down)

Examples:
  ./scripts/dev-start.sh infra
  ./scripts/dev-start.sh run "src/Orders/Orders.Api/Orders.Api.csproj"
  # Run Storefront API (BFF) natively — launchSettings.json uses port 5237 by convention
  ./scripts/dev-start.sh run "src/Customer Experience/Storefront.Api/Storefront.Api.csproj"
  # Quick start: infra + Storefront API (one command)
  ./scripts/dev-start.sh quick-start
USAGE
}

if [ -z "$CMD" ]; then
  print_usage
  exit 2
fi

case "$CMD" in
  infra)
    echo "Starting infrastructure (docker-compose --profile infrastructure up -d)"
    docker-compose --profile infrastructure up -d
    ;;

  run)
    if [ -z "${2:-}" ]; then
      echo "Usage: $0 run <project-csproj-path>" >&2
      exit 2
    fi
    PROJ_PATH="$2"
    echo "Running project: $PROJ_PATH"
    dotnet run --project "$PROJ_PATH"
    ;;

  quick-start)
    echo "Quick-start: starting infrastructure and running Storefront API (BFF)"
    docker-compose --profile infrastructure up -d
    dotnet run --project "src/Customer Experience/Storefront.Api/Storefront.Api.csproj"
    ;;

  all)
    echo "Bringing up full stack in containers (docker-compose --profile all up --build)"
    docker-compose --profile all up --build
    ;;

  down)
    echo "Stopping all containers (docker-compose --profile all down)"
    docker-compose --profile all down
    ;;

  *)
    echo "Unknown command: $CMD" >&2
    print_usage
    exit 2
    ;;
esac
