var builder = DistributedApplication.CreateBuilder(args);

// ===== HYBRID ARCHITECTURE =====
// Infrastructure (Postgres, RabbitMQ, Jaeger) runs via docker-compose with fixed ports.
// Aspire orchestrates only the .NET projects and provides unified observability dashboard.
//
// Why: Wolverine.RabbitMQ doesn't integrate with Aspire's dynamic service discovery.
//      APIs need fixed ports matching appsettings.json (localhost:5433, localhost:5672).
//
// To start infrastructure: docker-compose --profile infrastructure up -d
// To start everything: dotnet run --project src/CritterSupply.AppHost

// ===== BOUNDED CONTEXT APIS =====
// APIs read connection strings from appsettings.json (localhost:5433, localhost:5672)
// Aspire provides service-to-service discovery and observability only

// Orders BC - Order lifecycle and checkout orchestration (port 5231)
var ordersApi = builder.AddProject<Projects.Orders_Api>("crittersupply-aspire-orders-api");

// Payments BC - Authorization, capture, refunds (port 5232)
var paymentsApi = builder.AddProject<Projects.Payments_Api>("crittersupply-aspire-payments-api");

// Inventory BC - Stock levels and reservations (port 5233)
var inventoryApi = builder.AddProject<Projects.Inventory_Api>("crittersupply-aspire-inventory-api");

// Fulfillment BC - Picking, packing, shipping (port 5234)
var fulfillmentApi = builder.AddProject<Projects.Fulfillment_Api>("crittersupply-aspire-fulfillment-api");

// Customer Identity BC - Addresses and saved payment methods (port 5235)
var customerIdentityApi = builder.AddProject<Projects.CustomerIdentity_Api>("crittersupply-aspire-customeridentity-api");

// Shopping BC - Cart management (port 5236)
var shoppingApi = builder.AddProject<Projects.Shopping_Api>("crittersupply-aspire-shopping-api");

// Product Catalog BC - Product definitions and pricing (port 5133)
var productCatalogApi = builder.AddProject<Projects.ProductCatalog_Api>("crittersupply-aspire-productcatalog-api");

// Storefront API - BFF for Customer Experience (port 5237)
var storefrontApi = builder.AddProject<Projects.Storefront_Api>("crittersupply-aspire-storefront-api")
    .WithReference(shoppingApi)
    .WithReference(ordersApi)
    .WithReference(customerIdentityApi)
    .WithReference(productCatalogApi);

// ===== BLAZOR WEB APP =====

// Storefront Web - Customer-facing Blazor Server app
builder.AddProject<Projects.Storefront_Web>("crittersupply-aspire-storefront-web")
    .WithReference(storefrontApi)
    .WithReference(customerIdentityApi)
    .WithReference(shoppingApi)
    .WithReference(ordersApi)
    .WithReference(productCatalogApi);

builder.Build().Run();

// NOTE: VendorIdentity.Api (port 5240), VendorPortal.Api (port 5239), and Pricing.Api
// (port 5242) are intentionally not registered in Aspire AppHost. They use RabbitMQ
// transports that are not compatible with Aspire's dynamic service discovery model, and
// their connection strings are managed via appsettings.json / environment variables.
// Use docker-compose --profile vendor (or --profile pricing) to run them locally.
