# ADR 0003: Value Objects vs Primitives for Queryable Fields

**Status:** ✅ Accepted

**Date:** 2026-02-02

**Cycle:** 14

---

## Context

CritterSupply uses Marten as its primary document store for most BCs. Marten supports LINQ queries against document properties, which works well for primitive types (`string`, `int`, `Guid`) but has limitations with custom value object types that wrap those primitives.

During Cycle 14 (Product Catalog BC), the team initially modeled `Category` as a value object (`ProductCategory`) with a `Value` string property, following domain-driven design principles. This resulted in 19 out of 24 integration tests failing because Marten's LINQ provider could not translate `p.Category.Value == "Dogs"` into a valid SQL expression.

The team needed a clear decision rule: when to use value objects vs. primitive types on Marten document models.

---

## Decision

**Rule:** Use primitive types (`string`, `int`, `decimal`, etc.) for document fields that will be used in Marten LINQ queries (filtering, sorting, full-text search). Use value objects for complex structures that require business rules, validation, or special equality semantics — but only when those fields are NOT queried via LINQ.

**Applied to Product Catalog:**
- `Category` → `string` (queryable: filter products by category)
- `Sku` → `Sku` value object with JSON converter (not queried via LINQ; carries SKU format validation)
- `ProductName` → `ProductName` value object with JSON converter (not queried via LINQ in most scenarios)

---

## Rationale

- **Marten LINQ limitation:** Marten's LINQ provider translates expressions to JSONB path queries in PostgreSQL. Custom wrapper types (e.g., `record ProductCategory(string Value)`) add an intermediate property access (`Category.Value`) that Marten cannot translate at the document level.
- **Value objects still valid:** Value objects remain appropriate for fields with complex behavior (format validation, equality, JSON serialization rules) that are not LINQ-filtered. The `Sku` value object, for example, enforces SKU format while being stored as a plain string in JSONB.
- **JSON converters bridge the gap:** Value objects with proper `System.Text.Json` converters serialize to their primitive representation, enabling Marten to store and retrieve them correctly — but LINQ query translation still fails on properties wrapped in custom types.

---

## Consequences

**Positive:**
- Marten LINQ queries work correctly for all filterable fields
- Value objects are still used where they provide business value (validation, equality)
- Clear, actionable decision rule for future Marten document models

**Negative:**
- Some fields that are conceptually "domain types" are represented as primitives in the model
- Developers must remember this constraint when designing new Marten documents
- The rule requires developers to anticipate which fields will be queried at design time

---

## Alternatives Considered

**Custom LINQ provider extensions:** Investigated but not feasible without deep Marten internals work. The Marten team would need to add support for translating custom wrapper types.

**Always use primitives:** Too restrictive. Value objects provide genuine value for complex fields (validation, JSON converter, equality semantics). The constraint is specific to LINQ-queried fields.

**Computed columns / indexed expressions:** Marten supports duplicate field storage (`mt_doc` level) for indexing. Could store both the value object AND a primitive duplicate field. Rejected as over-engineered for current needs.

---

## References

- Cycle 14 retrospective: `docs/planning/cycles/cycle-14-product-catalog-core.md`
- [ADR 0018: Money Value Object as Canonical Currency](./0018-money-value-object-canonical-currency.md) — example of value object with JSON converter
- Skill guide: `docs/skills/modern-csharp-coding-standards.md` — value object patterns
