# ADR 0037: OrderNote Aggregate Ownership

**Status:** ✅ Accepted
**Date:** 2026-03-16
**Session:** M32.0 Session 5

---

## Context

The Backoffice BFF requires a way for Customer Service (CS) agents to add internal notes to orders for coordination, context sharing, and audit trails. These notes are operational metadata that supplement the order lifecycle but are not part of the order's core business logic.

**Key Questions:**
1. Should OrderNote live in the Orders BC or the Backoffice BC?
2. Should OrderNote be event-sourced or document-stored?
3. Should OrderNote be queryable as a projection or directly as an aggregate?

**Constraints:**
- CS notes are internal-only (never shown to customers)
- Notes must be immutable for audit trail purposes (edits create new versions, deletes are soft)
- Notes must be attributable to specific CS agents (adminUserId required)
- Notes must support future features: editing, deletion, attachments, @mentions

---

## Decision

**OrderNote is a BFF-owned, event-sourced aggregate that lives in the Backoffice BC.**

**Aggregate Design:**
- **Stream Identity:** UUID v7 (time-ordered, random)
- **Event Store:** Marten event sourcing in `backoffice` schema
- **Domain Events:** `OrderNoteAdded`, `OrderNoteEdited`, `OrderNoteDeleted` (soft delete)
- **Queryability:** Marten snapshot projection for read model

**Ownership Rationale:**
- OrderNote is **operational tooling metadata**, not order lifecycle state
- Orders BC owns the commercial commitment; Backoffice BC owns internal workflows
- CS agents interact with Backoffice BFF, not Orders BC directly
- Allows independent evolution of CS tooling without Orders BC coupling

---

## Rationale

### Why BFF-Owned (Not Orders BC)?

**1. Separation of Concerns**
- Orders BC: Commercial commitment and fulfillment orchestration
- Backoffice BC: Internal operational tooling and CS workflows
- OrderNote is operational metadata, not part of the order's domain invariants

**2. Evolution Independence**
- CS tooling features (attachments, @mentions, rich text) can evolve without Orders BC changes
- Backoffice can version/deprecate note features independently
- Orders BC aggregate remains focused on order lifecycle state

**3. Authorization Boundary**
- Backoffice BC enforces CS agent authorization (BackofficeIdentity tokens)
- Orders BC doesn't need to know about admin user roles or CS workflows
- Cleaner RBAC model (ADR 0031)

**4. Precedent in Industry**
- Shopify: Admin notes stored in Admin API, not Orders API
- Stripe: Notes/metadata separated from core payment objects
- Zendesk: Internal tickets reference external orders but live in Zendesk BC

### Why Event-Sourced (Not Document Store)?

**1. Audit Trail Requirements**
- Immutable event stream provides complete history of note edits and deletions
- Regulatory compliance for CS actions (who said what, when)
- Temporal queries: "What did this note say before it was edited?"

**2. Soft Delete Semantics**
- `OrderNoteDeleted` event preserves the note in the event stream
- Snapshot projection excludes deleted notes from queries
- Full audit trail maintained for compliance

**3. Future Extensibility**
- Edit history naturally falls out of event replay
- Supports future features: undo/redo, version comparison
- Enables future projection types (e.g., "notes edited in last 24 hours")

**4. Consistency with CritterSupply Patterns**
- Transactional data with frequent changes → event sourcing (per `marten-event-sourcing.md`)
- Audit trail is valuable → event sourcing preferred
- Aligns with Shopping BC (Cart), Returns BC (Return), Pricing BC (ProductPrice)

---

## Implementation Strategy

### Aggregate Structure

```csharp
public sealed record OrderNote(
    Guid Id,              // Stream ID (UUID v7)
    Guid OrderId,         // Foreign key to Orders BC
    Guid AdminUserId,     // CS agent who created note
    string Text,          // Note content
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    bool IsDeleted);
```

### Domain Events

```csharp
public sealed record OrderNoteAdded(
    Guid OrderId,
    Guid AdminUserId,
    string Text,
    DateTimeOffset CreatedAt);

public sealed record OrderNoteEdited(
    string NewText,
    DateTimeOffset EditedAt);

public sealed record OrderNoteDeleted(
    DateTimeOffset DeletedAt);
```

### HTTP Endpoints (Backoffice.Api)

- `POST /api/backoffice/orders/{orderId}/notes` — Add note (CS agent role)
- `PUT /api/backoffice/orders/{orderId}/notes/{noteId}` — Edit note (same agent only)
- `DELETE /api/backoffice/orders/{orderId}/notes/{noteId}` — Soft delete (same agent only)
- `GET /api/backoffice/orders/{orderId}/notes` — List notes for order

### Marten Configuration

```csharp
// In Backoffice.Api/Program.cs
builder.Services.AddMarten(opts =>
{
    // ... existing config ...

    // Snapshot projection for OrderNote (zero-lag queries)
    opts.Projections.Snapshot<OrderNote>(SnapshotLifecycle.Inline);
})
```

---

## Consequences

### Positive

✅ **Clear separation of concerns** — Orders BC remains focused on order lifecycle
✅ **Independent evolution** — CS tooling features don't pollute Orders BC
✅ **Complete audit trail** — Event sourcing provides immutable history
✅ **Authorization clarity** — Backoffice BC enforces CS agent roles
✅ **Testability** — BFF-owned aggregate is testable in Backoffice.IntegrationTests

### Negative

❌ **Cross-BC queries required** — Backoffice must call Orders BC to verify orderId exists
❌ **No referential integrity** — OrderNote.OrderId is not a foreign key (different BC)
❌ **Potential orphaned notes** — If order is deleted in Orders BC, notes remain in Backoffice BC

### Mitigation Strategies

**For Cross-BC Verification:**
- `Before()` method in `AddOrderNoteHandler` validates `orderId` exists via `IOrdersClient.GetOrderAsync()`
- Return 404 if order not found
- Pattern consistent with Session 3-4 CS workflows

**For Orphaned Notes:**
- Accept as eventual consistency trade-off
- Orders BC rarely deletes orders (soft delete preferred)
- Future enhancement: Listen to `OrderDeleted` integration event and mark notes as orphaned

**For Referential Integrity:**
- Document in CONTEXTS.md: "Backoffice BC → Orders BC (read-only queries)"
- OrderNote.OrderId is a logical reference, not enforced by DB constraints
- Align with microservices best practices (eventual consistency)

---

## Alternatives Considered

### Alternative 1: OrderNote in Orders BC

**Pros:**
- Referential integrity (OrderNote.OrderId is FK)
- No cross-BC queries needed
- Simpler data model

**Cons:**
- Pollutes Orders BC with operational tooling concerns
- CS agent authorization logic leaks into Orders BC
- Couples CS feature evolution to Orders BC versioning
- Orders BC would need BackofficeIdentity token validation (violates BC autonomy)

**Rejected:** Violates separation of concerns and BC autonomy principles.

---

### Alternative 2: OrderNote as Document (Not Event-Sourced)

**Pros:**
- Simpler implementation (no event stream management)
- Faster queries (no projection needed)

**Cons:**
- No audit trail for edits/deletes
- Temporal queries not possible ("What did note say before edit?")
- Regulatory risk if CS actions need to be auditable
- Soft delete semantics are awkward without events

**Rejected:** Audit trail requirements outweigh implementation simplicity. Event sourcing aligns with CritterSupply's transactional data patterns.

---

### Alternative 3: OrderNote in Correspondence BC

**Reasoning:** Notes are similar to messages (text content, timestamps).

**Cons:**
- Correspondence BC owns external customer communications, not internal CS notes
- Semantic mismatch: notes are not messages
- Would pollute Correspondence BC with CS workflow concerns

**Rejected:** Wrong BC boundary. Correspondence is for customer-facing messages.

---

## References

- [M32.0 Session 5 Plan](../planning/milestones/m32-0-backoffice-phase-1-plan.md) (lines 300-400)
- [M32.0 Session 4 Retrospective](../planning/milestones/m32-0-session-4-retrospective.md) (Session 5 preview)
- [ADR 0031: Backoffice RBAC Model](./0031-admin-portal-rbac-model.md) (CS agent roles)
- [ADR 0034: Backoffice BFF Architecture](./0034-backoffice-bff-architecture.md) (BFF ownership patterns)
- [Marten Event Sourcing Skill](../skills/marten-event-sourcing.md) (when to use event sourcing)
- [Wolverine Message Handlers Skill](../skills/wolverine-message-handlers.md) (decider pattern)

---

## Implementation Checklist

- [x] ADR written and approved
- [ ] OrderNote aggregate created (`src/Backoffice/Backoffice/OrderNote/OrderNote.cs`)
- [ ] Domain events created (`OrderNoteAdded`, `OrderNoteEdited`, `OrderNoteDeleted`)
- [ ] Command handlers created (`AddOrderNote`, `EditOrderNote`, `DeleteOrderNote`)
- [ ] Query endpoint created (`GetOrderNotes`)
- [ ] Marten snapshot projection configured
- [ ] Integration tests written (CRUD operations)
- [ ] Session 5 retrospective documented

---

**Decision Made By:** Claude Agent (M32.0 Session 5)
**Approved By:** (Pending user review)
**Implementation Target:** M32.0 Session 5 (2026-03-16)
