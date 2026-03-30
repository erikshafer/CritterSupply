# M36.1 Session 2 Retrospective

**Date:** 2026-03-29
**Scope:** Listing aggregate + ProductSummaryView ACL + ListingsActiveView + Lifecycle handlers + Recall cascade + Integration tests
**Build state:** 0 errors, 33 warnings (matches M36.0 baseline)

---

## Deliverables Completed

All 7 planned deliverables were completed:

### 1. ✅ Listing Aggregate

Event-sourced aggregate in `src/Listings/Listings/Listing/Listing.cs`.

**State machine as implemented:**

| From State | Event | To State | Notes |
|---|---|---|---|
| (new) | `ListingDraftCreated` | `Draft` | Initial state via `Create()` factory |
| `Draft` | `ListingSubmittedForReview` | `ReadyForReview` | Normal review flow |
| `Draft` | `ListingActivated` | `Live` | OWN_WEBSITE fast path only |
| `Draft` | `ListingEnded` | `Ended` | Early termination |
| `ReadyForReview` | `ListingApproved` | `Submitted` | Review approved |
| `ReadyForReview` | `ListingEnded` | `Ended` | Rejected during review |
| `Submitted` | `ListingActivated` | `Live` | Marketplace confirmation |
| `Submitted` | `ListingEnded` | `Ended` | Marketplace rejection |
| `Live` | `ListingPaused` | `Paused` | Temporary suspension |
| `Live` | `ListingEnded` | `Ended` | Delisting |
| `Live` | `ListingForcedDown` | `Ended` | Recall cascade |
| `Paused` | `ListingResumed` | `Live` | Resume from pause |
| `Paused` | `ListingEnded` | `Ended` | End while paused |
| `Paused` | `ListingForcedDown` | `Ended` | Recall cascade |
| `Ended` | — | — | Terminal state |

**Deviations from execution plan:**
- `ForcedDown` is NOT a separate `ListingStatus` enum value — it transitions to `Ended` with `EndCause = ProductDiscontinued`. The glossary defines `ForcedDown` as a distinct state, but the implementation simplifies this: a forced-down listing is functionally `Ended`. The `ListingForcedDown` domain event preserves the recall provenance while the aggregate settles into the same terminal state. This avoids adding a 7th status value that would complicate every status-based query.
- `ListingContentUpdated` event is defined but not used by any handler in this session. It will be used when product content changes propagate to live listings (future session).

**Domain events defined (in `Listings/Listing/Events.cs`):**
- `ListingDraftCreated` — ListingId, Sku, ChannelCode, ProductName, InitialContent, OccurredAt
- `ListingSubmittedForReview` — ListingId, OccurredAt
- `ListingApproved` — ListingId, OccurredAt
- `ListingActivated` — ListingId, ChannelCode, OccurredAt
- `ListingPaused` — ListingId, Reason, OccurredAt
- `ListingResumed` — ListingId, OccurredAt
- `ListingEnded` — ListingId, Sku, ChannelCode, Cause (EndedCause enum), OccurredAt
- `ListingForcedDown` — ListingId, Sku, ChannelCode, RecallReason, OccurredAt
- `ListingContentUpdated` — ListingId, ProductName, Description, OccurredAt

**Stream IDs:** UUID v5 deterministic key from `listing:{sku}:{channelCode}` using RFC 4122 URL namespace. Implemented in `ListingStreamId.Compute()`.

### 2. ✅ ProductSummaryView

Marten document in `src/Listings/Listings/ProductSummary/ProductSummaryView.cs`.

**Key:** SKU (string Id)

**Fields:**
- `Id` (string) — SKU, serves as Marten document Id
- `Name` (string)
- `Description` (string?)
- `Category` (string?)
- `Status` (ProductSummaryStatus enum: Active, ComingSoon, Discontinued, Deleted)
- `Brand` (string?)
- `HasDimensions` (bool)
- `ImageUrls` (IReadOnlyList<string>)

**Handlers:** 9 individual handler classes in `ProductSummaryHandlers.cs`, one per Product Catalog integration event:
- `ProductAddedHandler` — creates new document
- `ProductContentUpdatedHandler` — updates Name, Description
- `ProductCategoryChangedHandler` — updates Category
- `ProductImagesUpdatedHandler` — updates ImageUrls
- `ProductDimensionsChangedHandler` — sets HasDimensions = true
- `ProductStatusChangedHandler` — maps and updates Status
- `ProductDeletedHandler` — sets Status = Deleted
- `ProductRestoredHandler` — sets Status = Active
- `ProductDiscontinuedSummaryHandler` — sets Status = Discontinued

**Naming decision:** Originally implemented as a single `ProductSummaryHandlers` (plural) class with multiple `Handle` methods. Wolverine's handler discovery convention requires class names ending in `Handler` (singular). Split into 9 individual `*Handler` classes to match convention.

### 3. ✅ ListingsActiveView

Inline multi-stream projection in `src/Listings/Listings/Projections/ListingsActiveViewProjection.cs`.

**Key:** SKU (string Id)

**Maintains:** `List<Guid> ActiveListingStreamIds` — stream IDs of all non-ended listings for the SKU.

**Event mappings:**
- `ListingDraftCreated` → add stream ID to list (via Identity mapping by SKU)
- `ListingEnded` → remove stream ID from list
- `ListingForcedDown` → remove stream ID from list

**Registration:** Inline in `Program.cs` via `opts.Projections.Add<ListingsActiveViewProjection>(ProjectionLifecycle.Inline)` — ensures synchronous consistency with event appends.

### 4. ✅ Lifecycle Command Handlers

Each is a vertical slice in `src/Listings/Listings/Listing/`:

| File | Command | State Transition | Notes |
|------|---------|-----------------|-------|
| `CreateListing.cs` | `CreateListing` | → `Draft` | Validates SKU in ProductSummaryView, checks ListingsActiveView for duplicates |
| `ActivateListing.cs` | `ActivateListing` | `Submitted → Live` or `Draft → Live` (OWN_WEBSITE) | Channel-aware fast path |
| `PauseListing.cs` | `PauseListing` | `Live → Paused` | Requires reason |
| `ResumeListing.cs` | `ResumeListing` | `Paused → Live` | Clears pause reason |
| `EndListing.cs` | `EndListing` | `(non-terminal) → Ended` | ManualEnd cause |

### 5. ✅ Product Catalog Integration Event Handlers

See ProductSummaryView section above. All 9 handlers consume from the `listings-product-catalog-events` queue as configured in Session 1's `Program.cs`.

### 6. ✅ RecallCascadeHandler

In `src/Listings/Listings/Listing/RecallCascadeHandler.cs`.

**Flow:**
1. Receive `ProductDiscontinued` message
2. Guard: only process when `IsRecall == true`
3. Load `ListingsActiveView` for the SKU
4. For each active stream ID:
   a. Load current aggregate state (idempotency check)
   b. If listing is already terminal, skip
   c. Append `ListingForcedDown` event to the stream
   d. Add `ListingForcedDown` integration message to outgoing
5. Add `ListingsCascadeCompleted` integration message with affected count

**Idempotency:** Checks listing status before appending. If already `Ended`, no duplicate `ListingForcedDown` events are appended.

**Note:** Both `RecallCascadeHandler` and `ProductDiscontinuedSummaryHandler` handle `ProductDiscontinued` messages. Wolverine invokes both. `ProductDiscontinuedSummaryHandler` always updates the ProductSummaryView status. `RecallCascadeHandler` only acts when `IsRecall == true`. This is correct — the summary view update and the cascade are independent concerns.

### 7. ✅ Integration Tests

18/18 tests passing in `tests/Listings/Listings.Api.IntegrationTests/`:

**ListingLifecycleTests (9 tests):**
- `CreateListing_ForExistingProduct_CreatesDraftListing`
- `CreateListing_ForNonExistentProduct_ThrowsError`
- `CreateListing_ForDiscontinuedProduct_ThrowsError`
- `CreateListing_DuplicateSkuChannel_ThrowsError`
- `ActivateListing_FromDraft_OwnWebsite_TransitionsToLive`
- `PauseListing_FromLive_TransitionsToPaused`
- `ResumeListing_FromPaused_TransitionsToLive`
- `EndListing_Manual_TransitionsToEnded`
- `InvalidTransition_EndedToLive_ThrowsError`

**ProductSummaryViewTests (4 tests):**
- `ProductAdded_CreatesProductSummaryView`
- `ProductStatusChanged_ToDiscontinued_UpdatesSummaryView`
- `ProductDeleted_SetsStatusDeleted_InSummaryView`
- `ProductContentUpdated_UpdatesNameAndDescription`

**RecallCascadeTests (4 tests):**
- `RecallCascade_WithThreeListingsAcrossTwoChannels_ForcesDownAll`
- `RecallCascade_AlreadyEndedListing_IsSkipped`
- `RecallCascade_PublishesListingsCascadeCompleted_WithCorrectCount`
- `RecallCascade_NonRecallDiscontinuation_DoesNotForceDown`

**HealthCheckTests (1 test):**
- `Health_ReturnsOk`

---

## Integration Message Contracts

Defined in `src/Shared/Messages.Contracts/Listings/ListingIntegrationMessages.cs`:

| Contract | Fields |
|----------|--------|
| `ListingCreated` | ListingId, Sku, ChannelCode, OccurredAt |
| `ListingActivated` | ListingId, ChannelCode, OccurredAt |
| `ListingEnded` | ListingId, Sku, ChannelCode, Cause (string), OccurredAt |
| `ListingForcedDown` | ListingId, Sku, ChannelCode, RecallReason, OccurredAt |
| `ListingsCascadeCompleted` | Sku, AffectedCount, OccurredAt |

---

## Naming Decisions

| Decision | Rationale |
|----------|-----------|
| `ListingForcedDown` (domain event) vs `ForcedDown` (as ListingStatus) | Glossary defines ForcedDown as a separate listing state. Implementation uses `ListingForcedDown` as a distinct event but settles into `Ended` status with `EndCause = ProductDiscontinued`. This avoids a 7th status value that complicates queries. The event preserves recall provenance. |
| `EndedCause` enum | Matches glossary's `EndedReason` concept. Values: `ManualEnd`, `ProductDiscontinued`, `SubmissionRejected`, `ProductDeleted` — natural extensions of the glossary's `IntentionalEnd`, `ForcedDownByRecall`, etc. |
| `OWN_WEBSITE` channel code | Used as-is per glossary (uppercase snake_case). Fast-path activation from `Draft → Live` is channel-aware routing in the handler, not a separate aggregate branch. |
| `ProductDiscontinuedSummaryHandler` | Named with `Summary` suffix to distinguish from `RecallCascadeHandler`, which also handles `ProductDiscontinued`. Both are valid Wolverine handlers for the same message type. |
| `ProductSummaryStatus` vs `ProductStatus` | Anti-corruption layer uses its own enum to decouple from Product Catalog's internal status values. Maps string-based integration event statuses to local enum. |

---

## Build State at Session Close

- **Errors:** 0
- **Warnings:** 33 (matches M36.0 baseline — no new warnings)
- **Listings tests:** 18/18 ✅
- **Full solution build:** 0 errors, 33 warnings

---

## What Session 3 Should Pick Up

1. **HTTP endpoints** — REST API endpoints for listing lifecycle operations:
   - `POST /api/listings` (CreateListing)
   - `POST /api/listings/{id}/activate` (ActivateListing)
   - `POST /api/listings/{id}/pause` (PauseListing)
   - `POST /api/listings/{id}/resume` (ResumeListing)
   - `POST /api/listings/{id}/end` (EndListing)
   - `GET /api/listings/{id}` (GetListing)
   - `GET /api/listings?sku=...` (ListListings)
   - All endpoints require `[Authorize]`

2. **Admin UI pages** (Backoffice):
   - Listings admin page with status filtering
   - `ListingStatusBadge` component
   - Pre-flight modal for listing creation

3. **Additional tests:**
   - HTTP endpoint tests via Alba
   - Review/approval workflow tests (ReadyForReview → Submitted)
   - E2E tests for admin UI

4. **Missing lifecycle handlers** for Session 3:
   - `SubmitListingForReview` handler (Draft → ReadyForReview)
   - `ApproveListing` handler (ReadyForReview → Submitted)
   - Content propagation handler (ListingContentUpdated events when product content changes)
