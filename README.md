# CritterSupply

[![Build](https://github.com/erikshafer/CritterSupply/actions/workflows/dotnet.yml/badge.svg?branch=main)](https://github.com/erikshafer/CritterSupply/actions/workflows/dotnet.yml)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-18-336791?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![RabbitMQ](https://img.shields.io/badge/RabbitMQ-4.2-FF6600?logo=rabbitmq&logoColor=white)](https://www.rabbitmq.com/)

## 🤔 What Is This Repository? <a id='1.0'></a>

This repository demonstrates how to build production-grade, event-driven systems using a realistic e-commerce domain.

It also serves as a reference architecture for idiomatically leveraging the "Critter Stack"—[Wolverine](https://github.com/JasperFx/wolverine) and [Marten](https://github.com/JasperFx/marten)—to supercharge your .NET development. These tools just get out of your way so you can focus on the actual business problems at hand.

**Best suited for:** .NET developers learning event sourcing and CQRS, architects evaluating the Critter Stack for production use, teams transitioning from monolithic to event-driven architectures, and engineers looking for cross-BC integration reference patterns.

### 🛒 Ecommerce <a id='1.1'></a>

CritterSupply is a fictional pet supply retailer—the name a playful nod to the Critter Stack powering it, with the tagline "Stocked for every season."

E-commerce was chosen as the domain partly from the maintainer's industry experience, but more importantly because it's a domain most developers intuitively understand. Everyone has placed an order online. That familiarity lets us focus on *how* the system is built rather than getting bogged down explaining *what* it does.

### ️🔎️ Patterns in Practice <a id='1.2'></a>

E-commerce is a natural fit for the patterns this repository aims to demonstrate: event sourcing for capturing the full history of orders and inventory movements, stateful Sagas for coordinating multi-step processes like payment authorization and fulfillment, and reservation-based workflows where inventory is held pending confirmation rather than immediately decremented.

This isn't a reference architecture padded with unnecessary layers, abstractions, or onion architecture to appear "enterprise-ready." The patterns here are inspired by real production systems built with the Critter Stack—code that's actually running and handling real business problems, ranging from startups to large enterprises.

#### Short-List of Patterns, Paradigms, and Principles<a id='1.2.1'></a>

A non-exhaustive list of the patterns, paradigms, and principles demonstrated in this codebase, in no particular order:

- Event Sourcing (Orders, Payments, Inventory, Fulfillment)
- Command Query Responsibility Segregation (CQRS)
- Stateful Sagas (Order orchestration)
- Inbox Pattern (guaranteed message processing)
- Outbox Pattern (reliable message publishing)
- Reservation-based Workflows (Inventory management)
- Choreography vs Orchestration (BC integration patterns)
- Snapshot Pattern (temporal consistency — e.g., address captured at order placement)
- Backend-for-Frontend (BFF) Pattern (Customer Experience — Blazor Server; Vendor Portal — Blazor WASM)
- Vertical Slice Architecture (VSA)
- Domain-Driven Design (DDD)
- Traditional DDD with EF Core (Customer Identity, Vendor Identity, Backoffice Identity)
- A-Frame Architecture (pure business logic)
- Railway-Oriented Programming (Wolverine middleware)
- Behavior-Driven Development (BDD) with Reqnroll (reference implementation in Product Catalog; applied across multiple BCs)
- E2E Testing with Playwright (Storefront + Vendor Portal)
- Component Testing with bUnit (Storefront.Web)
- Multi-issuer JWT Strategy (Vendor Identity + Backoffice Identity co-existing auth systems)
- UUID v5 for Natural-Key Stream IDs (Promotions BC)

## 🤖 AI-assisted Development <a id='1.3'></a>

This project is built with Claude as a collaborative coding partner. Beyond just generating code, it's an exercise in teaching AI tools to think in event-driven patterns and leverage the Critter Stack idiomatically—helping to improve the guidance these tools can offer the broader community.

That is to say, the more these tools see well-structured examples, the better guidance they can offer developers exploring these approaches for the first time.

See [CLAUDE.md](./CLAUDE.md) for AI development guidelines and [docs/README.md](./docs/README.md) for comprehensive documentation structure.

**📋 Architectural Review:** See [docs/ARCHITECTURAL-REVIEW.md](./docs/ARCHITECTURAL-REVIEW.md) for an independent review of bounded context design, service communication patterns, and recommendations from an experienced software architect perspective.

### Custom Agents

CritterSupply includes specialized GitHub Copilot agents with domain expertise to assist with development:

- **👨‍💼 Principal Software Architect** ([`.github/agents/principal-architect.md`](./.github/agents/principal-architect.md)) - Expert in .NET, event-driven systems, distributed architecture, and the Critter Stack (Wolverine + Marten). Reviews code quality, system design, bounded context boundaries, and project trajectory with 15+ years of production experience.

- **🏪 Product Owner** ([`.github/agents/product-owner.md`](./.github/agents/product-owner.md)) - E-commerce domain expert with 10+ years experience in vendor relations, product/inventory management, and marketplace channels. Provides business-focused feedback on event-driven workflows, bounded context boundaries, and how business processes translate into distributed architecture.

- **🚀 DevOps Engineer** ([`.github/agents/devops-engineer.md`](./.github/agents/devops-engineer.md)) - DevOps/GitOps specialist with expertise in CI/CD orchestration, Infrastructure as Code (IaC), deployment strategies (blue/green, canary, rollback), GitHub Actions, Docker/Kubernetes, and observability (OpenTelemetry). Designs autonomous deployment pipelines with risk analysis and environment-aware strategy adaptation.

- **🧪 QA Engineer** ([`.github/agents/qa-engineer.md`](./.github/agents/qa-engineer.md)) - Seasoned quality assurance professional with expertise in manual and automated testing, BDD, and full-stack quality strategy. Expert in building test coverage for event-driven, distributed .NET systems using the Critter Stack (Wolverine + Marten) and verifying system integrity across bounded contexts.

- **🎨 UX Engineer** ([`.github/agents/ux-engineer.md`](./.github/agents/ux-engineer.md)) - Senior UX Engineer specializing in e-commerce experiences and Blazor frontends. Reviews components for accessibility (WCAG 2.1/2.2), responsive design, and interaction quality. Participates in domain modeling (Event Storming, Event Modeling) with a user-perspective lens, designs read models and dashboards from the consumer's information needs outward, and applies DDD and Team Topologies thinking to surface UX friction before it reaches implementation.

**How to use:** Tag the agent (`@principal-architect`, `@product-owner`, `@devops-engineer`, `@qa-engineer`, or `@ux-engineer`) in pull request or issue comments to get specialized feedback.

**Example prompts:**
```
@principal-architect Can you review the event sourcing implementation in this PR?

@product-owner Does this order cancellation flow match real-world e-commerce policies?

@devops-engineer How should we deploy this Orders BC refactor with zero downtime?

@qa-engineer What integration tests should we add to cover the new checkout flow?

@ux-engineer Does this checkout page layout follow good UX principles?

@principal-architect Is this bounded context boundary properly defined?

@product-owner Should "BackorderRequested" be a separate event or extend "ReservationFailed"?

@qa-engineer Is the BDD coverage sufficient for the Order saga happy path?
```

## 🛠️ Technology Stack <a id='1.4'></a>

- **Core:** C# 14+ (.NET 10), [Wolverine](https://wolverine.netlify.app/), [Marten](https://martendb.io/), [EF Core](https://learn.microsoft.com/en-us/ef/core/)
- **Infrastructure:** PostgreSQL, RabbitMQ, Docker, [.NET Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/)
- **Testing:** [Alba](https://jasperfx.github.io/alba/), [Testcontainers](https://dotnet.testcontainers.org/), xUnit, [Reqnroll](https://reqnroll.net/), [Playwright](https://playwright.dev/dotnet/) (E2E), [bUnit](https://bunit.dev/) (components)
- **UI:** [Blazor Server](https://learn.microsoft.com/en-us/aspnet/core/blazor/) + [Blazor WASM](https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models#blazor-webassembly), [MudBlazor](https://mudblazor.com/), SignalR
- **Observability:** OpenTelemetry, Jaeger (distributed tracing)

See [CLAUDE.md](./CLAUDE.md) for complete technology details and development guidelines.

## 🗺️ Bounded Contexts <a id='2.0'></a>

CritterSupply is organized into bounded contexts. As described in Domain-Driven Design, bounded contexts help lower the cost of consensus. If one is unfamiliar with the concept, a crude yet simple way of picturing it is that each context could have its own team in an organization. That's not a rule by any means, but hopefully that helps you paint a picture of how CritterSupply is divided up logically and physically in this repo.

### Architecture Overview

The system spans two primary flows: the customer-facing storefront and the vendor/operations side. Each is shown separately below for clarity.

#### Customer-Facing Flow

```mermaid
graph TB
    CE[🎁 Customer Experience<br/>Storefront BFF · Blazor Server + SignalR]
    Shopping[🛒 Shopping<br/>Cart Management]
    Orders[📨 Orders<br/>Order Orchestration Saga]
    Payments[💳 Payments<br/>Authorization & Capture]
    Inventory[📊 Inventory<br/>Stock & Reservations]
    Fulfillment[🚚 Fulfillment<br/>Picking · Packing · Shipping]
    Returns[🔄 Returns<br/>Return Auth & Exchanges]
    CustomerID[👤 Customer Identity<br/>Auth · Addresses · Profiles]
    Catalog[📦 Product Catalog<br/>Products & Catalog Data]
    Pricing[💰 Pricing<br/>MAP · Floor · Temporal Prices]
    Promotions[🏷️ Promotions<br/>Coupons & Discount Rules]
    Correspondence[📬 Correspondence<br/>Transactional Email & SMS]

    CE -->|Browse Products| Catalog
    CE -->|Get Cart| Shopping
    CE -->|View Orders| Orders
    CE -->|Get Customer Data| CustomerID
    Shopping -->|Checkout Handoff| Orders
    Orders -->|Authorize Payment| Payments
    Orders -->|Reserve Stock| Inventory
    Orders -->|Create Shipment| Fulfillment
    Returns -->|Refund Request| Payments
    Returns <-->|Return Coordination| Fulfillment
    Returns <-->|Return Notification| Orders
    Shopping -.->|Validate Coupon| Promotions
    Shopping -.->|Resolve Price| Pricing
    Orders -.->|Customer Snapshot| CustomerID
    Pricing -.->|Price Updates| Shopping
    Shopping -.->|CartUpdated| CE
    Orders -.->|OrderPlaced| CE
    Fulfillment -.->|Shipped| CE
    Payments -.->|PaymentResult| CE
    Orders -.->|OrderConfirmed| Correspondence
    Fulfillment -.->|ShipmentDispatched| Correspondence
    Returns -.->|ReturnApproved| Correspondence
    Payments -.->|RefundIssued| Correspondence
    Correspondence -.->|Customer Lookup| CustomerID

    classDef bff fill:#e1f5ff,stroke:#01579b,stroke-width:2px
    classDef core fill:#fff3e0,stroke:#e65100,stroke-width:2px
    classDef support fill:#f3e5f5,stroke:#4a148c,stroke-width:2px
    class CE bff
    class Shopping,Orders,Payments,Inventory,Fulfillment,Returns core
    class CustomerID,Catalog,Pricing,Promotions,Correspondence support
```

#### Vendor & Operations Flow

```mermaid
graph TB
    VP[🏪 Vendor Portal<br/>Vendor BFF · Blazor WASM]
    VendorID[🔐 Vendor Identity<br/>Vendor Auth & Tenancy · JWT]
    BO[🖥️ Backoffice<br/>Internal Ops Portal · Planned]
    BackofficeID[🛡️ Backoffice Identity<br/>Admin Auth · RBAC · JWT]
    Orders[📨 Orders<br/>Order Orchestration]
    Payments[💳 Payments<br/>Authorization & Capture]
    Fulfillment[🚚 Fulfillment<br/>Picking · Packing · Shipping]
    Catalog[📦 Product Catalog<br/>Products & Catalog Data]
    Pricing[💰 Pricing<br/>MAP · Floor · Temporal Prices]
    Inventory[📊 Inventory<br/>Stock & Reservations]

    VP -->|Authenticate| VendorID
    VP -->|View Orders| Orders
    VP -->|View Fulfillment| Fulfillment
    VP -->|View Payments| Payments
    VP -->|Manage Pricing| Pricing
    Catalog -.->|ProductPublished| Pricing
    VP -.->|PriceChangeRequest| Pricing
    BO -->|Authenticate| BackofficeID
    BO -.->|Admin Access| Orders
    BO -.->|Admin Access| Inventory
    BO -.->|Admin Access| Catalog

    classDef bff fill:#e1f5ff,stroke:#01579b,stroke-width:2px
    classDef identity fill:#e8f5e9,stroke:#2e7d32,stroke-width:2px
    classDef core fill:#fff3e0,stroke:#e65100,stroke-width:2px
    classDef planned fill:#f5f5f5,stroke:#9e9e9e,stroke-width:2px,stroke-dasharray:5 5
    class VP bff
    class VendorID,BackofficeID identity
    class Orders,Payments,Fulfillment,Catalog,Pricing,Inventory core
    class BO planned
```

**Legend (both diagrams):**
- **Solid arrows (→)**: Synchronous calls (HTTP composition or direct orchestration)
- **Dotted arrows (⋯→)**: Asynchronous integration messages (RabbitMQ)
- **Blue**: BFF layer (customer-facing or vendor-facing)
- **Green**: Identity contexts (JWT-issuing authentication services)
- **Orange**: Core business contexts (event-sourced)
- **Purple**: Supporting contexts
- **Grey dashed border**: Planned — not yet implemented

### Bounded Context Status

Below is a table of each context's focused responsibilities, along with its current implementation status.

| Context                       | Responsibility                                                              | Status      |
|-------------------------------|-----------------------------------------------------------------------------|-------------|
| 📨 **Orders**                 | Commercial commitment: checkout intake and post-purchase order orchestration | ✅ Complete |
| 💳 **Payments**               | Authorization, capture, refunds                                             | ✅ Complete |
| 🛒 **Shopping**               | Cart management                                                             | ✅ Complete |
| 📊 **Inventory**              | Stock levels and reservations                                               | ✅ Complete |
| 🚚 **Fulfillment**            | Picking, packing, shipping                                                  | ✅ Complete |
| 👤 **Customer Identity**      | Customer authentication, addresses, and profiles                            | ✅ Complete |
| 📦 **Product Catalog**        | Product definitions and catalog data                                        | ✅ Complete |
| 🎁 **Customer Experience**    | Storefront BFF (Blazor Server + SignalR)                                    | ✅ Complete |
| 🔐 **Vendor Identity**        | Vendor authentication and tenant management (JWT)                           | ✅ Complete |
| 🏪 **Vendor Portal**          | Vendor analytics, insights, and change requests (BFF)                       | ✅ Complete |
| 🔄 **Returns**                | Return authorization, exchanges, and refund coordination                    | ✅ Complete |
| 💰 **Pricing**                | MAP/floor prices, temporal pricing, vendor price management                 | ✅ Complete |
| 🏷️ **Promotions**             | Coupon codes and discount rules                                             | ✅ Complete |
| 📬 **Correspondence**         | Transactional email and SMS notifications                                   | ✅ Complete |
| 🛡️ **Backoffice Identity**    | Admin JWT authentication with RBAC (7 roles)                               | ✅ Complete |
| 🖥️ **Backoffice**             | Internal operations portal (gateway BFF)                                    | 🔜 Planned  |
| 🔍 **Search**                 | Full-text and faceted product search                                        | 🔜 Planned  |
| ⭐ **Recommendations**        | Personalized product recommendations                                        | 🔜 Planned  |
| 🎫 **Store Credit**           | Store credit issuance and redemption                                        | 🔜 Planned  |
| 📈 **Analytics**              | Business intelligence projections                                           | 🔜 Planned  |
| 🔧 **Operations Dashboard**   | Engineering/SRE observability tooling                                       | 🔜 Planned  |

### Vendor-Side Contexts

The vendor side of CritterSupply mirrors how a real pet supply retailer operates: suppliers authenticate through **Vendor Identity** (a dedicated JWT-issuing service with per-tenant isolation), then interact with the system through the **Vendor Portal** BFF—a Blazor WASM application where vendors manage pricing proposals, view order and fulfillment status for their products, and track payment settlements. **Backoffice Identity** issues admin tokens with role-based access control across seven defined roles, laying the groundwork for the planned **Backoffice** internal operations portal.

For detailed responsibilities, interactions, and event flows between contexts, see [CONTEXTS.md](./CONTEXTS.md).

### 🔌 External Service Integration (Stripe)

The Payments BC includes a reference implementation for third-party payment integration: a [research spike](./docs/planning/spikes/stripe-api-integration.md), [ADR 0010](./docs/decisions/0010-stripe-payment-gateway-integration.md), and [code examples](./docs/examples/stripe/) covering Stripe's webhook-driven model, two-phase authorize/capture aligned with the Order saga, HMAC-SHA256 signature verification, and idempotency keys for safe retries.

## ⏩ How to Run <a id='5.0'></a>

### Quick start (one command)

If you just want to get a running storefront quickly (infrastructure + Storefront API), run:

```bash
./scripts/dev-start.sh quick-start
```

This starts Postgres, RabbitMQ, Jaeger (infrastructure) and runs the Storefront API locally (launchSettings.json uses port 5237 by convention).

### Requirements <a id='5.2'></a>

- [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Docker Desktop](https://docs.docker.com/engine/install/)

### 🛠️ Local Development <a id='5.3'></a>

#### Option 1: Docker Compose + dotnet run (⭐ Recommended)

```bash
# 1. Start infrastructure (Postgres, RabbitMQ, Jaeger)
docker-compose --profile infrastructure up -d

# 2. Run a service (e.g., Customer Experience storefront)
dotnet run --project "src/Customer Experience/Storefront.Web/Storefront.Web.csproj"
# Navigate to http://localhost:5238
```

**See [docs/QUICK-START.md](./docs/QUICK-START.md) for a full step-by-step guide.**

---

#### Option 2: .NET Aspire (Optional — Enhanced Observability)

```bash
# Start infrastructure first
docker-compose --profile infrastructure up -d

# Then start all services via Aspire
dotnet run --project src/CritterSupply.AppHost

# 🚀 Aspire Dashboard: https://localhost:17265
# 🛍️ Storefront Web:   http://localhost:5238
```

**See [docs/ASPIRE-GUIDE.md](./docs/ASPIRE-GUIDE.md) for setup details or [ADR 0009](./docs/decisions/0009-aspire-integration.md) for the rationale.**

---

#### Option 3: Fully Containerized (Demos & Onboarding)

```bash
# Start infrastructure + all APIs + Blazor web in containers
docker-compose --profile all up --build

# 🛍️ Storefront Web: http://localhost:5238
```

**Selective startup:**

```bash
# Infrastructure + specific BCs
docker-compose --profile infrastructure --profile orders --profile shopping up
```

**Stop and cleanup:**

```bash
docker-compose --profile all down        # Stop all containers
docker-compose --profile all down -v     # Stop and remove volumes (fresh start)
```

#### Run Individual Bounded Contexts (Native)

Each BC can be run independently with `dotnet run`. See [CLAUDE.md](./CLAUDE.md) for port allocations and run commands.

```bash
dotnet run --project "src/Orders/Orders.Api/Orders.Api.csproj"        # Port 5231
dotnet run --project "src/Shopping/Shopping.Api/Shopping.Api.csproj"  # Port 5236
dotnet run --project "src/Product Catalog/ProductCatalog.Api/ProductCatalog.Api.csproj"  # Port 5133
```

#### 🧪 Manual API Testing

Each BC includes `.http` files for manual testing. See [docs/HTTP-FILES-GUIDE.md](./docs/HTTP-FILES-GUIDE.md) for usage instructions.

#### 🔭 Distributed Tracing with Jaeger

CritterSupply ships with [Jaeger](https://www.jaegertracing.io/) for distributed tracing. Jaeger starts automatically with the `infrastructure` profile — access the UI at **http://localhost:16686**. All API projects are instrumented with OpenTelemetry via OTLP export.

## 🏫 Resources <a id='9.0'></a>

- **Blog:** [event-sourcing.dev](https://www.event-sourcing.dev)
- **Wolverine:** [wolverine.netlify.app](https://wolverine.netlify.app/)
- **Marten:** [martendb.io](https://martendb.io/)
- **Tools:** [JetBrains Rider](https://www.jetbrains.com/rider/), [DataGrip](https://www.jetbrains.com/datagrip/)

## 👷‍♂️ Maintainer <a id='10.0'></a>

**Erik "Faelor" Shafer**

[LinkedIn](https://www.linkedin.com/in/erikshafer/) • [Blog](https://www.event-sourcing.dev) • [YouTube](https://www.youtube.com/@event-sourcing) • [Bluesky](https://bsky.app/profile/erikshafer.bsky.social)
