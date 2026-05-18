# M48.0 Session 3b Retrospective — Channels / Vendor / Admin Closeout

**Date:** 2026-05-15
**Milestone:** M48.0 — CritterSupply Business Architecture Extraction
**Session:** Session 3b — Promote the 4 dossiers deferred from S3 to S2 — full depth

## Outcome (executive summary)

S3b closed cleanly: **all 4 deferred channel/vendor/admin dossiers were promoted to S2 — full depth in place** — Backoffice (Variant C-hybrid), Pricing (Variant A — DCB-claim refuted), Promotions (Variant A — DCB-claim verified), Correspondence (Variant A — lightest). With this session, **all 18 BC dossiers exist at S2-full depth**. S3 is closed across S3 + S3b; M48.0 advances to S4 (cross-BC workflow tracing).

The 4 dossiers landed surface a substantial new layer of code-vs-S1 / code-vs-EM / code-vs-CONTEXTS.md drift that was not visible at stub depth — including two notable findings: **Pricing does NOT use Marten DCB** despite the S1 stub's wording suggesting otherwise (Pricing uses plain `Guid` stream ids with deterministic UUID v5 derivation, not v7), and **Promotions DOES use DCB end-to-end** (full tag-type + boundary-query + retry-policy wiring). These two findings together establish that the DCB-or-not claim must be verified per BC against source, never inherited from prose.

## Baseline

- Build at session open: **No code changed since S3a close** (S3a closed at 0 errors / 456 warnings incremental). S3b touched only files under `docs/`.
- Build at session close: identical — no code touched.
- Files changed: 7 — 4 BC dossiers, this retrospective, `docs/extraction/README.md` status table, `docs/planning/CURRENT-CYCLE.md`.

## Items Completed

| Item | Description | File | Counts |
|------|-------------|------|--------|
| S3f | Backoffice dossier (Variant C-hybrid: BFF + 1 ES aggregate) | `docs/extraction/bcs/backoffice.md` (369 lines) | 1 ES aggregate (`OrderNote`, UUID v7) / 3 events / 4 commands + 1 HTTP-only proxy / 6 projections (1 snapshot + 5 BFF) / 24 inbound RabbitMQ events across 21 handlers (Orders ×4, Payments ×2, Inventory ×3, Returns ×5, Fulfillment ×5, Catalog ×2, Correspondence ×3) / 0 outbound integration events / 24 HTTP endpoints across 9 feature folders / 22 Razor pages / 1 SignalR hub `/hub/backoffice` with 5 message types (2 emitted, 3 declared-only) / 9 composition-map BCs in CONTEXTS.md + 1 drift (Payments) / 137 E2E + 47 BDD scenarios |
| S3g | Pricing dossier (Variant A — DCB claim refuted) | `docs/extraction/bcs/pricing.md` (213 lines) | 1 aggregate (`ProductPrice`, **UUID v5 from `pricing:{SKU}` — NOT v7 as S1 said**) + 2 value objects + 1 read model / 6 commands (5 user-facing + 1 internal scheduled — S1 said 7; the 7th was a misclassified event) / 10 events (4 declared-but-unemitted: `FloorPriceSet`, `CeilingPriceSet`, `PriceCorrected`, `PriceDiscontinued`) / 2 inline projections / 3 outbound contracts defined / **0 wired** / 1 inbound (`ProductCatalog.ProductAdded`) |
| S3h | Promotions dossier (Variant A — DCB claim verified) | `docs/extraction/bcs/promotions.md` (227 lines) | 2 aggregates: `Promotion` (UUID v7 generated at command time) + `Coupon` (UUID v5 from `promotions:coupon:{code.ToUpperInvariant()}`) / **DCB confirmed**: full tag types `PromotionStreamId`/`CouponStreamId`, `opts.Events.RegisterTagType<T>().ForAggregate<TAggregate>()`, `DcbConcurrencyException` retry policy, `EventTagQuery` + `[BoundaryModel]` + `IEventBoundary<T>` in `RedeemCouponHandler` / 8 commands / 12 events (5 declared-but-unemitted: 4 Promotion lifecycle + `CouponExpired`) / 3 projections / 0 outbound integration contracts / 1 inbound (`Orders.OrderPlaced` — Phase-1 no-op) / 2 synchronous HTTP edges with Shopping (`ValidateCoupon`, `CalculateDiscount`) |
| S3i | Correspondence dossier (Variant A — lightest) | `docs/extraction/bcs/correspondence.md` (187 lines) | 1 aggregate (`Message`, UUID v7 — no static `StreamId(...)`) / 1 command (`SendMessage`) / 4 events (1 declared-but-unemitted: `MessageSkipped`) / 2 inline projections / 3 outbound contracts × 2 queues each = 6 publish routes (M33.0 added Backoffice operations queues) / **12 inbound handlers — not ~13 as S1 said** (Orders ×1 only, no `OrderCancelled` handler; Fulfillment ×6, Returns ×4, Payments ×1) / `IEmailProvider` + `ISmsProvider` + `IPushProvider`-not-defined; stub-only implementations registered as singletons; no production providers in tree; no feature-flag gating |

## Per-BC Dossier Notes

### Backoffice (S3f — Variant C-hybrid; heaviest in S3)

- 1 ES aggregate (`OrderNote`, UUID v7 minted in `AddOrderNoteEndpoint.cs:52`); 3 events; 4 commands + 1 HTTP-only proxy `CancelOrderCommand`. ADR 0037 governs the OrderNote-in-Backoffice ownership choice.
- 6 Marten projections (1 OrderNote snapshot + 5 BFF: `AdminDailyMetrics`, `AlertFeedView`, `ReturnMetricsView`, `CorrespondenceMetricsView`, `FulfillmentPipelineView`).
- 24 inbound RabbitMQ events across 21 notification handlers from 7 BCs. 0 outbound integration events — Backoffice publishes nothing cross-BC.
- 24 HTTP endpoints across 9 feature folders; 22 Razor pages.
- SignalR hub at `/hub/backoffice` with role-based groups; 5 message types (2 emitted, 3 declared-only).
- M46.0 operations-health endpoint `GET /api/backoffice/operations/dead-letters/summary` reads `wolverine_dead_letters` across all schemas (`OperationsHealth/GetDeadLetterSummary.cs`); endpoint opens a fresh `NpgsqlConnection` from the configured `postgres` connection string (per the marten-connection-sharing memory).
- Composition map: 9 upstream BCs in CONTEXTS.md + 1 drift (Payments — see CONTEXTS.md drift below).
- 137 E2E + 47 BDD scenarios across `docs/features/backoffice/` and the E2E suite.
- **EM-revised divergences** (`backoffice-event-modeling-revised.md` lines 478-549): `OrderNote` storage (EM said Marten document with composite key; code is event-sourced single-Guid stream per ADR 0037); `AlertAcknowledgment` (EM said separate aggregate; code stores ack as fields on `AlertFeedView`); `EscalationTicket` (Phase-2 in EM, not present in code); Fulfillment event names (EM uses retired `ShipmentDispatched` / `ShipmentDeliveryFailed`; code uses M41.0 successors `ShipmentHandedToCarrier` / `ReturnToSenderInitiated`); EM-tabled-but-missing handlers `RefundCompleted` and `StockReplenished`; code-but-not-EM-tabled handlers `BackorderCreatedHandler`, `GhostShipmentDetectedHandler`, `ShipmentLostInTransitHandler`.
- **CONTEXTS.md drift (forward-notes for S5):**
  1. **Payments** subscription is not in the Backoffice section table (lines 279-289); two handlers + queue subscriptions exist.
  2. **Fulfillment** is described as "queries" only; in fact Backoffice subscribes to 5 Fulfillment events and the typed `IFulfillmentClient` is registered without consumers.
- **Routes-without-instantiator (7 items, forward-notes for S5):**
  1. `CustomerService` policy uses `RequireRole("cs-agent")`; Identity emits `customer-service` — ~10 endpoints reachable only by `system-admin`. (Compounds the S3e role-claim-case finding.)
  2. `ProductManager` policy maps to `product-manager` role; `BackofficeRole` enum has no such value (7-role enum). Policy reachable only by `system-admin`.
  3. `IFulfillmentClient` registered in DI, no consumer in `Backoffice.Api/`.
  4. `IBackofficeIdentityClient` registered in DI, no consumer in `Backoffice.Api/` (Web shell calls Identity API directly via `BackofficeIdentityApi` named `HttpClient`).
  5. `IPricingClient` registered with no consumer; `PriceEdit.razor` calls `/api/pricing/...` and `/api/catalog/products/{sku}/...` against the `BackofficeApi` base URL — those paths are not served by `Backoffice.Api`.
  6. 3 SignalR message types declared without instantiator: `ActiveOrderIncremented`, `ActiveOrderDecremented`, `PendingReturnIncremented`.
  7. `Auditor` role — emitted by Identity but admitted by no API policy except the catch-all `Backoffice`.

### Pricing (S3g — Variant A — DCB claim refuted)

- 1 aggregate (`ProductPrice`) + 2 value objects (`Money`, `ScheduledPriceChange`) + 1 read model (`CurrentPriceView`).
- **Stream-ID:** UUID v5 (deterministic, SHA-1 from `pricing:{SKU}`) at `ProductPrice.cs:97-119`. **NOT v7 as S1 said.** ADR 0016 (UUID v5 for natural-key stream IDs) is the implemented convention.
- **DCB claim refuted:** Pricing does **not** use Marten DCB. No `[Tag]` attribute, no DCB tag record, no marker interface, plain `Guid` stream ID. This contradicts a common assumption inherited from S1/CONTEXTS.md prose. Pricing is conventional event-sourced.
- 6 commands (vs S1=7). S1 listed `FloorPriceSet`/`CeilingPriceSet` as "price-bound commands surfaced as records" — they are events, not commands. No `SetFloorPrice`/`SetCeilingPrice` exists.
- 10 events; 4 declared-but-unemitted (`FloorPriceSet`, `CeilingPriceSet`, `PriceCorrected`, `PriceDiscontinued`) — all have aggregate `Apply` branches but no command, scheduled-message, or integration handler emits them.
- 2 inline projections (matches S1).
- Integration: 3 outbound contracts defined / **0 wired** (`PricePublished`, `PriceUpdated`, `VendorPriceSuggestionSubmitted`); 1 inbound (`ProductCatalog.ProductAdded` on queue `pricing-product-added`). S1 called `VendorPriceSuggestionSubmitted` "bidirectional helper contract" — code shows it's one-direction (Vendor Portal → Pricing) per its XML-doc, and unwired on both ends.
- ADR list missing 0016 and 0017 in S1; both added.
- **CONTEXTS.md drift (forward-notes for S5):**
  1. CONTEXTS.md line 234 "Shopping → publishes — Published prices consumed by carts" — no integration publishes wired; Shopping reads via HTTP `GET /api/pricing/products?skus=...`.
  2. CONTEXTS.md line 235 "Vendor Portal ← receives commands; MAP violation alerts pushed back" — no in-tree wiring; no MAP-alert contract exists.
  3. CONTEXTS.md line 233 "Product Catalog ← receives" — partial; only `ProductAdded` subscribed (not `ProductDiscontinued`/`ProductDeleted`/etc.).
  4. CONTEXTS.md line 237 "Bulk pricing uses a saga with approval workflow" — no saga; only `BulkPricingJobId` correlation slot on `PriceChanged`.

### Promotions (S3h — Variant A — DCB claim verified end-to-end)

- 2 aggregates: `Promotion` (UUID v7 generated at command time by `Guid.CreateVersion7()` at `CreatePromotionHandler.cs:9-10`) and `Coupon` (UUID v5 from `promotions:coupon:{code.ToUpperInvariant()}` at `Coupon.cs:81-102`). Two distinct stream-ID strategies in one BC.
- **DCB claim verified:** `PromotionStreamId` and `CouponStreamId` are `public sealed record(Guid Value)` wrappers; both registered via `opts.Events.RegisterTagType<T>(name).ForAggregate<TAggregate>()` at `Program.cs:54-57`; `using Marten.Events.Dcb;` at `Program.cs:10`; `DcbConcurrencyException` retry policy at `Program.cs:86-89`. `RedeemCouponHandler` uses the full DCB API (`EventTagQuery` + `[BoundaryModel]` + `IEventBoundary<CouponRedemptionState>`) end-to-end. **Promotions is the canonical DCB usage in the codebase.**
- 8 commands (matches S1).
- 12 events (Promotion 8 + Coupon 4, matches S1); 5 declared-but-unemitted: `PromotionPaused`, `PromotionResumed`, `PromotionExpired`, `PromotionCancelled`, `CouponExpired`. All have `Apply` branches and are loaded by the DCB boundary query, but no command, scheduled-message, or integration handler emits them.
- 3 projections (matches S1; `CouponLookupViewProjection` is `MultiStreamProjection<CouponLookupView, string>` keyed by code for O(1) validation).
- Integration: **0 outbound** (no `src/Shared/Messages.Contracts/Promotions/` folder; no `PublishMessage<T>` route). 1 inbound (`Orders.OrderPlaced` via `OrderPlacedHandler` — Wolverine convention discovery, no explicit `ListenToRabbitQueue`). 1 in-process choreography (`CouponRedeemed` returned in `OutgoingMessages` from `RedeemCouponHandler` → `RecordPromotionRedemptionHandler`). 2 synchronous HTTP edges from Shopping (`GET /coupons/{code}/validate`, `POST /discounts/calculate`).
- **S1 deviation:** S1 framed `RecordPromotionRedemption` as a live command. Code reality (M40.0): the record is retained for back-compat but no `Handle(RecordPromotionRedemption, ...)` exists; the handler-class instead reacts to the `CouponRedeemed` *event* via choreography. ADR 0058 records the supersession.
- S1 said `RecordPromotionRedemption` "flows in via integration event from Orders" — incorrect. `OrderPlacedHandler` is a Phase-1 no-op returning empty `OutgoingMessages`; XML-doc documents the Phase-2 `RedeemCoupon` fan-out shape.
- **CONTEXTS.md drift (forward-notes for S5):**
  1. CONTEXTS.md lines 301-304 communicates-with table omits the inbound Orders subscription.
  2. CONTEXTS.md line 306 cites M30.1 as the most recent milestone; M40.0 (DCB) is more recent.
- **Routes-without-instantiator:** 1 — the `RecordPromotionRedemption` command + validator are defined and discoverable but no command handler exists and no caller sends it.

### Correspondence (S3i — Variant A — lightest)

- 1 aggregate `Message` (UUID v7; no static `StreamId(...)` derivation; the stream id is the `MessageId` generated in `MessageFactory.Create` via `Guid.CreateVersion7()` at `Message.cs:75`, passed through to Marten as `MartenOps.StartStream<Message>(message.Id, messageQueued)`).
- 1 command (`SendMessage`).
- 4 events; 1 declared-but-unemitted: `MessageSkipped` (wired into the aggregate `Apply` set and the `MessageListView` projection but instantiated only by `MessageFactory.Skip`, called only from a unit test at `MessageAggregateTests.cs:69`).
- 2 inline projections (matches S1).
- Integration: 3 outbound (`CorrespondenceQueued`, `CorrespondenceDelivered`, `CorrespondenceFailed`); each routed to two queues (original monitoring/analytics/admin set + M33.0 Backoffice operations queues = 6 publish routes total).
- **12 inbound handlers — not ~13 as S1 said.** Breakdown: Orders ×1 (`OrderPlaced` only — **no `OrderCancelled` handler** despite CONTEXTS.md and S1 listing it); Fulfillment ×6; Returns ×4; Payments ×1.
- Provider abstractions: `IEmailProvider` + `ISmsProvider` interfaces, single `ProviderResult` (`Success`/`ProviderId`/`FailureReason`/`IsRetriable`), three message records (`EmailMessage`, `SmsMessage`, `PushMessage`). Stub-only implementations (`StubEmailProvider`, `StubSmsProvider`); both registered as singletons unconditionally in DI — no feature-flag gating, no production providers in tree, no `IPushProvider` interface despite `PushMessage` existing.
- **CONTEXTS.md drift (forward-notes for S5):**
  1. CONTEXTS.md line 250 names `ShipmentDispatched` (no handler exists; the M41.0 successor `ShipmentHandedToCarrier` is what is subscribed).
  2. CONTEXTS.md line 250 lists 3 Fulfillment subscriptions; code subscribes to 6 (`BackorderCreated`, `DeliveryAttemptFailed`, `ShipmentLostInTransit` are absent from CONTEXTS.md).
  3. M33.0 Session-2 outbound routes onto three `backoffice-correspondence-*` queues are present in code but not in CONTEXTS.md's Correspondence entry.
- **Routes-without-instantiator:**
  1. `ISmsProvider`/`StubSmsProvider` registered in DI but no handler resolves the interface; every inbound handler hard-codes `channel: "Email"` and `SendMessageHandler` depends on `IEmailProvider` only.
  2. `PushMessage` record defined; no `IPushProvider` interface or implementation exists.

## Confirmation Checks (over the 4 dossiers written this session)

- **Banned evaluative language:** `grep -wEi 'good|bad|awkward|elegant|should|nicely|ugly|better|worse|properly|unfortunately'` over the 4 newly-written dossiers returns **no matches**. (Each sub-agent verified its own file before reporting back; one quoted XML-doc carrying "should" was paraphrased before commit by the Promotions sub-agent.)
- **Sibling-project / project-level successor framing:** `grep -wEi 'critterbids|crittercab'` returns **no matches**. The word "successor" is used only in the in-BC code-history sense explicitly permitted by the prompt.
- **Source-cited file paths for behavioral claims:** every behavioral claim in every written dossier source-cites a specific file (with line range where applicable). Structural lists cite the folder.
- **Routes-without-instantiator:** all such items discovered (Backoffice 7, Pricing 5 — counting the 4 unemitted events + 1 outbound, Promotions 1, Correspondence 2) are documented descriptively in the relevant dossier sections.
- **CONTEXTS.md drift:** every drift discovered is recorded descriptively in the relevant dossier (with code as authoritative) and forwarded for S5 in the per-BC notes above.
- **DCB verification per BC:** Pricing source-checked and refuted; Promotions source-checked and verified end-to-end. Treat as a method to apply during S5 if any other BC is implicitly assumed to use DCB.

## Cross-Reference Forward to S4

S3b work surfaced these non-obvious S4 workflow dependencies:

- **Backoffice notification fan-in workflow** — 24 inbound RabbitMQ events from 7 BCs power the operator dashboards. S4 candidate: fan-in trace for each major operator workflow (order-monitoring, returns-management, customer-service-history, fulfillment-pipeline) end-to-end, with BFF projections' source events visible per workflow.
- **Pricing publish gap** — 3 outbound contracts defined / 0 wired means Shopping's "consumes published prices" CONTEXTS.md narrative is implemented as **synchronous HTTP** (`GET /api/pricing/products?skus=...`), not RabbitMQ. S4 cart-time pricing trace must follow the HTTP edge, not the published events.
- **Promotions cart-time integration** — 2 synchronous HTTP edges (`ValidateCoupon`, `CalculateDiscount`) plus 1 in-process command (`RedeemCoupon` published from Shopping after order placement, choreographing into `RecordPromotionRedemption`) — S4 cart-checkout-promotion trace spans both edges.
- **Correspondence fan-in workflow** — 12 inbound handlers across 4 BCs for transactional messaging. S4 candidate: at least one notification traced end-to-end (e.g., order-placed → email-queued → email-delivered → backoffice-notified).

## Cross-Reference Forward to S5

Combined with the S2 + S2b + S3a forward-notes, the S5 starting inventory now contains the items below. **New from S3b in bold.**

- (carried) CONTEXTS.md Customer Experience integration table omits Inventory and Returns edges.
- (carried) CONTEXTS.md line 158 description of `AssignProductToVendor` as the "sole remaining document-store write path" is stale.
- (carried) CONTEXTS.md and `Fulfillment.Api/README.md` narrative diagrams still reference retired `ShipmentDispatched` / `ShipmentDeliveryFailed` events.
- (carried) Fulfillment integration contracts with no instantiator (`DeliveryAttemptFailed`, `GhostShipmentDetected`, `ItemPicked`).
- (carried) Returns `ReturnStatus` declared-but-unused values (`LabelGenerated`, `InTransit`).
- (carried from S3a) Listings recall cascade scope mismatch.
- (carried from S3a) Marketplaces document type undercount; `OrphanedEbayDraft` + `SweepOrphanedEbayDraftsHandler` unmentioned; `ProductSummaryView` misclassified.
- (carried from S3a) Marketplaces communicates-with table direction asymmetry.
- (carried from S3a) Vendor Identity `VendorUserActivated` route-without-instantiator; refresh tokens not server-persisted; `InvitationStatus.Expired` declared-but-unused; PBKDF2 vs prescribed Argon2id.
- (carried from S3a) Vendor Identity CONTEXTS.md entry omits 11 published events, `VendorTenantStatus` machine, ADR 0032.
- (carried from S3a) Vendor Portal document type overcount in S1.
- (carried from S3a) Vendor Portal CONTEXTS.md direction-inversion bug (`InventoryAdjusted`/`LowStockDetected`/`StockReplenished`).
- (carried from S3a) Vendor Portal CONTEXTS.md neighbour list omits Inventory + Product Catalog.
- (carried from S3a) Vendor Portal change-request contracts: 3 outbound + 7 inbound + 1 realtime declared without producers/consumers.
- (carried from S3a) Vendor Portal has zero named authorization policies.
- (carried from S3a) Backoffice Identity role-claim case mismatch (kebab vs Pascal). Worth functional verification.
- (carried from S3a) Backoffice Identity password hashing PBKDF2 vs prescribed Argon2id.
- **(new — S3b/Backoffice) CONTEXTS.md Backoffice section omits Payments subscription edge; describes Fulfillment as queries-only despite 5 inbound subscriptions.**
- **(new — S3b/Backoffice) Two authorization policies dead-coded: `CustomerService` policy maps to `cs-agent` (Identity emits `customer-service`); `ProductManager` policy maps to `product-manager` (no such enum value). ~10 + several endpoints reachable only by SystemAdmin.** This compounds the S3a Backoffice Identity role-claim case mismatch.
- **(new — S3b/Backoffice) `IFulfillmentClient`, `IBackofficeIdentityClient`, `IPricingClient` registered in DI without any consumer in `Backoffice.Api/`. `Auditor` role emitted but admitted by no policy.**
- **(new — S3b/Backoffice) 3 SignalR message types declared without instantiator: `ActiveOrderIncremented`, `ActiveOrderDecremented`, `PendingReturnIncremented`.**
- **(new — S3b/Backoffice) EM-revised divergences: `OrderNote` storage, `AlertAcknowledgment`, `EscalationTicket`, retired Fulfillment event names, EM-tabled-but-missing `RefundCompleted`/`StockReplenished`, code-but-not-EM-tabled `BackorderCreatedHandler`/`GhostShipmentDetectedHandler`/`ShipmentLostInTransitHandler`.**
- **(new — S3b/Pricing) Stream ID is UUID v5 from `pricing:{SKU}`, NOT UUID v7 as S1 stub said. ADR 0016 governs.**
- **(new — S3b/Pricing) Pricing does NOT use Marten DCB despite implicit assumptions. Plain `Guid` stream ids, no DCB tag types, no DCB Marten config.**
- **(new — S3b/Pricing) 4 declared-but-unemitted events (`FloorPriceSet`, `CeilingPriceSet`, `PriceCorrected`, `PriceDiscontinued`) — `Apply` branches exist but no emitter sites.**
- **(new — S3b/Pricing) 3 outbound contracts defined / 0 wired: `PricePublished`, `PriceUpdated`, `VendorPriceSuggestionSubmitted`. CONTEXTS.md narrative implies a publish edge to Shopping that does not exist.**
- **(new — S3b/Pricing) S1 commands list contained 2 misclassifications (`FloorPriceSet`, `CeilingPriceSet` are events, not commands).**
- **(new — S3b/Pricing) CONTEXTS.md line 237 "Bulk pricing uses a saga with approval workflow" — no saga; only a `BulkPricingJobId` correlation slot on `PriceChanged`.**
- **(new — S3b/Promotions) Promotions DOES use DCB end-to-end (verified). It is the canonical DCB usage in the codebase.**
- **(new — S3b/Promotions) 5 declared-but-unemitted events: `PromotionPaused`, `PromotionResumed`, `PromotionExpired`, `PromotionCancelled`, `CouponExpired`.**
- **(new — S3b/Promotions) `RecordPromotionRedemption` command record retained for back-compat but no handler exists; the handler-class instead reacts to the `CouponRedeemed` event via choreography (ADR 0058).**
- **(new — S3b/Promotions) `OrderPlacedHandler` is a Phase-1 no-op despite S1 framing it as the redemption-recording trigger.**
- **(new — S3b/Promotions) CONTEXTS.md most-recent-milestone for Promotions is M30.1; M40.0 (DCB) is later.**
- **(new — S3b/Correspondence) S1 listed Orders ×2 (including `OrderCancelled`); only `OrderPlaced` handler exists. CONTEXTS.md line 250 references the retired `ShipmentDispatched` event name.**
- **(new — S3b/Correspondence) 1 declared-but-unemitted event: `MessageSkipped` (only emitted from a unit test).**
- **(new — S3b/Correspondence) Provider abstractions stub-only; no production `IEmailProvider`/`ISmsProvider` implementations in tree; no `IPushProvider` interface despite `PushMessage` existing; no feature-flag gating.**

**S5 method addition:** DCB-or-not must be verified per BC against source — never inherited from prose. Pricing S1 implied DCB; refuted. Promotions S1 implied DCB; verified.

## Build State at Session Close

- Errors: 0 (delta from baseline: 0)
- Warnings: unchanged from S3a close (no code touched)
- Files changed: 7 — 4 BC dossiers, this retrospective, `docs/extraction/README.md` status table, `docs/planning/CURRENT-CYCLE.md`. No code changes; no project file changes.

## Verification Checklist

- [x] All 4 deferred channel/vendor/admin dossiers exist at full S2 depth in place.
- [x] **All 9 channel/vendor/admin dossiers exist at full S2 depth in place** across S3 + S3b.
- [x] **All 18 BC dossiers exist at full S2 depth in place** across S2 + S2b + S3 + S3b.
- [x] Variant assignments hold: Backoffice Variant C-hybrid; Pricing / Promotions / Correspondence Variant A.
- [x] Every stream-ID claim in the 4 written dossiers is verified against source. **Pricing UUID v5 (NOT v7); Promotions two strategies verified; Backoffice OrderNote UUID v7; Correspondence Message UUID v7.**
- [x] Every command count direct-enumerated. **Pricing reconciled S1=7 → 6; Backoffice reconciled to 4 + 1 HTTP-only proxy; Promotions matches S1=8; Correspondence matches S1=1.**
- [x] Every behavioral claim source-cites a specific file.
- [x] Routes-without-instantiator items discovered are documented descriptively (Backoffice 7, Pricing 5 incl. unemitted events, Promotions 1, Correspondence 2).
- [x] CONTEXTS.md drift discovered is recorded descriptively (code authoritative) and consolidated for S5 above.
- [x] DCB-or-not verified per BC against source: Pricing refuted, Promotions verified.
- [x] No written dossier contains evaluative language (verified by grep).
- [x] No written dossier references CritterBids, CritterCab, or any project-level successor framing (verified by grep).
- [x] No written dossier frames itself as preparation for a downstream operation.
- [x] `docs/extraction/README.md` status table reflects all 9 S3 rows at S2-full and overall status as 18-of-18.
- [x] `CURRENT-CYCLE.md` updated to reflect S3 closeout and S4 as next.
- [x] This retrospective committed.
- [x] EM reconciliation completed for Backoffice (`backoffice-event-modeling-revised.md`).
- [x] Build state confirmed: no code changed since S3a close; warnings unchanged.

## What Remains / Next Session

**S4 — cross-BC workflow tracing** is next.

S4 starting inventory now includes the per-workflow trace candidates surfaced across S2 + S2b + S3 + S3b above (recall cascade through Listings → Marketplaces; cross-product exchange across Returns ↔ Inventory ↔ Payments ↔ Storefront; vendor onboarding through Vendor Identity → Vendor Portal; marketplace adapter submission round-trip; Backoffice notification fan-in for the four major operator workflows; Pricing cart-time HTTP edge; Promotions cart-checkout-redemption span across 2 HTTP + 1 in-process command; Correspondence transactional messaging fan-in).

S5 starting inventory has expanded substantially across S3b — see Cross-Reference Forward to S5 above for the consolidated drift / route / unemitted-event list.

Milestone status: **M48.0 — S1 + S2 (with S2b) + S3 (with S3b) complete; S4 / S5 / S6 ahead.**
