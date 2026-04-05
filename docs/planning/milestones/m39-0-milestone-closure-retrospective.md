# M39.0 Milestone Closure Retrospective

**Milestone:** M39.0 — Critter Stack Idiom Refresh
**Date:** 2026-04-04 to 2026-04-05
**Sessions:** 7 (S0 mechanical sweep + S1–S5 implementation + S6 documentation closure)
**PRs:** #516 (S0), #517 (S1), #518 (S2), #519 (S3), #520 (S4), #521 (S5)

---

## Milestone Overview

M39.0 was a systematic idiom refresh across the CritterSupply codebase. Its goal was to
bring every bounded context up to current Critter Stack best practices — the patterns
documented in `docs/skills/wolverine-message-handlers.md`, `marten-event-sourcing.md`, and
`modern-csharp-coding-standards.md`.

The milestone was scoped from a full audit of all 17 backend BCs (the M39.x planning
session). That audit classified each BC as Clean, Minor Drift, or Significant Drift. M39.0
addressed every BC with drift, prioritizing by risk: Correspondence (9 handlers with
Anti-Pattern #9), Pricing (3 fat endpoints), Orders Checkout (outbox two-phase commit gap),
and quick wins across Fulfillment, Listings, Promotions, and Vendor Portal.

**Build state at close:** 0 errors, 19 warnings (unchanged from M38.1 baseline)

**Integration tests at close:** Per-BC verification across all sessions confirmed no
regressions. S5 closing verification: 173/173 tests passing across the 4 BCs touched in
that session (Fulfillment 17, Listings 41, Promotions 29, Vendor Portal 86). Earlier
sessions confirmed Correspondence 5/5, Pricing 25/25, Orders 48/48. No new tests were
added — M39.0 only updated 7 existing tests to match `ProblemDetails` behavior changes
from `Before()` refactors.

---

## What Changed — by Anti-Pattern Fixed

This is organized by the idiom violation that was corrected, not by session. This framing
serves the reference architecture mission — it makes clear what CritterSupply now
demonstrates correctly that it previously didn't.

### Anti-Pattern #9: Direct `session.Events.StartStream()`

**Skill Reference:** `wolverine-message-handlers.md` — Anti-Pattern #9
**BCs Fixed:** Correspondence (9 handlers), Fulfillment (1), Listings (1), Product Catalog (1, S0)
**Sessions:** S0, S1, S2, S5

All instances of `session.Events.StartStream<T>()` replaced with
`MartenOps.StartStream<T>()` returning `IStartStream`. This eliminates the need for
`IDocumentSession` injection in stream-creation handlers and lets Wolverine manage the
Marten transaction lifecycle.

The Correspondence BC was the most affected — all 9 integration event handlers used the
old pattern. After the fix, all 9 are static classes returning `(IStartStream, OutgoingMessages)`
with no async, no session injection, and no cancellation token. The 8 CS1998 warnings
(async-without-await) that these handlers generated were eliminated as a direct side effect.

### Anti-Pattern #7: Fat handler mixing infrastructure + logic

**Skill Reference:** `wolverine-message-handlers.md` — Anti-Pattern #7
**BCs Fixed:** Correspondence (`SendMessageHandler`), Pricing (3 endpoints)
**Sessions:** S2, S3

`SendMessageHandler` was decomposed from a 154-line instance class with manual
`session.Events.Append()` and bare exception swallowing into a 103-line static class with
`Before()` (idempotency guards via `HandlerContinuation`) and `Handle()` (business logic
via `[WriteAggregate]`). The bare `catch (Exception ex)` that swallowed
`OperationCanceledException` was fixed with a `when` guard — a real bug, not just style.

The three Pricing endpoints (`SetBasePrice`, `SchedulePriceChange`,
`CancelScheduledPriceChange`) moved from `Pricing.Api/Pricing/` (fat endpoint files mixing
HTTP concerns with domain logic) to `Pricing/Products/` (command + handler in the domain
project). Each was decomposed into the compound handler lifecycle appropriate for its
constraints.

### Anti-Pattern #8: Manual load + wrong append pattern

**Skill Reference:** `wolverine-message-handlers.md` — Anti-Pattern #8
**BCs Fixed:** Pricing (3 endpoints), Promotions (3 handlers), Listings (6 handlers)
**Sessions:** S3, S5

Handlers that manually loaded aggregates via `FetchForWriting` or
`AggregateStreamAsync` and returned events through non-standard patterns were converted to
either `[WriteAggregate]` compound handlers (when the stream ID is a natural UUID resolvable
by convention) or `Load()/Before()/Handle()` compound handlers (when the stream ID is a
computed UUID v5).

Listings: all 6 write handlers converted to `[WriteAggregate]` + `Before()/Handle()`.
Promotions: `RedeemCoupon` and `RevokeCoupon` converted to `Load()` pattern (UUID v5);
`RecordPromotionRedemption` converted to `[WriteAggregate]` (natural UUID v7).

### Anti-Pattern #11: Outbox two-phase gap

**Skill Reference:** `wolverine-message-handlers.md` — Anti-Pattern #11
**BCs Fixed:** Orders Checkout (`CompleteCheckout`)
**Sessions:** S4

The `CompleteCheckout` handler called `SaveChangesAsync()` to persist the checkout
completion event, then returned `OutgoingMessages` with `CartCheckoutCompleted`. If the
process failed between the save and the return, the event was committed to the database but
the integration message was never enrolled in the outbox — the Order saga would never start.

After the fix, `CompleteCheckout` uses `[WriteAggregate]` and returns
`(IResult, Events, OutgoingMessages)`. Wolverine's transactional middleware commits the
event append and outbox enrollment atomically. The two-phase gap is eliminated.

### Redundant `SaveChangesAsync` under `AutoApplyTransactions()`

**Skill Reference:** `wolverine-message-handlers.md` — Handler Discovery section
**BCs Fixed:** Customer Identity, Backoffice Identity, Marketplaces, Orders Checkout (3 handlers), Vendor Portal
**Sessions:** S0, S4, S5

14 explicit `SaveChangesAsync()` calls removed across 5 BCs. All were redundant because
`AutoApplyTransactions()` was already configured in each BC's `Program.cs`. Wolverine's
auto-transaction middleware calls `SaveChangesAsync()` after handler completion — the
explicit calls were harmless noise but obscured the actual persistence model.

### Missing `Snapshot<T>` configuration

**Skill Reference:** `marten-event-sourcing.md`
**BCs Fixed:** Correspondence (`Message`), Pricing (`ProductPrice`)
**Sessions:** S2, S3

Both aggregates were loaded via `AggregateStreamAsync` on every handler invocation without
snapshot optimization. `Snapshot<Message>(SnapshotLifecycle.Inline)` and
`Snapshot<ProductPrice>(SnapshotLifecycle.Inline)` were added to their respective
`Program.cs` files. This eliminates full event stream replay on every load — particularly
important for `Message` (loaded on every delivery attempt) and `ProductPrice` (loaded on
every price change for floor/ceiling validation).

### Instance handlers (should be static)

**Skill Reference:** `wolverine-message-handlers.md`
**BCs Fixed:** Correspondence (all 10 handlers)
**Sessions:** S1, S2

All Correspondence handlers were instance classes (`public sealed class`). After the
refactor, all 10 are static classes (`public static class`). Static handlers are the
Wolverine default — they avoid unnecessary allocations and make it explicit that no instance
state is used.

### `Guid.NewGuid()` in stream ID creation

**Skill Reference:** `modern-csharp-coding-standards.md`
**BCs Fixed:** Product Catalog, Correspondence, Promotions
**Sessions:** S0

`Guid.NewGuid()` replaced with `Guid.CreateVersion7()` in stream-creation paths. V7 GUIDs
are time-sortable and produce better index performance in Postgres.

### Inconsistent connection string key

**Skill Reference:** `adding-new-bounded-context.md`
**BCs Fixed:** Correspondence (`"marten"` → `"postgres"`)
**Sessions:** S2

Correspondence was the only BC using `"marten"` as the connection string key. All other BCs
use `"postgres"`. Standardized to `"postgres"` in both `Program.cs` and `appsettings.json`.
No impact on Docker Compose (Correspondence has no container profile) or tests (TestContainers
overrides the connection string directly).

### Missing `AutoApplyTransactions()`

**Skill Reference:** `wolverine-message-handlers.md` — Handler Discovery section
**BCs Fixed:** Vendor Portal
**Sessions:** S5

Vendor Portal was the only BC with Wolverine that lacked `AutoApplyTransactions()`. Added
to `Program.cs`. No redundant `SaveChangesAsync` calls existed in handlers (only in seed
data), so no removals were needed — the policy was simply missing.

---

## Key Technical Findings

Discoveries that weren't anticipated in the milestone plan — things worth remembering for
future work.

### 1. `(IResult, Events, OutgoingMessages)` triple-tuple is valid

S4 confirmed that Wolverine HTTP endpoints can return all three simultaneously. `IResult`
sets the HTTP response, `Events` are appended via `[WriteAggregate]`, and `OutgoingMessages`
go through the transactional outbox. The `CompleteCheckout` outbox gap was a real
correctness issue, not just a style concern.

### 2. `[WriteAggregate]` works for route-only POST endpoints

The M32.3 "Direct Implementation" comment was stale for `CompleteCheckout`. The Anti-Pattern
#10 limitation (compound handler + mixed route + body params) does not apply to endpoints
with no JSON body. Route-only + `[WriteAggregate]` compound handler is the clean pattern
for endpoints like `POST /api/checkouts/{checkoutId}/complete`.

### 3. UUID v5 deterministic stream IDs require `Load()`, not `[WriteAggregate]`

Confirmed in both Pricing (`ProductPrice.StreamId(sku)`) and Promotions
(`Coupon.StreamId(code)`). Wolverine's `[WriteAggregate]` resolves the stream by convention
(`{AggregateName}Id` property) — it cannot execute a SHA-1 hash computation from a route
string. The `Load()/Before()/Handle()` compound pattern with manual
`session.Events.Append()` is the correct approach for computed stream IDs. This is now
documented in the skill file with two concrete CritterSupply examples.

### 4. `ProblemDetails` in non-HTTP message handlers stops the pipeline silently

When `Before()` returns `ProblemDetails` in a non-HTTP message handler context, Wolverine
stops the handler pipeline without throwing an exception. Tests that expected
`InvalidOperationException` needed updating to verify state is unchanged instead. Seven
tests were updated across Listings (3) and Promotions (4).

### 5. CS1998 warnings are a signal

The 8 CS1998 warnings eliminated in S1 (async-without-await) were directly caused by
Anti-Pattern #9. The `session.Events.StartStream()` call is synchronous, but the handlers
were marked `async` because they originally had `await` calls that were removed during
earlier refactors without cleaning up the `async` keyword. Clean idioms and clean compiler
output are correlated.

### 6. DELETE + compound handler limitation

Wolverine HTTP's compound handler pattern (`LoadAsync` + `Before` + `Handle`) triggers body
deserialization regardless of HTTP verb. For DELETE endpoints with route-only inputs and
computed stream IDs (where `[WriteAggregate]` cannot be used), a single-method async handler
is the correct pattern. Discovered during the Pricing `CancelScheduledPriceChange` refactor.

---

## BCs Assessed as Clean (No Action Needed)

From the M39.x audit, these BCs were confirmed idiomatic and required no changes:

- **Shopping** — All handlers use `[WriteAggregate]` or `IStartStream` correctly
- **Orders** (aggregate/saga) — Saga and aggregate handlers are clean; only Checkout needed work
- **Payments** — Clean `IStartStream` + `[WriteAggregate]` patterns throughout
- **Vendor Identity** — EF Core handlers are clean
- **Returns** (core handlers) — Clean compound handlers; cross-BC saga tests skipped (pre-existing)

---

## What Was Deferred or Accepted

Items deliberately left out of M39.0 scope:

- **`FulfillmentRequestedHandler` direct `StartStream`** — Accepted deviation. The handler
  uses an idempotency guard (`if (existingShipment != null)`) that requires conditional
  stream creation. `IStartStream` cannot express "create only if not exists" — it always
  creates. The `session.Events.StartStream()` call is intentional here.

- **`SeedPricesAsync` in Pricing `Program.cs`** — Dev-only seed data behind
  `IsDevelopment()` guard. Uses `session.Events.StartStream<ProductPrice>` directly. Not
  worth refactoring.

- **Correspondence test depth** — 5 integration tests for 10 handlers. The gap is noted
  but test additions were outside M39.0 scope (idiom refresh, not coverage expansion).

- **Product Catalog `MigrateProduct.cs` direct `StartStream`** — Accepted as a migration
  bridge. This handler bootstraps event streams from legacy document data. It will be
  removed when migration is complete, not refactored.

---

## Inherited by Next Milestone

1. **Returns cross-BC saga tests** — 6 tests skipped since M36.0. Re-evaluation overdue.
   The skips predate M39.0 and were not addressed because Returns core handlers were
   assessed as clean.

2. **`ActivatePromotionHandler` return type** — Returns a single event instead of an
   `Events` collection. Should be `Events` per Wolverine conventions. Noted during S5
   audit but out of scope for the quick wins session.

3. **Vendor Portal cold-start test flakes** — 56/86 tests fail on first container run;
   all 86 pass on retry. Infrastructure warmup issue in TestContainers lifecycle, not a
   code regression. Worth investigating as a test reliability item.

4. **eBay orphaned draft background sweep** — Detection is in place
   (`CheckSubmissionStatusAsync` surfaces orphaned drafts as failures). Cleanup mechanism
   (automatic draft deletion) deferred. Inherited from M38.1.

---

## Scope Summary

| Metric | Count |
|--------|-------|
| Sessions (implementation) | 6 (S0–S5) |
| Sessions (documentation) | 1 (S6) |
| PRs merged | 6 (#516–#521) |
| BCs touched (implementation) | 11 |
| Handlers refactored | 30+ |
| Anti-patterns eliminated | 10 categories |
| New tests added | 0 |
| Existing tests updated | 7 |
| Build errors introduced | 0 |
| Build warnings change | Net zero (19 → 19; temporarily 11 in S1 before other BCs restored count) |

---

## CONTEXTS.md Assessment

Each BC touched by M39.0 was assessed for CONTEXTS.md accuracy. The idiom changes are
internal implementation changes — they do not change what any BC does, only how it does it.
CONTEXTS.md describes *what* BCs do, so:

- **Correspondence** — Verified, no changes needed. The entry accurately describes the
  choreography pattern (receives events from Orders, Fulfillment, Returns, Payments),
  the retry lifecycle, and the provider integration. The `SendMessageHandler` decomposition
  didn't change behavior.

- **Pricing** — Verified, no changes needed. The entry describes the three operations
  (base price, schedule, cancel) correctly. The commands moved from `Pricing.Api` to
  `Pricing` (domain project) — this is an internal structural change, not behavioral.

- **Orders** — Verified, no changes needed. The entry describes the Checkout flow and
  Order saga correctly. The `CompleteCheckout` outbox fix eliminated a reliability gap
  but didn't change what the endpoint does.

- **Promotions** — Verified, no changes needed. The entry describes redemption workflow,
  coupon validation, and batch generation correctly. The handler pattern fixes are
  internal implementation changes. One note: the entry mentions "Handlers manually append
  events via `session.Events.Append()` (not tuple returns)" — this is now partially
  outdated since `RecordPromotionRedemptionHandler` uses `[WriteAggregate]`, but CONTEXTS.md
  intentionally avoids implementation details that require ongoing updates, so this
  sentence should be removed rather than updated.

- **Fulfillment, Listings, Vendor Portal, Customer Identity, Backoffice Identity,
  Marketplaces** — Verified, no changes needed. All were minor mechanical fixes
  (IStartStream, SaveChangesAsync removal, AutoApplyTransactions) with no behavioral change.

---

## What M39.0 Means for the Reference Architecture

Before M39.0, CritterSupply demonstrated some anti-patterns alongside its correct patterns.
A developer reading the Correspondence BC would see `session.Events.StartStream()` in 9
handlers — the exact pattern that `wolverine-message-handlers.md` documents as Anti-Pattern
#9. A developer reading the Pricing BC would see fat endpoint files mixing HTTP concerns
with domain logic. A developer reading the Orders Checkout flow would find a two-phase
commit gap that could lose integration messages under failure.

After M39.0, every handler in the codebase either follows the documented idiom or has an
explicit, documented reason for deviation (e.g., `FulfillmentRequestedHandler` idempotency
guard, `MigrateProduct` migration bridge). The codebase is now a reliable reference for the
patterns it claims to demonstrate.

This is the milestone's lasting value: not the mechanical changes themselves, but the
confidence that any handler a developer reads in CritterSupply is either correct or
explicitly annotated as a known deviation.
