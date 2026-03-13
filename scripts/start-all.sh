#!/bin/bash

# Start all CritterSupply services (infrastructure + all APIs + Blazor web apps)
# This starts:
# - Postgres (port 5432 in container, 5433 on host)
# - RabbitMQ (port 5672 + management UI on 15672)
# - All BC APIs (Orders, Payments, Inventory, Fulfillment, CustomerIdentity, Shopping, Catalog, Storefront.Api, VendorPortal, VendorIdentity, Pricing, Returns)
# - Blazor Web Apps (Storefront.Web on port 5238, VendorPortal.Web on port 5241)

echo "🚀 Starting all CritterSupply services..."
echo ""
echo "Services will be available at:"
echo "  - Storefront.Web:        http://localhost:5238"
echo "  - VendorPortal.Web:      http://localhost:5241"
echo ""
echo "  - Storefront.Api:        http://localhost:5237"
echo "  - Orders.Api:            http://localhost:5231"
echo "  - Payments.Api:          http://localhost:5232"
echo "  - Inventory.Api:         http://localhost:5233"
echo "  - Fulfillment.Api:       http://localhost:5234"
echo "  - CustomerIdentity.Api:  http://localhost:5235"
echo "  - Shopping.Api:          http://localhost:5236"
echo "  - ProductCatalog.Api:    http://localhost:5133"
echo "  - VendorPortal.Api:      http://localhost:5239"
echo "  - VendorIdentity.Api:    http://localhost:5240"
echo "  - Pricing.Api:           http://localhost:5242"
echo "  - Returns.Api:           http://localhost:5245"
echo ""
echo "  - RabbitMQ UI:           http://localhost:15672 (guest/guest)"
echo ""
echo "Press Ctrl+C to stop all services"
echo ""

# Change to repository root (script is in /scripts/ subdirectory)
cd "$(dirname "$0")/.."

docker compose --profile all up --build
