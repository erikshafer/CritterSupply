# ADR 0042: `catalog:` Namespace UUID v5 Convention

**Status:** ⚠️ Proposed (Convention Documented, Implementation Deferred)

**Date:** 2026-03-29

**Context:**

The Product Catalog BC uses `Guid.NewGuid()` (random UUID v4) for Marten event stream IDs. This means there is no deterministic relationship between a product's SKU and its stream ID — the mapping is maintained via the `ProductCatalogView` inline projection.

The original M35.0 execution plan proposed using UUID v5 namespace hashing with a `catalog:` prefix to create deterministic stream IDs from SKUs. This convention was not implemented during M35.0 (see ADR 0041) but is documented here for future reference.

**Decision:**

Document the UUID v5 namespace convention for potential future adoption:

### The Convention

- **Namespace:** A well-known UUID namespace specific to Product Catalog (e.g., `6ba7b810-9dad-11d1-80b4-00c04fd430c8` — the URL namespace from RFC 4122)
- **Name format:** `catalog:{sku}` (e.g., `catalog:DOG-BOWL-001`)
- **Algorithm:** `Guid streamId = UuidV5.Create(CatalogNamespace, $"catalog:{sku}")`
- **Result:** Deterministic, repeatable stream ID derived from the SKU

### Benefits

1. **Idempotent stream creation:** `StartStream` with the same SKU always targets the same stream, enabling safe retries
2. **Direct stream lookup:** Read handlers can compute the stream ID from the SKU without querying the projection
3. **Cross-BC reference:** Other BCs that know a SKU can compute the stream ID independently (though they should not access the Product Catalog's event store directly)

### Current State

The implementation uses `Guid.NewGuid()` with projection-based SKU → stream ID mapping. This works correctly and all tests pass. The convention is documented here so it can be adopted when:
- A migration script is written to rewrite existing stream IDs
- The handler pattern is updated to compute stream IDs deterministically
- A UUID v5 utility library is added to the shared infrastructure

**Rationale:**

Recording this convention now ensures the naming pattern is consistent if/when multiple BCs adopt UUID v5 stream IDs. The `catalog:` prefix prevents namespace collisions between BCs that might share identifier spaces (e.g., a SKU in Product Catalog vs. a SKU reference in Listings BC).

**Consequences:**

- No code changes required — this is a documentation-only ADR
- Future adoption would require a data migration for existing streams
- The convention is available for Listings BC and other new BCs to adopt from the start if desired

**Alternatives Considered:**

1. **No namespace prefix:** Just hash the SKU directly. Rejected because it could collide with other BCs hashing the same SKU.
2. **Implement immediately:** Rejected because the current implementation works and migration adds risk without immediate benefit.
3. **Sequential integer IDs:** Rejected because Marten event streams use GUIDs natively.

**References:**
- ADR 0041 — Product Catalog ES Migration Decisions (explains why UUID v5 was deferred)
- `docs/planning/milestones/m36-1-phase-0-reconciliation.md` — Task 0.2 audit
- RFC 4122 — UUID v5 specification
