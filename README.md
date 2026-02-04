# CritterSupply

## 🤔 What Is This Repository? <a id='1.0'></a>

This repository demonstrates how to build robust, production-ready, event-driven systems using a realistic e-commerce domain.

It also serves as a reference architecture for idiomatically leveraging the "Critter Stack"—[Wolverine](https://github.com/JasperFx/wolverine) and [Marten](https://github.com/JasperFx/marten)—to supercharge your .NET development. These tools just get out of your way so you can focus on the actual business problems at hand.

### 🛒 Ecommerce <a id='1.1'></a>

CritterSupply is a fictional pet supply retailer—the name a playful nod to the Critter Stack powering it, with the tagline "Stocked for every season."

E-commerce was chosen as the domain partly from the maintainer's industry experience, but more importantly because it's a domain most developers intuitively understand. Everyone has placed an order online. That familiarity lets us focus on *how* the system is built rather than getting bogged down explaining *what* it does.

### ️🔎️ Patterns in Practice <a id='1.2'></a>

Beyond accessibility, e-commerce naturally demands the patterns this repository aims to demonstrate: event sourcing for capturing the full history of orders and inventory movements, stateful Sagas for coordinating multi-step processes like payment authorization and fulfillment, and reservation-based workflows where inventory is held pending confirmation rather than immediately decremented.

This isn't a reference architecture padded with unnecessary layers, abstractions, or onion architecture to appear "enterprise-ready." The patterns here are inspired by real production systems built with the Critter Stack—code that's actually running and handling real business problems, ranging from startups to large enterprises.

#### Short-List of Patterns, Paradigms, and Principles<a id='1.2.1'></a>

A non-exhaustive list of the patterns, paradigms, and principles demonstrated in this codebase, in no particular order:

- Event Sourcing (Orders, Payments, Inventory, Fulfillment)
- Command Query Responsibility Segregation (CQRS)
- Stateful Sagas (Order orchestration)
- Inbox Pattern (guaranteed message processing)
- Outbox Pattern (reliable message publishing)
- Reservation-based Workflows (inventory management)
- Choreography vs Orchestration (BC integration patterns)
- Snapshot Pattern (temporal consistency for addresses)
- Backend-for-Frontend (BFF) Pattern (Customer Experience)
- Vertical Slice Architecture (VSA)
- Behavior-Driven Development (BDD)
- Domain-Driven Design (DDD)
- Traditional DDD with EF Core (Customer Identity)
- A-Frame Architecture (pure business logic)
- Railway-Oriented Programming (Wolverine middleware)

### 🤖 AI-assisted Development <a id='1.3'></a>

This project is built with Claude as a collaborative coding partner. Beyond just generating code, it's an exercise in teaching AI tools to think in event-driven patterns and leverage the Critter Stack idiomatically—helping to improve the guidance these tools can offer the broader community.

That is to say, the more these tools see well-structured examples, the better guidance they can offer developers exploring these approaches for the first time.

See [CLAUDE.md](./CLAUDE.md) for the project-specific instructions Claude follows when working on this codebase.

#### 🚫 Thinking Machines <a id='1.3.1'></a>
Who knows. Maybe one day we'll ban "thinking machines" and have to build everything ourselves again. 😉 (see: Dune, Warhammer 40k, Battlestar Galactica, Mass Effect, and others)

### 🛠️ Technology Stack <a id='1.4'></a>

**Language & Runtime:**
- C# 14+ (.NET 10+)

**Core Frameworks:**
- [Wolverine](https://wolverine.netlify.app/) - Command/message handling, HTTP endpoints, sagas
- [Marten](https://martendb.io/) - Event sourcing and document store (Orders, Payments, Inventory, Fulfillment)
- [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/) - Traditional relational persistence (Customer Identity)

**Infrastructure:**
- PostgreSQL - Database for both Marten (document store and event store) and EF Core (traditional relational store)
- RabbitMQ - Message broker for cross-BC communication
- Docker - Containerization and local development

**Testing:**
- [Alba](https://jasperfx.github.io/alba/) - Integration testing with HTTP scenarios
- [Testcontainers](https://dotnet.testcontainers.org/) - Disposable database instances for tests
- xUnit - Test framework
- Shouldly - Assertion library
- FluentValidation - Command validation

**Future:**
- Blazor Server - Customer-facing UI (Customer Experience BC)
- SignalR - Real-time notifications
- .NET Aspire - Orchestration (planned)

## 🗺️ Bounded Contexts <a id='2.0'></a>

CritterSupply is organized into bounded contexts. As described in Domain-Driven Design, bounded contexts help lower the cost of consensus. If one is unfamiliar with the concept, a crude yet simple way of picturing it is that each context could have its own team in an organization. That's not a rule by any means, but hopefully that helps you paint a picture of how CritterSupply is divided up logically and physically in this repo.

Below is a table of each contexts' focused responsibilities, along with their current implementation status:

| Context                    | Responsibility                                 | Status     |
|----------------------------|------------------------------------------------|------------|
| 📨 **Orders**              | Order lifecycle and checkout                   | ✅ Complete |
| 💳 **Payments**            | Authorization, capture, refunds                | ✅ Complete |
| 🛒 **Shopping**            | Cart management                                | ✅ Complete |
| 📊 **Inventory**           | Stock levels and reservations                  | ✅ Complete |
| 🚚 **Fulfillment**         | Picking, packing, shipping                     | ✅ Complete |
| 👤 **Customer Identity**   | Addresses and saved payment methods            | ✅ Complete |
| 📦 **Product Catalog**     | Product definitions and pricing                | ✅ Complete |
| 🎁 **Customer Experience** | Storefront BFF (Blazor + SignalR)              | 🔜 Planned |
| 🏢 **Vendor Identity**     | Vendor user authentication & tenant management | 🔜 Planned |
| 📊 **Vendor Portal**       | Vendor analytics, insights, change requests    | 🔜 Planned |
| 🔄 **Returns**             | Return authorization and processing            | 🔜 Planned |

For detailed responsibilities, interactions, and event flows between contexts, see [CONTEXTS.md](./CONTEXTS.md).

## ⏩ How to Run <a id='5.0'></a>

### Requirements <a id='5.2'></a>

This software solution has multiple dependencies that need to be running locally.

- [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Docker Desktop](https://docs.docker.com/engine/install/)

### 🛠️ Local Development <a id='5.3'></a>

To run the solution locally, you have multiple options. Either run with Docker Compose and `dotnet` commands to run specific or all modules, or have everything orchestrated and launched with [Aspire](https://aspire.dev/).

#### 🐋 Run with Docker Compose

To launch Docker with the `all` profile, use this `docker-compose` command:

```bash
docker-compose --profile all up -d
```

#### 🏗️ Build

To `build` the entire solution, run this command in the root of the project:

```bash
dotnet build
```

#### 🏃🏻 Run Individual Bounded Contexts

Want to run a specific part of the business? Each bounded context can be run independently as a self-hosted API using the `dotnet run` command. Here are examples for each context:

```bash
# Run Orders BC
dotnet run --project "src/Order Management/Orders.Api/Orders.Api.csproj"

# Run Payments BC
dotnet run --project "src/Payment Processing/Payments.Api/Payments.Api.csproj"

# Run Shopping BC
dotnet run --project "src/Shopping/Shopping.Api/Shopping.Api.csproj"

# Run Inventory BC
dotnet run --project "src/Inventory Management/Inventory.Api/Inventory.Api.csproj"

# Run Fulfillment BC
dotnet run --project "src/Fulfillment Management/Fulfillment.Api/Fulfillment.Api.csproj"

# Run Customer Identity BC
dotnet run --project "src/Customer Identity/CustomerIdentity.Api/CustomerIdentity.Api.csproj"
```

Each BC exposes a Swagger UI at `/api` (e.g., `http://localhost:5000/api`).

#### 🧪 Test

To `test` the entire solution, run this command in the root of the project:

```bash
dotnet test
```

#### 💡 Run with Aspire <a id='5.3'></a>

To be implemented. It is on the roadmap.


## 🏫 Resources <a id='9.0'></a>

Blogs, articles, videos, and other resources will be listed here.

### Tools Used <a id='9.1'></a>

I stick with [JetBrains](https://www.jetbrains.com/)' suite of tools, such as their .NET specific IDE named [Rider](https://www.jetbrains.com/rider/), which is used exclusively with this project. I also use [DataGrip](https://www.jetbrains.com/datagrip/) from JetBrains when I need a dedicated window to database operations.

<img src="https://img.shields.io/badge/Rider-480C15?style=for-the-badge&logo=Rider&logoColor=white" alt="jetbrains rider">

<img src="https://img.shields.io/badge/DataGrip-2F0F3F?style=for-the-badge&logo=Rider&logoColor=white" alt="jetbrains datagrip">

## 👷‍♂️ Maintainer <a id='10.0'></a>

Erik "Faelor" Shafer

[<img src="https://img.shields.io/badge/LinkedIn-0077B5?style=for-the-badge&logo=linkedin&logoColor=white" />](https://www.linkedin.com/in/erikshafer/) [<img src="https://img.shields.io/badge/YouTube-FF0000?style=for-the-badge&logo=youtube&logoColor=white" />](https://www.youtube.com/@event-sourcing)

[![blog](https://img.shields.io/badge/blog-event--sourcing.dev-blue)](https://www.event-sourcing.dev/) [![Twitter Follow](https://img.shields.io/twitter/url?label=reach%20me%20%40Faelor&style=social&url=https%3A%2F%2Ftwitter.com%2Ffaelor)](https://twitter.com/faelor) ![Bluesky followers](https://img.shields.io/bluesky/followers/erikshafer.bsky.social) ![Twitch Status](https://img.shields.io/twitch/status/faelor)


- linkedin: [in/erikshafer](https://www.linkedin.com/in/erikshafer/)
- blog: [event-sourcing.dev](https://www.event-sourcing.dev)
- youtube: [@event-sourcing](https://www.youtube.com/@event-sourcing)
- bluesky: [erikshafer](https://bsky.app/profile/erikshafer.bsky.social)
