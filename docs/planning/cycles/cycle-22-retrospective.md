# Cycle 22 Retrospective: Vendor Portal Phases 1–6

**Dates:** 2026-03-08 to 2026-03-10
**Duration:** 3 days (phased delivery across 6 sign-off sessions)
**Status:** ✅ **COMPLETE** — all sign-offs obtained (UX, QA, PO, Principal Architect) for all 6 phases

---

## Objectives

**Primary Goal:** Build a production-quality Vendor Portal as CritterSupply's second frontend
bounded context, demonstrating JWT authentication, SignalR real-time communication, multi-tenant
isolation, and cross-BC messaging through a realistic change request workflow.

**Key Deliverables:**
1. **Phase 1** — JWT-authenticated Vendor Identity API (login, refresh, logout, tenant status)
2. **Phase 2** — Vendor Portal API (analytics, low-stock alerts, product catalog, dashboard)
3. **Phase 3** — Blazor WASM frontend (VendorPortal.Web) with SignalR hub connection
4. **Phase 4** — Change request management: full 7-state workflow, cross-BC messaging (Catalog BC),
   structured Q&A thread (InfoResponses), real-time decision notifications via SignalR
5. **Phase 5** — VendorAccount initialized by VendorTenantCreated, notification preferences (opt-out model),
   saved dashboard views with duplicate name guard, Settings page in Blazor WASM

---

## What Was Completed

### Phase 1 — JWT Auth Infrastructure
- VendorIdentity.Api: login, refresh, logout, JWT generation with custom claims (`VendorTenantId`, `VendorUserId`, `VendorTenantStatus`)
- `SymmetricSecurityKey` signing; `ValidateLifetime = true`; 30-second `ClockSkew`
- Role claims: `Admin`, `CatalogManager`, `ReadOnly`
- Refresh token via HttpOnly cookie (XSRF-safe for cross-origin WASM)

### Phase 2 — Vendor Portal API
- `VendorProductCatalogEntry` Marten document (read model populated from `VendorProductAssociated` integration event)
- Low-stock alerts with catch-up on reconnection (`since` timestamp query)
- Analytics handlers; dashboard summary with live pending change request count
- Multi-tenant enforcement: all queries filter by `VendorTenantId` from JWT claims only

### Phase 3 — Blazor WASM Frontend
- `VendorPortal.Web` (Blazor WASM, `Microsoft.NET.Sdk.BlazorWebAssembly`)
- In-memory JWT storage (`VendorAuthState` singleton); never localStorage
- `System.Threading.Timer` for background token refresh (no `IHostedService` in WASM)
- `VendorHubService` singleton for SignalR connection lifecycle
- CloudEvents envelope unwrapped in `VendorHubService.OnMessageReceived`
- Pages: Login, Dashboard with SignalR live updates

### Phase 4 — Change Request Workflow
- 7-state machine: `Draft → Submitted → NeedsMoreInfo ↔ Submitted → Approved/Rejected/Withdrawn/Superseded`
- Auto-supersede invariant: new submission for same `VendorTenantId + Sku + Type` supersedes existing active request
- 7 Catalog BC response handlers (one per message type: Description/Image/DataCorrection × Approved/Rejected, plus MoreInfoRequested)
- `VendorInfoResponse` record — structured Q&A thread (replaces string concatenation)
- `ChangeRequestDecisionPersonal` personal toast for submitting user; `ChangeRequestStatusUpdated` for tenant group
- Delete Draft (drafts) vs Withdraw (submitted/NeedsMoreInfo) semantic distinction in Blazor UI
- `ILogger<T>` injected in all Blazor pages; all catch blocks log at Debug level
- 59 integration tests passing

### Phase 5 — Saved Views + VendorAccount
- `VendorAccount` Marten document: Id = VendorTenantId, initialized by `VendorTenantCreated` event from Vendor Identity BC
- `NotificationPreferences` record: opt-out model with 4 boolean toggles (LowStockAlerts, ChangeRequestDecisions, InventoryUpdates, SalesMetrics), all default true
- `SavedDashboardView` / `DashboardFilterCriteria`: named filter configurations with 5 filter fields
- Duplicate view name guard (case-insensitive, 409 Conflict)
- 5 HTTP endpoints: GET/POST/DELETE dashboard-views, GET/PUT preferences
- `VendorTenantCreatedHandler` idempotent (skips if account already exists)
- Settings page in Blazor WASM: notification preference toggles with helper text, saved views table with delete confirmation dialog
- Settings link in MainLayout app bar + Dashboard Quick Actions
- ARIA accessibility: `role="group"` + `aria-labelledby` for notification preferences, `aria-label` on saved views table
- RabbitMQ queue subscription: `vendor-portal-tenant-created`
- 27 new integration tests (86 total across all phases, 100% pass rate)

**Phase 5 UX Improvements (from UX Engineer sign-off):**
- Helper text under each notification toggle explaining what it controls
- Delete confirmation dialog (`IDialogService`) preventing accidental view deletion
- Semantic form grouping for screen reader accessibility (WCAG 2.1 AA)

**Phase 5 QA Improvements (from QA Engineer sign-off):**
- Duplicate view name constraint (matches feature file acceptance criteria)
- Write endpoint 401 coverage (POST/DELETE/PUT without JWT)
- Cross-tenant DELETE isolation test
- POST→GET and DELETE→GET round-trip verification tests
- Full DashboardFilterCriteria 5-field serialization round-trip
- UpdatedAt timestamp assertions

### Phase 6 — Full Identity Lifecycle + Admin Tools
- 8 new admin HTTP endpoints in VendorIdentity.Api:
  - `POST .../users/{userId}/invitation/resend` — new SHA-256 token, reset 72h TTL, increment ResendCount
  - `POST .../users/{userId}/invitation/revoke` — cancel pending invitation with reason
  - `POST .../users/{userId}/deactivate` — deactivate user with last-admin protection
  - `POST .../users/{userId}/reactivate` — restore deactivated user to Active
  - `PATCH .../users/{userId}/role` — change user role with last-admin demotion protection
  - `POST .../tenants/{tenantId}/suspend` — suspend tenant with reason
  - `POST .../tenants/{tenantId}/reinstate` — lift suspension
  - `POST .../tenants/{tenantId}/terminate` — permanent termination with reason (per UX review)
- 2 new integration events: `VendorUserInvitationResent`, `VendorUserInvitationRevoked`
- `VendorTenantTerminated` event updated with `Reason` field (breaking contract change, all consumers updated)
- `VendorTenantTerminatedHandler` in Vendor Portal: auto-rejects in-flight change requests with "Vendor contract ended"
- EF Core migration `AddTerminationReason` for `TerminationReason` column on `VendorTenant`
- Last-admin protection invariant covers both deactivation and role demotion
- FluentValidation validators for all 8 commands with state transition guards
- 31 new integration tests (57 total for VendorIdentity, 143 total across Vendor Portal + Identity)

**Phase 6 UX Improvements (from UX Engineer sign-off):**
- Added `Reason` field to `TerminateVendorTenant` — every irreversible destructive action must carry an audit reason
- Consistent error messages: aligned "Cannot terminate an already terminated tenant" with pattern used by suspend/reinstate
- Advisory notes for future admin UI: confirmation friction on terminate, disable-with-tooltip for last-admin protection, visual distinction for auto-rejected change requests

**Phase 6 QA Improvements (from QA Engineer sign-off):**
- Not-found (404/400) tests for deactivate user, suspend tenant, terminate tenant
- Terminate tenant missing reason validation test
- Reinstate-terminated-tenant guard test (terminal state protection)
- Operations on suspended tenants (deactivate, change role, reactivate) — documents that identity-layer operations succeed on suspended tenants (enforcement at BFF layer)
- Operations on terminated tenants (deactivate, change role, invite) — same pattern
- Consecutive resend invitation token verification (proves different random bytes per call)
- Same-role assignment no-op test
- Deactivate already-deactivated user guard test

---

## Lessons Learned (Technical)

These are extracted from discovered bugs, code review findings, and architectural adjustments.
See also the skill file updates below — these lessons have been propagated into the appropriate
skill files for future reference.

---

### L1 — Marten LINQ Cannot Parameterize Enum Arrays

**What happened:** We defined `ChangeRequest.ActiveStatuses = [Draft, Submitted, NeedsMoreInfo]`
and tried to use `activeStatuses.Contains(r.Status)` in a LINQ `.Where()`. Tests failed with:

```
System.InvalidCastException: Writing values of 'VendorPortal.ChangeRequests.ChangeRequestStatus[]'
is not supported for parameters having NpgsqlDbType '-2147483639'.
```

Capturing the array into a local variable (`var active = ChangeRequest.ActiveStatuses`) does NOT
fix this — Npgsql still cannot serialize the C# enum array as a PostgreSQL parameter for LINQ.

**Fix:** Use explicit OR conditions in LINQ; use the static array for documentation and in-memory
`IsActive` checks only.

```csharp
// ❌ Does NOT work — Npgsql cannot serialize ChangeRequestStatus[] as LINQ parameter
var active = ChangeRequest.ActiveStatuses;
.Where(r => active.Contains(r.Status))

// ✅ Use explicit OR conditions in LINQ
.Where(r => r.Status == ChangeRequestStatus.Draft ||
            r.Status == ChangeRequestStatus.Submitted ||
            r.Status == ChangeRequestStatus.NeedsMoreInfo)

// ✅ Use pattern expression for in-memory IsActive check (O(1), no allocation)
public bool IsActive => Status is Draft or Submitted or NeedsMoreInfo;
```

**Propagated to:** `docs/skills/marten-document-store.md` → *LINQ Limitations: Enum Arrays*

---

### L2 — ProvideAdditionalInfo BuildCatalogMessage Bug (Entity State vs Command Value)

**What happened:** `ProvideAdditionalInfoHandler` appended `command.Response` to `request.InfoResponses`
(correct) but then called `BuildCatalogMessage(request, now)` which read `request.AdditionalNotes`
(the original draft notes) as the `AdditionalNotes` field in the outgoing Catalog BC message. The
vendor's actual response was silently discarded from the Catalog BC's message. This was found during
the Principal Architect review — 59 tests all passed, because no test asserted the Catalog BC
message payload.

**Root cause:** After writing `command.Response` into the InfoResponses list, the handler built
the outgoing message by reading entity state — but the entity's `AdditionalNotes` field still held
the old draft notes, not the new response.

**Fix:** Pass transient command values as explicit parameters to helper methods rather than re-reading
from entity state after mutation:

```csharp
// ❌ After-mutation entity state may not reflect what you think
request.InfoResponses.Add(new VendorInfoResponse(command.Response, now));
var msg = BuildCatalogMessage(request, now);  // Reads request.AdditionalNotes — WRONG

// ✅ Pass the transient value explicitly
var msg = BuildCatalogMessage(request, command.Response, now);  // Explicit — correct
```

**Propagated to:** `docs/skills/wolverine-message-handlers.md` → *Building Outgoing Messages: Command vs Entity State*

---

### L3 — WolverineHub Is Only Needed for Client→Server via SignalR

**What happened:** Early planning noted "VendorPortalHub will inherit WolverineHub in Phase 4 for
bidirectional change requests." Phase 4 implemented the full change request workflow — but
`VendorPortalHub` remained a plain `Hub`.

**Why:** Change requests use HTTP endpoints for client→server actions (POST /draft, POST /submit,
POST /withdraw). SignalR is used only for server→client notifications (decision toasts, status
badges). This is the right design: HTTP provides structured request/response with validation and
idempotency guards; SignalR provides broadcast notifications.

**WolverineHub is needed ONLY if:** clients send messages via WebSocket (not HTTP) that Wolverine
needs to dispatch to handlers. If your client→server actions are HTTP endpoints, a plain `Hub` is
correct even for "bidirectional" workflows.

**Propagated to:** `docs/skills/wolverine-signalr.md` → *Custom Hubs: When WolverineHub vs plain Hub*

---

### L4 — Document Store vs Event Sourcing: Correct Choice for State Machines

**What happened:** CONTEXTS.md initially described the ChangeRequest as an event-sourced aggregate
using `IStartStream`, `[ReadAggregate]`, and domain events. The actual implementation used plain
Marten document mutation. The CONTEXTS.md was wrong for several cycles.

**Heuristic:**

| Signal | Use Document Store | Use Event Sourcing |
|--------|--------------------|-------------------|
| Need replay / history | No | Yes |
| Complex state machine | ✓ Mutable document with status field | Only if audit trail is required |
| Other BCs own the history | ✓ Vendor Portal just tracks status | Owners need their own events |
| Temporal queries | No | Yes |
| Simple CRUD with occasional transitions | ✓ | - |

ChangeRequest: 7-state machine with transitions, no replay needed, Catalog BC owns the authoritative
workflow history. Correct choice: plain document.

**Propagated to:** `docs/skills/marten-document-store.md` → *When to Use Document Store vs Event Sourcing*

---

### L5 — Blazor Pages: Empty Catch Blocks Must Log

**What happened:** Code review found multiple empty catch blocks in Blazor pages:

```csharp
catch (Exception)
{
    Snackbar.Add("Failed to load.", Severity.Error);  // What failed? Where? Unknown.
}
```

With `async void` event handlers (like Blazor event callbacks), exceptions cannot propagate to a
caller — they are swallowed. Without logging, there is zero trace of what went wrong. This was
especially problematic in the SignalR reconnection path.

**Fix:**

```razor
@inject ILogger<Dashboard> Logger

catch (Exception ex)
{
    Logger.LogDebug(ex, "Exception loading dashboard summary.");
    Snackbar.Add("Failed to load.", Severity.Error);
}
```

**Additional nuance — static methods cannot use injected logger:**

```razor
// ❌ static — cannot access Logger injection
private static string GetSkuFromEnvelope(JsonElement envelope)
{
    try { ... }
    catch { }  // Can't log here
    return "unknown";
}

// ✅ instance method — can access Logger injection
private string GetSkuFromEnvelope(JsonElement envelope)
{
    try { ... }
    catch (Exception ex)
    {
        Logger.LogDebug(ex, "Failed to extract SKU from hub envelope.");
    }
    return "unknown";
}
```

**Propagated to:** `docs/skills/blazor-wasm-jwt.md` → *Catch Block Logging in Blazor Pages*

---

### L6 — Required Non-Nullable Fields on Message Records

**What happened:** `ChangeRequestDecisionPersonal` had `string? ChangeType = null` — a nullable
optional field with a default. All 7 handlers always pass a value. The nullable-with-default was
a design smell: it suggests the field might be legitimately absent, but it never is.

**Rule:** Message record fields that are always populated should be **required and non-nullable**.
Optional-with-default invites "I'll fill it in later" patterns and hides type-system guarantees.

```csharp
// ❌ Suggests ChangeType might be null — it never is
public sealed record ChangeRequestDecisionPersonal(
    ...
    string? ChangeType = null) : IVendorUserMessage;

// ✅ Required at construction site; compiler enforces it
public sealed record ChangeRequestDecisionPersonal(
    ...
    string ChangeType) : IVendorUserMessage;
```

**Propagated to:** `docs/skills/modern-csharp-coding-standards.md` → *Message Record Field Nullability*

---

### L7 — Returning IEnumerable<object> for Multi-Type Dispatch

**What happened:** Handlers that need to return multiple messages of different types can use
`IEnumerable<object>` and Wolverine will route each element based on its runtime type — one to
RabbitMQ, another to SignalR, etc.

```csharp
public static async Task<IEnumerable<object>> Handle(
    DescriptionChangeApproved @event,
    IDocumentSession session,
    CancellationToken ct)
{
    // ...
    return
    [
        new ChangeRequestStatusUpdated(...),     // → SignalR vendor:{tenantId} group
        new ChangeRequestDecisionPersonal(...)   // → SignalR user:{userId} group
    ];
}
```

Wolverine's publish rules (configured in `Program.cs`) determine routing for each concrete type.
This is a clean alternative to `OutgoingMessages` when the set of outgoing types is not known at
compile time or when mixing transport targets (RabbitMQ + SignalR).

**Return empty collection to no-op:** `return [];` — Wolverine is satisfied, nothing is published.

---

### L8 — CONTEXTS.md Must Be Updated in the Same Commit

**What happened:** CONTEXTS.md contained 7 errors when the Principal Architect reviewed Phase 4:

| Error | In CONTEXTS.md | Actual code |
|-------|---------------|-------------|
| Enum values | `DescriptionUpdate`, `ImageUpload` | `Description`, `Image` |
| Terminal state | `Replaced` | `Superseded` |
| Message name | `ProductCorrectionRequested` | `DataCorrectionRequested` |
| Vendor Portal status | "Phase 4 is next" | Phases 1-4 complete |
| Flow pattern | Event-sourced aggregate | Plain Marten document mutation |
| State transition | `Approved → Replaced` | Doesn't exist |
| Trigger | "auto-withdraws" | "auto-supersedes" |

All of these were introduced by describing planned behavior rather than actual implemented behavior,
or by not updating CONTEXTS.md when code diverged.

**Rule:** CONTEXTS.md is the architectural source of truth. Update it in the same commit as the
code change, not in a separate "docs catch-up" PR. If planning and implementation diverge,
CONTEXTS.md must reflect the implementation — not the plan.

---

### L9 — UX Semantic Distinction: Delete vs Withdraw

**What happened:** The initial design said "vendors can withdraw a Draft, Submitted, or NeedsMoreInfo
request." The implementation only showed a Withdraw button for Submitted/NeedsMoreInfo. The PO
raised this as a blocker.

**Resolution:** "Withdraw" and "Delete" are semantically distinct:

| Action | State | Meaning |
|--------|-------|---------|
| **Delete Draft** | Draft | The request never left the vendor; delete it as if it never existed |
| **Withdraw Request** | Submitted, NeedsMoreInfo | Recall a request already visible to the Catalog team |

Both actions route through the same API endpoint (`POST /withdraw`) — the distinction is purely UX.
The confirmation dialogs use different language: "Delete this draft?" vs "Withdraw this request?".

**Rule:** When two UI actions have different business semantics, give them different labels even if
the underlying API call is identical. Correct labels build trust; ambiguous labels cause support tickets.

---

### L10 — ADR Status Must Reflect Implementation Reality

**What happened:** ADR 0013 ("Migrate from SSE to SignalR") had status `⚠️ Proposed` even after
the Vendor Portal fully implemented SignalR across Phases 1–4. The Vendor Portal was explicitly
cited in ADR 0013 as the planned proof-of-concept.

**Resolution:** Promoted ADR 0013 to `✅ Accepted` with acceptance date. ADR 0004 (SSE) updated
to `🔄 Superseded`.

**Rule:** An ADR whose decision has been fully implemented should be marked `Accepted`. Leaving it
`Proposed` is misleading — it implies the decision is still open when it has already been made
and executed. Update the ADR in the same PR that completes the implementation.

---

### L11 — Identity Operations Succeed on Suspended/Terminated Tenants (By Design)

**What happened:** QA edge case tests (Phase 6) revealed that user identity operations (deactivate,
change role, reactivate, invite) succeed even when the tenant is Suspended or Terminated. Validators
only check `db.Tenants.AnyAsync(t => t.Id == tenantId)` — existence, not status.

**Why this is correct:** Suspension/termination enforcement happens at the Vendor Portal (BFF/login)
layer, not at the identity API layer. An admin may need to deactivate a user in a terminated tenant
(security cleanup), change roles in a suspended tenant (preparation for reinstatement), or revoke
invitations after suspension. Blocking these operations at the identity layer would make administrative
recovery workflows impossible.

**Rule:** Identity-layer validators should check entity existence, not business status constraints.
Business status enforcement belongs in the downstream consuming layers (BFF, login endpoints).

### L12 — Every Irreversible Destructive Action Must Carry an Audit Reason

**What happened:** Initial `TerminateVendorTenant` command had only `TenantId` (no reason). UX
Engineer review flagged this as a gap — every irreversible, destructive admin action should carry
a mandatory reason for audit trail parity with Suspend/Deactivate commands.

**Resolution:** Added `Reason` field to `TerminateVendorTenant` command, `TerminationReason` to
`VendorTenant` entity, `Reason` to `VendorTenantTerminated` integration event, and EF Core migration.

**Rule:** For irreversible operations, require a reason field even if it seems optional. Reasons
enable post-incident investigation, compliance audits, and customer support escalations.

---

## What Went Well

- **Phase-gate delivery** — each phase had explicit sign-offs before the next began; issues were caught early
- **Test-first habit** — 59 integration tests written as part of the work, not as an afterthought
- **Tenant isolation from day 1** — JWT claim enforcement (never request params) applied consistently across all endpoints; no cross-tenant leaks discovered
- **SignalR dual-group design** — `vendor:{tenantId}` + `user:{userId}` groups cleanly separated broadcast vs personal notifications
- **Structured data from the start** — `InfoResponses` list (rather than string concatenation) was chosen correctly in the final design even though it required migrating away from the initial concatenation approach

## What To Improve

- **CONTEXTS.md drift** — 7 errors found at final review (Phases 1-4), plus 5 endpoint URL mismatches found during Phase 5 PO review. Update CONTEXTS.md as endpoints are implemented, not after.
- **Catch-up assertion test** — the `BuildCatalogMessage` bug (L2) was NOT caught by any test because no test asserted the payload of the outgoing catalog message. Add assertions on the full outgoing message payload, not just status transitions.
- **ADR lifecycle discipline** — ADR 0013 was `Proposed` for the entire Phases 1-4 implementation
- **Feature file accuracy** — Feature file said "DashboardViewSaved domain event is recorded" but implementation uses Marten document store (not event sourcing). Feature files should reflect the actual persistence pattern. Updated in Phase 5.
- **POST→GET round-trip tests** — QA discovered that 201 response assertions without subsequent GET verification leaves false-positive risk. Always verify persistence with a round-trip test.

---

## Metrics

| Metric | Value |
|--------|-------|
| Integration tests added (Phases 1–4) | 59 |
| Integration tests added (Phase 5) | 27 |
| Total integration tests | 143 (86 Vendor Portal + 57 Vendor Identity) |
| Test pass rate at Phase 6 close | 100% (143/143) |
| ADRs updated | 2 (0004 superseded, 0013 accepted) |
| CONTEXTS.md corrections at final review | 7 (Phase 4) + 5 endpoint URL updates (Phase 5) + Phase 6 endpoints added |
| Skills updated | 4 (marten-document-store, wolverine-signalr, wolverine-message-handlers, blazor-wasm-jwt) |
| New skills created | 0 |
| Code review iterations | 2 (initial review, post-PO-blocker review) |
| PO blockers at first review | 3 (Phases 1-4, all resolved) + 2 (Phase 5, all resolved) |
| PA bugs found at final review | 1 (BuildCatalogMessage — fixed) |

---

## Sign-offs

| Role | Status | Date |
|------|--------|------|
| UX Engineer (Phase 3) | ✅ | Cycle 22 Phase 3 |
| UX Engineer (Phase 5) | ✅ | Cycle 22 Phase 5 (B1: helper text, B2: delete confirmation, B3: ARIA grouping) |
| UX Engineer (Phase 6) | ✅ | Cycle 22 Phase 6 (Reason on terminate, error message consistency, advisory notes) |
| QA Engineer (Phase 4) | ✅ | Cycle 22 Phase 4 |
| QA Engineer (Phase 5) | ✅ | Cycle 22 Phase 5 (3 required + 5 recommended, 86/86 tests) |
| QA Engineer (Phase 6) | ✅ | Cycle 22 Phase 6 (10 edge case tests, 57/57 VendorIdentity tests) |
| Product Owner (Phase 4) | ✅ | 2026-03-10 |
| Product Owner (Phase 5) | ✅ | 2026-03-10 (CONTEXTS.md URL updates + feature file corrections) |
| Product Owner (Phase 6) | ✅ | 2026-03-10 (8 admin endpoints, compensation handler, last-admin protection) |
| Principal Architect | ✅ | 2026-03-10 |
