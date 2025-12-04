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

### 🤖 AI-assisted Development <a id='1.3'></a>

This project is built with Claude as a collaborative coding partner. Beyond just generating code, it's an exercise in teaching AI tools to think in event-driven patterns and leverage the Critter Stack idiomatically—helping to improve the guidance these tools can offer the broader community.

That is to say, the more these tools see well-structured examples, the better guidance they can offer developers exploring these approaches for the first time.

See [CLAUDE.md](./CLAUDE.md) for the project-specific instructions Claude follows when working on this codebase.

#### 🚫 Thinking Machines <a id='1.3.1'></a>
Who knows. Maybe one day we'll ban "thinking machines" and have to build everything ourselves again. 😉 (see: Dune, Warhammer 40k, Battlestar Galactica, Mass Effect, and others)

## 🗺️ Bounded Contexts <a id='2.0'></a>

CritterSupply is organized into bounded contexts. As described in Domain-Driven Design, bounded contexts help lower the cost of consensus. If one is unfamiliar with the concept, a crude yet simple way of picturing it is that each context could have its own team in an organization. That's not a rule by any means, but hopefully that helps you paint a picture of how CritterSupply is divided up logically and physically in this repo.

Below is a table of each contexts' focused responsibilities, along with their current implementation status:

| Context            | Responsibility                  | Status         |
|--------------------|---------------------------------|----------------|
| 📨 **Orders**      | Order lifecycle and history     | 🛠️ Scaffolded |
| 💳 **Payments**    | Authorization, capture, refunds | 🛠️ Scaffolded |
| 🛒 **Shopping**    | Cart management and checkout    | 🔜 Planned     |
| 📊 **Inventory**   | Stock levels and reservations   | 🔜 Planned     |
| 📦 **Catalog**     | Product definitions and pricing | 🔜 Planned     |
| 🚚 **Fulfillment** | Picking, packing, shipping      | 🔜 Planned     |
| 👤 **Customers**   | Profiles and preferences        | 🔜 Planned     |

For detailed responsibilities, interactions, and event flows between contexts, see [CONTEXTS.md](./CONTEXTS.md).

## ⏩ Getting Started <a id='5.0'></a>

This software solution has multiple dependencies that need to be running locally.

- [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Docker Desktop](https://docs.docker.com/engine/install/)

### 🛠️ Local Development <a id='5.1'></a>
 
To launch Docker with the `all` profile, use this `docker-compose` command:

```bash
docker-compose --profile all up -d
```

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
