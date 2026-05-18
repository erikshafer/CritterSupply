# Promotions

> **Source folder:** `src/Promotions/`
> **Status:** Implemented
> **Most recent material milestone:** M40.0 — Dynamic Consistency Boundary: Promotions Coupon Redemption (`docs/decisions/0058-dcb-promotions-coupon-redemption.md`)
> **Dossier depth:** S2 — full

## Purpose

Promotions owns promotional campaigns and coupon codes. A campaign — the `Promotion` aggregate — is created in `Draft`, manually activated, and tracks redemption against an optional `UsageLimit`. From a campaign one or many coupons — the `Coupon` aggregate — are issued (singly via `IssueCoupon` or in bulk via `GenerateCouponBatch`) and then validated, redeemed, or revoked. Promotions exposes two synchronous HTTP query endpoints (`ValidateCoupon`, `CalculateDiscount`) that the Shopping BC calls at cart time, and a single Wolverine handler subscribed to `Orders.OrderPlaced` for downstream redemption fan-out (currently a Phase-1 no-op until Shopping propagates applied-coupon data through the integration message).

## Aggregates

Two event-sourced aggregates share the BC, with two distinct stream-ID derivation strategies.

### `Promotion`

- **Stream ID:** UUID v7 (time-ordered, non-deterministic). Generated at command time by `Guid.CreateVersion7()` inside `CreatePromotionHandler.Handle` (`src/Promotions/Promotions/Promotion/CreatePromotionHandler.cs#L9-L10`). The aggregate has no `StreamId(...)` factory method — the value originates at the command site, is then echoed into the `PromotionCreated` event payload (`PromotionId` field) and reused as the Marten stream id (`session.Events.Append(promotionId, wrapped)` at `CreatePromotionHandler.cs#L29`). The class XML-doc records the choice ("Stream key: UUID v7 (time-ordered) for natural chronological ordering of promotions") at `src/Promotions/Promotions/Promotion/Promotion.cs#L4-L5`.
- **DCB tag:** `PromotionStreamId(Guid Value)` strong-typed wrapper at `src/Promotions/Promotions/PromotionStreamId.cs#L9`. Registered against the aggregate via `opts.Events.RegisterTagType<PromotionStreamId>("promotion").ForAggregate<Promotion>()` at `src/Promotions/Promotions.Api/Program.cs#L56-L57`. Every event written to a `Promotion` stream is wrapped with `session.Events.BuildEvent(evt)` and tagged with `wrapped.AddTag(new PromotionStreamId(promotionId))` before `session.Events.Append` (`CreatePromotionHandler.cs#L27-L29`, `ActivatePromotionHandler.cs#L54-L56`, `GenerateCouponBatchHandler.cs#L61-L63`, `RecordPromotionRedemptionHandler.cs#L38-L40`). The XML-doc on `PromotionStreamId` records why the wrapper exists: a bare `Guid` cannot be used as a Marten DCB tag type because it has two public properties in .NET 10 (`PromotionStreamId.cs#L4-L7`).
- **Key state:** `Id` (`Guid`), `Name`, `Description`, `DiscountType` (enum), `DiscountValue` (decimal — percentage 0-100 in Phase 1; XML-doc at `Promotion.cs#L32-L38` records that `Money` is deferred to Phase 2), `Status` (`PromotionStatus`), `StartDate`, `EndDate`, `UsageLimit?` (null = unlimited; XML-doc records the optimistic-concurrency enforcement intent at `Promotion.cs#L57-L60`), `CurrentRedemptionCount`, `CreatedAt`, `ActivatedAt?`, `PausedAt?` (`src/Promotions/Promotions/Promotion/Promotion.cs#L11-L83`).
- **Lifecycle stages** (the `PromotionStatus` enum at `src/Promotions/Promotions/PromotionStatus.cs#L7-L14`):
  - `Draft` — set by `Create()` factory and the `Apply(PromotionCreated)` branch (`Promotion.cs#L86-L132`). Entered when `CreatePromotion` is handled.
  - `Active` — set by `Apply(PromotionActivated)` (`Promotion.cs#L134-L139`) and `Apply(PromotionResumed)` (`L148-L153`). The `Active` transition from `Draft` is the only one with a command emitter (`ActivatePromotionHandler.Handle` at `src/Promotions/Promotions/Promotion/ActivatePromotionHandler.cs#L40-L57`); the `Paused → Active` transition has no `ResumePromotion` command in the codebase.
  - `Paused`, `Expired`, `Cancelled` — `Apply` branches exist (`Promotion.cs#L141-L165`) but no command, scheduled message, or integration handler emits the corresponding events. See §Declared-but-unemitted events.
- **File:** `src/Promotions/Promotions/Promotion/Promotion.cs`.

### `Coupon`

- **Stream ID:** Deterministic UUID v5 (RFC 4122 §4.3, SHA-1 + URL namespace UUID) computed by the static `Coupon.StreamId(string code)` factory at `src/Promotions/Promotions/Coupon/Coupon.cs#L81-L102`. The implementation hashes `promotions:coupon:{code.ToUpperInvariant()}` against the URL namespace UUID `6ba7b810-9dad-11d1-80b4-00c04fd430c8`, then forces the version-5 nibble at byte 6 (`(hash[6] & 0x0F) | 0x50`) and the RFC 4122 variant at byte 8 (`(hash[8] & 0x3F) | 0x80`). Uppercase normalization makes the lookup case-insensitive (`"holiday2026"` and `"HOLIDAY2026"` resolve to the same stream). The class XML-doc records the deterministic-idempotency rationale at `Coupon.cs#L7-L9, L74-L80`.
- **DCB tag:** `CouponStreamId(Guid Value)` at `src/Promotions/Promotions/CouponStreamId.cs#L9`. Registered via `opts.Events.RegisterTagType<CouponStreamId>("coupon").ForAggregate<Coupon>()` at `src/Promotions/Promotions.Api/Program.cs#L54-L55`. Every event written to a `Coupon` stream is tagged with `wrapped.AddTag(new CouponStreamId(streamId))` before append (`IssueCouponHandler.cs#L55`, `RevokeCouponHandler.cs#L53`, and via the boundary at `RedeemCouponHandler.cs#L111-L112`).
- **Key state:** `Id` (`Guid`, derived from `Code`), `Code` (uppercase-normalized at the `Apply(CouponIssued)` boundary — `Coupon.cs#L107-L108`), `PromotionId`, `Status` (`CouponStatus`), `IssuedAt`, `RedeemedAt?`, `OrderId?`, `CustomerId?` (`Coupon.cs#L11-L54`).
- **Lifecycle stages** (the `CouponStatus` enum at `src/Promotions/Promotions/CouponStatus.cs#L7-L13`):
  - `Issued` — set by the `Create()` factory (`Coupon.cs#L60-L72`) and the `Apply(CouponIssued)` branch (`L104-L112`). Entered when `IssueCoupon` is handled.
  - `Redeemed` — set by `Apply(CouponRedeemed)` (`Coupon.cs#L114-L121`). Terminal for single-use (Phase 1; XML-doc at `Coupon.cs#L31-L33` notes multi-use is a future extension). Emitter: the DCB-aware `RedeemCouponHandler.Handle` (`src/Promotions/Promotions/Coupon/RedeemCouponHandler.cs#L92-L118`).
  - `Revoked` — set by `Apply(CouponRevoked)` (`Coupon.cs#L123-L127`). Emitter: `RevokeCouponHandler.Handle` (`src/Promotions/Promotions/Coupon/RevokeCouponHandler.cs#L39-L55`).
  - `Expired` — `Apply` branch exists (`Coupon.cs#L129-L133`) but no command or scheduled-message handler emits `CouponExpired`. See §Declared-but-unemitted events.
- **File:** `src/Promotions/Promotions/Coupon/Coupon.cs`.

### DCB confirmation

Both stream-id strategies are wired through Marten's Dynamic Consistency Boundary infrastructure. `Program.cs#L34-L57` calls `RegisterTagType<TStreamId>(name).ForAggregate<TAggregate>()` for each pair, and `Program.cs#L86-L89` registers a Wolverine retry policy for `DcbConcurrencyException` (`using Marten.Events.Dcb;` at `Program.cs#L10`). The DCB write path is exercised by `RedeemCouponHandler`, which uses Marten's native `EventTagQuery` + `[BoundaryModel]` + `IEventBoundary<T>` API to load events from both the `Coupon` and `Promotion` streams, project them into `CouponRedemptionState`, and append `CouponRedeemed` with cross-stream optimistic concurrency (`AssertDcbConsistency` at `SaveChangesAsync` time — XML-doc at `RedeemCouponHandler.cs#L86-L91`). All other handlers tag events for the DCB tag table without using the boundary API; their concurrency control is per-stream `FetchForWriting<T>` (`ActivatePromotionHandler.cs#L47`, `RecordPromotionRedemptionHandler.cs#L29`) or aggregate-existence checks via `AggregateStreamAsync<T>` (`IssueCouponHandler.cs#L13-L42`, `GenerateCouponBatchHandler.cs#L21`, `RevokeCouponHandler.cs#L20`).

### `CouponRedemptionState` (DCB boundary projection)

Projected at boundary-load time inside `RedeemCouponHandler.Load` from events in both the `Coupon` and `Promotion` streams (`src/Promotions/Promotions/Coupon/CouponRedemptionState.cs#L1-L75`). Not registered as a Marten projection in `Program.cs`; Marten constructs the state by reflectively dispatching `Apply(...)` methods over the events returned by the `EventTagQuery` declared at `RedeemCouponHandler.cs#L30-L36`. Carries the union of fields needed to enforce the four redemption invariants — coupon exists, coupon is `Issued`, promotion is `Active`, `CurrentRedemptionCount < UsageLimit` — in a single decision point. M40.0 S1B replaced the prior `ProjectFromCoupon` / `ProjectFromPromotion` static helpers with these `Apply(...)` methods (XML-doc at `CouponRedemptionState.cs#L9-L11`).

## Commands

Direct enumeration via `grep -rn "public sealed record" --include="*.cs" src/Promotions/` produces 29 record declarations; filtering for command-shape records (those handed to a static `Handle`/`LoadAsync`/`Before` method, not events / DTOs / projections / DCB tag types) yields **eight**, matching the S1 stub.

Grouped by aggregate.

### `Promotion` commands

- `CreatePromotion(Name, Description, DiscountType, DiscountValue, StartDate, EndDate, UsageLimit?)` — message-bus only (no HTTP route in this BC). Validator enforces non-empty name (≤200 chars), non-empty description (≤1000 chars), positive `DiscountValue`, percentage cap of 100 when `DiscountType == PercentageOff`, `EndDate > StartDate`, and positive `UsageLimit` when present (`src/Promotions/Promotions/Promotion/CreatePromotionValidator.cs#L7-L42`). Handler generates a UUID v7 promotion id, builds + tags the `PromotionCreated` event, and appends to a new stream (`src/Promotions/Promotions/Promotion/CreatePromotionHandler.cs#L7-L30`). No `StartStream` call — the comment at `L24-L26` records that pre-wrapped `IEvent` objects can lose tags through `StartStream`, so `Append` is used.
- `ActivatePromotion(PromotionId)` — message-bus only. Validator requires non-empty id (`src/Promotions/Promotions/Promotion/ActivatePromotionValidator.cs#L7-L11`). Compound handler: `LoadAsync` → `Before` (404 if missing, 409 if status not in `{Draft, Paused}`) → `Handle` (`FetchForWriting<Promotion>` for optimistic concurrency, then tagged `Append` of `PromotionActivated`) at `src/Promotions/Promotions/Promotion/ActivatePromotionHandler.cs#L10-L57`.
- `GenerateCouponBatch(PromotionId, Prefix, Count)` — message-bus only. Validator caps the batch at 10,000 coupons and the prefix at 20 chars (`src/Promotions/Promotions/Promotion/GenerateCouponBatchValidator.cs#L7-L24`). Handler validates the parent promotion is `Draft` or `Active`, mints a UUID v7 `BatchId`, fans out `IssueCoupon` commands via `OutgoingMessages`, and appends a `CouponBatchGenerated` event tagged with the parent `PromotionStreamId` (`src/Promotions/Promotions/Promotion/GenerateCouponBatchHandler.cs#L15-L67`). Code format: `{Prefix.ToUpperInvariant()}-{i:D4}` (`L46`).
- `RecordPromotionRedemption(PromotionId, OrderId, CustomerId, CouponCode, RedeemedAt)` — message-bus only; **superseded by choreography in M40.0**. The record is retained (`src/Promotions/Promotions/Promotion/RecordPromotionRedemption.cs#L15-L20`) with an XML-doc at `L10-L13` recording the supersession (it directs new callers to `RedeemCoupon` with `PromotionId` instead). The validator (`RecordPromotionRedemptionValidator.cs#L7-L29`) is also retained. There is no `RecordPromotionRedemptionHandler.Handle(RecordPromotionRedemption, ...)` method — the same-named handler class instead reacts to the `CouponRedeemed` *event* via Wolverine's event-handler discovery (`src/Promotions/Promotions/Promotion/RecordPromotionRedemptionHandler.cs#L24-L41`). A repo-wide grep for `RecordPromotionRedemption` outside the contract / validator finds only commentary in `OrderIntegration/OrderPlacedHandler.cs#L13-L14, L32-L33` and test references that name the legacy path — no caller sends the command. See §Routes-without-instantiator.

### `Coupon` commands

- `IssueCoupon(CouponCode, PromotionId)` — message-bus only. Validator requires non-empty code with format constraints (`src/Promotions/Promotions/Coupon/IssueCouponValidator.cs`). Handler verifies the parent `Promotion` exists and is `Draft` or `Active`, then checks the deterministic stream id is unused before tagging + appending `CouponIssued` to a new stream (`src/Promotions/Promotions/Coupon/IssueCouponHandler.cs#L7-L57`). Idempotency for duplicate codes is provided by the `existingCoupon is not null` check at `L38-L42`, complementing the deterministic UUID v5 derivation.
- `RedeemCoupon(CouponCode, PromotionId, OrderId, CustomerId, RedeemedAt)` — message-bus only. The `PromotionId` field was added in M40.0 to support the DCB boundary query spanning both streams (XML-doc at `src/Promotions/Promotions/Coupon/RedeemCoupon.cs#L7`). Validator at `src/Promotions/Promotions/Coupon/RedeemCouponValidator.cs`. Handler is the BC's only DCB-boundary handler: `Load` returns an `EventTagQuery` joining the coupon stream (`CouponIssued`, `CouponRedeemed`, `CouponRevoked`, `CouponExpired`) and the promotion stream (`PromotionCreated`, `PromotionActivated`, `PromotionPaused`, `PromotionResumed`, `PromotionCancelled`, `PromotionExpired`, `PromotionRedemptionRecorded`); `Before` enforces all four redemption invariants against the projected `CouponRedemptionState`; `Handle` appends `CouponRedeemed` via `IEventBoundary<CouponRedemptionState>.AppendOne` and additionally returns the same event in `OutgoingMessages` to cascade choreography to `RecordPromotionRedemptionHandler` (`src/Promotions/Promotions/Coupon/RedeemCouponHandler.cs#L23-L119`).
- `RevokeCoupon(CouponCode, Reason)` — message-bus only. Validator at `RevokeCouponValidator.cs`. Compound handler: `LoadAsync` recomputes `Coupon.StreamId(cmd.CouponCode)` and aggregates the stream → `Before` (404 if missing, 409 if already `Revoked` or `Expired`) → `Handle` tags + appends `CouponRevoked` (`src/Promotions/Promotions/Coupon/RevokeCouponHandler.cs#L8-L56`). The handler can revoke coupons in `Issued` *or* `Redeemed` status (the comment at `L36-L37` records the intent for post-fact fraud correction).

### Discount query (HTTP, not a command)

- `CalculateDiscount(IReadOnlyList<CartLineItem> CartItems, IReadOnlyList<string> CouponCodes)` — request DTO at `src/Promotions/Promotions/Discount/CalculateDiscount.cs#L9-L11`. Posted to `POST /api/promotions/discounts/calculate` (see §HTTP / API surface) and answered with `CalculateDiscountResponse`. No domain mutation occurs; no event is appended; the request shape is registered as a `public sealed record` for FluentValidation purposes (`CalculateDiscountValidator.cs`). Counted as the eighth "command" in the S1 enumeration; behaviorally a query.

There is no saga in this BC.

## Domain events

12 domain events grouped by aggregate, matching the S1 stub count exactly. All events are records under `src/Promotions/Promotions/Promotion/` and `src/Promotions/Promotions/Coupon/`. The event stream is persisted in the `promotions` Marten schema (`src/Promotions/Promotions.Api/Program.cs#L40`).

### `Promotion` stream events (8)

- `PromotionCreated(PromotionId, Name, Description, DiscountType, DiscountValue, StartDate, EndDate, UsageLimit?, CreatedAt)` (`src/Promotions/Promotions/Promotion/PromotionCreated.cs#L6-L15`). Emitted by `CreatePromotionHandler.Handle` (`CreatePromotionHandler.cs#L13-L29`). Aggregate apply: `Promotion.cs#L119-L132`. Boundary-state apply: `CouponRedemptionState.cs#L62-L67`.
- `PromotionActivated(PromotionId, ActivatedAt)` (`PromotionActivated.cs#L6-L8`). Emitter: `ActivatePromotionHandler.Handle` (`ActivatePromotionHandler.cs#L49-L56`). Apply: `Promotion.cs#L134-L139`; `CouponRedemptionState.cs#L69`.
- `PromotionPaused(PromotionId, PausedAt)` (`PromotionPaused.cs#L6-L8`). Apply: `Promotion.cs#L141-L146`; `CouponRedemptionState.cs#L70`. **Emitter:** none in tree.
- `PromotionResumed(PromotionId, ResumedAt)` (`PromotionResumed.cs#L6-L8`). Apply: `Promotion.cs#L148-L153`; `CouponRedemptionState.cs#L71`. **Emitter:** none.
- `PromotionExpired(PromotionId, ExpiredAt)` (`PromotionExpired.cs#L6-L8`). Apply: `Promotion.cs#L161-L165`; `CouponRedemptionState.cs#L73`. **Emitter:** none.
- `PromotionCancelled(PromotionId, CancelledAt)` (`PromotionCancelled.cs#L6-L8`). Apply: `Promotion.cs#L155-L159`; `CouponRedemptionState.cs#L72`. **Emitter:** none.
- `PromotionRedemptionRecorded(PromotionId, OrderId, CustomerId, CouponCode, RedeemedAt)` (`PromotionRedemptionRecorded.cs#L8-L13`). Emitter: `RecordPromotionRedemptionHandler.Handle(CouponRedeemed, ...)` (`RecordPromotionRedemptionHandler.cs#L24-L41`) — choreography reaction to `CouponRedeemed`, not a command handler. Apply: `Promotion.cs#L167-L171` (increments `CurrentRedemptionCount`); `CouponRedemptionState.cs#L74`.
- `CouponBatchGenerated(PromotionId, BatchId, Prefix, Count, GeneratedAt)` (`CouponBatchGenerated.cs#L7-L12`). Emitter: `GenerateCouponBatchHandler.Handle` (`GenerateCouponBatchHandler.cs#L53-L63`). Apply: `Promotion.cs#L173-L174` (no state change — XML-doc at `L174` records the design choice).

### `Coupon` stream events (4)

- `CouponIssued(CouponCode, PromotionId, IssuedAt)` (`CouponIssued.cs#L6-L9`). Emitter: `IssueCouponHandler.Handle` (`IssueCouponHandler.cs#L46-L56`). Apply: `Coupon.cs#L104-L112`; `CouponRedemptionState.cs#L48-L54`.
- `CouponRedeemed(CouponId, CouponCode, PromotionId, OrderId, CustomerId, RedeemedAt)` (`CouponRedeemed.cs#L7-L13`). Emitter: `RedeemCouponHandler.Handle` (`RedeemCouponHandler.cs#L100-L112`); also returned as an outgoing message at `L114-L117` to drive the choreography reaction. Apply: `Coupon.cs#L114-L121`; `CouponRedemptionState.cs#L56`.
- `CouponRevoked(CouponId, CouponCode, PromotionId, Reason, RevokedAt)` (`CouponRevoked.cs#L7-L12`). Emitter: `RevokeCouponHandler.Handle` (`RevokeCouponHandler.cs#L44-L54`). Apply: `Coupon.cs#L123-L127`; `CouponRedemptionState.cs#L57`.
- `CouponExpired(CouponCode, ExpiredAt)` (`CouponExpired.cs#L6-L8`). Apply: `Coupon.cs#L129-L133`; `CouponRedemptionState.cs#L58`. **Emitter:** none.

### Declared-but-unemitted events

Five of the 12 events have aggregate `Apply` branches and are loaded by the DCB boundary query, but no command, scheduled-message, or integration handler emits them: `PromotionPaused`, `PromotionResumed`, `PromotionExpired`, `PromotionCancelled`, `CouponExpired`. Repo-wide `grep -rn "new PromotionPaused\|new PromotionResumed\|new PromotionExpired\|new PromotionCancelled\|new CouponExpired" --include="*.cs" src/ tests/` returns no matches. They are reachable only by direct `session.Events.Append(...)` from a future caller (a scheduled-expiration sweep, an admin pause endpoint, or an end-date-driven background job — none currently exist). This mirrors the four declared-but-unemitted events found in the Pricing dossier.

## Projections

Three projections are registered in `Program.cs#L47-L51`; all run inline (zero lag, same transaction as the originating command).

- **`Promotion` snapshot** — inline; keyed by stream id (`Guid`); registered via `opts.Projections.Snapshot<Promotions.Promotion.Promotion>(SnapshotLifecycle.Inline)` (`Program.cs#L47`). Source events: all eight `Promotion`-stream events listed above. Served via `session.Events.AggregateStreamAsync<Promotion>(...)` from `IssueCouponHandler` (`L13-L15`), `GenerateCouponBatchHandler` (`L21`), `ActivatePromotionHandler.LoadAsync` (`L15`), `RevokeCouponHandler.LoadAsync` indirectly via `Coupon`, and the two HTTP query endpoints (`ValidateCoupon.cs#L42-L44`, `CalculateDiscount.cs#L42-L44`).
- **`Coupon` snapshot** — inline; keyed by stream id (`Guid`, derived from code); registered via `opts.Projections.Snapshot<Promotions.Coupon.Coupon>(SnapshotLifecycle.Inline)` (`Program.cs#L48`). Source events: all four `Coupon`-stream events. Served via `session.Events.AggregateStreamAsync<Coupon>(streamId, ...)` from `IssueCouponHandler` (`L34-L36`) and `RevokeCouponHandler.LoadAsync` (`L19-L20`).
- **`CouponLookupViewProjection`** — inline `MultiStreamProjection<CouponLookupView, string>` keyed by `CouponCode` (`src/Promotions/Promotions/Coupon/CouponLookupViewProjection.cs#L11-L21`). Registered at `Program.cs#L51` via `opts.Projections.Add<CouponLookupViewProjection>(ProjectionLifecycle.Inline)`. Maps the `Guid`-keyed `Coupon` event stream onto string-keyed `CouponLookupView` documents using `Identity<TEvent>(x => x.CouponCode)` for all four coupon events. `Create(CouponIssued)` materializes the document with uppercase-normalized `Id` and `Code`; subsequent `Apply(...)` branches mutate `Status` (`CouponLookupViewProjection.cs#L23-L60`).

`CouponLookupView` (`src/Promotions/Promotions/Coupon/CouponLookupView.cs#L10-L46`) is the hot-path read model for the `ValidateCoupon` and `CalculateDiscount` HTTP endpoints — `session.LoadAsync<CouponLookupView>(normalizedCode, ct)` at `ValidateCoupon.cs#L26` and `CalculateDiscount.cs#L33` — providing O(1) lookup by coupon code without replaying the `Coupon` event stream. The XML-doc at `CouponLookupView.cs#L4-L8` records the inline-lifecycle / uppercase-id contract.

## Integration events

### Outbound (published)

`src/Shared/Messages.Contracts/` does not contain a `Promotions/` subfolder (`find src/Shared/Messages.Contracts -type d` returns no Promotions entry; the sibling BCs Pricing, Orders, Listings, Marketplaces, etc. each have their own folder). No `PublishMessage<T>...ToRabbitExchange(...)` route is configured in `Promotions.Api/Program.cs#L97-L107`. Promotions does not publish any cross-BC integration messages — coupon redemption is exposed back to the system only through the choreography of `CouponRedeemed` returned in `OutgoingMessages` from `RedeemCouponHandler.Handle` (`RedeemCouponHandler.cs#L114-L117`), which is consumed by the in-process `RecordPromotionRedemptionHandler` and not routed externally.

### Inbound (subscribed)

One handler subscribes to a cross-BC integration message via Wolverine's handler-discovery convention (no explicit `.ListenToRabbitQueue(...)` call exists in `Program.cs`; routing is auto-provisioned by `opts.UseRabbitMq(...).AutoProvision()` at `Program.cs#L98-L106`).

- **`Messages.Contracts.Orders.OrderPlaced`** — handled by `OrderPlacedHandler.Handle` (`src/Promotions/Promotions/OrderIntegration/OrderPlacedHandler.cs#L36-L41`). Phase-1 implementation: returns an empty `OutgoingMessages` because `OrderPlaced` does not yet carry applied-coupon data. The XML-doc at `L18-L34` documents the Phase-2 fan-out shape — one `RedeemCoupon` command per applied coupon — which will be enabled when Shopping propagates the `AppliedCoupons` collection through the integration message. The handler is the only inbound subscription in the BC.

### HTTP synchronous integration with Shopping

In place of message-based integration on the cart-time read path, Shopping queries Promotions directly over HTTP. The contract is owned client-side in Shopping (`src/Shopping/Shopping/Clients/IPromotionsClient.cs#L7-L25`) and implemented by `PromotionsClient` (`src/Shopping/Shopping.Api/Clients/PromotionsClient.cs#L19-L85`). The HTTP client is registered at `src/Shopping/Shopping.Api/Program.cs#L124-L135` against the configuration key `Promotions:BaseUrl` and consumed by `ApplyCouponToCartHandler` (`src/Shopping/Shopping/Cart/ApplyCouponToCart.cs#L53, L117, L137`). Two endpoints are involved:

- **`GET /api/promotions/coupons/{code}/validate`** — invoked from `IPromotionsClient.ValidateCouponAsync` at cart time when the user enters a coupon code. The Shopping client uppercase-normalizes the code before the call (`PromotionsClient.cs#L22`) and treats `404` as `IsValid: false` with reason `"Coupon not found"` (`L26-L32`).
- **`POST /api/promotions/discounts/calculate`** — invoked from `IPromotionsClient.CalculateDiscountAsync` to refresh the cart's discounted total after coupon application. The request body shape is mirrored as a private record on the Shopping side (`PromotionsClient.cs#L97-L104`).

This is the only synchronous cross-BC edge in or out of Promotions; all other interactions are either in-process choreography or the single `OrderPlaced` subscription.

### Routes-without-instantiator

- **`RecordPromotionRedemption` command** — record + validator are defined (`RecordPromotionRedemption.cs#L15-L20`, `RecordPromotionRedemptionValidator.cs#L5-L29`) and registered for FluentValidation discovery via the Promotions assembly scan (`Program.cs#L94`), but no `Handle(RecordPromotionRedemption, ...)` method exists — `RecordPromotionRedemptionHandler.Handle` accepts a `CouponRedeemed` event instead (`RecordPromotionRedemptionHandler.cs#L24-L25`). The command is sent by no caller in `src/` or `tests/`. ADR 0058 records the supersession (`docs/decisions/0058-dcb-promotions-coupon-redemption.md`); the M40.0 XML-doc at `RecordPromotionRedemption.cs#L10-L13` retains it for backward compatibility.

### CONTEXTS.md drift

`CONTEXTS.md` lines 295–306 describe Promotions' external surface; reconciled against the code:

- The communicates-with table at `CONTEXTS.md#L301-L304` lists Shopping (`← queries`) and Pricing-planned (`→ queries`). It does **not** list the inbound Orders subscription. `OrderPlacedHandler` is registered (currently as a Phase-1 no-op) and is the only RabbitMQ subscription in the BC; the CONTEXTS row is missing the Orders edge. Code authoritative.
- The narrative at `CONTEXTS.md#L20` (the cross-BC integration map) describes Promotions as `→ queries` from Shopping for `ValidateCoupon` / `CalculateDiscount`, which matches the HTTP synchronous integration described above.
- `CONTEXTS.md#L306` cites M30.1 as the most recent Promotions milestone. The most recent material milestone for the BC is M40.0 (DCB introduction, ADR 0058) — M30.1 is the milestone that introduced the Shopping HTTP edge but is superseded by M40.0 for the redemption write path. Code authoritative; dossier header records M40.0.
- `CONTEXTS.md#L306` describes coupon stream IDs as "deterministic UUID v5 from code strings" — this matches `Coupon.StreamId(string)` (`Coupon.cs#L81-L102`). The promotion-side claim (UUID v7) at the S1 stub is verified by `CreatePromotionHandler.cs#L10` (`Guid.CreateVersion7()`).

The S3 prompt's premise that Promotions uses Marten DCB is borne out by source enumeration: both `PromotionStreamId` and `CouponStreamId` are registered as DCB tag types at `Program.cs#L54-L57`, every event-write site adds the appropriate tag, and `RedeemCouponHandler` uses the boundary API end-to-end. This contrasts with the Pricing dossier, which found no DCB tags in `src/Pricing/`. Forward-note for S5: confirm intended grouping of Promotions among DCB BCs (matches the prompt) vs. Pricing (does not match the prompt).

## Sagas / orchestration

Not applicable — this BC is not a saga orchestrator. The only orchestration primitive is the in-process choreography around `CouponRedeemed`: `RedeemCouponHandler.Handle` returns `CouponRedeemed` in `OutgoingMessages`, which is dispatched through Wolverine's local in-memory bus to `RecordPromotionRedemptionHandler.Handle(CouponRedeemed, ...)`, which then performs `FetchForWriting<Promotion>` + tagged `Append` of `PromotionRedemptionRecorded`. Both sides are within the same process; the choreography exists to keep the redemption write split across the two streams while preserving the DCB consistency boundary at the decision point. ADR 0058 records the design rationale.

## HTTP / API surface

All endpoints are routed via Wolverine HTTP attribute discovery (`MapWolverineEndpoints` at `src/Promotions/Promotions.Api/Program.cs#L138-L141`). FluentValidation runs through `UseFluentValidationProblemDetailMiddleware()` (`Program.cs#L140`).

### Query (consumed by Shopping)

- `GET /api/promotions/coupons/{code}/validate` — `ValidateCoupon`. Handler at `src/Promotions/Promotions.Api/Queries/ValidateCoupon.cs#L16-L86`. Loads `CouponLookupView` by uppercase-normalized code (`L23-L26`); when present, additionally aggregates the parent `Promotion` stream to enforce the active-and-in-window invariants (`L42-L76`). Returns `Ok<CouponValidationResult>` for both valid and invalid cases — invalid responses carry a structured reason string (`CouponValidationResult.Invalid(...)` factory at `src/Promotions/Promotions/Coupon/CouponValidationResult.cs`). Anonymous (no `[Authorize]`).
- `POST /api/promotions/discounts/calculate` — `CalculateDiscountEndpoint`. Handler at `src/Promotions/Promotions.Api/Queries/CalculateDiscount.cs#L17-L79`. Phase-1 single-coupon, percentage-discount only: short-circuits to `CalculateZeroDiscount` when there are no coupons, when the coupon is missing or non-`Issued`, when the parent promotion is not `Active`, when current time is outside the promotion window, or when the discount type is not `PercentageOff` (`L24-L63`). Per-line discount math at `L85-L109` (XML-doc at `L82-L84` records the Phase-1 stub for floor-price enforcement; Phase 2 will query Pricing). Anonymous.

### Health and ops

- `GET /health`, `GET /alive` — Aspire defaults via `MapDefaultEndpoints()` (`Program.cs#L131`).
- `GET /api/v1/health` — explicit health check, development-only (`Program.cs#L133-L136`).
- `GET /` — `301 Moved Permanently` to `/api` (`Program.cs#L143-L147`).
- `GET /api/v1/swagger.json` and `/api` Swagger UI — development-only (`Program.cs#L116-L128`).

The eight commands (`CreatePromotion`, `ActivatePromotion`, `GenerateCouponBatch`, `RecordPromotionRedemption`, `IssueCoupon`, `RedeemCoupon`, `RevokeCoupon`, `CalculateDiscount`-as-query) are reachable only via the Wolverine bus or — in the case of `CalculateDiscount` — the `POST` endpoint above. There is no admin / Backoffice HTTP surface in `Promotions.Api`; campaign administration is performed by sending the commands directly through the message bus (the integration tests exercise this path via `bus.InvokeAsync(...)`).

## Frontend surface

Not applicable — no frontend in this BC. Backoffice is the planned operator-facing UI (`docs/decisions/0031-admin-portal-rbac-model.md` references a `PromotionManager` role), but no Promotions-specific Razor page is registered in any frontend project at this depth.

## Identity / auth posture

- **Schemes:** None registered. `Promotions.Api/Program.cs` declares no `AddAuthentication(...)` / `AddJwtBearer(...)` / `AddAuthorization(...)` calls (verified by `grep -n "JwtBearer\|Authorize" src/Promotions/Promotions.Api/Program.cs` — zero matches).
- **Policies:** None — no `AddAuthorization(opts => opts.AddPolicy(...))` block.
- **Endpoint guards:** No `[Authorize]` attributes anywhere in `src/Promotions/`. Both HTTP endpoints (`/api/promotions/coupons/{code}/validate` and `/api/promotions/discounts/calculate`) are anonymous, consistent with their consumer being the Shopping BC's server-side `PromotionsClient` over an in-cluster network call rather than a browser-direct request.
- **Source:** `src/Promotions/Promotions.Api/Program.cs`.

## Tests as behavioral evidence

No Gherkin features under `docs/features/promotions/` (`find docs/features -type d` does not list a `promotions` directory).

No unit-test project — `tests/Promotions/` contains only `Promotions.IntegrationTests/` (xUnit, Alba + Testcontainers via `TestFixture` / `IntegrationTestCollection`).

Integration tests by file:

- `PromotionLifecycleTests.cs` (4) — `CreatePromotion_WithValidData_CreatesPromotionInDraftStatus`, `ActivatePromotion_FromDraft_ActivatesSuccessfully`, `ActivatePromotion_WhenAlreadyActive_Fails`, `IssueCoupon_ForActivePromotion_CreatesCoupon`. Covers the `CreatePromotion` → `ActivatePromotion` → `IssueCoupon` happy path and the activation-from-active rejection.
- `CouponValidationTests.cs` (6) — `ValidateCoupon_WhenCouponValid_ReturnsValid`, `_WhenCouponNotFound_ReturnsInvalid`, `_WhenPromotionNotActive_ReturnsInvalid`, `_WhenPromotionExpired_ReturnsInvalid`, `_WhenPromotionNotStarted_ReturnsInvalid`, `_CaseInsensitive_Works`. Exercises the `GET /api/promotions/coupons/{code}/validate` endpoint across all five `CouponValidationResult.Invalid(...)` branches plus case-insensitive lookup.
- `DiscountCalculationTests.cs` (8) — `CalculateDiscount_WithNoCoupons_ReturnsZeroDiscount`, `_WithValidCoupon_AppliesPercentageDiscount`, `_WithInvalidCoupon_ReturnsZeroDiscount`, `_WithRedeemedCoupon_ReturnsZeroDiscount`, `_WithExpiredPromotion_ReturnsZeroDiscount`, `_WithNotYetStartedPromotion_ReturnsZeroDiscount`, `_WithCaseInsensitiveCouponCode_AppliesDiscount`, `_WithMultipleItems_CalculatesCorrectTotals`. Exercises `POST /api/promotions/discounts/calculate` across all short-circuit branches plus the multi-line-item arithmetic path.
- `CouponRedemptionTests.cs` (13) — `RedeemCoupon_WithValidIssuedCoupon_RedeemsSuccessfully`, `_WhenAlreadyRedeemed_Fails`, `RevokeCoupon_ForIssuedCoupon_RevokesSuccessfully`, `_ForRedeemedCoupon_RevokesSuccessfully`, `_WhenAlreadyRevoked_Fails`, `RedeemCoupon_IncrementsPromotionRedemptionCount`, `_WhenPromotionCapExceeded_Fails`, `_WhenPromotionIsNotActive_Fails`, `_CausesPromotionRedemptionRecorded`, `GenerateCouponBatch_ForActivePromotion_CreatesMultipleCoupons`, `_ForDraftPromotion_CreatesSuccessfully`, `OrderPlacedHandler_InPhase1_ReturnsEmptyMessages`, `RedeemCoupon_ConcurrentRedemption_SecondIsRejectedByDcb`. The last test (`tests/Promotions/Promotions.IntegrationTests/CouponRedemptionTests.cs#L583-...`) exercises the DCB cross-stream concurrency assertion and is the behavioral evidence anchoring ADR 0058.

Total: 31 integration tests across 4 classes. Shared lifecycle: `tests/Promotions/Promotions.IntegrationTests/TestFixture.cs` (Postgres + RabbitMQ Testcontainers).

## ADRs

- **ADR 0058** — Dynamic Consistency Boundary: Promotions Coupon Redemption. Establishes the DCB write path (`EventTagQuery` + `[BoundaryModel]` + `IEventBoundary<T>`) for `RedeemCouponHandler`, the `PromotionId` addition to the `RedeemCoupon` command, and the choreography reaction in `RecordPromotionRedemptionHandler` that supersedes the legacy `RecordPromotionRedemption` command path. File: `docs/decisions/0058-dcb-promotions-coupon-redemption.md`.

The S1 stub's ADR list cited only ADR 0058. No other ADR materially shapes Promotions (ADR 0031 — Backoffice RBAC — references a `PromotionManager` role but is not anchored in `src/Promotions/` since the BC has no `[Authorize]` attributes).

## Prior event modeling

- `docs/planning/promotions-event-modeling.md` — 10-stage event-modeling workshop output (2026-03-15). Defines the two aggregates, the `Draft → Active → (Paused ↔ Active) → Expired/Cancelled` Promotion lifecycle, the `Issued → Redeemed/Revoked/Expired` Coupon lifecycle, and the UUID v7 / UUID v5 stream-key split. Stage 1 (`Brain Dump`) records the rationale for UUID v7 on `Promotion` ("no natural key") at the workshop document. The pause / resume / cancel / expire transitions documented in the workshop are not yet wired to commands or scheduled-message handlers in current code.

## Source citations (S2 full)

- `src/Promotions/` (folder root)
- `src/Promotions/Promotions/Promotion/Promotion.cs`, `PromotionStreamId.cs` (in `src/Promotions/Promotions/`), `PromotionStatus.cs`
- `src/Promotions/Promotions/Coupon/Coupon.cs`, `CouponStatus.cs`, `CouponStreamId.cs` (in `src/Promotions/Promotions/`)
- `src/Promotions/Promotions/Coupon/CouponRedemptionState.cs`
- `src/Promotions/Promotions/DiscountType.cs`, `Constants.cs`, `AssemblyAttributes.cs`
- `src/Promotions/Promotions/Promotion/CreatePromotion.cs`, `ActivatePromotion.cs`, `GenerateCouponBatch.cs`, `RecordPromotionRedemption.cs`
- `src/Promotions/Promotions/Promotion/CreatePromotionHandler.cs`, `ActivatePromotionHandler.cs`, `GenerateCouponBatchHandler.cs`, `RecordPromotionRedemptionHandler.cs`
- `src/Promotions/Promotions/Promotion/CreatePromotionValidator.cs`, `ActivatePromotionValidator.cs`, `GenerateCouponBatchValidator.cs`, `RecordPromotionRedemptionValidator.cs`
- `src/Promotions/Promotions/Promotion/PromotionCreated.cs`, `PromotionActivated.cs`, `PromotionPaused.cs`, `PromotionResumed.cs`, `PromotionExpired.cs`, `PromotionCancelled.cs`, `PromotionRedemptionRecorded.cs`, `CouponBatchGenerated.cs`
- `src/Promotions/Promotions/Coupon/IssueCoupon.cs`, `RedeemCoupon.cs`, `RevokeCoupon.cs`
- `src/Promotions/Promotions/Coupon/IssueCouponHandler.cs`, `RedeemCouponHandler.cs`, `RevokeCouponHandler.cs`
- `src/Promotions/Promotions/Coupon/IssueCouponValidator.cs`, `RedeemCouponValidator.cs`, `RevokeCouponValidator.cs`
- `src/Promotions/Promotions/Coupon/CouponIssued.cs`, `CouponRedeemed.cs`, `CouponRevoked.cs`, `CouponExpired.cs`
- `src/Promotions/Promotions/Coupon/CouponLookupView.cs`, `CouponLookupViewProjection.cs`, `CouponValidationResult.cs`
- `src/Promotions/Promotions/Discount/CalculateDiscount.cs`, `CalculateDiscountResponse.cs`, `CalculateDiscountValidator.cs`, `CartLineItem.cs`, `LineItemDiscount.cs`
- `src/Promotions/Promotions/OrderIntegration/OrderPlacedHandler.cs`
- `src/Promotions/Promotions.Api/Program.cs`
- `src/Promotions/Promotions.Api/Queries/ValidateCoupon.cs`, `CalculateDiscount.cs`
- `src/Shared/Messages.Contracts/Orders/OrderPlaced.cs` (inbound contract)
- `src/Shopping/Shopping/Clients/IPromotionsClient.cs`, `src/Shopping/Shopping.Api/Clients/PromotionsClient.cs`, `src/Shopping/Shopping.Api/Program.cs` (HTTP consumer wiring), `src/Shopping/Shopping/Cart/ApplyCouponToCart.cs` (consuming handler)
- `CONTEXTS.md` (section: `Promotions`, lines 295–306; cross-BC map at line 20)
- `docs/decisions/0058-dcb-promotions-coupon-redemption.md`
- `docs/planning/promotions-event-modeling.md`
- `tests/Promotions/Promotions.IntegrationTests/PromotionLifecycleTests.cs`, `CouponValidationTests.cs`, `DiscountCalculationTests.cs`, `CouponRedemptionTests.cs`, `TestFixture.cs`
