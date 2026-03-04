#!/bin/bash

# Start all CritterSupply services (infrastructure + all APIs + Blazor web)
# This starts:
# - Postgres (port 5432 in container, 5433 on host)
# - RabbitMQ (port 5672 + management UI on 15672)
# - All 8 BC APIs (Orders, Payments, Inventory, Fulfillment, CustomerIdentity, Shopping, Catalog, Storefront.Api)
# - Blazor Web (Storefront.Web on port 5238)

echo "🚀 Starting all CritterSupply services..."
echo ""
echo "Services will be available at:"
echo "  - Blazor Web:        http://localhost:5238"
echo "  - Storefront.Api:    http://localhost:5237"
echo "  - Orders.Api:        http://localhost:5231"
echo "  - Payments.Api:      http://localhost:5232"
echo "  - Inventory.Api:     http://localhost:5233"
echo "  - Fulfillment.Api:   http://localhost:5234"
echo "  - CustomerIdentity:  http://localhost:5235"
echo "  - Shopping.Api:      http://localhost:5236"
echo "  - ProductCatalog:    http://localhost:5133"
echo "  - RabbitMQ UI:       http://localhost:15672 (guest/guest)"
echo ""
echo "Press Ctrl+C to stop all services"
echo ""

docker-compose --profile all up --build
