# Listings

> **Source folder:** `src/Listings/`
> **Status:** Implemented
> **Most recent material milestone:** M36.1 — Listings BC closure (event-sourced lifecycle + recall cascade)
> **Dossier depth:** S2 — full

## Purpose

Listings owns the channel-listing lifecycle — the state machine that decides when and how a product SKU appears on a given marketplace channel. A listing moves through Draft → ReadyForReview → Submitted → Live → Paused → Ended, with branch states for force-down (e.g. recall cascade triggered by an upstream `ProductDiscontinued` event with `IsRecall=true`). Listings consumes Product Catalog events through a local `ProductSummaryView` anti-corruption layer so that internal Catalog status values never leak into the listing aggregate.

## Aggregates

### `Listing`

- **Stream ID:** Deterministic UUID v5 computed by `ListingStreamId.Compute(sku, channelCode)` (`src/Listings/Listings/Listing/ListingStreamId.cs#L21-L31`). The implementation hashes the literal byte string `listing:{sku}:{channelCode}` against the RFC 4122 URL namespace UUID (`6ba7b810-9dad-11d1-80b4-00c04fd430c8`) using SHA‑1 and forces the version-5 / RFC 4122-variant bits at offsets 6 and 8 (`src/Listings/Listings/Listing/ListingStreamId.cs#L33-L66`). The stream ID is computed at `CreateListingHandler.Handle` before `MartenOps.StartStream<Listing>(listingId, @event)` is called (`src/Listings/Listings/Listing/CreateListing.cs#L49-L60`); subsequent commands address the same stream by passing `ListingId` directly. The same key is recomputed inside `CreateListingHandler` to enforce the "no duplicate active listing for SKU+channel" invariant against `ListingsActiveView` (`src/Listings/Listings/Listing/CreateListing.cs#L52-L55`).
- **Key state:** `Id`, `Sku`, `ChannelCode`, `ProductName`, `Content?`, `Status` (`ListingStatus` enum), `CreatedAt`, `ActivatedAt?`, `EndedAt?`, `EndCause?` (`EndedCause` enum), `PauseReason?` (`src/Listings/Listings/Listing/Listing.cs#L8-L19`). The five optional fields are populated only by lifecycle transitions: `ActivatedAt` by `ListingActivated.Apply`, `EndedAt` + `EndCause` by `ListingEnded.Apply` and `ListingForcedDown.Apply`, `PauseReason` by `ListingPaused.Apply` (cleared by `ListingResumed.Apply`).
- **Lifecycle stages** (the `ListingStatus` enum at `src/Listings/Listings/Listing/ListingStatus.cs#L9-L15` defines six states):
  - `Draft` — set by the `Create()` factory when the stream is opened with `ListingDraftCreated` (`src/Listings/Listings/Listing/Listing.cs#L21-L32`).
  - `ReadyForReview` — set by `Apply(ListingSubmittedForReview)` (`src/Listings/Listings/Listing/Listing.cs#L34-L35`).
  - `Submitted` — set by `Apply(ListingApproved)` (`src/Listings/Listings/Listing/Listing.cs#L37-L38`). The state name "Submitted" denotes "approved, awaiting marketplace activation," not "submitted for review."
  - `Live` — set by `Apply(ListingActivated)` (and re-entered from `Paused` via `Apply(ListingResumed)`) (`src/Listings/Listings/Listing/Listing.cs#L40-L46, L57-L62`).
  - `Paused` — set by `Apply(ListingPaused)` along with the `PauseReason` string (`src/Listings/Listings/Listing/Listing.cs#L48-L55`).
  - `Ended` — terminal state; reached via `Apply(ListingEnded)` (records `EndCause`) or `Apply(ListingForcedDown)` (forces `EndCause = ProductDiscontinued`) (`src/Listings/Listings/Listing/Listing.cs#L64-L78`). The `IsTerminal` derived flag returns `Status == Ended` (`src/Listings/Listings/Listing/Listing.cs#L91`).
- **File:** `src/Listings/Listings/Listing/Listing.cs`.

The aggregate is a write-only record per the Decider pattern — all guards live in the command handlers' `Before` methods rather than in the aggregate itself (`src/Listings/Listings/Listing/Listing.cs#L3-L6`).

## Commands

Direct enumeration via `grep -rn "public sealed record" --include="*.cs" src/Listings/` yields seven command-shape records (matching the S1 stub count). All commands target the single `Listing` aggregate; all flow through Wolverine compound handlers with a `Before(...) → ProblemDetails` guard followed by a `Handle(... [WriteAggregate] Listing listing)` body that returns `Events` (and, for outward-facing transitions, an `OutgoingMessages` collection).

- `CreateListing(string Sku, string ChannelCode, string? InitialContent)` — opens a new `Listing` stream after validating the SKU exists in `ProductSummaryView` and is not in `Discontinued`/`Deleted` status, computing the deterministic stream ID, and rejecting duplicate active SKU+channel pairs via `ListingsActiveView`. Handler: `src/Listings/Listings/Listing/CreateListing.cs#L31-L73`. Request DTO + endpoint: `src/Listings/Listings.Api/Listings/ListingEndpoints.cs#L29-L42`.
- `SubmitListingForReview(Guid ListingId)` — appends `ListingSubmittedForReview`; rejected unless current status is `Draft` (`src/Listings/Listings/Listing/SubmitListingForReview.cs#L21-L38`).
- `ApproveListing(Guid ListingId)` — appends `ListingApproved` and emits the outbound `ListingApproved` integration message enriched with `ProductName` + `Category` from `ProductSummaryView`; rejected unless current status is `ReadyForReview` (`src/Listings/Listings/Listing/ApproveListing.cs#L23-L57`). The handler carries an `// TODO(M37.0)` marker recording the future move to a Marketplaces-side ACL (per ADR 0050) (`src/Listings/Listings/Listing/ApproveListing.cs#L40`).
- `ActivateListing(Guid ListingId)` — appends `ListingActivated` and emits the outbound `ListingActivated` integration message; the guard accepts `Submitted → Live` for marketplace channels and additionally accepts `Draft → Live` when `ChannelCode == "OWN_WEBSITE"` (case-insensitive), bypassing the review states for the in-house storefront (`src/Listings/Listings/Listing/ActivateListing.cs#L23-L60`).
- `PauseListing(Guid ListingId, string Reason)` — appends `ListingPaused`; rejected unless current status is `Live`. `Reason` is required and capped at 500 characters (`src/Listings/Listings/Listing/PauseListing.cs#L9-L39`).
- `ResumeListing(Guid ListingId)` — appends `ListingResumed`; rejected unless current status is `Paused` (`src/Listings/Listings/Listing/ResumeListing.cs#L9-L38`).
- `EndListing(Guid ListingId)` — appends `ListingEnded` with `EndedCause.ManualEnd` and emits the outbound `ListingEnded` integration message; rejected if `IsTerminal` (`src/Listings/Listings/Listing/EndListing.cs#L10-L55`).

**Reconciliation note on the S1 stub command count.** S1 enumerated 7 commands. Direct re-enumeration in S2 confirms 7. No deviation.

There are no saga-internal commands — Listings is not a saga orchestrator. Inbound integration events from Marketplaces and Product Catalog are handled directly against the aggregate inside `IDocumentSession` handlers rather than reified as local commands; see §Integration events below.

## Domain events

All events live in `src/Listings/Listings/Listing/Events.cs` and are persisted in the `listings` Marten schema. The `Listing` snapshot is inline-projected via `opts.Projections.Snapshot<Listings.Listing.Listing>(SnapshotLifecycle.Inline)` (`src/Listings/Listings.Api/Program.cs#L43`).

### `Listing` aggregate events

- `ListingDraftCreated(ListingId, Sku, ChannelCode, ProductName, InitialContent?, OccurredAt)` — a new listing draft has been opened for a SKU on a channel (`src/Listings/Listings/Listing/Events.cs#L6-L13`). Stream-opening event.
- `ListingSubmittedForReview(ListingId, OccurredAt)` — the listing has been routed to internal review (`src/Listings/Listings/Listing/Events.cs#L17-L20`).
- `ListingApproved(ListingId, OccurredAt)` — internal review has approved the listing for marketplace submission (`src/Listings/Listings/Listing/Events.cs#L24-L27`).
- `ListingActivated(ListingId, ChannelCode, OccurredAt)` — the listing is now Live on its channel (`src/Listings/Listings/Listing/Events.cs#L31-L34`). Raised both by the explicit `ActivateListing` HTTP command and by the `MarketplaceListingActivatedHandler` reacting to the Marketplaces BC.
- `ListingPaused(ListingId, Reason, OccurredAt)` — a Live listing has been temporarily withdrawn (`src/Listings/Listings/Listing/Events.cs#L39-L42`).
- `ListingResumed(ListingId, OccurredAt)` — a Paused listing has returned to Live (`src/Listings/Listings/Listing/Events.cs#L47-L49`).
- `ListingEnded(ListingId, Sku, ChannelCode, Cause, OccurredAt)` — the listing has reached terminal state via the normal flow. `Cause` is the `EndedCause` enum (`ManualEnd`, `ProductDiscontinued`, `SubmissionRejected`, `ProductDeleted`) (`src/Listings/Listings/Listing/Events.cs#L54-L60`, enum at `L84-L90`). Raised by `EndListing` (cause `ManualEnd`) and by `MarketplaceSubmissionRejectedHandler` (cause `SubmissionRejected`).
- `ListingForcedDown(ListingId, Sku, ChannelCode, RecallReason, OccurredAt)` — the listing has been force-ended by the recall cascade. Distinct from `ListingEnded` because the cascade bypasses normal state guards and supplies a free-form `RecallReason` string (`src/Listings/Listings/Listing/Events.cs#L65-L71`). The `Apply` method always sets `EndCause = ProductDiscontinued`.
- `ListingContentUpdated(ListingId, ProductName?, Description?, OccurredAt)` — content has been propagated from a Product Catalog change (`src/Listings/Listings/Listing/Events.cs#L75-L81`). Raised by `ContentPropagationHandler` against Live listings only (`src/Listings/Listings/ProductSummary/ContentPropagationHandler.cs#L17-L46`).

Total: 9 events (matches S1).

## Projections

Two projections are registered in `Program.cs` against the event stream; one additional document type is maintained imperatively by integration-event handlers and is therefore not registered as a Marten projection.

- **`Listing` snapshot** — inline; keyed by stream ID (`Guid`); source events: all 9 listed above. Registered at `src/Listings/Listings.Api/Program.cs#L43` via `opts.Projections.Snapshot<Listings.Listing.Listing>(SnapshotLifecycle.Inline)`. Serves the `GET /api/listings/{id}` and `GET /api/listings/all` queries through `session.Query<Listing>()` (`src/Listings/Listings.Api/Listings/ListingEndpoints.cs#L160-L175, L196-L226`).
- **`ListingsActiveView`** — inline `MultiStreamProjection<ListingsActiveView, string>` keyed by `Sku`; source events: `ListingDraftCreated`, `ListingEnded`, `ListingForcedDown` (`src/Listings/Listings/Projections/ListingsActiveViewProjection.cs#L31-L36, L51-L77`). Registered at `src/Listings/Listings.Api/Program.cs#L46`. Serves the `CreateListingHandler` duplicate-prevention check (`src/Listings/Listings/Listing/CreateListing.cs#L52-L55`), the `RecallCascadeHandler` enumeration of affected listings (`src/Listings/Listings/Listing/RecallCascadeHandler.cs#L31-L41`), and the `ContentPropagationHandler` enumeration of propagation targets (`src/Listings/Listings/ProductSummary/ContentPropagationHandler.cs#L24-L26`). The set tracks "non-terminal" listings (anything that has had `ListingDraftCreated` but not yet `ListingEnded`/`ListingForcedDown`); see the CONTEXTS.md drift note in §Integration events.

The `ProductSummaryView` ACL (`src/Listings/Listings/ProductSummary/ProductSummaryView.cs#L8-L31`) is a Marten document keyed by `Sku` (`string Id`). It is **not** an event-stream projection — it is mutated directly by nine Wolverine integration-message handlers (`src/Listings/Listings/ProductSummary/ProductSummaryHandlers.cs`) that call `session.Store(view with { ... })`. The ACL exposes a Listings-local enum `ProductSummaryStatus` (`Active`, `ComingSoon`, `Discontinued`, `Deleted`) (`src/Listings/Listings/ProductSummary/ProductSummaryView.cs#L36-L42`) so that Product Catalog's internal `ProductStatus` values never reach the `Listing` aggregate. The mapping from the wire string to the local enum is a closed switch in `ProductAddedHandler.MapStatus` (`src/Listings/Listings/ProductSummary/ProductSummaryHandlers.cs#L40-L46`) and `ProductStatusChangedHandler.Handle` (`src/Listings/Listings/ProductSummary/ProductSummaryHandlers.cs#L120-L126`); both fall back to `Active` for unknown values. Listings is one of two ACL instances in the system; the Marketplaces BC maintains an independent `ProductSummaryView` per ADR 0050.

## Integration events

### Outbound (published)

All published from inside command/event handlers via `OutgoingMessages` returned alongside the persisted event:

- `ListingCreated(ListingId, Sku, ChannelCode, OccurredAt)` — published by `CreateListingHandler` (`src/Listings/Listings/Listing/CreateListing.cs#L66-L71`). Contract: `src/Shared/Messages.Contracts/Listings/ListingIntegrationMessages.cs#L6-L10`.
- `ListingApproved(ListingId, Sku, ChannelCode, ProductName, Category?, Price?, OccurredAt)` — published by `ApproveListingHandler`; carries enriched product fields (`ProductName`, `Category`) sourced from the local `ProductSummaryView`, and an unset `Price = null` placeholder (`src/Listings/Listings/Listing/ApproveListing.cs#L42-L52`). Contract: `src/Shared/Messages.Contracts/Listings/ListingIntegrationMessages.cs#L17-L24`. Subscriber: Marketplaces BC (drives adapter submission); enrichment is the explicit Phase-2 tradeoff captured in ADR 0050 against the eventual Marketplaces-side ACL.
- `ListingActivated(ListingId, ChannelCode, OccurredAt)` — published by `ActivateListingHandler` (the explicit HTTP path, `src/Listings/Listings/Listing/ActivateListing.cs#L52-L55`) and by `MarketplaceListingActivatedHandler` (the choreography path from Marketplaces, `src/Listings/Listings/Listing/MarketplaceListingActivatedHandler.cs#L40-L43`). Contract: `src/Shared/Messages.Contracts/Listings/ListingIntegrationMessages.cs#L29-L32`.
- `ListingEnded(ListingId, Sku, ChannelCode, Cause, OccurredAt)` — published by `EndListingHandler` (cause `ManualEnd`, `src/Listings/Listings/Listing/EndListing.cs#L48-L53`) and by `MarketplaceSubmissionRejectedHandler` (cause `SubmissionRejected`, `src/Listings/Listings/Listing/MarketplaceSubmissionRejectedHandler.cs#L45-L50`). `Cause` is serialized as the `EndedCause` enum's string name. Contract: `src/Shared/Messages.Contracts/Listings/ListingIntegrationMessages.cs#L37-L43`.
- `ListingForcedDown(ListingId, Sku, ChannelCode, RecallReason, OccurredAt)` — published by `RecallCascadeHandler` for each affected listing (`src/Listings/Listings/Listing/RecallCascadeHandler.cs#L57-L62`). Contract: `src/Shared/Messages.Contracts/Listings/ListingIntegrationMessages.cs#L48-L54`.
- `ListingsCascadeCompleted(Sku, AffectedCount, OccurredAt)` — published by `RecallCascadeHandler` once per recall (always, including when zero listings are affected) (`src/Listings/Listings/Listing/RecallCascadeHandler.cs#L34-L39, L67-L70`). Contract: `src/Shared/Messages.Contracts/Listings/ListingIntegrationMessages.cs#L59-L62`.

`Program.cs` declares explicit RabbitMQ exchange routes for two of the six outbound contracts — `ListingActivated` → `listings-listing-activated` and `ListingEnded` → `listings-listing-ended` (`src/Listings/Listings.Api/Program.cs#L122-L125`). The other four (`ListingCreated`, `ListingApproved`, `ListingForcedDown`, `ListingsCascadeCompleted`) are emitted via `OutgoingMessages` without an explicit `PublishMessage` route; their delivery relies on Wolverine's auto-provisioning conventions configured via `.AutoProvision()` (`src/Listings/Listings.Api/Program.cs#L102`).

### Inbound (subscribed)

Three RabbitMQ queues are listened to (`src/Listings/Listings.Api/Program.cs#L107-L114`):

1. **`listings-product-catalog-events`** — carries the nine Product Catalog events that drive the `ProductSummaryView` ACL. Each has a dedicated handler in `src/Listings/Listings/ProductSummary/ProductSummaryHandlers.cs`:
   - `ProductCatalog.ProductAdded` → `ProductAddedHandler` (`L17-L37`) — creates the ACL document.
   - `ProductCatalog.ProductContentUpdated` → `ProductContentUpdatedHandler` (`L52-L63`) — updates `Name` + `Description`. The same message is also handled by `ContentPropagationHandler` (`src/Listings/Listings/ProductSummary/ContentPropagationHandler.cs#L17-L46`), which propagates the change as `ListingContentUpdated` to all Live listings for that SKU; both handlers run for each delivery.
   - `ProductCatalog.ProductCategoryChanged` → `ProductCategoryChangedHandler` (`L69-L77`).
   - `ProductCatalog.ProductImagesUpdated` → `ProductImagesUpdatedHandler` (`L83-L91`).
   - `ProductCatalog.ProductDimensionsChanged` → `ProductDimensionsChangedHandler` (`L97-L105`).
   - `ProductCatalog.ProductStatusChanged` → `ProductStatusChangedHandler` (`L111-L129`).
   - `ProductCatalog.ProductDeleted` → `ProductDeletedHandler` (`L135-L143`) — sets ACL status to `Deleted`.
   - `ProductCatalog.ProductRestored` → `ProductRestoredHandler` (`L149-L157`) — sets ACL status to `Active`.
   - `ProductCatalog.ProductDiscontinued` → `ProductDiscontinuedSummaryHandler` (`L164-L172`) — sets ACL status to `Discontinued`. The same message is also routed to `RecallCascadeHandler` on a separate queue (next).
2. **`listings-product-recall`** — carries the priority copy of `ProductCatalog.ProductDiscontinued` consumed by `RecallCascadeHandler` (`src/Listings/Listings/Listing/RecallCascadeHandler.cs#L17-L74`). The handler returns immediately when `IsRecall == false`; otherwise it loads `ListingsActiveView` for the SKU, force-downs every non-terminal listing, and publishes `ListingForcedDown` per listing plus a single `ListingsCascadeCompleted` summary. The contract field `ProductDiscontinued.IsRecall` (default `false`) and the optional `Reason` are defined at `src/Shared/Messages.Contracts/ProductCatalog/ProductDiscontinued.cs#L12-L13`.
3. **`listings-marketplace-outcome-events`** — bound to two Marketplaces exchanges (`marketplaces-listing-activated`, `marketplaces-submission-rejected`) via `.DeclareExchange(...).BindQueue(...)` (`src/Listings/Listings.Api/Program.cs#L103-L104`). Carries:
   - `Marketplaces.MarketplaceListingActivated` → `MarketplaceListingActivatedHandler` (`src/Listings/Listings/Listing/MarketplaceListingActivatedHandler.cs#L19-L45`). Transitions `Submitted → Live`. Idempotent: silent no-op if the listing is missing, already `Live`, or in any state other than `Submitted`.
   - `Marketplaces.MarketplaceSubmissionRejected` → `MarketplaceSubmissionRejectedHandler` (`src/Listings/Listings/Listing/MarketplaceSubmissionRejectedHandler.cs#L19-L51`). Transitions `Submitted → Ended` with cause `SubmissionRejected`.

Total inbound: 11 distinct integration message types across three queues.

### Routes-without-instantiator

None found. Every inbound route declared in `src/Listings/Listings.Api/Program.cs` has at least one matching handler discoverable in `src/Listings/Listings/`. The only routing asymmetry is on the outbound side and is documented above (four of the six outbound contracts publish without explicit `PublishMessage` exchange routes, relying on auto-provision).

### CONTEXTS.md drift

`CONTEXTS.md` lines 170–174 describe the recall cascade as force-downing "all Live and Paused listings for the affected SKU." The code force-downs every non-terminal listing — i.e. any state other than `Ended` (`Draft`, `ReadyForReview`, `Submitted`, `Live`, `Paused`). This follows from `ListingsActiveView` adding stream IDs on `ListingDraftCreated` and removing them only on `ListingEnded` / `ListingForcedDown` (`src/Listings/Listings/Projections/ListingsActiveViewProjection.cs#L31-L36`), and from `RecallCascadeHandler` iterating the full `ActiveListingStreamIds` set with only an `IsTerminal` guard at the per-listing step (`src/Listings/Listings/Listing/RecallCascadeHandler.cs#L43-L48`). The code is authoritative; the CONTEXTS.md sentence is a narrower summary than the implementation.

## Sagas / orchestration

Not applicable — this BC is not a saga orchestrator. The two Marketplaces-driven state transitions (`Submitted → Live`, `Submitted → Ended`) are choreographed via the standalone Wolverine handlers `MarketplaceListingActivatedHandler` and `MarketplaceSubmissionRejectedHandler` rather than as steps in a stateful saga. The recall cascade is similarly choreographed: `RecallCascadeHandler` reacts to `ProductCatalog.ProductDiscontinued (IsRecall=true)` and emits per-listing `ListingForcedDown` events plus a single terminal `ListingsCascadeCompleted` integration message.

## HTTP / API surface

All endpoints are defined in `src/Listings/Listings.Api/Listings/ListingEndpoints.cs` and registered via Wolverine HTTP attribute routing (`MapWolverineEndpoints` at `src/Listings/Listings.Api/Program.cs#L155-L158`). Every endpoint is `[Authorize]` (JWT Bearer; see §Identity / auth posture). FluentValidation runs on every command via `UseFluentValidationProblemDetailMiddleware()` on the HTTP middleware chain (`src/Listings/Listings.Api/Program.cs#L157`).

### Listing lifecycle (mutation)

- `POST /api/listings` — create a new listing draft. Body: `CreateListingRequest(Sku, ChannelCode, InitialContent?)`. Returns `201 Created` with `CreateListingResponse(ListingId, Sku, ChannelCode)` and a `Location` header. Handler: `CreateListingEndpoint.Handle` (`src/Listings/Listings.Api/Listings/ListingEndpoints.cs#L31-L42`). Auth: `[Authorize]` (JWT).
- `POST /api/listings/{id}/submit-for-review` — invoke `SubmitListingForReview`. Handler: `SubmitForReviewEndpoint.Handle` (`L46-L55`).
- `POST /api/listings/{id}/approve` — invoke `ApproveListing`. Handler: `ApproveListingEndpoint.Handle` (`L59-L68`).
- `POST /api/listings/{id}/activate` — invoke `ActivateListing`. Handler: `ActivateListingEndpoint.Handle` (`L72-L81`).
- `POST /api/listings/{id}/pause?reason={...}` — invoke `PauseListing`. Note: `reason` is bound as a query/route parameter rather than a body field. Handler: `PauseListingEndpoint.Handle` (`L85-L95`).
- `POST /api/listings/{id}/resume` — invoke `ResumeListing`. Handler: `ResumeListingEndpoint.Handle` (`L99-L108`).
- `POST /api/listings/{id}/end` — invoke `EndListing`. Handler: `EndListingEndpoint.Handle` (`L112-L121`).

### Listing read

- `GET /api/listings/{id}` — load a single listing snapshot via `session.Events.AggregateStreamAsync<Listing>(id)`. Returns `ListingResponse` or `404`. Handler: `GetListingEndpoint.Handle` (`src/Listings/Listings.Api/Listings/ListingEndpoints.cs#L131-L155`). Consumed by Backoffice's listing detail page (`tests/Backoffice/Backoffice.E2ETests/Pages/ListingsAdminPage.cs`, `Features/ListingsDetail.feature.cs`).
- `GET /api/listings?sku={sku}` — list all listings for a single SKU via `session.Query<Listing>().Where(l => l.Sku == sku)`. Returns `400` when `sku` is omitted. Handler: `ListListingsEndpoint.Handle` (`src/Listings/Listings.Api/Listings/ListingEndpoints.cs#L159-L181`).
- `GET /api/listings/all?page=&pageSize=&status=` — paginated admin listing query. `page` defaults to 1; `pageSize` defaults to 25 and is clamped to `[1, 100]`; optional `status` parses against the `ListingStatus` enum (case-insensitive). Returns `PaginatedListingsResponse(Items, TotalCount, Page, PageSize)`. Handler: `ListAllListingsEndpoint.Handle` (`src/Listings/Listings.Api/Listings/ListingEndpoints.cs#L195-L228`). Consumed by Backoffice's listings admin page.

Per `CONTEXTS.md` line 172, Backoffice is the sole HTTP consumer of these endpoints; this is corroborated by the Backoffice E2E test fixtures listed above. CORS allows the Backoffice WASM origin only (`Cors:BackofficeOrigin`, default `http://localhost:5244`) (`src/Listings/Listings.Api/Program.cs#L21-L31`).

## Frontend surface

Not applicable — no frontend in this BC. The Backoffice BC owns the listings admin and detail pages; see `tests/Backoffice/Backoffice.E2ETests/Pages/ListingsAdminPage.cs` and the `ListingsAdmin.feature` / `ListingsDetail.feature` files in the Backoffice E2E suite.

## Identity / auth posture

- **Scheme:** JWT Bearer (single default scheme registered via `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)`) (`src/Listings/Listings.Api/Program.cs#L52-L72`).
- **Issuer / audience:** `Jwt:Issuer` defaults to `backoffice-identity`; `Jwt:Audience` defaults to `listings-api`. Signing key from `Jwt:SigningKey` (HMAC-SHA256 via `SymmetricSecurityKey`). `ValidateIssuer = true`, `ValidateAudience = true`, `ValidateIssuerSigningKey = true`, `ValidateLifetime = true`, `ClockSkew = 30s`.
- **Policies:** None registered. `builder.Services.AddAuthorization()` is called with no named policies; every endpoint uses the bare `[Authorize]` attribute (`src/Listings/Listings.Api/Listings/ListingEndpoints.cs#L33, L48, L61, L74, L87, L101, L114, L132, L161, L198`).
- **Roles:** `RoleClaimType = "role"` is configured (`src/Listings/Listings.Api/Program.cs#L70`) but no `[Authorize(Roles=...)]` constraints are applied — any valid Backoffice-issued JWT grants access to all listing endpoints.
- **Anonymous endpoints:** `GET /health` (`src/Listings/Listings.Api/Program.cs#L162-L163`) and the Aspire `MapDefaultEndpoints()` health/alive probes (`L152`).
- **Source:** `src/Listings/Listings.Api/Program.cs`.

## Tests as behavioral evidence

No Gherkin features under `docs/features/listings/`; the Backoffice-level Gherkin (`tests/Backoffice/Backoffice.E2ETests/Features/ListingsAdmin.feature` / `ListingsDetail.feature`) covers the admin UI workflows that depend on this BC, but is owned by the Backoffice dossier.

Integration tests under `tests/Listings/Listings.Api.IntegrationTests/` (xUnit, Alba + Testcontainers via the shared `IntegrationTestCollection`):

- `ListingLifecycleTests` (9 tests) — the create/activate/pause/resume/end transitions invoked via `ExecuteAndWaitAsync` on the Wolverine bus, including `OWN_WEBSITE` Draft → Live fast-path coverage. File: `tests/Listings/Listings.Api.IntegrationTests/ListingLifecycleTests.cs`.
- `ListingEndpointTests` (9 tests) — the HTTP surface end-to-end via Alba. File: `tests/Listings/Listings.Api.IntegrationTests/ListingEndpointTests.cs`.
- `ReviewWorkflowTests` (5 tests) — Draft → ReadyForReview → Submitted transitions and the guards on each step. File: `tests/Listings/Listings.Api.IntegrationTests/ReviewWorkflowTests.cs`.
- `RecallCascadeTests` (4 tests) — exercise `RecallCascadeHandler` against multiple Live + Paused + Draft listings, confirming the per-listing `ListingForcedDown` plus the summary `ListingsCascadeCompleted` and the `IsRecall == false` short-circuit. File: `tests/Listings/Listings.Api.IntegrationTests/RecallCascadeTests.cs`.
- `MarketplaceListingActivatedHandlerTests` (3 tests) — Submitted → Live transition, idempotency on already-Live, no-op on missing/unsubmitted listings. File: `tests/Listings/Listings.Api.IntegrationTests/MarketplaceListingActivatedHandlerTests.cs`.
- `MarketplaceSubmissionRejectedHandlerTests` (3 tests) — Submitted → Ended (`SubmissionRejected`) transition, idempotency on already-Ended, no-op on missing listings. File: `tests/Listings/Listings.Api.IntegrationTests/MarketplaceSubmissionRejectedHandlerTests.cs`.
- `ContentPropagationTests` (3 tests) — `ProductContentUpdated` propagation to Live listings only, with `Draft`/`Paused` listings left untouched. File: `tests/Listings/Listings.Api.IntegrationTests/ContentPropagationTests.cs`.
- `ProductSummaryViewTests` (4 tests) — the nine Product Catalog → ACL handlers in `ProductSummaryHandlers.cs`. File: `tests/Listings/Listings.Api.IntegrationTests/ProductSummaryViewTests.cs`.
- `HealthCheckTests` (1 test) — `GET /health` returns `200 Healthy`. File: `tests/Listings/Listings.Api.IntegrationTests/HealthCheckTests.cs`.

Total: 41 integration tests across 9 test classes. Shared lifecycle: `tests/Listings/Listings.Api.IntegrationTests/TestFixture.cs` (Postgres + RabbitMQ Testcontainers) and `IntegrationTestCollection.cs` (xUnit collection fixture).

No `@pending` or `@wip` scenarios.

## ADRs

- **ADR 0042** — `catalog:` Namespace UUID v5 Convention. Establishes the Product-Catalog-side `catalog:{sku}` UUID v5 convention. Listings adopts the parallel `listing:{sku}:{channelCode}` convention in `ListingStreamId.Compute` (`src/Listings/Listings/Listing/ListingStreamId.cs#L9-L13`); the source comment notes "Per ADR 0042's catalog: pattern — Listings BC uses listing: namespace." File: `docs/decisions/0042-catalog-namespace-uuid-v5-convention.md`. Note: ADR 0042 itself is marked `⚠️ Proposed (Convention Documented, Implementation Deferred)` for Product Catalog; the convention is implemented in Listings today.
- **ADR 0048** — Marketplace Document Entity Design. A Marketplaces-scope decision; the part materially shaping Listings is the implication that Marketplaces is a Marten document store (not event-sourced), which means cross-BC integration to Marketplaces is the only way Listings reaches marketplace state. File: `docs/decisions/0048-marketplace-document-entity-design.md`.
- **ADR 0049** — Category Mapping Ownership. Places category-to-marketplace mapping inside the Marketplaces BC, not Listings. The Listings-side consequence is that the `ListingApproved` integration message carries `Category` from the Listings ACL but does not attempt any channel-specific category translation (`src/Listings/Listings/Listing/ApproveListing.cs#L40-L52`). File: `docs/decisions/0049-category-mapping-ownership.md`.
- **ADR 0050** — Marketplaces ProductSummaryView Anti-Corruption Layer. Records the deliberate Phase-2 tradeoff: `ListingApproved` is enriched with `ProductName` / `Category` / `Price` so Marketplaces can build submissions without a local product cache, with the long-term direction being a Marketplaces-side ACL that mirrors the one Listings already maintains. The `// TODO(M37.0)` marker in `src/Listings/Listings/Listing/ApproveListing.cs#L40` is the in-code reference to this ADR. File: `docs/decisions/0050-marketplaces-product-summary-acl.md`.

## Prior event modeling

- `docs/planning/catalog-listings-marketplaces-discovery.md` — the joint Product Owner + UX Engineer discovery session that surfaced the channel-listing lifecycle states, the recall use case, and the cross-BC ownership of category mapping (decisions D1, D4, D5 cited as resolved).
- `docs/planning/catalog-listings-marketplaces-evolution-plan.md` — the Principal Architect's resolution of D1–D10, including the Listings BC's event-sourced selection and the `listing:{sku}:{channelCode}` UUID v5 stream-ID derivation.
- `docs/planning/catalog-listings-marketplaces-cycle-plan.md` — the cross-functional cycle-by-cycle execution plan that scheduled the Listings BC build-out culminating in M36.1.
- `docs/planning/catalog-listings-marketplaces-glossary.md` — the team-standard glossary anchoring the ubiquitous language used in this dossier (Listing, ChannelCode, Live, Paused, Recall Cascade, Force-Down).

## Source citations (S2 full)

- `src/Listings/` (folder root)
- `src/Listings/Listings/Listing/Listing.cs`, `ListingStatus.cs`, `ListingStreamId.cs`, `Events.cs`
- `src/Listings/Listings/Listing/CreateListing.cs`, `SubmitListingForReview.cs`, `ApproveListing.cs`, `ActivateListing.cs`, `PauseListing.cs`, `ResumeListing.cs`, `EndListing.cs`
- `src/Listings/Listings/Listing/MarketplaceListingActivatedHandler.cs`, `MarketplaceSubmissionRejectedHandler.cs`, `RecallCascadeHandler.cs`
- `src/Listings/Listings/ProductSummary/ProductSummaryView.cs`, `ProductSummaryHandlers.cs`, `ContentPropagationHandler.cs`
- `src/Listings/Listings/Projections/ListingsActiveViewProjection.cs`
- `src/Listings/Listings.Api/Program.cs`
- `src/Listings/Listings.Api/Listings/ListingEndpoints.cs`
- `src/Shared/Messages.Contracts/Listings/ListingIntegrationMessages.cs`
- `src/Shared/Messages.Contracts/ProductCatalog/ProductDiscontinued.cs` (inbound recall contract)
- `CONTEXTS.md` (section: `Listings`, lines 162–174)
- `docs/decisions/0042-catalog-namespace-uuid-v5-convention.md`
- `docs/decisions/0048-marketplace-document-entity-design.md`
- `docs/decisions/0049-category-mapping-ownership.md`
- `docs/decisions/0050-marketplaces-product-summary-acl.md`
- `docs/planning/catalog-listings-marketplaces-discovery.md`
- `docs/planning/catalog-listings-marketplaces-evolution-plan.md`
- `docs/planning/catalog-listings-marketplaces-cycle-plan.md`
- `docs/planning/catalog-listings-marketplaces-glossary.md`
- `tests/Listings/Listings.Api.IntegrationTests/ListingLifecycleTests.cs`, `ListingEndpointTests.cs`, `ReviewWorkflowTests.cs`, `RecallCascadeTests.cs`, `MarketplaceListingActivatedHandlerTests.cs`, `MarketplaceSubmissionRejectedHandlerTests.cs`, `ContentPropagationTests.cs`, `ProductSummaryViewTests.cs`, `HealthCheckTests.cs`, `TestFixture.cs`, `IntegrationTestCollection.cs`
