# ADR 0002: EF Core for Customer Identity BC

**Status:** ✅ Accepted

**Date:** 2026-01-19

**Cycle:** 13

---

## Context

CritterSupply's initial Customer Identity BC used Marten (document store / event sourcing) for customer data persistence, consistent with the rest of the system. As the system matured, the team evaluated whether Marten is the right fit for Customer Identity specifically.

Customer data has several characteristics that differ from event-sourced BCs:
- **Current state only:** Customer service workflows query current customer state (address, email, preferences) — the history of changes is rarely needed
- **Relational structure:** `Customer` has a one-to-many relationship with `CustomerAddress` entities with foreign key semantics, cascade deletes, and referential integrity
- **Standard CRUD:** Customer management (register, update address, deactivate) is straightforward CRUD, not a complex workflow requiring event sourcing
- **Pedagogical value:** CritterSupply serves as a reference architecture. Demonstrating that Wolverine works with existing EF Core codebases (not just Marten) is valuable for teams adopting Wolverine incrementally

---

## Decision

Migrate Customer Identity BC from Marten to Entity Framework Core (EF Core) with PostgreSQL (Npgsql provider).

- **ORM:** EF Core 9+
- **Provider:** `Npgsql.EntityFrameworkCore.PostgreSQL`
- **Schema management:** EF Core migrations
- **Connection string key:** `"postgres"` (distinct from Marten BCs which use `"marten"`)

---

## Rationale

- **Relational model is a natural fit:** `Customer` → `CustomerAddress` is a classic one-to-many relationship with referential integrity. EF Core's fluent API expresses this cleanly; Marten requires workarounds.
- **No need for event history:** Customer BC queries current state. Event sourcing's audit trail capability is not required here.
- **EF Core migration story:** Schema evolution via EF Core migrations is more familiar for teams coming from traditional .NET backgrounds, reinforcing CritterSupply's reference architecture value.
- **Wolverine compatibility:** Wolverine's `UseEntityFrameworkCoreTransactions()` integration provides the same transactional inbox/outbox guarantees as Marten — no loss of messaging reliability.

---

## Consequences

**Positive:**
- Clean entity model with navigation properties (`Customer.Addresses`)
- EF Core migrations enable schema evolution without Marten document store overhead
- Demonstrates Wolverine works seamlessly with EF Core (pedagogical value)
- Referential integrity enforced by the database

**Negative:**
- Two ORM patterns in the solution (Marten for most BCs, EF Core for Customer Identity)
- Team must understand both patterns when working across BCs
- EF Core migration management adds operational overhead not present with Marten

---

## Alternatives Considered

**Keep Marten:** Rejected because Marten's document model doesn't naturally represent relational Customer → Address entities. Queryable nested structures require workarounds.

**Use Dapper/raw SQL:** Rejected as too low-level; EF Core provides the right abstraction level for this use case.

---

## References

- Cycle 13 retrospective: `docs/planning/cycles/cycle-13-customer-identity-ef-core.md`
- [ADR 0012: Simple Session-Based Authentication](./0012-simple-session-based-authentication.md) — Cookie auth choice for Customer Identity
- Skill guide: `docs/skills/efcore-wolverine-integration.md`
- Workflow doc: `docs/workflows/customer-identity-workflows.md`
