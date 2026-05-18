# Marketplaces

> **Source folder:** `src/Marketplaces/`
> **Status:** Implemented
> **Most recent material milestone:** M40.0 — Real marketplace adapters (Amazon SP-API / Walmart / eBay) with vault-backed credentials and resilience policies
> **Dossier depth:** S2 — full (Variant D — Marten document store BC)

## Purpose

Marketplaces owns the channel configuration and adapter-orchestration layer that submits Listings BC outputs to external marketplace APIs (Amazon US, Walmart US, eBay US). It holds the channel registry, the internal-to-channel category map, and the per-channel `IMarketplaceAdapter` implementations that authenticate, submit, poll, and deactivate listings on the external systems. A local `ProductSummaryView` ACL isolates the BC from Listings' integration message payloads (per ADR 0050) and a separate `OrphanedEbayDraft` document tracks the eBay create-offer / publish-offer recovery lifecycle (per ADR 0055 follow-up). This BC stores all of its state in Marten document collections — there are no event-sourced aggregates and no domain events.

## Document types (instead of Aggregates)

Direct enumeration of `opts.Schema.For<...>().Identity(x => x.Id)` calls in `src/Marketplaces/Marketplaces.Api/Program.cs#L48-L59` yields four registered Marten document types. The S1 stub recorded 2 (`Marketplace`, `CategoryMapping`); S2 reconciliation surfaces two additional document types (`ProductSummaryView`, `OrphanedEbayDraft`) — see "S1 reconciliation note" at the end of this section.

### `Marketplace`

- **Identity:** `string Id` — the stable channel code (e.g. `AMAZON_US`, `WALMART_US`, `EBAY_US`) used as the Marten document key. Identity is configured at `src/Marketplaces/Marketplaces.Api/Program.cs#L48` via `opts.Schema.For<Marketplace>().Identity(x => x.Id)`. Field declared at `src/Marketplaces/Marketplaces/Marketplaces/Marketplace.cs#L11` with `init` accessor (channel code is fixed at registration).
- **Storage:** Marten document store, schema `marketplaces` (`src/Marketplaces/Marketplaces.Api/Program.cs#L45`), written via `session.Store()`. **Not event-sourced.**
- **Notable fields:** `DisplayName`, `IsActive` (registration sets `true`; `DeactivateMarketplace` flips to `false`), `IsOwnWebsite` (always `false` for documents in this collection — `OWN_WEBSITE` is the Listings BC fast-path, not modelled here per the file comment at `src/Marketplaces/Marketplaces/Marketplaces/Marketplace.cs#L5-L6`), `ApiCredentialVaultPath` (vault path used by adapters to look up secrets), `CreatedAt`, `UpdatedAt`.
- **File:** `src/Marketplaces/Marketplaces/Marketplaces/Marketplace.cs`.
- **Lifecycle:** Created by `RegisterMarketplaceEndpoint.Handle` via `session.Store(marketplace)` after an idempotent existence check by `ChannelCode` (`src/Marketplaces/Marketplaces.Api/Marketplaces/RegisterMarketplace.cs#L57-L80`). Mutated by `UpdateMarketplaceEndpoint.Handle` (display name, vault path, `UpdatedAt`) via `session.Store(marketplace)` (`src/Marketplaces/Marketplaces.Api/Marketplaces/UpdateMarketplace.cs#L43-L51`). Mutated by `DeactivateMarketplaceEndpoint.Handle` (sets `IsActive = false`, updates `UpdatedAt`) via `session.Store(marketplace)` (`src/Marketplaces/Marketplaces.Api/Marketplaces/DeactivateMarketplace.cs#L31-L40`); deactivation is idempotent and only emits `MarketplaceDeactivated` on the active → inactive transition (`#L43-L48`). No `session.Delete()` call sites — there is no marketplace deletion path. Seeded for `AMAZON_US`, `WALMART_US`, `EBAY_US` in Development by `MarketplacesSeedData.SeedAsync` (`src/Marketplaces/Marketplaces.Api/MarketplacesSeedData.cs#L36-L70`).

### `CategoryMapping`

- **Identity:** `string Id` derived as a composite key `"{ChannelCode}:{InternalCategory}"`. Derivation rule applied at write sites (`src/Marketplaces/Marketplaces.Api/CategoryMappings/SetCategoryMapping.cs#L48`: `var compositeId = $"{command.ChannelCode}:{command.InternalCategory}"`) and at the read site (`src/Marketplaces/Marketplaces.Api/CategoryMappings/GetCategoryMapping.cs#L27`). The `Id` field carries the composite string and is `init`-only (`src/Marketplaces/Marketplaces/CategoryMappings/CategoryMapping.cs#L11`); identity registered at `src/Marketplaces/Marketplaces.Api/Program.cs#L51`.
- **Storage:** Marten document store, schema `marketplaces`, written via `session.Store()`. **Not event-sourced.**
- **Notable fields:** `ChannelCode`, `InternalCategory`, `MarketplaceCategoryId` (the channel-side category id, e.g. `AMZN-PET-DOGS-001`), `MarketplaceCategoryPath` (optional human-readable path), `LastVerifiedAt`.
- **File:** `src/Marketplaces/Marketplaces/CategoryMappings/CategoryMapping.cs`.
- **Lifecycle:** Created or updated by `SetCategoryMappingEndpoint.Handle` via `session.Store(...)` — the handler loads by composite key, mutates the mutable fields and re-stores when the mapping exists, otherwise constructs a new `CategoryMapping` and stores it (`src/Marketplaces/Marketplaces.Api/CategoryMappings/SetCategoryMapping.cs#L51-L72`). Read by `ListingApprovedHandler.Handle` to translate the internal product category to the marketplace-specific category id during submission (`src/Marketplaces/Marketplaces.Api/Listings/ListingApprovedHandler.cs#L70-L81`). No `session.Delete()` call sites. Seeded with 18 mappings (6 internal categories × 3 channels) in Development by `MarketplacesSeedData.SeedAsync` (`src/Marketplaces/Marketplaces.Api/MarketplacesSeedData.cs#L83-L115`).

### `ProductSummaryView` (ACL document)

- **Identity:** `string Id` — the product SKU (`src/Marketplaces/Marketplaces/Products/ProductSummaryView.cs#L17`). Identity registered at `src/Marketplaces/Marketplaces.Api/Program.cs#L54`. Marketplaces never derives this id locally — it is taken from the Product Catalog integration message's `Sku` field.
- **Storage:** Marten document store, schema `marketplaces`, written via `session.Store()`. **Not event-sourced.** Mutated exclusively by Wolverine integration-message handlers; no HTTP endpoint mutates this collection (the handlers are in `src/Marketplaces/Marketplaces/Products/ProductSummaryHandlers.cs` per the file comment at `src/Marketplaces/Marketplaces/Products/ProductSummaryView.cs#L4-L5`).
- **Notable fields:** `ProductName` (used for marketplace submission payloads), `Category` (used to look up the `CategoryMapping` for the `{ChannelCode, Category}` pair), `BasePrice` (used as default listing price when the inbound `ListingApproved` message carries no channel-specific price), `Status` (the local `ProductSummaryStatus` enum at `src/Marketplaces/Marketplaces/Products/ProductSummaryView.cs#L47-L53` — `Active`, `ComingSoon`, `Discontinued`, `Deleted`).
- **File:** `src/Marketplaces/Marketplaces/Products/ProductSummaryView.cs`.
- **Lifecycle:** Created by `ProductAddedHandler.Handle` on `ProductCatalog.ProductAdded` via `session.Store(new ProductSummaryView { ... })` after an idempotency check (`src/Marketplaces/Marketplaces/Products/ProductSummaryHandlers.cs#L15-L29`). Mutated by `ProductContentUpdatedHandler.Handle` (sets `ProductName` only — `Description` is intentionally omitted per the handler comment at `src/Marketplaces/Marketplaces/Products/ProductSummaryHandlers.cs#L42`), `ProductCategoryChangedHandler.Handle` (sets `Category`), `ProductStatusChangedHandler.Handle` (maps the inbound string `NewStatus` to the local enum) (`src/Marketplaces/Marketplaces/Products/ProductSummaryHandlers.cs#L46-L93`). All three update handlers no-op when the SKU has not yet been added (`session.LoadAsync<ProductSummaryView>(message.Sku)` returning `null` early-returns). No `session.Delete()` call sites — Marketplaces does not currently react to `ProductCatalog.ProductDeleted`/`ProductDiscontinued`/`ProductRestored`/`ProductImagesUpdated`/`ProductDimensionsChanged`.

### `OrphanedEbayDraft`

- **Identity:** `string Id` — the prefixed orphan id (e.g. `ebay-OFFER-ABC-123`), the same value carried in `SubmissionResult.OrphanedExternalSubmissionId` so re-detection upserts the same document (`src/Marketplaces/Marketplaces.Api/Listings/OrphanedEbayDraft.cs#L20-L23`). Identity registered at `src/Marketplaces/Marketplaces.Api/Program.cs#L59`.
- **Storage:** Marten document store, schema `marketplaces`, written via `session.Store()`. **Not event-sourced.**
- **Notable fields:** `ListingId`, `Sku`, `ChannelCode` (defaults to `"EBAY_US"`), `DetectedAt`, `CleanupAttempts`, `LastCleanupAttemptAt`, `LastFailureReason`, `IsCleaned`, `CleanedAt`. Tracks the lifecycle of an UNPUBLISHED eBay offer left behind when `EbayMarketplaceAdapter.SubmitListingAsync`'s create-offer step succeeds but the publish-offer step fails.
- **File:** `src/Marketplaces/Marketplaces.Api/Listings/OrphanedEbayDraft.cs`.
- **Lifecycle:** Created by `ListingApprovedHandler.Handle` when the adapter result populates `OrphanedExternalSubmissionId` — `session.Store(new OrphanedEbayDraft { ... })` runs alongside the rejection / activation publish on the same Wolverine session (`src/Marketplaces/Marketplaces.Api/Listings/ListingApprovedHandler.cs#L124-L136`). Mutated and (logically, via `IsCleaned = true`) terminated by `SweepOrphanedEbayDraftsHandler.Handle`: each pass loads the oldest uncleaned drafts (`session.Query<OrphanedEbayDraft>().Where(o => !o.IsCleaned).OrderBy(o => o.DetectedAt)`), invokes `IMarketplaceAdapter.DeleteOrphanedDraftAsync`, and re-stores the document with updated `CleanupAttempts` / `IsCleaned` / `CleanedAt` / `LastFailureReason` (`src/Marketplaces/Marketplaces.Api/Listings/SweepOrphanedEbayDraftsHandler.cs#L54-L116`). No `session.Delete()` call sites — cleaned orphans remain as audit history with `IsCleaned = true`.

**S1 reconciliation note.** S1 enumerated 2 document types (`Marketplace`, `CategoryMapping`); S2 direct enumeration of `opts.Schema.For<...>().Identity(...)` registrations yields 4 (`Marketplace`, `CategoryMapping`, `ProductSummaryView`, `OrphanedEbayDraft`). The two additions are not new since S1 — they were present but uncategorised: `ProductSummaryView` is the ACL collection (the S1 stub listed it under "Projections" rather than under "Aggregates"), and `OrphanedEbayDraft` ships with the M38.1 follow-up to ADR 0055 and is the orphan-tracking sibling of the in-flight `CheckWalmartFeedStatus` poll path. Both are first-class Marten documents under the same `Identity()` registration shape as the configuration documents, so they are surfaced here.

## Domain events

Not applicable — this BC uses Marten document store rather than event sourcing. Lifecycle is expressed through document state transitions written via `session.Store()` (and never `session.Delete()`) rather than appended events. Mutations to the `ProductSummaryView` ACL collection and to the `OrphanedEbayDraft` collection are driven by inbound integration events (`ProductCatalog.*` and the locally-emitted `SweepOrphanedEbayDrafts` scheduled message) rather than by HTTP commands; details are covered in §Integration events below.

## Commands

Direct enumeration via `grep -rn "public sealed record" --include="*.cs" src/Marketplaces/` yields nine `public sealed record` declarations. Filtering for command-shape records (handler-targeted records that are not response DTOs and not interface payload types) gives **five** commands:

- `RegisterMarketplace(string ChannelCode, string DisplayName, string? ApiCredentialVaultPath = null)` — registers a new marketplace document. Idempotent on `ChannelCode`: returns 200 with the existing document instead of 409 when the channel code is already registered. Publishes `MarketplaceRegistered` only on the new-registration branch. Validator caps `ChannelCode` at 50 chars and `DisplayName` at 200 chars (`src/Marketplaces/Marketplaces.Api/Marketplaces/RegisterMarketplace.cs#L26-L34`). Handler: `src/Marketplaces/Marketplaces.Api/Marketplaces/RegisterMarketplace.cs#L41-L94`.
- `UpdateMarketplace(string ChannelCode, string DisplayName, string? ApiCredentialVaultPath = null)` — updates display name and vault path of an existing marketplace; returns 404 when missing. Validator at `src/Marketplaces/Marketplaces.Api/Marketplaces/UpdateMarketplace.cs#L19-L27`. Handler: `src/Marketplaces/Marketplaces.Api/Marketplaces/UpdateMarketplace.cs#L33-L54`.
- `SetCategoryMappingRequest(string ChannelCode, string InternalCategory, string MarketplaceCategoryId, string? MarketplaceCategoryPath = null)` — upserts a `CategoryMapping` keyed by composite `{ChannelCode}:{InternalCategory}`. Updates `LastVerifiedAt` on every call. Validator at `src/Marketplaces/Marketplaces.Api/CategoryMappings/SetCategoryMapping.cs#L19-L29`. Handler: `src/Marketplaces/Marketplaces.Api/CategoryMappings/SetCategoryMapping.cs#L35-L74`.
- `CheckWalmartFeedStatus(Guid ListingId, string Sku, string ChannelCode, string ExternalFeedId, int AttemptCount)` — internal Marketplaces-only scheduled message (file comment at `src/Marketplaces/Marketplaces.Api/Listings/CheckWalmartFeedStatus.cs#L4-L8` calls out that it is **not** added to `Messages.Contracts`). Drives the Walmart submission-status poll loop. Handler: `src/Marketplaces/Marketplaces.Api/Listings/CheckWalmartFeedStatusHandler.cs#L14-L87`. Self-reschedules with escalating delays (2 m, 5 m, 10 m, 20 m, 30 m+) until `MaxAttempts = 10` (`#L16, L79-L86`); terminates with `MarketplaceListingActivated` on `IsLive`, `MarketplaceSubmissionRejected` on `IsFailed` or attempt-cap exhaustion.
- `SweepOrphanedEbayDrafts(int BatchSize = 25)` — internal Marketplaces-only scheduled message (file comment at `src/Marketplaces/Marketplaces.Api/Listings/SweepOrphanedEbayDrafts.cs#L9` calls out that it is **not** added to `Messages.Contracts`). Drives the recurring orphan-cleanup sweep. Handler: `src/Marketplaces/Marketplaces.Api/Listings/SweepOrphanedEbayDraftsHandler.cs#L26-L126`. Self-reschedules every 24 hours (`SweepInterval`, `#L29`); first instance is scheduled on host startup by `OrphanedEbayDraftSweepStartupService.ExecuteAsync` (`src/Marketplaces/Marketplaces.Api/Listings/OrphanedEbayDraftSweepStartupService.cs#L31-L60`).

The remaining four `public sealed record` declarations are not commands: `RegisterMarketplaceResponse` is a response DTO; `ListingSubmission`, `SubmissionResult`, and `SubmissionStatus` are the data-carrier records exchanged across the `IMarketplaceAdapter` interface (`src/Marketplaces/Marketplaces/Adapters/IMarketplaceAdapter.cs#L8-L46`).

The five HTTP read endpoints (`GetMarketplace`, `ListMarketplaces`, `DeactivateMarketplace`, `GetCategoryMapping`, `ListCategoryMappings`) are bound directly to route parameters and have no command record — they are listed under §HTTP / API surface.

**Reconciliation note on the S1 stub command count.** S1 deferred enumeration to S3. Direct enumeration in S2 yields five command-shape records. No prior count exists to deviate from.

## Projections

Marketplaces registers no Marten projections — no calls to `opts.Projections.Snapshot<...>()`, `opts.Projections.Add<...>()`, or `MultiStreamProjection<,>` exist anywhere in `src/Marketplaces/Marketplaces.Api/Program.cs` or under `src/Marketplaces/`. The BC's read surface is the four document collections themselves, served via `session.LoadAsync<T>()` and `session.Query<T>()`.

The `ProductSummaryView` ACL collection occupies the same role as a projection — translating Product Catalog integration events into a Marketplaces-local read model — but is implemented as a Marten document mutated imperatively by Wolverine integration-message handlers (`session.Store(view)` after `session.LoadAsync<ProductSummaryView>(message.Sku)`), not as a `MultiStreamProjection`. It is documented above under §Document types.

This Marketplaces ACL is **independent of** the Listings BC's `ProductSummaryView` ACL (per ADR 0050): the two BCs maintain their own copies of the ACL collection in their own database schemas (`marketplaces.product_summary_view` vs. `listings.product_summary_view`), with different field sets — Marketplaces' carries `ProductName` / `Category` / `BasePrice` / `Status` (`src/Marketplaces/Marketplaces/Products/ProductSummaryView.cs#L12-L41`), Listings' carries `Name` / `Description` / `Category` / `Status`. The two ACLs subscribe to overlapping but distinct subsets of the Product Catalog integration event surface (Marketplaces handles 4, Listings handles 9 — see §Integration events).

## External adapter

The external adapter surface is the substantive mechanism layer of this BC — Marketplaces is the system's only outbound external-service integration point.

### Interface and data carriers

`IMarketplaceAdapter` (`src/Marketplaces/Marketplaces/Adapters/IMarketplaceAdapter.cs#L58-L93`) defines four operations: `string ChannelCode { get; }`, `Task<SubmissionResult> SubmitListingAsync(ListingSubmission, CancellationToken)`, `Task<SubmissionStatus> CheckSubmissionStatusAsync(string externalSubmissionId, CancellationToken)`, `Task<bool> DeactivateListingAsync(string externalListingId, CancellationToken)`, and `Task<bool> DeleteOrphanedDraftAsync(string externalSubmissionId, CancellationToken)`. The `ListingSubmission` record carries `ListingId`, `Sku`, `ChannelCode`, `ProductName`, `Description?`, `Category?`, `Price`, and an optional `ChannelExtensions` dictionary for channel-specific attributes (`#L8-L16`). `SubmissionResult` carries `IsSuccess`, `ExternalSubmissionId?`, `ErrorMessage?`, and `OrphanedExternalSubmissionId?` — the last is populated only when a multi-step adapter flow partially succeeded and left a draft on the platform (`#L31-L35`). `SubmissionStatus` carries `IsLive`, `IsFailed`, optional `FailureReason` (`#L42-L46`).

### Implementations

Six implementations exist — three production adapters + three test stubs — registered in DI based on the `Marketplaces:UseRealAdapters` configuration flag at `src/Marketplaces/Marketplaces.Api/Program.cs#L97-L155`:

- **`AmazonMarketplaceAdapter`** (`src/Marketplaces/Marketplaces/Adapters/AmazonMarketplaceAdapter.cs`) — `ChannelCode = "AMAZON_US"`. Authenticates via Login with Amazon (LWA) OAuth 2.0 client-credentials + refresh-token flow against `https://api.amazon.com/auth/o2/token`, then calls SP-API `PUT /listings/2021-08-01/items/{sellerId}/{sku}` against `https://sellingpartnerapi-na.amazon.com` (`#L37-L38, L60-L80`). Token caching in-memory under a `SemaphoreSlim`-guarded refresh path. Per ADR 0052.
- **`WalmartMarketplaceAdapter`** (`src/Marketplaces/Marketplaces/Adapters/WalmartMarketplaceAdapter.cs`) — `ChannelCode = "WALMART_US"`. Authenticates via OAuth 2.0 client-credentials grant (HTTP Basic to `https://marketplace.walmartapis.com/v3/token`), then submits an `MP_ITEM` feed to `https://marketplace.walmartapis.com/v3/feeds?feedType=MP_ITEM` with `WM_SEC.ACCESS_TOKEN`, `WM_CONSUMER.ID`, `WM_SVC.NAME`, and a per-request `WM_QOS.CORRELATION_ID` (`#L37-L38, L65-L76`). Per ADR 0053.
- **`EbayMarketplaceAdapter`** (`src/Marketplaces/Marketplaces/Adapters/EbayMarketplaceAdapter.cs`) — `ChannelCode = "EBAY_US"`. Authenticates via OAuth 2.0 refresh-token grant against `https://api.ebay.com/identity/v1/oauth2/token` with the `sell.inventory` scope; submission is two HTTP calls — `POST /sell/inventory/v1/offer` (create offer) followed by `POST /sell/inventory/v1/offer/{offerId}/publish` (publish offer) (`#L43-L46, L60-L80`). The two-step shape is the source of the `OrphanedEbayDraft` lifecycle: when create succeeds and publish fails, the adapter returns `SubmissionResult` with `OrphanedExternalSubmissionId` set so `ListingApprovedHandler` can persist the orphan for later sweep cleanup. Per ADR 0054.
- **`StubAmazonAdapter`**, **`StubWalmartAdapter`**, **`StubEbayAdapter`** (`src/Marketplaces/Marketplaces/Adapters/Stub{Amazon,Walmart,Ebay}Adapter.cs`) — return immediate success with synthetic correlation IDs (`amzn-{guid}`, similar shape per channel) after a 100 ms `Task.Delay`. `DeleteOrphanedDraftAsync` is a no-op returning `true` per the interface contract (`src/Marketplaces/Marketplaces/Adapters/IMarketplaceAdapter.cs#L79-L83`). Used as the default adapter set in Development and CI.

### DI registration and channel-code dispatch

The adapter set is selected in `Program.cs` by reading `Marketplaces:UseRealAdapters` (`src/Marketplaces/Marketplaces.Api/Program.cs#L97`). When `true`, the three production adapters are registered as singletons against three named `HttpClient` pipelines (`AmazonSpApi`, `WalmartApi`, `EbayApi`); when `false` (the default for Development and CI), the three stubs are registered. After registration, an `IReadOnlyDictionary<string, IMarketplaceAdapter>` keyed case-insensitively by `ChannelCode` is composed from the registered services (`#L157-L159`) and consumed by `ListingApprovedHandler`, `CheckWalmartFeedStatusHandler`, and `SweepOrphanedEbayDraftsHandler` to dispatch by channel.

### Resilience policies

Per ADR 0056, each production-adapter `HttpClient` is wrapped in a Polly `Microsoft.Extensions.Http.Resilience` pipeline composed of a retry strategy + a circuit breaker (`src/Marketplaces/Marketplaces.Api/Program.cs#L105-L148`):

- **Retry:** `MaxRetryAttempts = 3`, `BackoffType = Exponential`, `Delay = 1 s` (`#L105-L110`). `Microsoft.Extensions.Http.Resilience` defaults `ShouldHandle` to HTTP 408 / 429 / 5xx and `HttpRequestException`. The 401 status is intentionally excluded — auth failures are routed through each adapter's token-refresh path (file comment at `#L103-L104`).
- **Circuit breaker:** `FailureRatio = 0.5`, `MinimumThroughput = 5`, `SamplingDuration = 30 s`, `BreakDuration = 30 s` (`#L115-L121`). One breaker per named `HttpClient` (Amazon / Walmart / eBay are isolated from each other).
- **HTTP client timeout:** 30 s on every named client (`#L124, L133, L142`).

The retry + breaker pipeline is added via `.AddResilienceHandler(name, pipeline => { pipeline.AddRetry(...); pipeline.AddCircuitBreaker(...); })` for each of the three production adapters at `#L125-L147`.

### Vault integration

Adapter credentials are fetched at request time from `IVaultClient` (`src/Marketplaces/Marketplaces/Credentials/IVaultClient.cs#L9-L11`). Two implementations:

- `DevVaultClient` (`src/Marketplaces/Marketplaces/Credentials/DevVaultClient.cs`) — reads from `IConfiguration` under the `Vault:` prefix; registered when `app.Environment.IsDevelopment()` is `true` (`src/Marketplaces/Marketplaces.Api/Program.cs#L90-L91`). A startup guard at `#L221-L224` throws `InvalidOperationException` if `DevVaultClient` is registered while the environment is not Development.
- `EnvironmentVaultClient` (`src/Marketplaces/Marketplaces/Credentials/EnvironmentVaultClient.cs`) — reads from environment variables using the convention `path/with-dashes` → `VAULT__PATH__WITH_DASHES` (`#L34`); registered in non-Development environments (`src/Marketplaces/Marketplaces.Api/Program.cs#L92-L93`). Per ADR 0051.

Production adapters call `IVaultClient.GetSecretAsync(path, ct)` with channel-prefixed paths — `amazon/client-id`, `amazon/seller-id`, `amazon/marketplace-id`, `walmart/client-id`, `walmart/seller-id`, `ebay/client-id`, `ebay/marketplace-id`, etc. (citation: `src/Marketplaces/Marketplaces/Adapters/AmazonMarketplaceAdapter.cs#L58-L60`, `src/Marketplaces/Marketplaces/Adapters/WalmartMarketplaceAdapter.cs#L58`, `src/Marketplaces/Marketplaces/Adapters/EbayMarketplaceAdapter.cs#L65`). Each adapter caches the access token under a `SemaphoreSlim`-guarded refresh path (`AmazonMarketplaceAdapter#L31-L34`).

### Outbox and sequence numbering

There is no per-adapter outbox or sequence-numbering layer in this BC. Outbound integration messages emitted from the same Wolverine handler as the adapter call (e.g. `MarketplaceListingActivated` / `MarketplaceSubmissionRejected`) ride the Wolverine Marten outbox configured at `src/Marketplaces/Marketplaces.Api/Program.cs#L174-L176` (`AutoApplyTransactions`, `UseDurableLocalQueues`, `UseDurableOutboxOnAllSendingEndpoints`). The Walmart status-poll loop is the sequencing mechanism for the asynchronous Walmart feed — `CheckWalmartFeedStatusHandler` reschedules itself with escalating delays (`src/Marketplaces/Marketplaces.Api/Listings/CheckWalmartFeedStatusHandler.cs#L72-L86`) until the feed is marked PROCESSED, ERROR, or the 10-attempt cap is exhausted. The eBay orphan-tracking loop is the equivalent for eBay — `SweepOrphanedEbayDraftsHandler` reschedules itself every 24 h (`src/Marketplaces/Marketplaces.Api/Listings/SweepOrphanedEbayDraftsHandler.cs#L29, L124`) and processes orphans oldest-first (`#L54-L58`). Walmart's outbound `externalListingId` is encoded as `wmrt-{sku}` (not the feed id) per ADR 0057 (`CheckWalmartFeedStatusHandler#L46-L51`).

## Integration events

Outbound publishes are wired explicitly in `src/Marketplaces/Marketplaces.Api/Program.cs#L197-L204`; inbound queues are listened to at `#L191-L194`. The four contract records live in `src/Shared/Messages.Contracts/Marketplaces/MarketplaceIntegrationMessages.cs`.

### Outbound (published)

- `MarketplaceRegistered(ChannelCode, DisplayName, OccurredAt)` — published from `RegisterMarketplaceEndpoint.Handle` on the new-registration branch only (`src/Marketplaces/Marketplaces.Api/Marketplaces/RegisterMarketplace.cs#L82-L85`). Routed to RabbitMQ exchange `marketplaces-registered` (`src/Marketplaces/Marketplaces.Api/Program.cs#L201-L202`).
- `MarketplaceDeactivated(ChannelCode, OccurredAt)` — published from `DeactivateMarketplaceEndpoint.Handle` only on the active → inactive transition (`src/Marketplaces/Marketplaces.Api/Marketplaces/DeactivateMarketplace.cs#L43-L48`). Routed to exchange `marketplaces-deactivated` (`src/Marketplaces/Marketplaces.Api/Program.cs#L203-L204`).
- `MarketplaceListingActivated(ListingId, Sku, ChannelCode, ExternalListingId, OccurredAt)` — published by `ListingApprovedHandler` on synchronous success for Amazon and eBay (`src/Marketplaces/Marketplaces.Api/Listings/ListingApprovedHandler.cs#L151-L156`), and by `CheckWalmartFeedStatusHandler` on PROCESSED for Walmart (`src/Marketplaces/Marketplaces.Api/Listings/CheckWalmartFeedStatusHandler.cs#L46-L51`). Routed to exchange `marketplaces-listing-activated` (`src/Marketplaces/Marketplaces.Api/Program.cs#L197-L198`). Subscriber: Listings BC (drives `Submitted → Live` transition).
- `MarketplaceSubmissionRejected(ListingId, Sku, ChannelCode, Reason, OccurredAt)` — published by `ListingApprovedHandler` from each of the five guard branches (missing `ProductSummaryView`, missing category, missing category mapping, inactive marketplace, no adapter registered) and from the adapter-failure branch (`src/Marketplaces/Marketplaces.Api/Listings/ListingApprovedHandler.cs#L48-L106, L160-L165`); also published by `CheckWalmartFeedStatusHandler` on ERROR or attempt-cap exhaustion (`src/Marketplaces/Marketplaces.Api/Listings/CheckWalmartFeedStatusHandler.cs#L26-L34, L55-L67`). Routed to exchange `marketplaces-submission-rejected` (`src/Marketplaces/Marketplaces.Api/Program.cs#L199-L200`). Subscriber: Listings BC (drives `Submitted → Ended` with cause `SubmissionRejected`).

### Inbound (subscribed)

Two RabbitMQ queues are listened to (`src/Marketplaces/Marketplaces.Api/Program.cs#L191-L194`):

1. **`marketplaces-listings-events`** — carries `Listings.ListingApproved`, handled by `ListingApprovedHandler` (`src/Marketplaces/Marketplaces.Api/Listings/ListingApprovedHandler.cs#L29-L170`). Skips channel `OWN_WEBSITE` (`#L41-L42`); otherwise loads the local `ProductSummaryView` and `CategoryMapping` for `{ChannelCode, productSummary.Category}`, verifies the marketplace is active, resolves the adapter from the channel-code dictionary, builds a `ListingSubmission` from the local ACL data (no fields read from the inbound message payload beyond `ListingId`, `Sku`, `ChannelCode`), invokes the adapter, persists any returned orphan, and emits the appropriate outbound integration event.
2. **`marketplaces-product-catalog-events`** — carries 4 `ProductCatalog.*` integration events that drive the local ACL. Each has a dedicated handler in `src/Marketplaces/Marketplaces/Products/ProductSummaryHandlers.cs`:
   - `ProductCatalog.ProductAdded` → `ProductAddedHandler` (`#L13-L37`) — creates the ACL document.
   - `ProductCatalog.ProductContentUpdated` → `ProductContentUpdatedHandler` (`#L44-L54`) — updates `ProductName` only.
   - `ProductCatalog.ProductCategoryChanged` → `ProductCategoryChangedHandler` (`#L60-L70`) — updates `Category`.
   - `ProductCatalog.ProductStatusChanged` → `ProductStatusChangedHandler` (`#L77-L93`) — maps `NewStatus` to local `ProductSummaryStatus` enum.

Total inbound: **5 distinct integration message types** across two queues. Total outbound: **4 distinct integration message types**.

### Routes-without-instantiator

None found. Every inbound queue declared in `src/Marketplaces/Marketplaces.Api/Program.cs#L191-L194` resolves to at least one Wolverine handler in `src/Marketplaces/`. Every outbound `PublishMessage<T>().ToRabbitExchange(...)` route at `#L197-L204` has at least one emitting site — the four outbound contracts each appear in at least one `outgoing.Add(new <Contract>(...))` site (citations under §Outbound above).

The Marketplaces ACL handles 4 of Product Catalog's 9 outbound events (`ProductAdded`, `ProductContentUpdated`, `ProductCategoryChanged`, `ProductStatusChanged`); the other 5 (`ProductImagesUpdated`, `ProductDimensionsChanged`, `ProductDeleted`, `ProductRestored`, `ProductDiscontinued`) are not currently consumed by Marketplaces — they reach the queue under the auto-provisioned exchange-binding convention but no handler is registered for those types in this BC. This is a one-direction subscription gap (the contracts are owned by Product Catalog and consumed by Listings; Marketplaces simply does not subscribe to those types) rather than a route-without-handler asymmetry on a Marketplaces-declared route.

### CONTEXTS.md drift

The `CONTEXTS.md` Marketplaces section (`CONTEXTS.md#L178-L189`) describes the `Marketplace` and `CategoryMapping` document collections, the `IMarketplaceAdapter` interface and its three production implementations, the `ProductSummaryView` ACL, the `IVaultClient` abstraction, the Polly resilience pipeline, the Walmart-only status-poll path, and the Walmart deactivation identifier design. Two divergences against the code (code authoritative — forwarded to S5):

1. **`OrphanedEbayDraft` document collection unmentioned.** `CONTEXTS.md` does not enumerate the `OrphanedEbayDraft` Marten document or the `SweepOrphanedEbayDraftsHandler` background sweep. The collection ships with the M38.1 follow-up to ADR 0055 and is one of the four registered document types (`src/Marketplaces/Marketplaces.Api/Program.cs#L59`).
2. **Communicates-with table omits Listings outbound direction.** The `CONTEXTS.md` Marketplaces communicates-with table (`CONTEXTS.md#L184-L187`) shows two arrows — Listings ← receives and Product Catalog ← receives. The code emits `MarketplaceListingActivated` and `MarketplaceSubmissionRejected` outbound to Listings (subscriber confirmed in `docs/extraction/bcs/listings.md` §Inbound `listings-marketplace-outcome-events` queue). The Listings row in CONTEXTS.md describes the relationship as "← receives" with the inline note covering both directions, so this is a presentation drift rather than a hidden integration.

## Sagas / orchestration

Not applicable — this BC is not a saga orchestrator. The Listings ↔ Marketplaces integration is choreographed: Listings publishes `ListingApproved`, Marketplaces submits to the adapter and replies with either `MarketplaceListingActivated` (drives Listings' `Submitted → Live`) or `MarketplaceSubmissionRejected` (drives Listings' `Submitted → Ended` with cause `SubmissionRejected`). The Walmart status-poll loop and the eBay orphan-cleanup sweep are local workflows — driven by Marketplaces-internal scheduled messages (`CheckWalmartFeedStatus`, `SweepOrphanedEbayDrafts`) that are explicitly excluded from `Messages.Contracts` per the source comments at `src/Marketplaces/Marketplaces.Api/Listings/CheckWalmartFeedStatus.cs#L4-L8` and `src/Marketplaces/Marketplaces.Api/Listings/SweepOrphanedEbayDrafts.cs#L9`.

## HTTP / API surface

All endpoints are defined in `src/Marketplaces/Marketplaces.Api/Marketplaces/`, `src/Marketplaces/Marketplaces.Api/CategoryMappings/` and registered via Wolverine HTTP attribute routing (`MapWolverineEndpoints` at `src/Marketplaces/Marketplaces.Api/Program.cs#L256-L259`). Every domain endpoint is `[Authorize]` (JWT Bearer; see §Identity / auth posture). FluentValidation runs on every command via `UseFluentValidationProblemDetailMiddleware()` on the HTTP middleware chain (`#L258`).

### Marketplace management

- `POST /api/marketplaces` — register a marketplace. Body: `RegisterMarketplace(ChannelCode, DisplayName, ApiCredentialVaultPath?)`. Idempotent on `ChannelCode` (200 + existing on duplicate, 201 + `Location` on new). Returns `RegisterMarketplaceResponse(ChannelCode, DisplayName, IsActive, CreatedAt)`. Handler: `src/Marketplaces/Marketplaces.Api/Marketplaces/RegisterMarketplace.cs#L48-L94`.
- `PUT /api/marketplaces/{channelCode}` — update display name and vault path. Body: `UpdateMarketplace(ChannelCode, DisplayName, ApiCredentialVaultPath?)`. Returns the mutated `Marketplace` document or 404. Handler: `src/Marketplaces/Marketplaces.Api/Marketplaces/UpdateMarketplace.cs#L33-L54`.
- `POST /api/marketplaces/{channelCode}/deactivate` — flip `IsActive` to `false`. Idempotent (200 + current state when already inactive). Publishes `MarketplaceDeactivated` only on the active → inactive transition. Handler: `src/Marketplaces/Marketplaces.Api/Marketplaces/DeactivateMarketplace.cs#L15-L52`.
- `GET /api/marketplaces/{channelCode}` — fetch one. Handler: `src/Marketplaces/Marketplaces.Api/Marketplaces/GetMarketplace.cs#L13-L28`.
- `GET /api/marketplaces` — list all, ordered by `Id`. Handler: `src/Marketplaces/Marketplaces.Api/Marketplaces/ListMarketplaces.cs#L12-L26`.

### Category mapping management

- `POST /api/category-mappings` — upsert a `CategoryMapping`. Body: `SetCategoryMappingRequest(ChannelCode, InternalCategory, MarketplaceCategoryId, MarketplaceCategoryPath?)`. 200 on update, 201 on create. Handler: `src/Marketplaces/Marketplaces.Api/CategoryMappings/SetCategoryMapping.cs#L35-L74`.
- `GET /api/category-mappings/{channelCode}/{internalCategory}` — fetch one (404 on miss). Handler: `src/Marketplaces/Marketplaces.Api/CategoryMappings/GetCategoryMapping.cs#L13-L38`.
- `GET /api/category-mappings?channelCode={code}` — list with optional channel filter, ordered by `InternalCategory` (filtered) or `Id` (unfiltered). Handler: `src/Marketplaces/Marketplaces.Api/CategoryMappings/ListCategoryMappings.cs#L13-L34`.

### Health

- `GET /health` — liveness check returning `{ Status = "Healthy", Service = "Marketplaces.Api" }`. `AllowAnonymous` (`src/Marketplaces/Marketplaces.Api/Program.cs#L262-L263`). Aspire `MapDefaultEndpoints()` registers the `/alive` and `/health` probes (`#L253`).

CORS allows the Backoffice WASM origin only (`Cors:BackofficeOrigin`, default `http://localhost:5244`) (`src/Marketplaces/Marketplaces.Api/Program.cs#L26-L36, L247`).

## Frontend surface

Not applicable — no frontend in this BC. The Backoffice BC owns the marketplaces admin UI; see `tests/Backoffice/Backoffice.E2ETests/Pages/MarketplacesListPage.cs` and the `MarketplacesAdmin.feature` Reqnroll feature in the Backoffice E2E suite.

## Identity / auth posture

- **Scheme:** JWT Bearer (single default scheme registered via `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)`) (`src/Marketplaces/Marketplaces.Api/Program.cs#L70-L85`).
- **Issuer / audience:** `Jwt:Issuer` defaults to `backoffice-identity`; `Jwt:Audience` defaults to `marketplaces-api`. Signing key from `Jwt:SigningKey` (HMAC-SHA256 via `SymmetricSecurityKey`). `ValidateIssuer = true`, `ValidateAudience = true`, `ValidateIssuerSigningKey = true`, `ValidateLifetime = true`, `ClockSkew = 30 s`, `RoleClaimType = "role"`.
- **Policies:** None registered. `builder.Services.AddAuthorization()` is called with no named policies (`#L87`); every domain endpoint uses bare `[Authorize]`.
- **Anonymous endpoints:** `GET /health` (`#L262-L263`) and the Aspire `MapDefaultEndpoints()` health/alive probes (`#L253`).
- **Source:** `src/Marketplaces/Marketplaces.Api/Program.cs`.

## Tests as behavioral evidence

No Gherkin features under `docs/features/marketplaces/`; the Backoffice-level Gherkin (`tests/Backoffice/Backoffice.E2ETests/Features/MarketplacesAdmin.feature` + `MarketplacesAdminSteps.cs`) covers the admin UI workflows that depend on this BC and is owned by the Backoffice dossier.

Integration tests under `tests/Marketplaces/Marketplaces.Api.IntegrationTests/` (xUnit, Alba + Testcontainers via the shared `IntegrationTestCollection`):

- `MarketplaceCrudTests` (9 tests) — register / update / deactivate / get / list endpoints end-to-end.
- `CategoryMappingTests` (6 tests) — set / get / list endpoints end-to-end including the composite-key derivation.
- `ListingSubmissionFlowTests` (6 tests) — the `ListingApprovedHandler` flow end-to-end across the five guard branches and the success branches.
- `MarketplaceMessagePublishingTests` (4 tests) — outbound publish coverage of `MarketplaceRegistered` / `MarketplaceDeactivated`.
- `ProductSummaryViewTests` (5 tests) — the four ACL handlers in `ProductSummaryHandlers.cs`.
- `AmazonMarketplaceAdapterTests` (12 tests) — production Amazon adapter against a wired-up `HttpMessageHandler` test double.
- `WalmartMarketplaceAdapterTests` (14 tests) — production Walmart adapter coverage.
- `EbayMarketplaceAdapterTests` (18 tests) — production eBay adapter, including the create-offer / publish-offer two-step path and orphan-id surfacing.
- `WalmartPollingHandlerTests` (5 tests) — `CheckWalmartFeedStatusHandler` reschedule and termination behaviour.
- `SweepOrphanedEbayDraftsHandlerTests` (6 tests) — `SweepOrphanedEbayDraftsHandler` batch processing, retry, and reschedule.
- `AdapterResilienceTests` (9 tests) — Polly retry + circuit-breaker behaviour against the named `HttpClient` pipelines.
- `EnvironmentVaultClientTests` (6 tests) — `EnvironmentVaultClient` path-to-environment-variable convention from ADR 0051.
- `SeedDataTests` (2 tests) — `MarketplacesSeedData.SeedAsync` produces the three canonical marketplaces and the 18 (6 × 3) seed `CategoryMapping` documents.
- `HealthCheckTests` (1 test) — `GET /health` returns `200 Healthy`.
- Helper: `Helpers/MarketplaceAdapterTestHelpers.cs` (no `[Fact]`/`[Theory]`).
- Suite fixture: `TestFixture.cs` (Postgres + RabbitMQ Testcontainers), `IntegrationTestCollection.cs` (xUnit collection fixture), `Usings.cs`.

Total: 103 integration tests across 14 test classes.

No `@pending` or `@wip` scenarios are referenced from the test sources reviewed.

## ADRs

- **ADR 0048 — Marketplace Document Entity Design.** Establishes `Marketplace` as a Marten document (not event-sourced), with `ChannelCode` as the natural key. Materially: shapes the §Document types section above and the entire BC's persistence model. File: `docs/decisions/0048-marketplace-document-entity-design.md`.
- **ADR 0049 — Category Mapping Ownership.** Places the channel-to-category translation table inside Marketplaces BC (not Listings or Product Catalog). Materially: shapes the `CategoryMapping` composite-key design and the `ListingApprovedHandler` lookup at `src/Marketplaces/Marketplaces.Api/Listings/ListingApprovedHandler.cs#L70-L81`. File: `docs/decisions/0049-category-mapping-ownership.md`.
- **ADR 0050 — Marketplaces ProductSummaryView Anti-Corruption Layer.** Establishes the Marketplaces-local `ProductSummaryView` ACL as independent from Listings'. Materially: shapes §Document types `ProductSummaryView` and §Projections (the two ACLs are not shared). File: `docs/decisions/0050-marketplaces-product-summary-acl.md`.
- **ADR 0051 — Vault Implementation Strategy.** Establishes `IVaultClient` with `DevVaultClient` (configuration-backed) for Development and `EnvironmentVaultClient` (env-var-backed) for production, plus the path-to-env-var convention at `src/Marketplaces/Marketplaces/Credentials/EnvironmentVaultClient.cs#L34`. Enforced by the `Program.cs` startup guard at `#L221-L224`. File: `docs/decisions/0051-vault-implementation-strategy.md`.
- **ADR 0052 — Amazon SP-API Authentication.** Login with Amazon (LWA) OAuth 2.0 client-credentials + refresh-token flow consumed by `AmazonMarketplaceAdapter` (`src/Marketplaces/Marketplaces/Adapters/AmazonMarketplaceAdapter.cs#L13-L17`). File: `docs/decisions/0052-amazon-spapi-authentication.md`.
- **ADR 0053 — Walmart Marketplace API Authentication.** OAuth 2.0 client-credentials grant (HTTP Basic) consumed by `WalmartMarketplaceAdapter` (`src/Marketplaces/Marketplaces/Adapters/WalmartMarketplaceAdapter.cs#L13-L17`). File: `docs/decisions/0053-walmart-marketplace-api-authentication.md`.
- **ADR 0054 — eBay Sell API Authentication.** OAuth 2.0 refresh-token grant consumed by `EbayMarketplaceAdapter`, plus the two-step offer create / publish submission shape (`src/Marketplaces/Marketplaces/Adapters/EbayMarketplaceAdapter.cs#L13-L29`). File: `docs/decisions/0054-ebay-sell-api-authentication.md`.
- **ADR 0055 — Submission Status Polling Architecture.** Establishes the per-submission scheduled-message poll loop for Walmart only (Amazon and eBay activate synchronously); fixes the escalating delay schedule and `MaxAttempts = 10` cap (`src/Marketplaces/Marketplaces.Api/Listings/CheckWalmartFeedStatusHandler.cs#L16, L79-L86`). The M38.1 follow-up adds the eBay orphaned-draft surface (`OrphanedEbayDraft` document + `SweepOrphanedEbayDraftsHandler`). File: `docs/decisions/0055-submission-status-polling-architecture.md`.
- **ADR 0056 — Marketplace Adapter Resilience Patterns.** Establishes the per-adapter Polly retry + circuit-breaker pipeline applied via `Microsoft.Extensions.Http.Resilience` (`src/Marketplaces/Marketplaces.Api/Program.cs#L105-L148`); the deliberate exclusion of HTTP 401 from retry (handled by each adapter's token-refresh path). File: `docs/decisions/0056-marketplace-adapter-resilience-patterns.md`.
- **ADR 0057 — Walmart Deactivation Identifier Design.** Establishes that `MarketplaceListingActivated.ExternalListingId` for Walmart encodes `wmrt-{sku}` (not the transient feed id), so that downstream `DeactivateListingAsync` can submit a `RETIRE_ITEM` feed (`src/Marketplaces/Marketplaces.Api/Listings/CheckWalmartFeedStatusHandler.cs#L42-L51`). File: `docs/decisions/0057-walmart-deactivation-identifier-design.md`.

## Prior event modeling

- `docs/planning/catalog-listings-marketplaces-discovery.md` — joint Product Owner + UX Engineer discovery session that surfaced the channel-listing lifecycle and the cross-BC ownership of category mapping (decision D5 placing category mapping inside Marketplaces).
- `docs/planning/catalog-listings-marketplaces-evolution-plan.md` — Principal Architect's resolution of D1–D10, including decision D3 (Marketplaces BC owns marketplace API call mechanism), D4 (Marketplace document identity), D5 (Category mapping ownership).
- `docs/planning/catalog-listings-marketplaces-cycle-plan.md` — cross-functional cycle-by-cycle execution plan that scheduled the Marketplaces BC build-out (Cycles 32–33 foundation; Phase 2 work culminating in M40.0 real adapters).
- `docs/planning/catalog-listings-marketplaces-glossary.md` — team-standard glossary anchoring the ubiquitous language used in this dossier (`ChannelCode`, `Marketplace`, `CategoryMapping`, `MarketplaceCategoryId`, `Vault`, `ProductSummaryView`).

## Source citations (S2 full)

- `src/Marketplaces/` (folder root)
- `src/Marketplaces/Marketplaces/Marketplaces/Marketplace.cs`
- `src/Marketplaces/Marketplaces/CategoryMappings/CategoryMapping.cs`
- `src/Marketplaces/Marketplaces/Products/ProductSummaryView.cs`, `ProductSummaryHandlers.cs`
- `src/Marketplaces/Marketplaces/Adapters/IMarketplaceAdapter.cs`
- `src/Marketplaces/Marketplaces/Adapters/AmazonMarketplaceAdapter.cs`, `WalmartMarketplaceAdapter.cs`, `EbayMarketplaceAdapter.cs`
- `src/Marketplaces/Marketplaces/Adapters/StubAmazonAdapter.cs`, `StubWalmartAdapter.cs`, `StubEbayAdapter.cs`
- `src/Marketplaces/Marketplaces/Credentials/IVaultClient.cs`, `DevVaultClient.cs`, `EnvironmentVaultClient.cs`
- `src/Marketplaces/Marketplaces.Api/Program.cs`
- `src/Marketplaces/Marketplaces.Api/MarketplacesSeedData.cs`
- `src/Marketplaces/Marketplaces.Api/Marketplaces/RegisterMarketplace.cs`, `UpdateMarketplace.cs`, `DeactivateMarketplace.cs`, `GetMarketplace.cs`, `ListMarketplaces.cs`
- `src/Marketplaces/Marketplaces.Api/CategoryMappings/SetCategoryMapping.cs`, `GetCategoryMapping.cs`, `ListCategoryMappings.cs`
- `src/Marketplaces/Marketplaces.Api/Listings/ListingApprovedHandler.cs`
- `src/Marketplaces/Marketplaces.Api/Listings/CheckWalmartFeedStatus.cs`, `CheckWalmartFeedStatusHandler.cs`
- `src/Marketplaces/Marketplaces.Api/Listings/OrphanedEbayDraft.cs`, `SweepOrphanedEbayDrafts.cs`, `SweepOrphanedEbayDraftsHandler.cs`, `OrphanedEbayDraftSweepStartupService.cs`
- `src/Shared/Messages.Contracts/Marketplaces/MarketplaceIntegrationMessages.cs`
- `src/Shared/Messages.Contracts/Listings/ListingIntegrationMessages.cs` (inbound `ListingApproved` contract)
- `src/Shared/Messages.Contracts/ProductCatalog/` (inbound 4 ACL contracts)
- `CONTEXTS.md` (section: `Marketplaces`, lines 178–189)
- `docs/decisions/0048-marketplace-document-entity-design.md`
- `docs/decisions/0049-category-mapping-ownership.md`
- `docs/decisions/0050-marketplaces-product-summary-acl.md`
- `docs/decisions/0051-vault-implementation-strategy.md`
- `docs/decisions/0052-amazon-spapi-authentication.md`
- `docs/decisions/0053-walmart-marketplace-api-authentication.md`
- `docs/decisions/0054-ebay-sell-api-authentication.md`
- `docs/decisions/0055-submission-status-polling-architecture.md`
- `docs/decisions/0056-marketplace-adapter-resilience-patterns.md`
- `docs/decisions/0057-walmart-deactivation-identifier-design.md`
- `docs/planning/catalog-listings-marketplaces-discovery.md`
- `docs/planning/catalog-listings-marketplaces-evolution-plan.md`
- `docs/planning/catalog-listings-marketplaces-cycle-plan.md`
- `docs/planning/catalog-listings-marketplaces-glossary.md`
- `tests/Marketplaces/Marketplaces.Api.IntegrationTests/MarketplaceCrudTests.cs`, `CategoryMappingTests.cs`, `ListingSubmissionFlowTests.cs`, `MarketplaceMessagePublishingTests.cs`, `ProductSummaryViewTests.cs`, `AmazonMarketplaceAdapterTests.cs`, `WalmartMarketplaceAdapterTests.cs`, `EbayMarketplaceAdapterTests.cs`, `WalmartPollingHandlerTests.cs`, `SweepOrphanedEbayDraftsHandlerTests.cs`, `AdapterResilienceTests.cs`, `EnvironmentVaultClientTests.cs`, `SeedDataTests.cs`, `HealthCheckTests.cs`, `Helpers/MarketplaceAdapterTestHelpers.cs`, `TestFixture.cs`, `IntegrationTestCollection.cs`
