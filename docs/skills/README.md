# CritterSupply Skills Documentation

This directory contains implementation patterns and best practices for building with the Critter Stack (Wolverine + Marten).

## 📚 Quick Navigation

### By Use Case

**Starting from Scratch:**
- 🏗️ [Adding a New Bounded Context](./adding-new-bounded-context.md) - Complete checklist: projects, Docker, Postgres database, Aspire, CONTEXTS.md, tests

**Creating Commands & Handlers:**
- 🎯 [Wolverine Message Handlers](./wolverine-message-handlers.md) - Command/query handlers, HTTP endpoints, compound handlers, return patterns
- 🔄 [Wolverine Sagas](./wolverine-sagas.md) - Stateful orchestration sagas, multi-BC coordination, compensation chains, idempotency

**Working with Aggregates:**
- 📝 [Event-Sourced Aggregate Design](./marten-event-sourcing.md) - Event-sourced aggregates, decider pattern, factory methods
- 📦 [Marten Document Store](./marten-document-store.md) - Document models, CRUD patterns, value objects for queryable fields

**Traditional Relational Data:**
- 🗄️ [EF Core + Wolverine Integration](./efcore-wolverine-integration.md) - Entity models, DbContext setup, migrations, navigation properties
- 🔄 [EF Core Projections with Marten](./efcore-marten-projections.md) - EF Core as projection target for Marten event streams, Polecat/SQL Server support

**Building BFFs:**
- 🎁 [BFF Real-time Patterns](./bff-realtime-patterns.md) - View composition, HTTP clients, SignalR/SSE updates, Blazor integration, MudBlazor
- 📡 [Wolverine + SignalR](./wolverine-signalr.md) - Wolverine's native SignalR transport: hub setup, authentication (session & JWT), group routing, client integration, Marten side-effect pipeline

**Testing Your Code:**
- 🧪 [CritterStack Testing Patterns](./critterstack-testing-patterns.md) - Alba integration tests, pure function testing, TestFixture patterns
- 🐳 [TestContainers Integration Tests](./testcontainers-integration-tests.md) - Why real infrastructure, container setup, performance tips
- 🥒 [Reqnroll BDD Testing](./reqnroll-bdd-testing.md) - Gherkin features, step definitions, when to use BDD
- 🎭 [E2E Testing with Playwright](./e2e-playwright-testing.md) - Browser E2E tests, real Kestrel, POM, MudBlazor patterns

**Code Organization & Style:**
- 📂 [Vertical Slice Organization](./vertical-slice-organization.md) - File structure, colocation, project naming conventions
- 💎 [Modern C# Coding Standards](./modern-csharp-coding-standards.md) - Records, immutability, value objects, collection patterns

**Infrastructure & Setup:**
- 🏗️ [Adding a New Bounded Context](./adding-new-bounded-context.md) - Complete step-by-step guide: .NET projects, Docker, Postgres, Aspire, CONTEXTS.md, tests

**External Integrations:**
- 🔌 [External Service Integration](./external-service-integration.md) - Strategy pattern, stub vs production, graceful degradation

---

## 🛠️ By Technology

### Wolverine
- [Wolverine Message Handlers](./wolverine-message-handlers.md) - Command/query handling, HTTP endpoints
- [Wolverine Sagas](./wolverine-sagas.md) - Stateful orchestration, multi-BC coordination, compensation chains

### Marten
- [Event-Sourced Aggregate Design](./marten-event-sourcing.md) - Event-sourced aggregates, projections
- [Marten Document Store](./marten-document-store.md) - Document models, queries, value objects
- [EF Core Projections with Marten](./efcore-marten-projections.md) - Projecting events to relational tables via EF Core

### Entity Framework Core
- [EF Core + Wolverine Integration](./efcore-wolverine-integration.md) - Traditional relational persistence with Wolverine
- [EF Core Projections with Marten](./efcore-marten-projections.md) - EF Core as projection target, Polecat/SQL Server patterns

### Testing Frameworks
- [CritterStack Testing Patterns](./critterstack-testing-patterns.md) - Alba, Wolverine, Marten integration tests
- [TestContainers Integration Tests](./testcontainers-integration-tests.md) - Real infrastructure testing
- [Reqnroll BDD Testing](./reqnroll-bdd-testing.md) - Behavior-driven development with Gherkin
- [E2E Testing with Playwright](./e2e-playwright-testing.md) - Browser E2E with real Kestrel servers

### UI Frameworks
- [BFF Real-time Patterns](./bff-realtime-patterns.md) - Blazor Server, MudBlazor, SSE/SignalR
- [Wolverine + SignalR](./wolverine-signalr.md) - Wolverine's native SignalR transport, real-time hub patterns

---

## 🎯 By Development Phase

### Planning Phase
1. **New BC?** → Start with [Adding a New Bounded Context](./adding-new-bounded-context.md) for the full checklist
2. Review [Vertical Slice Organization](./vertical-slice-organization.md) for file structure
3. Write Gherkin features (see [Reqnroll BDD Testing](./reqnroll-bdd-testing.md))
4. Choose persistence strategy:
   - Event sourcing? → [Event-Sourced Aggregate Design](./marten-event-sourcing.md)
   - Document store? → [Marten Document Store](./marten-document-store.md)
   - Traditional relational? → [EF Core + Wolverine Integration](./efcore-wolverine-integration.md)
   - Event sourcing with relational projections? → [EF Core Projections with Marten](./efcore-marten-projections.md)
   - Multi-BC orchestration over time? → [Wolverine Sagas](./wolverine-sagas.md)

### Implementation Phase
1. Write handlers → [Wolverine Message Handlers](./wolverine-message-handlers.md)
2. Follow coding standards → [Modern C# Coding Standards](./modern-csharp-coding-standards.md)
3. Integrate external services? → [External Service Integration](./external-service-integration.md)
4. Building BFF? → [BFF Real-time Patterns](./bff-realtime-patterns.md)
5. Adding SignalR real-time? → [Wolverine + SignalR](./wolverine-signalr.md)

### Testing Phase
1. Write integration tests → [CritterStack Testing Patterns](./critterstack-testing-patterns.md)
2. Set up TestContainers → [TestContainers Integration Tests](./testcontainers-integration-tests.md)
3. Implement BDD scenarios → [Reqnroll BDD Testing](./reqnroll-bdd-testing.md)
4. Write browser E2E tests → [E2E Testing with Playwright](./e2e-playwright-testing.md)

---

## 🤖 How AI Agents Use Skill Files

This section answers the question: *"Does Copilot automatically read skill files, or do I need to reference them explicitly?"*

The short answer: **it depends on the tool.** Here's the full breakdown.

---

### What's Automatically Loaded vs. What's Not

| What | Auto-loaded? | Notes |
|------|-------------|-------|
| `CLAUDE.md` (root) | ✅ **Yes** | Configured as custom instructions for Claude-based agents; GitHub Copilot Coding Agent reads it automatically |
| Individual skill files (`docs/skills/*.md`) | ❌ **No** | Must be explicitly referenced or fetched on demand |
| `CONTEXTS.md` | ❌ **No** | Must be referenced explicitly (though CLAUDE.md instructs agents to check it first) |

**Key insight:** `CLAUDE.md` acts as the AI's *table of contents* and *behavioral guide*. It tells AI agents **when** to go read a specific skill file — but the agent only actually reads that file if it proactively fetches it or if you explicitly provide it.

---

### How Each AI Tool Handles Skills

#### GitHub Copilot Coding Agent (this agent, via GitHub Issues)

When you open an issue and assign it to Copilot, it:

1. Automatically reads `CLAUDE.md` as its custom instructions
2. Recognizes the "Skill Invocation Guide" section — a map of *when* to read which skill
3. **Proactively fetches** the relevant skill file(s) before implementing (e.g., reads `wolverine-sagas.md` before writing a saga)

**Bottom line:** For the Copilot Coding Agent, you **do not need to paste skill content** into issues. Simply describe your task. The agent will follow the `CLAUDE.md` guidance and read the appropriate skill files on its own.

> **Tip:** Write clear, specific issue descriptions. The agent decides which skills are relevant based on what you ask. If you want it to apply a particular skill, mention it: *"Use the saga pattern from `docs/skills/wolverine-sagas.md`"*.

---

#### GitHub Copilot Chat (IDE — VS Code, Visual Studio, Rider)

Copilot Chat does **not** automatically read any files in your repo. It only sees:
- Files you have currently open in the editor
- Files or symbols you explicitly reference

**How to use skill files in Copilot Chat:**

```
// Reference a skill file directly
#file:docs/skills/wolverine-sagas.md
How do I write a saga that coordinates the Orders and Payments bounded contexts?
```

```
// Reference multiple files
#file:docs/skills/wolverine-message-handlers.md #file:docs/skills/marten-event-sourcing.md
Add an event-sourced aggregate with a Wolverine handler for creating a new Shipment.
```

> **Best practice:** Open the relevant skill file in a tab before chatting, then use `#file:` to include it. Copilot Chat respects these references within a single conversation.

---

#### Claude Desktop / Claude.ai (Project-based)

When using Claude with a **Project**:

1. Add `CLAUDE.md` content to the Project's *Instructions* (system prompt)
2. Individual skill files can be uploaded as **Project Knowledge** documents
3. Once in Project Knowledge, they are available to Claude throughout all conversations in that project

**How to set up:**

1. Copy the contents of `CLAUDE.md` into your Project's instructions
2. Upload skill files you use frequently to Project Knowledge (drag-and-drop `.md` files)
3. Now Claude will reference them automatically within that project

> **Without a Project:** Paste the relevant skill file content directly into your conversation. Copy-pasting is fully effective — the content is what matters, not the file path.

---

#### Cursor / Windsurf / Other AI-first IDEs

Most AI-first IDEs support a global "rules" or "instructions" file (e.g., `.cursorrules`, `.windsurfrules`). These are loaded automatically for every session.

**Recommended setup:**

1. Point your IDE's rules file at `CLAUDE.md` or copy its contents into your rules file
2. Use the IDE's `@file` or `@docs` mention syntax to reference individual skill files when needed

---

### Practical Workflow Guide

**Scenario 1: You're using Copilot Coding Agent (GitHub Issues)**

✅ Just write a clear issue. The agent reads `CLAUDE.md` automatically and fetches relevant skills.

Example issue description:
> Implement a Wolverine saga for coordinating order placement across the Orders and Payments BCs. The saga should handle payment authorization and timeout after 10 minutes.

The agent will automatically read `wolverine-sagas.md` before implementing.

---

**Scenario 2: You're using Copilot Chat in your IDE**

Reference the skill file(s) explicitly at the start of your prompt:

```
#file:docs/skills/wolverine-sagas.md

Help me implement a saga for order placement that waits for payment confirmation.
```

---

**Scenario 3: You're using Claude or another chat-based AI tool**

Option A — Copy and paste the skill file content into your prompt:
> Here is our project's saga pattern guide: [paste contents of wolverine-sagas.md]
> Now help me implement a saga for...

Option B — Use a Claude Project with skill files uploaded as Project Knowledge (best for recurring work).

Option C — Simply describe what you need and mention "follow CritterSupply conventions." If the AI doesn't know the conventions, paste the relevant skill file content.

---

### Why This Design?

Skill files are kept **separate from `CLAUDE.md`** intentionally:

- **Context window efficiency**: Loading all skills upfront would waste ~50k tokens of context on every prompt. Skills are fetched only when needed.
- **Modular updates**: Each skill file can evolve independently without changing the main AI config.
- **Human readability**: Skill files read as standalone guides — useful for developers too, not just AI agents.

`CLAUDE.md` is the **index** (always loaded). Skill files are the **chapters** (loaded on demand).

---

### TL;DR

| Situation | What to do |
|-----------|-----------|
| Using Copilot Coding Agent (GitHub Issues) | Write a clear issue — agent reads skills automatically |
| Using Copilot Chat in IDE | Use `#file:docs/skills/your-skill.md` in your prompt |
| Using Claude Desktop (with Project) | Upload skill files to Project Knowledge |
| Using any chat AI ad-hoc | Copy-paste the relevant skill file content into your prompt |
| Not sure which skill applies | Check the [Skill Invocation Guide in CLAUDE.md](../CLAUDE.md#skill-invocation-guide) |

---

## 🔗 Related Documentation

- **[CLAUDE.md](../CLAUDE.md)** - AI development guidelines and skill invocation guide
- **[CONTEXTS.md](../CONTEXTS.md)** - Bounded context specifications (architectural source of truth)
- **[docs/decisions/](../docs/decisions/)** - Architectural Decision Records (ADRs)
- **[docs/planning/](../docs/planning/)** - Development cycles and roadmap
- **[docs/features/](../docs/features/)** - BDD feature specifications

---

## 📝 Document Conventions

Each skill document follows this structure:

1. **When to Use** - Clear guidance on when this skill applies
2. **Core Concepts** - Key patterns and principles
3. **Examples** - Code samples with explanations
4. **Common Pitfalls** - What to avoid
5. **Testing** - How to test the pattern
6. **See Also** - Links to related skills

---

## 🆘 Getting Help

**Can't find what you need?**

1. Check the [skill invocation guide in CLAUDE.md](../CLAUDE.md#skill-invocation-guide) for AI-specific guidance
2. Review [CONTEXTS.md](../CONTEXTS.md) for bounded context integration patterns
3. Search for ADRs in [docs/decisions/](../docs/decisions/) for architectural decisions

**Common Questions:**

- *"How do I build a real-time hub with SignalR and Wolverine?"* → [Wolverine + SignalR](./wolverine-signalr.md)
- *"How do I create a new command handler?"* → [Wolverine Message Handlers](./wolverine-message-handlers.md)
- *"How do I test my handler?"* → [CritterStack Testing Patterns](./critterstack-testing-patterns.md)
- *"Should I use event sourcing or document store?"* → Compare [Event-Sourced Aggregate Design](./marten-event-sourcing.md) vs [Marten Document Store](./marten-document-store.md)
- *"How do I build a saga that coordinates multiple BCs?"* → [Wolverine Sagas](./wolverine-sagas.md)
- *"How do I organize my code?"* → [Vertical Slice Organization](./vertical-slice-organization.md)
- *"When should I use EF Core?"* → [EF Core + Wolverine Integration](./efcore-wolverine-integration.md)
- *"How do I add a new bounded context?"* → [Adding a New Bounded Context](./adding-new-bounded-context.md)

---

**Last Updated:** 2026-03-09
