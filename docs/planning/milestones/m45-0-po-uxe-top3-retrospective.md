# M45.0 — PO/UXE Top-3 Fixes Retrospective

> **Status:** ✅ Complete
> **Date:** 2026-05-08
> **Author:** Principal Architect (with QA Engineer test-extension pass)
> **PR:** `copilot/address-critical-issues-from-discussions`
> **Source:** `docs/research/state-of-repo-2026-05.md` §7.3 — synthesis of PO + UXE feedback

## TL;DR

Three issues from the PO/UXE consultation in the State-of-Repo report were
addressed in a single cycle, each followed by a QA-led test-extension pass:

| #  | Item                                                              | Origin   | Outcome                                                                                                                                           |
| -- | ----------------------------------------------------------------- | -------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| S1 | Storefront UI ↔ notification-handler reconciliation               | UXE §7.2 | ✅ **Fixed.** OrderConfirmation now correctly distinguishes Backordered / HandedToCarrier / DeliveryAttemptFailed / LostInTransit / ReturnToSenderInitiated, and renders the full Returns lifecycle. 43 new bUnit tests. |
| S2 | MultiShipmentView + CarrierPerformanceView identity resolution    | PO §7.1  | ✅ **Fixed.** 5 domain events enriched with `OrderId` / `Carrier` so the projections key on the right discriminator instead of `Guid.Empty` / `"Unknown"`. 22 new projection unit tests. |
| S6 | Abandoned-cart recovery audit                                     | PO §7.1  | ✅ **Audited.** Confirmed the implementation gap (event defined + reducer present, no emitter, no integration message, no Correspondence handler). Charter input memo filed; not implemented this cycle (multi-session scope). |

**Test totals:** Fulfillment 62/62 ✅ (40 + 22 new), Storefront.Web
OrderConfirmation 43/43 ✅ (all new), Shopping 32/32 ✅ (untouched, smoke-checked
for regressions). Solution build clean (0 errors).

## Context

The 2026-05-06/08 reconciliation pass on `state-of-repo-2026-05.md`
introduced §7 — explicit PO and UXE sign-off. §7.3 surfaced **ten new items
(S1–S10)** the original report had missed. The user's directive for this
cycle: "begin addressing some of the most critical items… for the top 3
issues that were determined from your discussions with the UX Engineer and
Product Owner."

Selection criteria for "top 3":

- **PO and/or UXE explicitly emphasised the item.** S1 was UXE's strongest
  finding ("implemented but invisible — exactly the class the user asked us
  to flag"). S2 was PO's strongest re-prioritisation ("top-five support
  contact driver in real eComm"). S6 was a direct PO ask ("verify
  Correspondence actually consumes them").
- **Surgical scope.** Items that would require a multi-session remaster (S3
  cross-product exchange, S4 post-placement modifications, S5 fraud-review
  saga state, S7 Store Credit BC) were filtered out — those belong in the
  Orders or Returns remaster charters per PO §7.1.
- **No new BC.** The cycle was framed as "fine-tuning" per the user's
  guidance for this session.

## What shipped

### Issue #1 (S1) — Storefront UI ↔ notification-handler reconciliation

**The bug.** `OrderConfirmation.razor`'s `shipment-status-changed` SSE
dispatch hardcoded `_currentStatus = "Shipped"` regardless of the
`NewStatus` payload. The Storefront BC has 20 notification handlers in
`src/Customer Experience/Storefront/Notifications/` that all funnel through
4 SignalR discriminators (`cart-updated`, `order-status-changed`,
`shipment-status-changed`, `return-status-changed`). The `NewStatus` field
is what distinguishes `Backordered` from `Delivered` from
`LostInTransit` — and the UI was discarding it.

**Customer-visible impact (UXE §7.2):** Returns and backorder customers saw
**zero** real-time feedback. A backordered order would show "Shipped" in the
order chip. A delivery failure would show "Shipped". A lost-in-transit event
would show "Shipped".

**The fix.**

1. Added three pure static helpers on `OrderConfirmation`
   (`internal static`):
   - `MapShipmentStatus(string newStatus)` — domain `NewStatus` → display
     status string. Covers all 9 statuses the notification handlers can
     produce, with safe pass-through for unknown values.
   - `BuildShipmentMessage(string newStatus, string? trackingNumber)` —
     customer-facing message for each shipment status, gracefully handling
     null tracking numbers.
   - `BuildReturnMessage(string newStatus, string? details)` — customer-
     facing message for each of the 7 Returns lifecycle states.
2. Rewrote the `shipment-status-changed` case to read `NewStatus` and
   delegate to the helpers.
3. Added a new `return-status-changed` case (previously unhandled despite
   8 Returns notification handlers being wired).
4. Extended `GetStatusColor` and `GetDisplayStatus` to cover Backordered,
   Lost in Transit, Returning to Sender, and `Return:*` variants.

**Tests (QA Engineer-led extension pass).** 43 new tests in
`tests/Customer Experience/Storefront.Web.UnitTests/Components/Pages/OrderConfirmationTests.cs`:

- Pure-function `Theory`/`Fact` coverage of all three helpers (every status
  the notification handlers can emit, plus pass-through for unknown values).
- bUnit render coverage of each new SSE dispatch path with hand-crafted
  JSON payloads matching `StorefrontEvent.cs` (cart-updated /
  order-status-changed / shipment-status-changed / return-status-changed
  discriminators, camelCase property names per System.Text.Json web
  defaults).
- Explicit **regression guard**: a test that asserts
  `shipment-status-changed` with `newStatus="Backordered"` does NOT produce
  the chip text "Shipped" (the original bug).

### Issue #2 (S2) — MultiShipmentView + CarrierPerformanceView identity resolution

**The debt.** Both projections were carrying flagged-debt Identity
resolution from the Fulfillment remaster S3 (M41.0). See
`docs/planning/milestones/fulfillment-remaster-s3-retrospective.md` gaps #2
and #4:

- `MultiShipmentViewProjection` keyed by `OrderId` — but
  `TrackingNumberAssigned`, `ShipmentDelivered`, and `ReshipmentCreated`
  didn't carry `OrderId`. Identity calls fell back to `Guid.Empty`,
  bucketing **every** tracking/delivery/reshipment update across all orders
  into a single `Guid.Empty`-keyed view. Multi-order isolation was broken.
- `CarrierPerformanceViewProjection` keyed by carrier name — but
  `GhostShipmentDetected` and `CarrierClaimResolved` didn't carry
  `Carrier`. Identity calls fell back to `"Unknown"`, hiding per-carrier
  reliability signal.

The Shipment stream id is a **one-way UUID v5 hash** of `OrderId`
(`Shipment.StreamId`), so neither could be recovered from stream metadata
alone — the events themselves had to grow.

**Customer-visible impact (PO §7.1):** Split-shipment "where is my order?"
is a top-five support contact driver in real eComm. The
`MultiShipmentView` is what powers customer-facing tracking pages once
those land — and it was structurally incapable of showing per-order
state.

**The fix.**

1. Enriched 5 domain events with the missing identity fields (always as the
   first positional parameter for clarity):
   - `TrackingNumberAssigned(Guid OrderId, …)` *(was 3-arg)*
   - `ShipmentDelivered(Guid OrderId, …)` *(was 2-arg)*
   - `ReshipmentCreated(Guid OrderId, …)` *(was 4-arg)*
   - `GhostShipmentDetected(string Carrier, …)` *(was 3-arg)*
   - `CarrierClaimResolved(string Carrier, …)` *(was 3-arg)*
2. Updated the 5 emitters (`CarrierHandlers.cs`,
   `ArrangeAlternateCarrier.cs`, `CreateReshipment.cs`,
   `GhostShipmentDetection.cs`, `ResolveCarrierClaim.cs`). The carrier and
   order id are always available in the loaded `Shipment` aggregate at
   emission time — no extra reads required.
3. `ResolveCarrierClaim` was decomposed from a single `Handle` into a
   compound `LoadAsync` + `Handle` so that the carrier could be read off
   the loaded shipment without re-querying the session.
4. Replaced `Identity<…>(_ => Guid.Empty)` and `Identity<…>(_ => "Unknown")`
   in both projections with proper field-based resolution. Added explanatory
   doc comments pointing back to the S3 retrospective and this memo.

**Tests (QA Engineer-led extension pass).** 22 new tests across two new
files:

- `tests/Fulfillment/Fulfillment.UnitTests/Shipments/MultiShipmentViewProjectionTests.cs`
  (10 tests) — `Apply` semantics for all 4 events, the deterministic
  `Shipment.StreamId(orderId)` linkage, **multi-order isolation** (two
  orderIds → two disjoint views), constructor smoke test guarding against
  `Identity<>` typos.
- `tests/Fulfillment/Fulfillment.UnitTests/Shipments/CarrierPerformanceViewProjectionTests.cs`
  (12 tests) — counter increments for all 7 events, `OpenClaims` floor-zero
  with simultaneous `ResolvedClaims` increment, **per-carrier isolation**
  (UPS vs FedEx → two disjoint views), constructor smoke test.

The existing 40 Fulfillment unit tests were updated for the new event
shapes and continue to pass. Total Fulfillment unit suite: **62/62 ✅**.

### Issue #3 (S6) — Abandoned-cart recovery audit

**The audit.** `CartAbandoned` is **defined as a domain event** and
**applied by the `Cart` reducer**, with three unit tests covering the
`Apply` behavior. Beyond that, **nothing happens**:

- No emitter — `grep "new CartAbandoned" src/` returns zero results outside
  test files.
- No integration message in `Messages.Contracts/Shopping/`.
- No `PublishMessage<CartAbandoned>()` in `Shopping.Api/Program.cs` (the
  publish list at lines 87–99 covers only the 6 cart-mutation events).
- No `CartAbandonedHandler` in `Correspondence/Messages/`.
- No background scheduler / TTL job.

The Shopping README at `src/Shopping/Shopping.Api/README.md` lines 41,
119, 184, and 302 already says "⚠️ not yet implemented" four times.

**Why we deferred implementation.** Implementing this end-to-end is a
multi-session arc (scheduler + integration message + Correspondence handler
+ email template + BDD scenarios + integration tests with virtual
`TimeProvider`). It also requires a product decision (per-cart timers vs.
periodic sweep — see Shopping README §302) that is out of scope for a
fine-tuning cycle.

**The deliverable.** A formal charter input memo at
`docs/planning/milestones/m45-0-abandoned-cart-gap-memo.md` documenting the
gap, the recommended placement (Correspondence "lifecycle expansion" cycle
is the smallest-scope landing spot), and acceptance criteria for "complete".
The memo is the answer to PO §7.1's question "verify that Correspondence
actually consumes them" so future planning sessions don't re-derive it.

## Findings

### What went well

- **Pure-function refactor on the Razor side.** Extracting
  `MapShipmentStatus` / `BuildShipmentMessage` / `BuildReturnMessage` as
  `internal static` helpers made the SSE dispatch trivially testable as
  pure functions. The 28 helper-Theory tests are deterministic and ran in
  sub-second time. This is a pattern worth repeating for Blazor SSE
  dispatch in other pages.
- **Domain-event enrichment was the right call (vs. a custom Marten
  grouper).** The earlier S3 debt note suggested "a production
  implementation would need Marten's `ViewProjection` base class with
  custom stream matching." Once we stepped back, the right answer was much
  simpler: **put the discriminator on the event**. The OrderId and Carrier
  are already known at emission time, and the events naturally belong to
  those identifiers. No custom grouper, no async lookup, no
  rebuild-on-replay surprise.
- **QA Engineer caught a real chip-label drift.** The QA pass flagged that
  `MapShipmentStatus("DeliveryAttemptFailed")` returns
  `"Delivery Failed"` but `GetDisplayStatus("Delivery Failed")` then maps
  to `"Delivery Issue"`. We confirmed this is **intentional UX softening**
  (the backend status name is harsher than what the customer should see),
  but the test suite now pins both layers explicitly so the divergence is
  visible to anyone touching either switch. The architect would have
  missed this without the QA pass.
- **The audit-only S6 result was the right size for a fine-tuning cycle.**
  We did not paper over the gap with a half-implementation; we documented
  it precisely so the next planning session can weigh it against other
  work.

### Challenges

- **Two `ShipmentDelivered` records (domain event + integration message).**
  When the architect started enriching the domain event with `OrderId`,
  the integration `Messages.Contracts.Fulfillment.ShipmentDelivered`
  already had `OrderId`. Tests that consumed the integration message
  (Returns / Backoffice cross-BC tests) looked alarming at first because
  they appeared to construct the "new" 4-arg form — but they were actually
  constructing the integration message all along. Easy to confuse on a
  first read; flagging here so the next person walks through the type
  closures with `grep -rn "record ShipmentDelivered"` before touching
  either.
- **`ResolveCarrierClaim` had to grow a `LoadAsync` step** to access the
  carrier off the loaded `Shipment` aggregate. This is the right Wolverine
  compound-handler shape (`Before` → `LoadAsync` → `Handle`), but the
  refactor changed the handler signature, so future tests of this command
  need to pass a `Shipment` argument, not just `(command, session)`.
  Existing emitter callers were unaffected (Wolverine resolves `LoadAsync`
  automatically).
- **bUnit auth-state mocking.** The OrderConfirmation page reads
  `AuthenticationStateProvider.GetAuthenticationStateAsync()` in
  `OnInitializedAsync`. The QA Engineer's test pattern uses an
  unauthenticated state so `OnAfterRenderAsync` skips SignalR setup
  cleanly — this is the right shape but slightly non-obvious. Worth
  copying into `docs/skills/bunit-component-testing.md` as a canonical
  example for "pages that conditionally subscribe to SignalR".
- **The Shopping README already documented S6 as a known gap.** The
  PO consultation surfaced something the team had already self-flagged,
  but never elevated above the README footnote. There's a meta-process
  observation here: README "⚠️ not yet implemented" footnotes are good
  for engineers in the file but invisible to PO/architect-level planning.
  The M45.0 charter memo elevates this one to the planning surface;
  similar footnotes elsewhere in the codebase deserve the same treatment.

### What we did not do

- Address S3 (Cross-product exchange end-to-end audit), S4 (Order post-
  placement modifications), S5 (Fraud-review / OnHold saga state), S7
  (Store Credit BC), S8 (Storefront/Vendor Portal a11y + empty-state
  sweep), S9 (Operations Dashboard MVP shape decision), S10 (Vendor
  Portal cold-start framing). Items S3–S5 are charter inputs for the
  Orders/Returns remasters per PO §7.1; S7 is future-BC; S8/S9/S10 are
  separate scopes.
- Implement abandoned-cart recovery (S6). Out of scope for a fine-tuning
  cycle — see the gap memo for the recommended placement.
- Reconcile the `MapShipmentStatus` → `GetDisplayStatus` "Delivery Failed"
  vs. "Delivery Issue" labeling drift identified by QA. Confirmed
  intentional softening of UX copy; both layers are pinned by tests now.
- Re-render integration tests for the projection-keying fix. The pure-
  function `Apply(...)` unit tests cover the projection logic
  exhaustively; full Marten daemon-driven integration coverage would need
  TestContainers and is the natural next step in a Docker-enabled session.

## Code & test totals

| Area                                           | Production files modified | Production files added | Test files added | Net new tests |
| ---------------------------------------------- | ------------------------: | ---------------------: | ---------------: | ------------: |
| Storefront.Web (S1)                            |                         1 |                      0 |                1 |            43 |
| Fulfillment (S2 — events, projections, emitters) |                         8 |                      0 |                2 |            22 |
| Documentation (S6 + retro)                     |                         0 |                      2 |                0 |             0 |
| **Total**                                      |                     **9** |                  **2** |            **3** |        **65** |

| Suite                          | Pass | Fail | Skipped | Total |
| ------------------------------ | ---: | ---: | ------: | ----: |
| Fulfillment.UnitTests          |   62 |    0 |       0 |    62 |
| Storefront.Web.UnitTests (OrderConfirmation only) | 43 | 0 | 0 | 43 |
| Shopping.UnitTests (regression smoke) | 32 | 0 | 0 | 32 |
| Solution build                 |  ✅ 0 errors, 340 warnings *(unchanged)* |

Note: the broader `Storefront.Web.UnitTests` suite has 5 pre-existing
failures in `OrderHistoryTests` (caused by `OrderHistory.razor`'s
`IHttpClientFactory` injection without a matching test mock — flagged by
the QA Engineer as B2). Those are unrelated to this cycle and are listed
as carryover below.

## Carryover into the next cycle

| # | Item                                                                                | Source              | Suggested home                                                |
| - | ----------------------------------------------------------------------------------- | ------------------- | ------------------------------------------------------------- |
| 1 | Abandoned-cart recovery — implement per the M45.0 gap memo                          | S6 / PO §7.1        | Correspondence "lifecycle expansion" cycle                    |
| 2 | `OrderHistoryTests` 5 pre-existing failures (missing `IHttpClientFactory` mock)     | QA Engineer B2      | Single-session test fix; port to the `MockHttpClientFactory` pattern already in `ProductsTests` / new `OrderConfirmationTests` |
| 3 | `MapShipmentStatus` / `GetDisplayStatus` label-drift reconciliation                 | QA Engineer B1      | Optional polish; UX call on whether `"Delivery Failed"` or `"Delivery Issue"` is the canonical chip label |
| 4 | Marten daemon-driven integration tests for `MultiShipmentViewProjection` and `CarrierPerformanceViewProjection` | M45.0 deferred | Docker-enabled session; complements the unit-test pure-function coverage |
| 5 | bUnit "pages that conditionally subscribe to SignalR" pattern in `docs/skills/bunit-component-testing.md` | M45.0 lesson | Skill-doc refresh — small |

Items 4–5 are nice-to-haves; 1–3 are concrete debt.

## Sources

- `docs/research/state-of-repo-2026-05.md` §7.1 (PO sign-off), §7.2 (UXE
  sign-off), §7.3 (synthesis)
- `docs/planning/milestones/fulfillment-remaster-s3-retrospective.md` (S3
  gaps #2 and #4 — the original debt note for S2)
- `docs/planning/milestones/m45-0-abandoned-cart-gap-memo.md` (the S6
  charter input)
- `src/Shopping/Shopping.Api/README.md` (the four "⚠️ not yet implemented"
  footnotes that informed the S6 audit)
- QA Engineer subagent report appended to the M45.0 PR thread

---

*End of retrospective.*
