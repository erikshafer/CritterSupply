# ADR 0019: Bulk Pricing Job Audit Trail via Event Sourcing

**Status:** ✅ Accepted

> **Note:** "Admin Portal" was renamed to "Backoffice" and "Admin Identity" to "BackofficeIdentity" in [ADR 0033](./0033-admin-portal-to-backoffice-rename.md).

**Date:** 2026-03-08

**Context:** Pricing BC Phase 2+ — How do we ensure bulk pricing job approvals are auditable?

---

## Context

Bulk pricing jobs allow merchandising managers to update prices for large SKU sets (e.g., 500 SKUs in a "Back to School" campaign). Jobs exceeding 100 SKUs require explicit approval before execution.

**Approval workflow:**
1. User submits bulk job (`SubmitBulkPricingJob` command)
2. If `TotalSkuCount > 100` → job enters `AwaitingApproval` state
3. Merchandising Manager reviews job
4. Manager approves (`ApproveBulkPricingJob`) or rejects (`RejectBulkPricingJob`)
5. On approval → job transitions to `Processing`, executes price changes

**Audit requirement (non-negotiable):**
- WHO approved/rejected the job (manager identity)
- WHEN the decision was made (timestamp)
- WHY the decision was made (rejection reason if rejected)
- Decision must be **immutable** and **tamper-proof**

This ADR decides how to persist the approval audit trail.

---

## Decision

**Use event-sourced saga with durable approval events. Audit trail is the event stream itself.**

### BulkPricingJob Saga

**Pattern:** Wolverine saga (mutable class : Saga)

**Stream:** One event stream per bulk job (`BulkPricingJobId`)

**Events (Approval/Rejection):**
```csharp
public sealed record BulkJobApproved(
    Guid JobId,
    Guid ApprovedBy,           // ← Manager identity (Guid)
    DateTimeOffset ApprovedAt); // ← Decision timestamp

public sealed record BulkJobRejected(
    Guid JobId,
    string RejectionReason,    // ← Required justification
    Guid RejectedBy,           // ← Manager identity
    DateTimeOffset RejectedAt);
```

**Persistence:** Marten event store (`pricing.mt_events` table)

**Audit Query:**
```csharp
// Read raw events from stream
var events = await session.Events.FetchStreamAsync(bulkJobId);
var approvalEvent = events.OfType<BulkJobApproved>().FirstOrDefault();

// Extract audit fields
var approver = approvalEvent.ApprovedBy;
var approvedAt = approvalEvent.ApprovedAt;
```

---

## Rationale

### Why Event Stream as Audit Trail?

**1. Append-Only Immutability**

Event streams in Marten are **append-only**. Once `BulkJobApproved` is written, it cannot be modified or deleted (without database-level tampering, which leaves forensic traces).

**2. Temporal Ordering**

Event stream preserves **exact order** of events. Audit query reconstructs who did what when:

```
Stream: bulk-job-12345
1. BulkJobSubmitted { SubmittedBy: alice@critter.test, TotalSkuCount: 250 }
2. BulkJobSubmittedForApproval { SubmittedAt: 2026-03-08T10:00:00Z }
3. BulkJobApproved { ApprovedBy: bob@critter.test, ApprovedAt: 2026-03-08T10:15:32Z }
4. BulkJobStarted { StartedAt: 2026-03-08T10:15:33Z }
5. BulkJobItemProcessed { Sku: "DOG-FOOD-5LB", ProcessedAt: 2026-03-08T10:15:34Z }
... (248 more BulkJobItemProcessed events)
250. BulkJobCompleted { ProcessedCount: 250, CompletedAt: 2026-03-08T10:17:12Z }
```

Audit report: *"Bob approved 250-SKU job at 10:15:32 AM. Job completed at 10:17:12 AM (1m40s). Submitted by Alice at 10:00 AM."*

**3. No Separate Audit Table**

Event stream **is** the audit trail. No need for:
- Separate `BulkApprovalRecord` table
- Separate `BulkJobAuditLog` table
- Dual writes (saga state + audit record)

One write (event append) satisfies both state management and audit requirements.

**4. Leverages Existing Infrastructure**

Marten event store is already configured for Pricing BC. No new storage mechanism required.

**5. Audit Query Simplicity**

```csharp
// Backoffice: "Show me all approvals by Bob in March 2026"
var events = await session.Events.QueryRawEventDataOnly<BulkJobApproved>()
    .Where(e => e.Data.ApprovedBy == bobId)
    .Where(e => e.Timestamp >= new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero))
    .Where(e => e.Timestamp < new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero))
    .ToListAsync();
```

---

## Consequences

### Positive

✅ **Immutable:** Event stream is append-only (tamper-proof)
✅ **Simple:** No separate audit table, dual writes, or sync logic
✅ **Leverage existing infra:** Marten event store already configured
✅ **Temporal ordering:** Event stream preserves exact sequence of actions
✅ **Single source of truth:** Saga state AND audit trail in one stream

### Negative

⚠️ **Audit query requires event stream read**

**Pattern:**
```csharp
var events = await session.Events.FetchStreamAsync(jobId);
var approvalEvent = events.OfType<BulkJobApproved>().FirstOrDefault();
```

**Not a traditional document query:**
```csharp
// ❌ Cannot do this
var approval = await session.LoadAsync<BulkApprovalRecord>(jobId);
```

**Mitigation (if query performance becomes an issue in Phase 3+):**
- Create async projection: `BulkJobApprovalLogProjection` → `BulkApprovalRecord` document
- Projection listens to `BulkJobApproved`, `BulkJobRejected` events
- Backoffice queries document (fast) instead of event stream (slower)

**Phase 2 decision:** Event stream query is sufficient. Defer projection until query performance degrades.

---

## Alternatives Considered

### Alternative 1: Separate BulkApprovalRecord Document

**Pattern:** Dual write — saga event + explicit document

```csharp
public sealed class ApproveBulkPricingJobHandler
{
    public async Task Handle(ApproveBulkPricingJob command, IDocumentSession session)
    {
        // Write 1: Saga event
        var saga = await session.LoadAsync<BulkPricingJob>(command.JobId);
        saga.Approve(command.ApprovedBy);

        // Write 2: Audit record
        var auditRecord = new BulkApprovalRecord(
            command.JobId,
            command.ApprovedBy,
            DateTimeOffset.UtcNow);
        session.Store(auditRecord);

        await session.SaveChangesAsync();
    }
}
```

**Pros:**
- ✅ Audit query is simple document load (`session.LoadAsync<BulkApprovalRecord>()`)
- ✅ No event stream read required

**Cons:**
- ❌ **Dual write:** Two writes in same transaction (saga event + audit document)
- ❌ **Redundant storage:** Audit data duplicated (event stream + document)
- ❌ **Potential inconsistency:** If saga event succeeds but document write fails, audit trail incomplete
- ❌ **More code:** Separate document model, projection logic, storage configuration

**Why rejected:** Adds complexity without significant benefit. Event stream **already** provides durable audit trail. Dual write is redundant.

---

### Alternative 2: Saga as Document (Not Event-Sourced)

**Pattern:** Store saga state as mutable document (not event stream)

```csharp
public sealed record BulkPricingJob(
    Guid Id,
    BulkJobStatus Status,
    Guid? ApprovedBy,          // ← Nullable, set on approval
    DateTimeOffset? ApprovedAt);
```

**Pros:**
- ✅ Simple audit query (`session.LoadAsync<BulkPricingJob>()`)
- ✅ No event stream required

**Cons:**
- ❌ **Mutable:** Approval fields can be overwritten (not tamper-proof)
- ❌ **No temporal history:** Cannot reconstruct "when did approval happen relative to submission?"
- ❌ **No ordering guarantees:** If multiple managers try to approve concurrently, last write wins (no conflict detection)
- ❌ **Loses saga orchestration benefits:** Wolverine saga pattern designed for event-sourced sagas

**Why rejected:** Mutability defeats audit trail purpose. Event sourcing provides immutability and temporal ordering guarantees.

---

### Alternative 3: External Audit Service

**Pattern:** Publish `BulkJobApprovalLogged` integration event → External audit service stores in separate database

**Pros:**
- ✅ Centralized audit trail for all BCs (not just Pricing)
- ✅ Separate compliance-focused storage (e.g., write-once-read-many DB)

**Cons:**
- ❌ **Added complexity:** New service, new database, new failure mode
- ❌ **Eventual consistency:** Audit log may lag behind saga state
- ❌ **Not required for reference architecture:** CritterSupply demonstrates domain patterns, not enterprise compliance infrastructure

**Why rejected:** Over-engineering for reference architecture goals. Event stream audit trail is sufficient. External audit service can be added later if compliance requirements demand it.

---

## Implementation

### Saga Structure

```csharp
public sealed class BulkPricingJob : Saga
{
    public Guid Id { get; set; }  // Wolverine saga correlation ID
    public BulkJobStatus Status { get; set; }
    public int TotalSkuCount { get; set; }
    public int ProcessedCount { get; set; }
    public int FailedCount { get; set; }

    // Audit fields (set by events)
    public Guid? SubmittedBy { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? RejectedBy { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }

    // Event handlers (mutate state)
    public void Handle(BulkJobSubmitted e)
    {
        Status = e.TotalSkuCount > 100 ? BulkJobStatus.AwaitingApproval : BulkJobStatus.Processing;
        TotalSkuCount = e.TotalSkuCount;
        SubmittedBy = e.SubmittedBy;
        SubmittedAt = e.SubmittedAt;
    }

    public void Handle(BulkJobApproved e)
    {
        Status = BulkJobStatus.Processing;
        ApprovedBy = e.ApprovedBy;       // ← Audit field set
        ApprovedAt = e.ApprovedAt;       // ← Audit field set
    }

    public void Handle(BulkJobRejected e)
    {
        Status = BulkJobStatus.Rejected;
        RejectedBy = e.RejectedBy;       // ← Audit field set
        RejectedAt = e.RejectedAt;       // ← Audit field set
        RejectionReason = e.RejectionReason;
        MarkCompleted();  // Terminal state
    }

    // ... other event handlers (BulkJobItemProcessed, BulkJobCompleted, etc.)
}
```

### Command Handler (Approval)

```csharp
public sealed record ApproveBulkPricingJob(Guid JobId, Guid ApprovedBy);

public static class ApproveBulkPricingJobHandler
{
    public static BulkJobApproved Handle(
        ApproveBulkPricingJob command,
        BulkPricingJob saga)  // ← Wolverine loads saga from event stream
    {
        if (saga.Status != BulkJobStatus.AwaitingApproval)
            throw new InvalidOperationException($"Cannot approve job in {saga.Status} status");

        return new BulkJobApproved(
            command.JobId,
            command.ApprovedBy,
            DateTimeOffset.UtcNow);
    }
}
```

Wolverine:
1. Loads `BulkPricingJob` saga from event stream
2. Executes handler → returns `BulkJobApproved` event
3. Appends event to stream
4. Applies event to saga state (calls `saga.Handle(BulkJobApproved)`)
5. Persists updated saga state

**Result:** Approval is durably recorded in event stream. Audit query reads this event.

---

### Audit Query (Backoffice)

```csharp
public static async Task<BulkJobAuditView> GetBulkJobAudit(
    Guid jobId,
    IDocumentSession session)
{
    var events = await session.Events.FetchStreamAsync(jobId);

    var submitted = events.OfType<BulkJobSubmitted>().First();
    var approved = events.OfType<BulkJobApproved>().FirstOrDefault();
    var rejected = events.OfType<BulkJobRejected>().FirstOrDefault();
    var completed = events.OfType<BulkJobCompleted>().FirstOrDefault();

    return new BulkJobAuditView(
        JobId: jobId,
        SubmittedBy: submitted.SubmittedBy,
        SubmittedAt: submitted.SubmittedAt,
        ApprovedBy: approved?.ApprovedBy,
        ApprovedAt: approved?.ApprovedAt,
        RejectedBy: rejected?.RejectedBy,
        RejectedAt: rejected?.RejectedAt,
        RejectionReason: rejected?.RejectionReason,
        CompletedAt: completed?.CompletedAt,
        ProcessedCount: completed?.ProcessedCount ?? 0);
}
```

---

## Marten Configuration

```csharp
// Pricing.Api/Program.cs
builder.Host.UseWolverine(opts =>
{
    opts.Policies.UseDurableInboxOnAllListeners();  // Saga durability
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
});

builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("marten")!);
    opts.DatabaseSchemaName = "pricing";

    // Saga event sourcing configuration
    opts.Events.StreamIdentity = StreamIdentity.AsGuid;
    opts.Events.UseNumericRevisions = true;  // Optimistic concurrency
});
```

---

## References

- **Event Modeling:** `docs/planning/pricing-event-modeling.md` — "Aggregate 3: BulkPricingJob" section
- **Skill:** `docs/skills/wolverine-sagas.md` — Saga patterns, event sourcing, audit trails
- **Related Saga:** Orders BC `Order` saga (event-sourced, similar pattern)
- **Marten Docs:** Event store queries, `FetchStreamAsync`, `QueryRawEventDataOnly`

---

## Open Questions

**Q: Should Backoffice query event stream directly, or wait for projection?**

**A (Phase 2 decision):** Event stream query is sufficient. Bulk job approval audits are infrequent queries (compliance reports, manager investigations). Performance is acceptable. Defer projection to Phase 3+ if query volume increases.

**Q: What if event stream query is too slow for Backoffice UI?**

**A (Phase 3+ mitigation):** Create async projection:
```csharp
public sealed class BulkJobApprovalLogProjection : SingleStreamProjection<BulkApprovalRecord>
{
    public BulkApprovalRecord Create(BulkJobApproved e) => new(...);
    public BulkApprovalRecord Create(BulkJobRejected e) => new(...);
}
```
Backoffice queries document (`session.LoadAsync<BulkApprovalRecord>()`) instead of event stream.

**Q: Should approval events be published as integration messages?**

**A (Phase 2 decision):** No. `BulkJobApproved` is internal to Pricing BC. No other BC needs to react to bulk job approvals. If Backoffice needs real-time notifications (Phase 3+), publish lightweight `BulkJobApprovalNotification` to Backoffice's SignalR hub (not a global integration event).

---

**This ADR establishes event-sourced sagas as the audit trail mechanism for bulk pricing jobs, eliminating the need for separate audit tables or dual writes.**
