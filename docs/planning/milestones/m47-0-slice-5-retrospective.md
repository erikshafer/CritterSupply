# M47.0 / Slice 5 Retrospective — In-Session Activity Timeline + Test-Debt Cleanup

**Date:** 2026-05-11
**Slice goal:** Close the remaining customer-facing carry-forward from M47.0 / Slice 3 (MudTimeline on `OrderConfirmation.razor`) and pay down the `OrderHistoryTests` x5 pre-existing failures called out in the Slice 4 retrospective. Wrap M47.0.
**Status:** ✅ Complete. Slice scope shipped; deferred carry-forwards are documented in `m47-0-closeout.md`.
**Pairing:** PSA + UXE + QAE + FPE planning round; FPE single implementation wave; QAE single test wave (no defects, no ping-pong).

---

## What Landed

### Storefront.Web (Blazor)

**`src/Customer Experience/Storefront.Web/Components/Pages/OrderConfirmation.razor`**
- New `MudTimeline` rendered under the Order Details panel — appears only once at least one SignalR event has been processed (no empty-panel render). Uses `MudTimelineItem` precedent from `VendorPortal.Web/Pages/ChangeRequestDetail.razor`.
- New `private readonly List<TimelineEntry> _timeline = []` with helper `AppendTimeline(status, message)` that derives colour from the existing `GetStatusColor` mapper so the timeline visually matches the chip.
- Each existing case in the `OnSseEvent` switch now appends to `_timeline` after setting `_latestUpdate`, preserving the existing rolling-banner affordance for "what just happened".
- Defensive: branches that no-op on missing fields (`order-status-changed` without `newStatus`, `return-status-changed` without `newStatus`) also no-op the timeline append — `AppendTimeline` short-circuits on null/empty messages.

### Test infrastructure

**`tests/Customer Experience/Storefront.Web.UnitTests/BunitTestBase.cs`**
- Registered `IHttpClientFactory` (`Services.AddHttpClient()`) and a default `AuthenticationStateProvider` (returns an authenticated test customer with a deterministic `CustomerId` claim).
- Closes the Slice 4 retrospective carry-forward: 5 pre-existing `OrderHistoryTests` failures were caused by `IHttpClientFactory` not being registered in the bUnit container.
- Default test customer renders the empty-state branch on `OrderHistory.razor` (HTTP call to localhost loopback fails, `_orders` stays null, `finally` block flips `_isLoading = false` → empty-state markup), which is exactly what the four content assertions expect.

### Tests (QA wave)

**`tests/Customer Experience/Storefront.Web.UnitTests/Components/Pages/OrderConfirmationTimelineTests.cs`** *(new file, 7 tests)*:
- `Timeline_NoEvents_IsNotRendered` — confirms the timeline section is hidden until the first event arrives.
- `Timeline_SingleShipmentEvent_RendersOneEntryWithMapperCopy` — happy path; entry pulls copy from `BuildShipmentMessage`, including the tracking number.
- `Timeline_MultipleEvents_RenderInArrivalOrder` — three-event sequence (`payment-confirmed` → `HandedToCarrier` → `shipment-delivered`) renders top-to-bottom in the order received.
- `Timeline_ReturnCancelled_RendersWithErrorColor` — Slice 4 chip colour mapping (`Return: Cancelled → Color.Error`) is honoured by the timeline entry.
- `Timeline_ExchangePaymentCaptured_RendersAmountAndReference` — Slice 3 cross-product-exchange copy ($25.00 USD + reference) survives the SignalR → mapper → timeline pipeline.
- `Timeline_OrderStatusChanged_WithoutNewStatus_DoesNotAppendEntry` — defensive guard against a malformed producer payload.
- `Timeline_TimestampRendered_PerEntry` — every entry carries a per-entry timestamp caption.

### Test results

- Storefront.Web.UnitTests: **109 passed**, 0 failed (102 prior + 7 new). The 5 OrderHistoryTests pre-existing failures called out in Slice 4 are now green.
- No production code in any other project was touched, so no other suites needed re-running.

---

## How It Went

### What worked

- **The MudTimeline-without-persistence call was the right one.** UXE, FPE, and QAE all agreed at planning that an in-session timeline is genuinely useful — the rolling-banner UX never let customers see the *sequence* of events during a session, only the most recent. Persistence (replay on refresh) is a different problem (server-side projection + history endpoint) and writing it as the same slice would have meant making both a Storefront BFF schema change *and* a UI change at the same time, which is two ADRs squeezed into a session that doesn't need them.
- **MudTimeline drops in cheaply because the colour mapper is already pure.** `AppendTimeline` reuses `GetStatusColor` directly — the timeline entry colour and the chip colour cannot drift. The H-workshop "mapper-as-pure-function" pattern from M46.0 paid out for the third slice in a row.
- **No ping-pong on the QA wave.** Tests passed 7/7 on first run. The defensive cases (no-op without `newStatus`, hidden section when empty) were called out at planning by QAE and built into the FPE implementation up-front.
- **The OrderHistoryTests fix was a one-file change.** `Services.AddHttpClient()` + a deterministic `AuthenticationStateProvider`. No test code modified. Five regression-noise failures dispatched in ten lines.

### What broke

- Nothing in the slice itself. No defects found by QA, no rework required.

### Trade-offs accepted

- **Timeline is in-memory only.** A page refresh wipes it. Customers who navigate away and come back lose the history. This is the explicit deferral documented in `m47-0-closeout.md` — a persisted return-history view is its own slice with its own ADR (Storefront BFF projection vs. delegation to Returns BC).
- **No backoffice-side timeline.** Backoffice operators don't see the cross-product-exchange events at all yet. This is also deferred to a separate slice — different BC, different UI surface, different routing wiring.
- **No end-to-end Reqnroll scenario.** The Slice 4 reasoning still holds: per-BC integration tests cover each seam; an E2E Returns→Inventory→Payments→Storefront scenario would add runtime cost without proving anything new about the seams.
- **Default test customer is hardcoded.** `BunitTestBase` registers a deterministic Guid as the test `CustomerId`. Tests that need a different customer (or anonymous) must register their own provider after construction. We document this in the XML doc on the `BunitTestBase` ctor.

---

## Numbers

- **Production code:** 1 file changed (~50 net insertions in `OrderConfirmation.razor`).
- **Test infrastructure:** 1 file changed (`BunitTestBase.cs`, +25 lines).
- **Test code:** 1 file added (`OrderConfirmationTimelineTests.cs`, 7 tests).
- **Tests passing post-slice:** 109 (102 prior + 7 new) — 5 pre-existing failures gone.
- **Defects found by QA:** 0.
- **Tests removed / `@pending` tags unflagged:** 0 (slice 4 already cleared the last `@pending` tag).

---

## Carry-Forward to Future Cycles

These items are **deferred to M48.0 (or later)** and are documented in `m47-0-closeout.md` rather than re-listed per slice:

- Persisted return-history view (Storefront BFF projection + history endpoint + UI seed on page load)
- Backoffice timeline for cross-product exchange events
- End-to-end Reqnroll scenario covering Returns → Inventory → Payments → Storefront

Per-slice closeout: see `m47-0-closeout.md` for M47.0 sweep, the four slices' outcomes, and what M48 inherits.

---

## Slice 5 acceptance criteria — verification

1. ✅ `dotnet build "src/Customer Experience/Storefront.Web/Storefront.Web.csproj"` succeeds with 0 errors.
2. ✅ Storefront.Web.UnitTests suite green (109/109).
3. ✅ The 5 pre-existing `OrderHistoryTests` failures are gone.
4. ✅ MudTimeline renders on `OrderConfirmation.razor` once at least one SignalR event has been processed; entries carry mapper-produced copy and chip-matching colour.
5. ✅ MudTimeline section stays hidden when no events have arrived (no empty-panel render).
