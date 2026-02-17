# Aspire Architecture Diagrams

This document provides visual representations of CritterSupply's architecture with and without Aspire.

## Current Architecture (Without Aspire)

```mermaid
graph TB
    subgraph "Developer Machine"
        Dev[Developer]
        Docker[Docker Compose<br/>docker-compose.yml]
        
        subgraph "Manual Execution"
            Orders[dotnet run Orders.Api<br/>Port 5231]
            Payments[dotnet run Payments.Api<br/>Port 5232]
            Shopping[dotnet run Shopping.Api<br/>Port 5236]
            Inventory[dotnet run Inventory.Api<br/>Port 5233]
            Fulfillment[dotnet run Fulfillment.Api<br/>Port 5234]
            CustomerID[dotnet run CustomerIdentity.Api<br/>Port 5235]
            Catalog[dotnet run ProductCatalog.Api<br/>Port 5133]
            StorefrontAPI[dotnet run Storefront.Api<br/>Port 5237]
            StorefrontWeb[dotnet run Storefront.Web<br/>Port 5238]
        end
        
        subgraph "Docker Containers"
            Postgres[(PostgreSQL<br/>Port 5433)]
            RabbitMQ[RabbitMQ<br/>Ports 5672/15672]
        end
    end
    
    Dev -->|1. docker-compose up -d| Docker
    Docker -->|Starts| Postgres
    Docker -->|Starts| RabbitMQ
    
    Dev -->|2. dotnet run| Orders
    Dev -->|3. dotnet run| Payments
    Dev -->|4. dotnet run| Shopping
    Dev -->|5. dotnet run| Inventory
    Dev -->|6. dotnet run| Fulfillment
    Dev -->|7. dotnet run| CustomerID
    Dev -->|8. dotnet run| Catalog
    Dev -->|9. dotnet run| StorefrontAPI
    Dev -->|10. dotnet run| StorefrontWeb
    
    Orders -.->|ConnectionString| Postgres
    Payments -.->|ConnectionString| Postgres
    Shopping -.->|ConnectionString| Postgres
    Inventory -.->|ConnectionString| Postgres
    Fulfillment -.->|ConnectionString| Postgres
    CustomerID -.->|ConnectionString| Postgres
    Catalog -.->|ConnectionString| Postgres
    StorefrontAPI -.->|ConnectionString| Postgres
    
    Orders -.->|AMQP Config| RabbitMQ
    Payments -.->|AMQP Config| RabbitMQ
    Shopping -.->|AMQP Config| RabbitMQ
    Inventory -.->|AMQP Config| RabbitMQ
    Fulfillment -.->|AMQP Config| RabbitMQ
    StorefrontAPI -.->|AMQP Config| RabbitMQ
    
    StorefrontWeb -->|http://localhost:5237| StorefrontAPI
    
    style Dev fill:#e1f5ff
    style Docker fill:#fff3e0
    style Postgres fill:#f3e5f5
    style RabbitMQ fill:#f3e5f5
```

**Current Pain Points:**
- 🔴 **10 manual commands** to start everything
- 🔴 **9-10 terminal windows** to monitor logs
- 🔴 **Hardcoded connection strings** in appsettings.json
- 🔴 **No unified view** of service health
- 🔴 **Manual port tracking** (what's running on what port?)

---

## Future Architecture (With Aspire)

```mermaid
graph TB
    subgraph "Developer Machine"
        Dev[Developer]
        
        subgraph "Aspire AppHost<br/>src/CritterSupply.AppHost"
            AppHost[Program.cs<br/>Orchestration Logic]
        end
        
        subgraph "Aspire Dashboard<br/>http://localhost:15000"
            Dashboard[Unified Dashboard]
            Resources[Resources Tab<br/>Service Status]
            Console[Console Tab<br/>Aggregated Logs]
            Traces[Traces Tab<br/>Distributed Tracing]
            Metrics[Metrics Tab<br/>Performance Data]
        end
        
        subgraph "Managed Services"
            Orders[Orders.Api<br/>Auto Port]
            Payments[Payments.Api<br/>Auto Port]
            Shopping[Shopping.Api<br/>Auto Port]
            Inventory[Inventory.Api<br/>Auto Port]
            Fulfillment[Fulfillment.Api<br/>Auto Port]
            CustomerID[CustomerIdentity.Api<br/>Auto Port]
            Catalog[ProductCatalog.Api<br/>Auto Port]
            StorefrontAPI[Storefront.Api<br/>Auto Port]
            StorefrontWeb[Storefront.Web<br/>Auto Port]
        end
        
        subgraph "Aspire-Managed Containers"
            Postgres[(PostgreSQL<br/>Auto Port)]
            RabbitMQ[RabbitMQ<br/>Auto Port]
        end
        
        subgraph "ServiceDefaults<br/>Shared Configuration"
            ServiceDefaults[Extensions.cs<br/>OpenTelemetry<br/>Health Checks<br/>Service Discovery]
        end
    end
    
    Dev -->|1. dotnet run| AppHost
    AppHost -->|Opens| Dashboard
    AppHost -->|Starts & Manages| Orders
    AppHost -->|Starts & Manages| Payments
    AppHost -->|Starts & Manages| Shopping
    AppHost -->|Starts & Manages| Inventory
    AppHost -->|Starts & Manages| Fulfillment
    AppHost -->|Starts & Manages| CustomerID
    AppHost -->|Starts & Manages| Catalog
    AppHost -->|Starts & Manages| StorefrontAPI
    AppHost -->|Starts & Manages| StorefrontWeb
    AppHost -->|Starts Container| Postgres
    AppHost -->|Starts Container| RabbitMQ
    
    Orders -->|Uses| ServiceDefaults
    Payments -->|Uses| ServiceDefaults
    Shopping -->|Uses| ServiceDefaults
    Inventory -->|Uses| ServiceDefaults
    Fulfillment -->|Uses| ServiceDefaults
    CustomerID -->|Uses| ServiceDefaults
    Catalog -->|Uses| ServiceDefaults
    StorefrontAPI -->|Uses| ServiceDefaults
    StorefrontWeb -->|Uses| ServiceDefaults
    
    AppHost -.->|Injects ConnectionString| Orders
    AppHost -.->|Injects ConnectionString| Payments
    AppHost -.->|Injects ConnectionString| Shopping
    AppHost -.->|Injects ConnectionString| Inventory
    AppHost -.->|Injects ConnectionString| Fulfillment
    AppHost -.->|Injects ConnectionString| CustomerID
    AppHost -.->|Injects ConnectionString| Catalog
    AppHost -.->|Injects ConnectionString| StorefrontAPI
    
    AppHost -.->|Injects Service URL| StorefrontWeb
    
    Orders -.->|Auto-configured| Postgres
    Payments -.->|Auto-configured| Postgres
    Shopping -.->|Auto-configured| Postgres
    Inventory -.->|Auto-configured| Postgres
    Fulfillment -.->|Auto-configured| Postgres
    CustomerID -.->|Auto-configured| Postgres
    Catalog -.->|Auto-configured| Postgres
    StorefrontAPI -.->|Auto-configured| Postgres
    
    Orders -.->|Auto-configured| RabbitMQ
    Payments -.->|Auto-configured| RabbitMQ
    Shopping -.->|Auto-configured| RabbitMQ
    Inventory -.->|Auto-configured| RabbitMQ
    Fulfillment -.->|Auto-configured| RabbitMQ
    StorefrontAPI -.->|Auto-configured| RabbitMQ
    
    StorefrontWeb -.->|Service Discovery| StorefrontAPI
    
    Dashboard -->|Monitors| Orders
    Dashboard -->|Monitors| Payments
    Dashboard -->|Monitors| Shopping
    Dashboard -->|Monitors| Inventory
    Dashboard -->|Monitors| Fulfillment
    Dashboard -->|Monitors| CustomerID
    Dashboard -->|Monitors| Catalog
    Dashboard -->|Monitors| StorefrontAPI
    Dashboard -->|Monitors| StorefrontWeb
    Dashboard -->|Monitors| Postgres
    Dashboard -->|Monitors| RabbitMQ
    
    style Dev fill:#e1f5ff
    style AppHost fill:#c8e6c9
    style Dashboard fill:#fff9c4
    style ServiceDefaults fill:#f8bbd0
    style Postgres fill:#f3e5f5
    style RabbitMQ fill:#f3e5f5
```

**Aspire Benefits:**
- ✅ **1 command** to start everything (`dotnet run` in AppHost)
- ✅ **Unified dashboard** for all services at http://localhost:15000
- ✅ **Service discovery** (no hardcoded connection strings/URLs)
- ✅ **Real-time health checks** in dashboard
- ✅ **Aggregated logs** in Console tab
- ✅ **Distributed tracing** in Traces tab
- ✅ **Automatic port assignment** (no conflicts)
- ✅ **OpenTelemetry by default** (metrics, traces, structured logs)

---

## Aspire Dashboard Features

```mermaid
graph LR
    subgraph "Aspire Dashboard<br/>http://localhost:15000"
        Resources[Resources Tab]
        Console[Console Tab]
        Structured[Structured Logs Tab]
        Traces[Traces Tab]
        Metrics[Metrics Tab]
        
        subgraph "Resources Tab"
            ServiceList[Service List<br/>✅ Running/🔴 Failed]
            HealthChecks[Health Check Status]
            Endpoints[Endpoint URLs]
            EnvVars[Environment Variables]
        end
        
        subgraph "Console Tab"
            AllLogs[Aggregated Logs<br/>All Services]
            Filter[Filter by Service]
            Search[Search Text]
        end
        
        subgraph "Structured Logs Tab"
            StructuredView[JSON Log View]
            LogLevel[Filter by Level]
            TimeRange[Time Range Selector]
        end
        
        subgraph "Traces Tab"
            DistributedTraces[Distributed Traces<br/>Cross-Service Flows]
            TraceDetail[Trace Details<br/>Timing, Errors]
            SpanList[Span List<br/>Per Operation]
        end
        
        subgraph "Metrics Tab"
            CPUUsage[CPU Usage]
            MemoryUsage[Memory Usage]
            RequestRate[Request Rate]
            ErrorRate[Error Rate]
        end
        
        Resources --> ServiceList
        Resources --> HealthChecks
        Resources --> Endpoints
        Resources --> EnvVars
        
        Console --> AllLogs
        Console --> Filter
        Console --> Search
        
        Structured --> StructuredView
        Structured --> LogLevel
        Structured --> TimeRange
        
        Traces --> DistributedTraces
        Traces --> TraceDetail
        Traces --> SpanList
        
        Metrics --> CPUUsage
        Metrics --> MemoryUsage
        Metrics --> RequestRate
        Metrics --> ErrorRate
    end
    
    style Resources fill:#e1f5ff
    style Console fill:#fff3e0
    style Structured fill:#f3e5f5
    style Traces fill:#c8e6c9
    style Metrics fill:#fff9c4
```

---

## Service Discovery Flow

### Before Aspire (Hardcoded URLs)

```mermaid
sequenceDiagram
    participant StorefrontWeb as Storefront.Web<br/>(Blazor)
    participant Code as Program.cs
    participant StorefrontApi as Storefront.Api<br/>(BFF)
    
    Note over StorefrontWeb: Startup
    StorefrontWeb->>Code: Configure HttpClient
    Code->>Code: client.BaseAddress =<br/>new Uri("http://localhost:5237")
    Note over Code: ❌ Hardcoded URL!<br/>Breaks if port changes
    StorefrontWeb->>StorefrontApi: HTTP GET<br/>http://localhost:5237/api/cart
    StorefrontApi-->>StorefrontWeb: Cart data
```

### After Aspire (Service Discovery)

```mermaid
sequenceDiagram
    participant AppHost as Aspire AppHost
    participant StorefrontWeb as Storefront.Web<br/>(Blazor)
    participant Config as Configuration
    participant StorefrontApi as Storefront.Api<br/>(BFF)
    
    Note over AppHost: Startup
    AppHost->>StorefrontApi: Start service on port 52001<br/>(dynamic)
    AppHost->>Config: Inject:<br/>services:storefront-api:http:0<br/>= http://localhost:52001
    AppHost->>StorefrontWeb: Start with injected config
    StorefrontWeb->>Config: Get URL:<br/>Configuration["services:storefront-api:http:0"]
    Config-->>StorefrontWeb: http://localhost:52001
    StorefrontWeb->>StorefrontApi: HTTP GET<br/>http://localhost:52001/api/cart
    StorefrontApi-->>StorefrontWeb: Cart data
    
    Note over StorefrontWeb,StorefrontApi: ✅ No hardcoded URL!<br/>Port dynamically assigned
```

---

## Connection String Injection

### Before Aspire (Hardcoded in appsettings.json)

```mermaid
sequenceDiagram
    participant Orders as Orders.Api
    participant AppSettings as appsettings.json
    participant Postgres as PostgreSQL
    
    Orders->>AppSettings: Read ConnectionStrings:marten
    AppSettings-->>Orders: Host=localhost;Port=5433;...
    Note over AppSettings: ❌ Hardcoded!<br/>Must match docker-compose.yml
    Orders->>Postgres: Connect to localhost:5433
    Postgres-->>Orders: Connection established
```

### After Aspire (Injected by AppHost)

```mermaid
sequenceDiagram
    participant AppHost as Aspire AppHost
    participant Postgres as PostgreSQL Container
    participant Config as Configuration
    participant Orders as Orders.Api
    
    AppHost->>Postgres: Start container on port 52000<br/>(dynamic)
    Postgres-->>AppHost: Container ready
    AppHost->>Config: Inject ConnectionStrings:marten<br/>= Host=localhost;Port=52000;...
    AppHost->>Orders: Start with injected config
    Orders->>Config: Read ConnectionStrings:marten
    Config-->>Orders: Host=localhost;Port=52000;...
    Orders->>Postgres: Connect to localhost:52000
    Postgres-->>Orders: Connection established
    
    Note over AppHost,Orders: ✅ Dynamic connection string!<br/>No hardcoded ports
```

---

## Distributed Tracing Example

### Scenario: Customer Places Order

```mermaid
sequenceDiagram
    participant User as Customer
    participant Web as Storefront.Web<br/>(Blazor)
    participant BFF as Storefront.Api<br/>(BFF)
    participant Shopping as Shopping.Api
    participant Orders as Orders.Api
    participant RMQ as RabbitMQ
    participant Payments as Payments.Api
    participant Inventory as Inventory.Api
    
    Note over User,Inventory: Aspire Traces This Entire Flow!
    
    User->>Web: Click "Place Order"
    activate Web
    Web->>BFF: POST /api/checkout/complete
    activate BFF
    BFF->>Shopping: POST /api/cart/{id}/initiate-checkout
    activate Shopping
    Shopping->>RMQ: Publish CheckoutInitiated
    Shopping-->>BFF: 202 Accepted
    deactivate Shopping
    BFF-->>Web: 202 Accepted
    deactivate BFF
    Web-->>User: "Processing order..."
    deactivate Web
    
    RMQ->>Orders: Consume CheckoutInitiated
    activate Orders
    Orders->>Orders: Create Checkout aggregate
    Orders->>RMQ: Publish CheckoutCompleted
    Orders->>RMQ: Publish OrderPlaced
    deactivate Orders
    
    RMQ->>Payments: Consume OrderPlaced<br/>(Authorize Payment)
    activate Payments
    Payments->>Payments: Authorize $100
    Payments->>RMQ: Publish PaymentAuthorized
    deactivate Payments
    
    RMQ->>Inventory: Consume OrderPlaced<br/>(Reserve Stock)
    activate Inventory
    Inventory->>Inventory: Reserve 2x SKU123
    Inventory->>RMQ: Publish StockReserved
    deactivate Inventory
    
    Note over Web,Inventory: ✅ Aspire Dashboard Shows:<br/>- Full trace from User → Inventory<br/>- Timing of each step<br/>- Any errors or retries<br/>- Message publish/consume correlation
```

**In Aspire Dashboard (Traces Tab):**
- See entire flow as single trace
- Drill down into each span (operation)
- View timing: "Shopping.Api took 45ms, Orders.Api took 230ms, Payments.Api took 120ms"
- Identify bottlenecks: "Why is Orders.Api slow?"
- Correlate logs: Click trace → see all logs for that request

---

## Project Structure Comparison

### Before Aspire

```
CritterSupply/
├── src/
│   ├── Orders/
│   │   ├── Orders/
│   │   └── Orders.Api/
│   ├── Payments/
│   │   ├── Payments/
│   │   └── Payments.Api/
│   ├── Shopping/
│   │   ├── Shopping/
│   │   └── Shopping.Api/
│   ├── ... (5 more BCs)
│   └── Shared/
│       └── Messages.Contracts/
├── tests/
├── docker-compose.yml  ← Infrastructure orchestration
├── CritterSupply.slnx
└── README.md
```

### After Aspire

```
CritterSupply/
├── src/
│   ├── CritterSupply.AppHost/  ← NEW: Aspire orchestration
│   │   ├── Program.cs
│   │   └── appsettings.json
│   ├── CritterSupply.ServiceDefaults/  ← NEW: Shared Aspire config
│   │   ├── Extensions.cs
│   │   └── CritterSupply.ServiceDefaults.csproj
│   ├── Orders/
│   │   ├── Orders/
│   │   └── Orders.Api/  (references ServiceDefaults)
│   ├── Payments/
│   │   ├── Payments/
│   │   └── Payments.Api/  (references ServiceDefaults)
│   ├── Shopping/
│   │   ├── Shopping/
│   │   └── Shopping.Api/  (references ServiceDefaults)
│   ├── ... (5 more BCs, all reference ServiceDefaults)
│   └── Shared/
│       └── Messages.Contracts/
├── tests/  (unchanged - still use TestContainers)
├── docker-compose.yml  ← Still used for CI/CD
├── CritterSupply.slnx  (includes AppHost + ServiceDefaults)
└── README.md  (updated with Aspire instructions)
```

**Key Additions:**
- ✅ `CritterSupply.AppHost` - Orchestrates everything
- ✅ `CritterSupply.ServiceDefaults` - Shared OpenTelemetry + health checks + service discovery
- ✅ Each API project references ServiceDefaults (1 line in .csproj)

---

## Summary

### Architecture Changes
- **Orchestration**: Manual (`docker-compose` + 9x `dotnet run`) → Automated (Aspire AppHost)
- **Configuration**: Hardcoded → Service Discovery
- **Observability**: Manual → Built-in (OpenTelemetry)
- **Developer Experience**: Fragmented → Unified Dashboard

### What Stays the Same
- **Domain Logic**: 100% unchanged
- **Wolverine/Marten**: 100% unchanged
- **TestContainers**: 100% unchanged
- **CI/CD**: 100% unchanged

### Net Result
- **DX Improvement**: 10x better (10+ commands → 1 command)
- **Code Changes**: Minimal (~50 lines across 20 files)
- **Risk**: Low (additive, preserves existing workflows)
- **Effort**: 4-6 hours
