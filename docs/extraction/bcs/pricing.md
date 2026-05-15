# Pricing

> **Source folder:** `src/Pricing/`
> **Status:** Implemented
> **Most recent material milestone:** M30.1 — Coupon-aware Shopping/Pricing integration (Shopping consumes published prices)
> **Dossier depth:** S2 — full

## Purpose

Pricing owns the per-SKU price record — the base price, the floor (minimum sellable amount, internal margin protection), the ceiling (MAP / policy maximum), pending and activated scheduled price changes, retroactive corrections, and a terminal discontinuation state. Pricing reacts to a Product Catalog lifecycle event (`ProductAdded`) to seed an `Unpriced` event stream for each new SKU, and exposes the seeded stream through HTTP endpoints used by Backoffice (`PricingManager` role) and a bulk-read endpoint that Shopping consumes at add-to-cart time. Money is represented uniformly through a `Money` value object that enforces ISO 4217 currency, two-decimal rounding, and same-currency comparison guards.

## Aggregates

### `ProductPrice`

- **Stream ID:** Deterministic UUID v5, computed by `ProductPrice.StreamId(sku)` at `src/Pricing/Pricing/Products/ProductPrice.cs#L97-L119`. The implementation hashes the literal byte string `pricing:{SKU.ToUpperInvariant()}` against the RFC 4122 URL namespace UUID (`6ba7b810-9dad-11d1-80b4-00c04fd430c8`) using SHA-1 and forces the version-5 / RFC 4122-variant bits at offsets 6 and 8 (`src/Pricing/Pricing/Products/ProductPrice.cs#L107-L118`). Class-level XML-doc cites ADR 0016 for the rationale (`src/Pricing/Pricing/Products/ProductPrice.cs#L6-L9`).
- **Marten configuration:** Plain `StreamIdentity = StreamIdentity.AsGuid` with `opts.Projections.Snapshot<ProductPrice>(SnapshotLifecycle.Inline)` (`src/Pricing/Pricing.Api/Program.cs#L45-L48`). The same `StreamId(sku)` factory is recomputed at every command site (`SetInitialPriceHandler.Handle` `src/Pricing/Pricing/Products/SetInitialPrice.cs#L72`, `SetBasePriceHandler.LoadAsync` `src/Pricing/Pricing/Products/SetBasePrice.cs#L43`, `ChangePriceHandler.Handle` `src/Pricing/Pricing/Products/ChangePrice.cs#L48`, `SchedulePriceChangeHandler.LoadAsync` `src/Pricing/Pricing/Products/SchedulePriceChange.cs#L50`, `ActivateScheduledPriceChangeHandler.LoadAsync` `src/Pricing/Pricing/Products/SchedulePriceChange.cs#L167`, `CancelScheduledPriceChangeHandler.HandleAsync` `src/Pricing/Pricing/Products/CancelScheduledPriceChange.cs#L29`, `ProductAddedHandler.Handle` `src/Pricing/Pricing/Products/ProductAddedHandler.cs#L17`, dev seed `src/Pricing/Pricing.Api/Program.cs#L250`).
- **DCB tag:** None. Despite the M48.0 S3 prompt's premise that Pricing uses Marten DCB stream-id tagging, source enumeration finds no `[Tag]` attribute, no DCB tag record, no marker interface, and no `IStreamId`-style wrapper on `ProductPrice`. The aggregate is addressed by a plain `Guid` derived from the SKU. Compare with the Promotions BC (`PromotionStreamId` / `CouponStreamId`), which does carry DCB tags. Code-authoritative; see §CONTEXTS.md drift below for the documentation pointer affected.
- **Key state:** `Id`, `Sku` (uppercase-normalized), `Status` (`PriceStatus` enum), `BasePrice?`, `FloorPrice?`, `CeilingPrice?`, `PreviousBasePrice?`, `PreviousPriceSetAt?`, `PendingSchedule?` (the embedded `ScheduledPriceChange` value object), `RegisteredAt`, `LastChangedAt?` (`src/Pricing/Pricing/Products/ProductPrice.cs#L11-L73`).
- **Lifecycle stages** (the `PriceStatus` enum at `src/Pricing/Pricing/Products/PriceStatus.cs#L7-L25`):
  - `Unpriced` — set by the `Create()` factory (`src/Pricing/Pricing/Products/ProductPrice.cs#L75-L88`) and entered when `ProductAddedHandler` opens the stream with `ProductRegistered` (`src/Pricing/Pricing/Products/ProductAddedHandler.cs#L21-L26`).
  - `Published` — set by `Apply(InitialPriceSet)` (`src/Pricing/Pricing/Products/ProductPrice.cs#L131-L139`).
  - `Discontinued` — terminal; set by `Apply(PriceDiscontinued)` (`src/Pricing/Pricing/Products/ProductPrice.cs#L196-L201`). The `PriceDiscontinued` event has an `Apply` method on the aggregate and a projector branch but is not emitted by any command or integration handler in the current codebase (see §Routes-without-instantiator).
- **File:** `src/Pricing/Pricing/Products/ProductPrice.cs`.

### Value objects

- `Money` — `public sealed record` at `src/Pricing/Pricing/Money.cs#L11-L109`. Private constructor, `Of(decimal, string = "USD")` factory enforces `Length == 3` ISO-4217 code, non-negative amount, two-decimal `MidpointRounding.AwayFromZero` rounding, uppercase currency normalization (`src/Pricing/Pricing/Money.cs#L31-L50`). Operator overloads `<`, `>`, `<=`, `>=` call `AssertSameCurrency`, which throws `InvalidOperationException` on currency mismatch (`src/Pricing/Pricing/Money.cs#L55-L86, L103-L108`). `(decimal)money` is an explicit cast only; no implicit conversion (`src/Pricing/Pricing/Money.cs#L92`). JSON converter pinned via `[JsonConverter(typeof(MoneyJsonConverter))]` at `src/Pricing/Pricing/Money.cs#L10`.
- `ScheduledPriceChange` — `public sealed record` at `src/Pricing/Pricing/Products/ScheduledPriceChange.cs#L7-L35`. Embedded inside `ProductPrice.PendingSchedule`; carries `ScheduleId`, `ScheduledPrice`, `ScheduledFor`, `ScheduledBy`, `ScheduledAt`. Created in `ProductPrice.Apply(PriceChangeScheduled)` (`src/Pricing/Pricing/Products/ProductPrice.cs#L149-L161`); cleared in `Apply(ScheduledPriceChangeCancelled)` (`L163-L167`) and `Apply(ScheduledPriceActivated)` (`L169-L177`).

## Commands

Direct enumeration via `grep -rn "public sealed record" --include="*.cs" src/Pricing/` against the handler set yields **six** command-shape records, against the S1 stub's count of seven. The deviation is reconciled below.

- `SetInitialPrice(string Sku, decimal Amount, string Currency, decimal? FloorAmount, decimal? CeilingAmount, Guid SetBy, DateTimeOffset PricedAt)` — message-bus only (no HTTP route). Validator enforces non-empty SKU, positive amount, 3-character currency, and three cross-field rules: `Amount >= FloorAmount`, `Amount <= CeilingAmount`, `FloorAmount <= CeilingAmount` (`src/Pricing/Pricing/Products/SetInitialPrice.cs#L11-L63`). Handler loads the aggregate via `AggregateStreamAsync<ProductPrice>(streamId)`, throws if the stream does not exist or status is not `Unpriced`, and returns the tuple `(ProductPrice, InitialPriceSet)` (`src/Pricing/Pricing/Products/SetInitialPrice.cs#L65-L100`).
- `SetBasePrice(string Sku, decimal Amount, string Currency = "USD")` — HTTP `POST /api/pricing/products/{sku}/base-price` with `[Authorize(Policy = "PricingManager")]` (`src/Pricing/Pricing/Products/SetBasePrice.cs#L15, L90-L91`). Compound handler: `LoadAsync` → `Before` (404 if missing, 400 if `Discontinued`, 400 if floor/ceiling violated) → `Handle` returns `(IResult, OutgoingMessages)` (`src/Pricing/Pricing/Products/SetBasePrice.cs#L38-L147`). Branches on current `Status`: emits `InitialPriceSet` for `Unpriced` and `PriceChanged` for `Published`. Uses `session.Events.Append` directly — does not use the `[WriteAggregate]` attribute pattern. The `OutgoingMessages` collection returned at `L100, L121, L146` is always empty.
- `ChangePrice(string Sku, decimal NewAmount, string Currency, string? Reason, Guid ChangedBy, DateTimeOffset ChangedAt, Guid? BulkPricingJobId = null, Guid? SourceSuggestionId = null)` — message-bus only (no HTTP route). Validator enforces non-empty SKU, positive amount, 3-character currency (`src/Pricing/Pricing/Products/ChangePrice.cs#L21-L39`). Handler requires `Status == Published`, requires `BasePrice != null`, enforces floor/ceiling, and returns `(ProductPrice, PriceChanged)` (`src/Pricing/Pricing/Products/ChangePrice.cs#L41-L93`). The `BulkPricingJobId` and `SourceSuggestionId` fields are correlation slots populated on `PriceChanged` but never written to by any in-tree caller; they are the audit hook anticipated by ADR 0019.
- `SchedulePriceChange(string Sku, decimal NewAmount, string Currency, DateTimeOffset ScheduledFor)` — HTTP `POST /api/pricing/products/{sku}/schedule` with `[Authorize(Policy = "PricingManager")]` (`src/Pricing/Pricing/Products/SchedulePriceChange.cs#L15-L19, L108-L109`). Validator enforces `ScheduledFor > UtcNow` (`src/Pricing/Pricing/Products/SchedulePriceChange.cs#L34-L36`). Compound handler: `LoadAsync` → `Before` (404 if missing, 400 if not `Published`, 409 if `PendingSchedule != null` with diagnostic `Extensions["existingSchedule"]`, 400 if floor/ceiling violated) → `Handle` appends `PriceChangeScheduled` and calls `messaging.ScheduleAsync(new ActivateScheduledPriceChange(sku, scheduleId), cmd.ScheduledFor)` (`src/Pricing/Pricing/Products/SchedulePriceChange.cs#L40-L146`). The handler comment at `L132` explains the use of `IMessageBus` over `OutgoingMessages` for the scheduled-message path.
- `CancelScheduledPriceChange(string Sku, Guid ScheduleId)` — HTTP `DELETE /api/pricing/products/{sku}/schedule/{scheduleId}` with `[Authorize(Policy = "PricingManager")]` (`src/Pricing/Pricing/Products/CancelScheduledPriceChange.cs#L12, L21-L22`). Single-method async handler; inline guards (404 if missing, 404 if `PendingSchedule == null`, 404 if `ScheduleId` mismatch) followed by `session.Events.Append(streamId, ScheduledPriceChangeCancelled(...))` (`src/Pricing/Pricing/Products/CancelScheduledPriceChange.cs#L19-L62`). Class XML-doc records the choice not to use the compound `Load/Before/Handle` shape because `DELETE` carries no body (`src/Pricing/Pricing/Products/CancelScheduledPriceChange.cs#L14-L18`). The Wolverine durable scheduled message remains queued; it is discarded on activation by the stale-schedule guard described in the next entry.
- `ActivateScheduledPriceChange(string Sku, Guid ScheduleId)` — internal scheduled message, not user-facing. Defined inline at `src/Pricing/Pricing/Products/SchedulePriceChange.cs#L152`. Handler is compound (`LoadAsync` → `Before` returning `HandlerContinuation.Stop` for missing aggregate, missing pending schedule, or mismatched `ScheduleId` → `Handle` appending `ScheduledPriceActivated`) (`src/Pricing/Pricing/Products/SchedulePriceChange.cs#L160-L197`). The `Stop` continuation is used in place of `ProblemDetails` because there is no HTTP caller; the comment block at `L154-L159` records this.

**Reconciliation note on the S1 stub command count.** S1 enumerated seven commands and additionally listed `FloorPriceSet` / `CeilingPriceSet` as "price-bound commands surfaced as records." The current code defines six commands (five user-facing + one internal scheduled). `FloorPriceSet` and `CeilingPriceSet` are domain events (`src/Pricing/Pricing/Products/FloorPriceSet.cs#L9-L16`, `src/Pricing/Pricing/Products/CeilingPriceSet.cs#L8-L15`), not commands; no `SetFloorPrice` or `SetCeilingPrice` command exists. The S1 count is reconciled to 6 (or 5 user-facing) and the floor/ceiling-as-command framing is dropped. See §Routes-without-instantiator — the floor/ceiling events have aggregate `Apply` methods and projection handlers but no emitter.

There is no saga in this BC; the only orchestration is the single delayed Wolverine message used to fire `ActivateScheduledPriceChange`.

## Domain events

All events are records under `src/Pricing/Pricing/Products/`. The event stream is persisted in the `pricing` Marten schema (`src/Pricing/Pricing.Api/Program.cs#L41`). The `ProductPrice` snapshot is inline-projected (`src/Pricing/Pricing.Api/Program.cs#L48`).

- `ProductRegistered(Guid ProductPriceId, string Sku, DateTimeOffset RegisteredAt)` (`src/Pricing/Pricing/Products/ProductRegistered.cs#L9-L12`). Emitted by `ProductAddedHandler` (`src/Pricing/Pricing/Products/ProductAddedHandler.cs#L21-L27`). Aggregate apply: `src/Pricing/Pricing/Products/ProductPrice.cs#L121-L129`.
- `InitialPriceSet(Guid ProductPriceId, string Sku, Money Price, Money? FloorPrice, Money? CeilingPrice, Guid SetBy, DateTimeOffset PricedAt)` (`src/Pricing/Pricing/Products/InitialPriceSet.cs#L8-L15`). Emitted by `SetInitialPriceHandler.Handle` (`src/Pricing/Pricing/Products/SetInitialPrice.cs#L89-L98`) and by the `Unpriced` branch of `SetBasePriceHandler.Handle` (`src/Pricing/Pricing/Products/SetBasePrice.cs#L102-L122`). Apply: `ProductPrice.cs#L131-L139`.
- `PriceChanged(Guid ProductPriceId, string Sku, Money OldPrice, Money NewPrice, DateTimeOffset PreviousPriceSetAt, string? Reason, Guid ChangedBy, DateTimeOffset ChangedAt, Guid? BulkPricingJobId, Guid? SourceSuggestionId)` (`src/Pricing/Pricing/Products/PriceChanged.cs#L8-L18`). Emitted by `ChangePriceHandler.Handle` (`src/Pricing/Pricing/Products/ChangePrice.cs#L79-L91`) and by the `Published` branch of `SetBasePriceHandler.Handle` (`src/Pricing/Pricing/Products/SetBasePrice.cs#L124-L138`). Apply: `ProductPrice.cs#L141-L147`.
- `PriceChangeScheduled(Guid ProductPriceId, string Sku, Guid ScheduleId, Money ScheduledPrice, DateTimeOffset ScheduledFor, Guid ScheduledBy, DateTimeOffset ScheduledAt)` (`src/Pricing/Pricing/Products/PriceChangeScheduled.cs#L8-L15`). Emitted by `SchedulePriceChangeHandler.Handle` (`src/Pricing/Pricing/Products/SchedulePriceChange.cs#L121-L130`). Apply: `ProductPrice.cs#L149-L161`.
- `ScheduledPriceActivated(Guid ProductPriceId, string Sku, Guid ScheduleId, Money ActivatedPrice, DateTimeOffset ActivatedAt)` (`src/Pricing/Pricing/Products/ScheduledPriceActivated.cs#L9-L14`). Emitted by `ActivateScheduledPriceChangeHandler.Handle` (`src/Pricing/Pricing/Products/SchedulePriceChange.cs#L182-L195`). Apply: `ProductPrice.cs#L169-L177`.
- `ScheduledPriceChangeCancelled(Guid ProductPriceId, string Sku, Guid ScheduleId, string? CancellationReason, Guid CancelledBy, DateTimeOffset CancelledAt)` (`src/Pricing/Pricing/Products/ScheduledPriceChangeCancelled.cs#L9-L15`). Emitted by `CancelScheduledPriceChangeHandler.HandleAsync` (`src/Pricing/Pricing/Products/CancelScheduledPriceChange.cs#L46-L54`). Apply: `ProductPrice.cs#L163-L167`.
- `FloorPriceSet(Guid ProductPriceId, string Sku, Money? OldFloorPrice, Money FloorPrice, Guid SetBy, DateTimeOffset SetAt, DateTimeOffset? ExpiresAt)` (`src/Pricing/Pricing/Products/FloorPriceSet.cs#L9-L16`). Apply: `ProductPrice.cs#L179-L183`. Projection branch: `CurrentPriceViewProjection.cs#L93-L100`. **Emitter:** none in the current codebase.
- `CeilingPriceSet(Guid ProductPriceId, string Sku, Money? OldCeilingPrice, Money CeilingPrice, Guid SetBy, DateTimeOffset SetAt, DateTimeOffset? ExpiresAt)` (`src/Pricing/Pricing/Products/CeilingPriceSet.cs#L8-L15`). Apply: `ProductPrice.cs#L185-L189`. Projection branch: `CurrentPriceViewProjection.cs#L102-L109`. **Emitter:** none.
- `PriceCorrected(Guid ProductPriceId, string Sku, Money CorrectedPrice, Money PreviousPrice, string CorrectionReason, Guid CorrectedBy, DateTimeOffset CorrectedAt)` (`src/Pricing/Pricing/Products/PriceCorrected.cs#L9-L16`). Apply: `ProductPrice.cs#L191-L196`. Projection branch: `CurrentPriceViewProjection.cs#L111-L119`. **Emitter:** none.
- `PriceDiscontinued(Guid ProductPriceId, string Sku, DateTimeOffset DiscontinuedAt)` (`src/Pricing/Pricing/Products/PriceDiscontinued.cs#L8-L11`). Apply: `ProductPrice.cs#L198-L202`. Projection branch: `CurrentPriceViewProjection.cs#L121-L130`. **Emitter:** none. No handler exists for the corresponding `Messages.Contracts.ProductCatalog.ProductDiscontinued` integration message inside Pricing (cf. Listings, which does subscribe).

Total: 10 events (matches S1). Four of the ten (`FloorPriceSet`, `CeilingPriceSet`, `PriceCorrected`, `PriceDiscontinued`) are wired into both the aggregate `Apply` set and the inline projection but have no command-, handler-, or HTTP-side emitter — they are reachable only by direct `session.Events.Append(...)` from a future caller.

## Projections

Two projections are registered in `Program.cs` against the `ProductPrice` event stream (`src/Pricing/Pricing.Api/Program.cs#L48-L51`); both run inline (zero lag, same transaction as the originating command).

- **`ProductPrice` snapshot** — inline; keyed by stream ID (`Guid`); registered via `opts.Projections.Snapshot<ProductPrice>(SnapshotLifecycle.Inline)` (`src/Pricing/Pricing.Api/Program.cs#L48`). Source events: all ten listed above (the aggregate has an `Apply` for each).
- **`CurrentPriceViewProjection`** — inline `MultiStreamProjection<CurrentPriceView, string>` keyed by `Sku` (`src/Pricing/Pricing/Products/CurrentPriceViewProjection.cs#L11-L26`). Registered at `src/Pricing/Pricing.Api/Program.cs#L51` via `opts.Projections.Add<CurrentPriceViewProjection>(ProjectionLifecycle.Inline)`. Maps the `Guid`-keyed event stream onto string-keyed `CurrentPriceView` documents using `Identity<TEvent>(x => x.Sku)` for nine of the ten events (`ProductRegistered` is excluded — the `CurrentPriceView` document is created on `InitialPriceSet`, not `ProductRegistered`, per `Create(InitialPriceSet)` at `L29-L43`). Apply branches: `PriceChanged` (`L45-L55`), `PriceChangeScheduled` (`L57-L66`), `ScheduledPriceChangeCancelled` (`L68-L77`), `ScheduledPriceActivated` (`L79-L91`), `FloorPriceSet` (`L93-L100`), `CeilingPriceSet` (`L102-L109`), `PriceCorrected` (`L111-L119`), `PriceDiscontinued` (`L121-L130`).

`CurrentPriceView` (`src/Pricing/Pricing/Products/CurrentPriceView.cs#L11-L85`) is the SKU-keyed read model — the hot-path document loaded by the `GET /api/pricing/products/{sku}` and `GET /api/pricing/products?skus=...` endpoints. The XML-doc at `L7-L9` records the case-sensitivity contract: SKUs must be `ToUpperInvariant()`-normalized at the API boundary because Marten string IDs are case-sensitive; `GetPrice` and `GetBulkPrices` both apply that normalization (`src/Pricing/Pricing.Api/Pricing/GetPrice.cs#L20`, `src/Pricing/Pricing.Api/Pricing/GetBulkPrices.cs#L29-L33`).

## Integration events

### Outbound (published)

Three contracts exist under `src/Shared/Messages.Contracts/Pricing/`:

- `Pricing.PricePublished(string Sku, decimal BasePrice, string Currency, DateTimeOffset PublishedAt)` (`src/Shared/Messages.Contracts/Pricing/PricePublished.cs#L8-L12`).
- `Pricing.PriceUpdated(string Sku, decimal OldPrice, decimal NewPrice, string Currency, DateTimeOffset EffectiveAt)` (`src/Shared/Messages.Contracts/Pricing/PriceUpdated.cs#L8-L13`).
- `Pricing.VendorPriceSuggestionSubmitted(Guid SuggestionId, string Sku, Guid VendorTenantId, decimal SuggestedPrice, string Currency, string? VendorJustification, DateTimeOffset SubmittedAt)` (`src/Shared/Messages.Contracts/Pricing/VendorPriceSuggestionSubmitted.cs#L9-L16`).

`Pricing.Api/Program.cs` declares no `PublishMessage<T>` route for any of the three contracts (`src/Pricing/Pricing.Api/Program.cs#L120-L154`). A repo-wide grep for `PricePublished | PriceUpdated | VendorPriceSuggestionSubmitted` outside the contract files themselves returns only one comment match (`src/Pricing/Pricing/Products/PriceCorrected.cs#L6` — XML-doc text only, not a publish call). No `OutgoingMessages.Add(new PricePublished(...))` or `bus.PublishAsync(new PriceUpdated(...))` call site exists. All three contracts are defined but unwired on the publish side; `VendorPriceSuggestionSubmitted` is documented in its own XML-doc as Vendor-Portal-published / Pricing-consumed (`src/Shared/Messages.Contracts/Pricing/VendorPriceSuggestionSubmitted.cs#L4-L8`), so the namespace location does not imply Pricing is the publisher.

### Inbound (subscribed)

One RabbitMQ queue is listened to (`src/Pricing/Pricing.Api/Program.cs#L152-L153`):

- **`pricing-product-added`** — `.ListenToRabbitQueue("pricing-product-added").ProcessInline()`. Carries `Messages.Contracts.ProductCatalog.ProductAdded`, published by the Product Catalog BC to the `product-catalog-product-added` exchange (`src/Product Catalog/ProductCatalog.Api/Program.cs#L134-L135`). Handler: `ProductAddedHandler.Handle` (`src/Pricing/Pricing/Products/ProductAddedHandler.cs#L15-L27`), which returns `MartenOps.StartStream<ProductPrice>(streamId, new ProductRegistered(...))`. Idempotency: the XML-doc at `L10-L12` records that Marten will throw on a duplicate stream ID; Wolverine's transactional outbox provides the at-least-once delivery boundary. The handler does not branch on `IsRecall`.

Total inbound: one integration message type across one queue.

### Routes-without-instantiator

- `Pricing.PricePublished` — contract defined, no publisher in tree, no in-tree subscriber.
- `Pricing.PriceUpdated` — contract defined, no publisher in tree, no in-tree subscriber.
- `Pricing.VendorPriceSuggestionSubmitted` — contract defined, no publisher in tree (Vendor Portal is not yet implemented per the contract's own XML-doc), no in-tree subscriber.
- `Messages.Contracts.ProductCatalog.ProductDiscontinued` — published by Product Catalog (`src/Product Catalog/ProductCatalog.Api/Program.cs#L136-L137`); Pricing has no listener queue for it and no `ProductDiscontinuedHandler`. Consequently, the `PriceDiscontinued` event emitter described in §Domain events does not exist.
- Domain events `FloorPriceSet`, `CeilingPriceSet`, `PriceCorrected`, `PriceDiscontinued` — declared, applied, and projected but not emitted by any in-tree command or integration handler.

### CONTEXTS.md drift

`CONTEXTS.md` lines 225–237 describe Pricing's external surface and ADR alignment; reconciled against the code:

- Line 234 ("Shopping → publishes — Published prices consumed by carts") implies Pricing publishes integration events to Shopping. The code defines `Pricing.PricePublished` and `Pricing.PriceUpdated` contracts but does not publish them (no `PublishMessage<T>` route, no emitter). Shopping currently consumes the `GET /api/pricing/products?skus=...` HTTP read endpoint at add-to-cart time (Pricing-side contract: `src/Pricing/Pricing.Api/Pricing/GetBulkPrices.cs#L18-L49`); the message-based "publishes" arrow is a forward-looking statement, not the current wiring. Code authoritative.
- Line 235 ("Vendor Portal ← receives commands — Vendor price updates; MAP violation alerts pushed back") implies a Pricing↔Vendor Portal integration. The only contract anchoring this is `VendorPriceSuggestionSubmitted`, which is unpublished and unconsumed. No "MAP violation alert" outbound contract exists in `src/Shared/Messages.Contracts/Pricing/`. Code authoritative.
- Line 233 ("Product Catalog ← receives — Product lifecycle events trigger price rule setup") is partially current: only `ProductAdded` is wired (one of several Product Catalog lifecycle events); `ProductDiscontinued`, `ProductDeleted`, `ProductRestored` etc. are not subscribed.
- Line 237 references ADR 0019 with the description "Bulk pricing uses a saga with approval workflow." No bulk-pricing saga exists in `src/Pricing/`; the only in-tree trace of bulk pricing is the unset `BulkPricingJobId` correlation slot on `PriceChanged` (`src/Pricing/Pricing/Products/PriceChanged.cs#L17`). Code authoritative.
- The S1 stub's ADR list omits ADR 0016 ("UUID v5 for Natural Key Stream IDs"), which is the ADR cited in `ProductPrice`'s class XML-doc (`src/Pricing/Pricing/Products/ProductPrice.cs#L8-L9, L101-L103`). The S1 stub also asserted "Stream ID: UUID v7"; the implementation is UUID v5. Both deviations are corrected here; the ADR list below adds ADR 0016 and ADR 0017.

The M48.0 S3 prompt's premise that Pricing "uses Marten DCB" is not borne out by source enumeration; there is no DCB tag type in `src/Pricing/`. Forward-note for S5: confirm intended grouping of Pricing among DCB BCs vs. plain-Guid event-sourced BCs.

## Sagas / orchestration

Not applicable — this BC is not a saga orchestrator. The only orchestration primitive is `messaging.ScheduleAsync(new ActivateScheduledPriceChange(...), cmd.ScheduledFor)` inside `SchedulePriceChangeHandler.Handle` (`src/Pricing/Pricing/Products/SchedulePriceChange.cs#L132-L134`), backed by Wolverine's durable scheduled-message infrastructure (`opts.Policies.UseDurableLocalQueues()` at `src/Pricing/Pricing.Api/Program.cs#L123`). Cancellation is a stale-message guard rather than a true cancellation: `ActivateScheduledPriceChangeHandler.Before` returns `HandlerContinuation.Stop` when the loaded aggregate's `PendingSchedule.ScheduleId` does not match the message's `ScheduleId` (`src/Pricing/Pricing/Products/SchedulePriceChange.cs#L169-L180`). The XML-doc at `src/Pricing/Pricing/Products/ScheduledPriceChangeCancelled.cs#L4-L7` records the architectural choice to discard rather than cancel.

## HTTP / API surface

All endpoints are routed via Wolverine HTTP attribute discovery (`MapWolverineEndpoints` at `src/Pricing/Pricing.Api/Program.cs#L188-L191`). FluentValidation runs through `UseFluentValidationProblemDetailMiddleware()` (`src/Pricing/Pricing.Api/Program.cs#L190`).

### Mutation

- `POST /api/pricing/products/{sku}/base-price` — `SetBasePrice`. `[Authorize(Policy = "PricingManager")]`. Source: `src/Pricing/Pricing/Products/SetBasePrice.cs#L90-L147`.
- `POST /api/pricing/products/{sku}/schedule` — `SchedulePriceChange`. `[Authorize(Policy = "PricingManager")]`. Source: `src/Pricing/Pricing/Products/SchedulePriceChange.cs#L108-L146`.
- `DELETE /api/pricing/products/{sku}/schedule/{scheduleId}` — `CancelScheduledPriceChange`. `[Authorize(Policy = "PricingManager")]`. Source: `src/Pricing/Pricing/Products/CancelScheduledPriceChange.cs#L21-L62`.

The `SetInitialPrice` and `ChangePrice` commands are reachable only via the Wolverine bus (no `[WolverinePost]`); their primary in-tree caller is the test suite (see §Tests as behavioral evidence).

### Read

- `GET /api/pricing/products/{sku}` — single-SKU current price; `session.LoadAsync<CurrentPriceView>(sku.ToUpperInvariant())`; returns `404` when the document is absent (`src/Pricing/Pricing.Api/Pricing/GetPrice.cs#L14-L27`). Anonymous (no `[Authorize]`).
- `GET /api/pricing/products?skus={comma-separated}` — bulk SKU current prices via `session.LoadManyAsync<CurrentPriceView>(skuList)`. Caps at 50 SKUs per request and returns partial results without `404` for missing SKUs (`src/Pricing/Pricing.Api/Pricing/GetBulkPrices.cs#L18-L49`). Anonymous. The XML-doc at `L13-L17` records the SLA target: `< 100ms p95` for 50 SKUs.

### Health and ops

- `GET /health`, `GET /alive` — Aspire defaults via `MapDefaultEndpoints()` (`src/Pricing/Pricing.Api/Program.cs#L181`).
- `GET /api/v1/health` — explicit health check, development-only (`src/Pricing/Pricing.Api/Program.cs#L183-L186`).
- `GET /` — `301 Moved Permanently` to `/api` (`src/Pricing/Pricing.Api/Program.cs#L193-L197`).
- `GET /api/v1/swagger.json` and `/api` Swagger UI — development-only (`src/Pricing/Pricing.Api/Program.cs#L163-L175`).

A development-only seeder (`SeedPricesAsync` at `src/Pricing/Pricing.Api/Program.cs#L208-L257`) starts 27 sample `ProductPrice` streams idempotently (gate: `Query<CurrentPriceView>().CountAsync() > 0`).

## Frontend surface

Not applicable — no frontend in this BC. The Backoffice BC owns the price-management UI consuming the `PricingManager`-policy endpoints.

## Identity / auth posture

- **Schemes:** Two JWT Bearer schemes are registered (`src/Pricing/Pricing.Api/Program.cs#L74-L107`):
  - Default `JwtBearerDefaults.AuthenticationScheme` — `Jwt:Issuer` defaults to `vendor-identity`, `Jwt:Audience` defaults to `vendor-portal`, `HMAC-SHA256` via `SymmetricSecurityKey` from `Jwt:SigningKey` (with a development fallback string at `L70`), `ClockSkew = 30s`.
  - `"Backoffice"` scheme — `Authority = https://localhost:5249`, `Audience = https://localhost:5249`, `RequireHttpsMetadata = false` in development, `RoleClaimType = "role"`.
- **Policies:** One — `PricingManager` (`src/Pricing/Pricing.Api/Program.cs#L110-L118`). Pinned to the `Backoffice` scheme; requires either `PricingManager` or `SystemAdmin` role. Applied to the three mutation endpoints listed above. The two read endpoints carry no `[Authorize]` attribute and are anonymous.
- **JWT subject capture:** All three command sites that resolve the acting user write `Guid.Empty` and carry a `// TODO: Extract from JWT claim` marker (`src/Pricing/Pricing/Products/SetBasePrice.cs#L110, L133`; `src/Pricing/Pricing/Products/SchedulePriceChange.cs#L127`; `src/Pricing/Pricing/Products/CancelScheduledPriceChange.cs#L51`). The audit fields on `InitialPriceSet`, `PriceChanged`, `PriceChangeScheduled`, and `ScheduledPriceChangeCancelled` therefore record `Guid.Empty` rather than the real principal in the current code.
- **Source:** `src/Pricing/Pricing.Api/Program.cs`.

## Tests as behavioral evidence

No Gherkin features under `docs/features/pricing/`.

Unit tests under `tests/Pricing/Pricing.UnitTests/` (xUnit; in-process; no infrastructure):

- `MoneyFactoryTests` (14), `MoneyEqualityTests` (13), `MoneyOperatorTests` (19), `MoneyJsonSerializationTests` (16) — the `Money` value object's factory rules, equality semantics, comparison guards, and `MoneyJsonConverter` round-trips.
- `ProductPriceStreamIdTests` (12) — determinism, case-insensitivity, version-5 / RFC 4122-variant bit assertions for `ProductPrice.StreamId`.
- `ProductPriceApplyTests` (15) — every aggregate `Apply(...)` branch including the four no-emitter events (`FloorPriceSet`, `CeilingPriceSet`, `PriceCorrected`, `PriceDiscontinued`).
- `CurrentPriceViewProjectionTests` (11) — every projection `Apply(...)` branch.
- `SetInitialPriceValidatorTests` (8), `ChangePriceValidatorTests` (6) — FluentValidation rule coverage.
- `SetInitialPriceHandlerTests` (4), `ChangePriceHandlerTests` (6), `ProductAddedHandlerTests` (4) — handler logic exercised against `IDocumentSession` doubles.

Integration tests under `tests/Pricing/Pricing.Api.IntegrationTests/` (xUnit, Alba + Testcontainers via `IntegrationTestCollection`):

- `SetBasePriceEndpointTests` (6) — the `Unpriced → Published` and `Published → Published` HTTP branches plus the `Discontinued` rejection.
- `SchedulePriceChangeEndpointTests` (4) — successful scheduling, conflict on existing `PendingSchedule`, floor/ceiling rejection, validation rejection.
- `CancelScheduledPriceChangeEndpointTests` (4) — successful cancellation, `404` on missing aggregate, `404` on missing schedule, `404` on `ScheduleId` mismatch.
- `GetPriceEndpointTests` (4), `GetBulkPricesEndpointTests` (7) — read-side coverage including the 50-SKU cap and partial-result behaviour.

Total: 132 unit + 25 integration = 157 tests across this BC. Shared lifecycle: `tests/Pricing/Pricing.Api.IntegrationTests/TestFixture.cs` (Postgres + RabbitMQ Testcontainers) and `IntegrationTestCollection.cs`.

## ADRs

- **ADR 0016** — UUID v5 for Natural Key Stream IDs. Establishes the deterministic SHA-1-based stream-id derivation Pricing uses for `ProductPrice` (`src/Pricing/Pricing/Products/ProductPrice.cs#L8-L9, L97-L119`). File: `docs/decisions/0016-uuid-v5-for-natural-key-stream-ids.md`.
- **ADR 0017** — Price Freeze at Add-to-Cart. The cross-BC contract anchoring the `Pricing.PricePublished` and `Pricing.PriceUpdated` integration messages and the cart-side price snapshot (Pricing is the publisher side; Shopping is the consumer side). The contract files reference this ADR by issue number (`docs/decisions/0017-price-freeze-at-add-to-cart.md#L315`). File: `docs/decisions/0017-price-freeze-at-add-to-cart.md`.
- **ADR 0018** — Money Value Object as Canonical Monetary Representation. The `Money` record at `src/Pricing/Pricing/Money.cs` and the `MoneyJsonConverter` are the implementation. File: `docs/decisions/0018-money-value-object-canonical-currency.md`.
- **ADR 0019** — Bulk Pricing Job Audit Trail via Event Sourcing. Anchors the `BulkPricingJobId` and `SourceSuggestionId` correlation slots on `PriceChanged` (`src/Pricing/Pricing/Products/PriceChanged.cs#L17-L18`). The bulk-pricing saga referenced in CONTEXTS.md line 237 is not yet implemented. File: `docs/decisions/0019-bulk-pricing-job-audit-trail.md`.
- **ADR 0020** — MAP vs Floor Price Distinction. Anchors the `FloorPrice` (margin-protection minimum) vs `CeilingPrice` (MAP / policy maximum) split on `ProductPrice` and the `FloorPriceSet` / `CeilingPriceSet` event pair; XML-doc at `src/Pricing/Pricing/Products/ProductPrice.cs#L36-L42` notes the Phase-2 split-out of a separate `MapPrice` for vendor MAP obligations. File: `docs/decisions/0020-map-vs-floor-price-distinction.md`.

## Prior event modeling

- `docs/planning/pricing-event-modeling.md` — the source-of-truth event-modeling artifact for the BC; defines the seven-command lifecycle, the `Messages.Contracts.Pricing` integration namespace (lines 389, 1125), and the `VendorPriceSuggestionSubmitted` integration boundary (line 544) anticipated for Vendor Portal Phase 2+.
- `docs/planning/pricing-ux-review.md` — the UX review artifact that resolved the `InitialPriceSet`-vs-`PriceChanged` event split (referenced from `src/Pricing/Pricing/Products/InitialPriceSet.cs#L4-L6`).

## Source citations (S2 full)

- `src/Pricing/` (folder root)
- `src/Pricing/Pricing/Products/ProductPrice.cs`, `PriceStatus.cs`, `ScheduledPriceChange.cs`
- `src/Pricing/Pricing/Products/SetInitialPrice.cs`, `SetBasePrice.cs`, `ChangePrice.cs`, `SchedulePriceChange.cs`, `CancelScheduledPriceChange.cs`
- `src/Pricing/Pricing/Products/ProductAddedHandler.cs`
- `src/Pricing/Pricing/Products/InitialPriceSet.cs`, `PriceChanged.cs`, `PriceChangeScheduled.cs`, `ScheduledPriceActivated.cs`, `ScheduledPriceChangeCancelled.cs`, `FloorPriceSet.cs`, `CeilingPriceSet.cs`, `PriceCorrected.cs`, `PriceDiscontinued.cs`, `ProductRegistered.cs`
- `src/Pricing/Pricing/Products/CurrentPriceView.cs`, `CurrentPriceViewProjection.cs`
- `src/Pricing/Pricing/Money.cs`, `MoneyJsonConverter.cs`, `Constants.cs`, `AssemblyAttributes.cs`
- `src/Pricing/Pricing.Api/Program.cs`
- `src/Pricing/Pricing.Api/Pricing/GetPrice.cs`, `GetBulkPrices.cs`
- `src/Shared/Messages.Contracts/Pricing/PricePublished.cs`, `PriceUpdated.cs`, `VendorPriceSuggestionSubmitted.cs`
- `src/Shared/Messages.Contracts/ProductCatalog/ProductAdded.cs` (inbound contract)
- `src/Product Catalog/ProductCatalog.Api/Program.cs` (publisher of `ProductAdded`)
- `CONTEXTS.md` (section: `Pricing`, lines 225–237)
- `docs/decisions/0016-uuid-v5-for-natural-key-stream-ids.md`
- `docs/decisions/0017-price-freeze-at-add-to-cart.md`
- `docs/decisions/0018-money-value-object-canonical-currency.md`
- `docs/decisions/0019-bulk-pricing-job-audit-trail.md`
- `docs/decisions/0020-map-vs-floor-price-distinction.md`
- `docs/planning/pricing-event-modeling.md`
- `docs/planning/pricing-ux-review.md`
- `tests/Pricing/Pricing.UnitTests/MoneyFactoryTests.cs`, `MoneyEqualityTests.cs`, `MoneyOperatorTests.cs`, `MoneyJsonSerializationTests.cs`, `ProductPriceStreamIdTests.cs`, `ProductPriceApplyTests.cs`, `CurrentPriceViewProjectionTests.cs`, `SetInitialPriceValidatorTests.cs`, `ChangePriceValidatorTests.cs`, `SetInitialPriceHandlerTests.cs`, `ChangePriceHandlerTests.cs`, `ProductAddedHandlerTests.cs`
- `tests/Pricing/Pricing.Api.IntegrationTests/SetBasePriceEndpointTests.cs`, `SchedulePriceChangeEndpointTests.cs`, `CancelScheduledPriceChangeEndpointTests.cs`, `GetPriceEndpointTests.cs`, `GetBulkPricesEndpointTests.cs`, `TestFixture.cs`, `IntegrationTestCollection.cs`
