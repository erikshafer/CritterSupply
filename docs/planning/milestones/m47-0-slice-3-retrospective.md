# M47.0 / Slice 3 Retrospective — Storefront Timeline Display for Cross-Product Exchange Payments

**Date:** 2026-05-11
**Slice goal:** Surface the structured payment metadata added to two integration messages in M47.0 / Slice 2 (`Returns.ExchangeAdditionalPaymentCaptured` and `Returns.ExchangePartialRefundIssued`) all the way through to the customer's real-time UI on `/order-confirmation/{OrderId}`.
**Status:** ✅ Complete (display + tests). MudTimeline component, Backoffice timeline, and persisted history view remain explicit carry-forwards.
**Pairing:** PSA + UXE + QAE + FEE planning round; PSA + FEE single implementation wave; QAE single test wave (no defects, no second iteration).

---

## What Landed

### Storefront BC

**`src/Customer Experience/Storefront/RealTime/StorefrontEvent.cs`**
- New `JsonDerivedType` discriminator `"return-exchange-payment-changed"`.
- New `ReturnExchangePaymentChanged` record carrying:
  `ReturnId`, `OrderId`, `CustomerId`, `PaymentKind` (`"Capture"` | `"Refund"`), `PaymentId`, `Amount`, `Currency`, `PaymentReference`, `OccurredAt`. Implements `IStorefrontWebSocketMessage` so Wolverine's SignalR transport routes it.

**`src/Customer Experience/Storefront/Notifications/ExchangeAdditionalPaymentCapturedHandler.cs`** *(new)*
- Pure static `Handle()` consuming `Messages.Contracts.Returns.ExchangeAdditionalPaymentCaptured`.
- Emits `SignalRMessage<ReturnExchangePaymentChanged>` with `PaymentKind = "Capture"`, scoped to `customer:{CustomerId}`.

**`src/Customer Experience/Storefront/Notifications/ExchangePartialRefundIssuedHandler.cs`** *(new)*
- Mirror handler for the refund path. Maps `OriginalPaymentId` → `PaymentId` and `TransactionId` → `PaymentReference` so both flows share a single SignalR shape over a single discriminator.

### Storefront.Web (Blazor)

**`src/Customer Experience/Storefront.Web/RealTime/StorefrontStatusMapper.cs`**
- New pure helper `BuildExchangePaymentMessage(paymentKind, amount, currency, paymentReference)` — currency-aware copy with defensive fallbacks for unknown ISO codes / unknown payment kinds / null-or-empty references.
- New helper `StorefrontEventReader.GetDecimal(JsonElement, string)` — companion to the existing `GetString` / `GetInt32` for monetary values delivered via SignalR.

**`src/Customer Experience/Storefront.Web/Components/Pages/OrderConfirmation.razor`**
- New `case "return-exchange-payment-changed":` in the SignalR `OnSseEvent` switch — reads the typed fields, sets `_currentStatus = "Exchange Payment Updated"`, sets `_latestUpdate` from the mapper.
- `GetStatusColor` / `GetDisplayStatus` extended with the new status label (rendered as `Color.Success` chip).

### New tests (QA wave)

**`tests/Customer Experience/Storefront.Api.IntegrationTests/SignalRNotificationTests.cs`** — 3 new tests:
- `ExchangeAdditionalPaymentCaptured_Handler_ReturnsGroupScopedExchangePaymentMessage` — happy path, asserts customer isolation + every structured field round-trips.
- `ExchangePartialRefundIssued_Handler_ReturnsGroupScopedExchangePaymentMessage` — happy path, asserts `OriginalPaymentId → PaymentId` and `TransactionId → PaymentReference` mapping.
- `ReturnExchangePaymentChanged_Message_ImplementsSignalRMarkerInterface` — regression guard against accidental marker-interface removal (would silently drop SignalR routing).

**`tests/Customer Experience/Storefront.Web.UnitTests/RealTime/StorefrontStatusMapperExchangePaymentTests.cs`** *(new file, 12 tests)*:
- Capture / Refund happy paths (USD).
- Non-USD currency rendering (EUR).
- Empty / null `paymentReference` → reference clause omitted gracefully.
- **Defensive battery** (QA bug-bounty targets): unknown ISO currency code (`"ZZZ"`), null currency, whitespace currency, lowercase currency normalised, unknown `paymentKind` falls back to a generic but useful line.

---

## How It Went

### What worked

- **Structured event, single discriminator.** The choice to define one new typed event (`ReturnExchangePaymentChanged` with `PaymentKind` field) rather than overloading `ReturnStatusChanged` or shipping two separate events kept the OrderConfirmation switch from doubling and gives FEE one place to evolve future affordances (copy-button, receipt deep-link). Precedent: `ShipmentStatusChanged.TrackingNumber`. UXE confirmed this matches the future "trust UI" direction.
- **Plan-then-execute with the full virtual team.** The PSA + UXE + QAE + FEE round-table at the top of the session caught the currency-formatting trap (UXE: "do not use `RegionInfo` — it throws on unknown ISO codes") *before* implementation, not during. ~2 minutes of planning saved a defect cycle.
- **Mapper-as-pure-function pattern paid out again.** `BuildExchangePaymentMessage` slotted next to `BuildReturnMessage` and `BuildShipmentMessage` with no friction, and unit-tested cleanly without any Blazor / bUnit infrastructure. The H-workshop M46.0 outcome continues to compound.
- **No-defect ping-pong cycle.** PSA wrote handlers, FEE wrote mapper + UI, QAE wrote tests — all green on first run. Total 57 Storefront.Api integration tests pass (54 prior + 3 new); 18 mapper-related unit tests pass (6 prior + 12 new).

### What broke

- **Nothing in the slice itself.** No defects found by QA, no rework required.
- **Pre-existing OrderHistory bUnit failures (5 tests)** were observed during test runs but are unrelated — they reference a missing `IHttpClientFactory` registration in the bUnit test base and pre-date this slice (file last touched in Slice 1). Tracked as a separate cleanup item.

### Trade-offs accepted

- **No first-class MudTimeline component.** The existing OrderConfirmation page renders one rolling `_latestUpdate` banner; we extended it. A persisted, scrollable timeline of all events for an order is a richer UX but also larger scope (needs a server-side projection, a history endpoint, and a new MudBlazor layout). Deferred to a future cycle. UXE: "The single-banner UX still satisfies the slice 2 carry-forward intent — surface the new fields in real time."
- **No Backoffice timeline.** The Returns BC publishes `ExchangeAdditionalPaymentCaptured` / `ExchangePartialRefundIssued` to the storefront fan-out only (lines 185–188 of `Returns.Api/Program.cs` are scoped to `storefront-returns-events`). Adding Backoffice consumers is a separate slice that touches a different BC and a different UI surface.
- **No E2E (Playwright) coverage of the new copy.** No existing Returns E2E to extend cheaply. The handler tests + mapper unit tests give us wiring + copy correctness; an E2E would only verify Blazor's render of pre-validated state.
- **`PaymentId` is carried but not yet displayed.** Reserved for a future "view receipt" deep-link affordance — keeping it on the wire today avoids a contract bump later.

### Things to remember next slice

- **Slice 4 (compensation paths) will reuse the same SignalR event.** When `ExchangeDeltaCaptureFailed` becomes customer-visible, it will likely route a new return-status update (`"Exchange Cancelled"` via `BuildReturnMessage`) rather than a new exchange-payment event — capture failures are *absence* of a charge, not a charge of zero. Confirm with UXE at slice-4 framing.
- **`StorefrontStatusMapper` is now the single home for all storefront copy.** Three helpers (shipment / return / exchange-payment) — any new SignalR consumer (Backoffice timeline, account dashboard) reaches for these. The H-workshop direction continues to hold.
- **Currency formatting:** if/when we want native locale formatting (`$25.00` vs `25.00 USD`), do it via a `Try*` pattern around `RegionInfo` — never directly, because invalid ISO codes will reach the UI eventually.

---

## Numbers

- **Production code:** 5 files changed/created, ~150 net insertions.
  - `StorefrontEvent.cs` (+30 lines: new event type + discriminator)
  - `ExchangeAdditionalPaymentCapturedHandler.cs` (+40 lines, new)
  - `ExchangePartialRefundIssuedHandler.cs` (+42 lines, new)
  - `StorefrontStatusMapper.cs` (+60 lines: new helper + GetDecimal)
  - `OrderConfirmation.razor` (+15 lines: new switch case + status colour/display)
- **Test code:** 2 files touched, 15 new tests (3 handler integration + 12 mapper unit).
- **Tests passing post-slice:** 57 Storefront.Api integration (54 prior + 3 new) + 18 RealTime unit (6 prior + 12 new).
- **Defects found by QA:** 0.
- **Tests removed / `@pending` tags unflagged:** 0 (this slice doesn't close any Gherkin scenarios — those are owned by Slice 4).

---

## Carry-Forward to Future Slices / Cycles

| Item | Owner | Notes |
|------|-------|-------|
| MudTimeline component (true scrollable order history) | UXE + FEE | Needs server-side projection + history endpoint; future cycle |
| Backoffice timeline display of the same events | Backoffice PSA | Add `backoffice-returns-events` queue routing in `Returns.Api/Program.cs`; new Backoffice handlers |
| Persisted timeline / history view per order | Future PSA | Today the OrderConfirmation page is real-time-only — no replay on refresh |
| E2E (Playwright) coverage of the new copy | QA | No existing Returns E2E to extend cheaply |
| Receipt deep-link from `PaymentId` | FEE | Field is already on the wire; UI affordance pending Payments-side public receipt endpoint |
| Pre-existing OrderHistory bUnit `IHttpClientFactory` failures | Storefront QA | Unrelated to this slice; needs a fixture-side service registration tweak |
| PO sign-off on no-interstitial UX deviation | PO + UXE | Unchanged from Slice 2 retro — tracked in ADR 0062 §Open Items |

---

## Slice 3 acceptance criteria — verification

1. ✅ `dotnet build CritterSupply.slnx` succeeds with 0 errors.
2. ✅ Storefront.Api integration suite green (57/57).
3. ✅ Storefront.Web mapper unit tests green (12/12 new + 6/6 pre-existing).
4. ✅ Customer sees the new exchange-payment update on `/order-confirmation/{OrderId}` in real time, with correctly-formatted amount + ISO currency + payment reference.
5. ✅ SignalR scoping is `customer:{CustomerId}` only (regression-guarded).
6. ✅ Unknown / missing currency or reference does not crash the UI (defensive battery in mapper tests).
