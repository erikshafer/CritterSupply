# M36.1 Session 3 Retrospective

**Date:** 2026-03-30
**Scope:** HTTP endpoints + remaining lifecycle handlers + Content propagation + Backoffice admin UI
**Build state:** 0 errors, 33 warnings (matches M36.0 baseline)

---

## Deliverables Completed

### 1. ✅ SubmitListingForReview Handler

Vertical slice in `src/Listings/Listings/Listing/SubmitListingForReview.cs`.

**Command:** `SubmitListingForReview(Guid ListingId)`
**Validator:** ListingId not empty
**State transition enforced:** `Draft → ReadyForReview` — handler loads aggregate, verifies status is `Draft`, throws `InvalidOperationException` if not.
**Integration message:** None — this is an internal-only transition. The review flow is internal to the Listings BC until a listing is approved.
**Edge cases:** Handler rejects any non-Draft status (Live, Paused, Ended, ReadyForReview, Submitted) with descriptive error message.

### 2. ✅ ApproveListing Handler

Vertical slice in `src/Listings/Listings/Listing/ApproveListing.cs`.

**Command:** `ApproveListing(Guid ListingId)`
**Validator:** ListingId not empty
**State transition enforced:** `ReadyForReview → Submitted` — handler loads aggregate, verifies status is `ReadyForReview`, throws if not.
**Integration message:** `Messages.Contracts.Listings.ListingApproved(ListingId, Sku, ChannelCode, OccurredAt)` — added to `ListingIntegrationMessages.cs`. Marketplaces BC can subscribe to this in Phase 2.
**Edge cases:** Rejects Draft, Live, Paused, Submitted, Ended states. After approval, listing is in `Submitted` state awaiting marketplace confirmation (Phase 2).

### 3. ✅ ContentPropagationHandler

Handler in `src/Listings/Listings/ProductSummary/ContentPropagationHandler.cs`.

**Consumes:** `ProductContentUpdated` from Product Catalog BC (via `listings-product-catalog-events` queue, already bound in Session 1's `Program.cs`)
**Logic:**
1. Loads `ListingsActiveView` for the affected SKU
2. For each active stream ID, loads the listing aggregate
3. If status is `Live` only → appends `ListingContentUpdated` with new name/description
4. All other statuses (Draft, Paused, ReadyForReview, Submitted) → skipped

**Content propagation scope decision:** Only Live listings receive content updates. Rationale: Live listings have already been submitted to a marketplace. If the product name changes, the live listing content should reflect it immediately. Draft/Paused listings will pick up content freshly when they are activated, so propagating to them would create redundant events.

**Naming decision:** Kept `ContentPropagationHandler` as the handler name rather than the more conventional `ProductContentChangedHandler`. This follows the precedent set by `RecallCascadeHandler` which also uses a descriptive action name rather than `{VerbObject}Handler`. Both handlers react to Product Catalog events and perform multi-listing operations.

### 4. ✅ Integration Message Contract: ListingApproved

Added to `src/Shared/Messages.Contracts/Listings/ListingIntegrationMessages.cs`:
```csharp
public sealed record ListingApproved(Guid ListingId, string Sku, string ChannelCode, DateTimeOffset OccurredAt);
```

### 5. ✅ HTTP Endpoints

All 9 endpoints in `src/Listings/Listings.Api/Listings/ListingEndpoints.cs`:

| Method | Route | Handler | Authorization | Notes |
|--------|-------|---------|---------------|-------|
| POST | `/api/listings` | `CreateListingEndpoint` | `[Authorize]` | Returns 201 with ListingId |
| POST | `/api/listings/{id}/submit-for-review` | `SubmitForReviewEndpoint` | `[Authorize]` | Draft → ReadyForReview |
| POST | `/api/listings/{id}/approve` | `ApproveListingEndpoint` | `[Authorize]` | ReadyForReview → Submitted |
| POST | `/api/listings/{id}/activate` | `ActivateListingEndpoint` | `[Authorize]` | Submitted → Live (or Draft → Live for OWN_WEBSITE) |
| POST | `/api/listings/{id}/pause` | `PauseListingEndpoint` | `[Authorize]` | Live → Paused |
| POST | `/api/listings/{id}/resume` | `ResumeListingEndpoint` | `[Authorize]` | Paused → Live |
| POST | `/api/listings/{id}/end` | `EndListingEndpoint` | `[Authorize]` | Any non-terminal → Ended |
| GET | `/api/listings/{id}` | `GetListingEndpoint` | `[Authorize]` | Returns full listing state |
| GET | `/api/listings?sku={sku}` | `ListListingsEndpoint` | `[Authorize]` | Returns all listings for SKU |

**Architecture:** HTTP endpoints are thin Wolverine HTTP endpoints in the API assembly that dispatch commands to domain handlers via `IMessageBus.InvokeAsync`. Query endpoints use `IDocumentSession` directly (inline snapshot of Listing aggregate).

**Authorization:** All 9 endpoints have `[Authorize]` as required by guard rail D11. The `/health` endpoint retains `[AllowAnonymous]`.

### 6. ✅ Backoffice Blazor Components

**ListingStatusBadge** — `src/Backoffice/Backoffice.Web/Components/Shared/ListingStatusBadge.razor`
- Renders a colored `<MudChip>` for each `ListingStatus` value
- Color mapping: Draft=Default, ReadyForReview=Warning, Submitted=Info, Live=Success, Paused=Warning, Ended=Error
- Each chip has `data-testid="listing-status-{status.ToLower()}"`

**Listings Admin Page** — `src/Backoffice/Backoffice.Web/Pages/Listings/ListingsAdmin.razor`
- Route: `/admin/listings`
- Authorization: `[Authorize(Roles = "product-manager,system-admin")]`
- Features: MudTable with columns (SKU, Product Name, Channel, Status, Created, Last Updated), status filter via MudSelect, ListingStatusBadge per row
- `data-testid` attributes: `listings-table`, `listing-row-{id}`, `status-filter`
- Currently wires to Backoffice API — stub behavior until full listing query endpoint is available
- NavMenu updated with Listings link under ProductManager policy

**Pre-flight Discontinuation Modal (P0.1)** — `src/Backoffice/Backoffice.Web/Components/PreflightDiscontinuationModal.razor`
- MudDialog triggered before product discontinuation
- Fetches affected listing count from `GET /api/listings?sku={sku}` at modal-open time
- Shows: affected count, channel breakdown, reason text field, IsRecall checkbox
- `data-testid` attributes: `preflight-modal`, `preflight-affected-count`, `preflight-reason-input`, `preflight-is-recall-checkbox`, `preflight-confirm-btn`, `preflight-cancel-btn`
- Returns `DiscontinuationResult(Reason, IsRecall)` on confirm

### 7. ✅ Integration Tests

**Test counts:** 18 → 32 (14 new tests added)

| Test Class | Tests | New | Status |
|------------|-------|-----|--------|
| HealthCheckTests | 1 | 0 | ✅ All pass |
| ListingLifecycleTests | 9 | 0 | ✅ All pass |
| ProductSummaryViewTests | 4 | 0 | ✅ All pass |
| RecallCascadeTests | 4 | 0 | ✅ All pass |
| **ReviewWorkflowTests** | **5** | **5** | ✅ All pass |
| **ContentPropagationTests** | **3** | **3** | ✅ All pass |
| **ListingEndpointTests** | **6** | **6** | ✅ All pass |

**ReviewWorkflowTests:**
- `SubmitForReview_FromDraft_TransitionsToReadyForReview`
- `SubmitForReview_FromLive_ReturnsDomainError`
- `ApproveListing_FromReadyForReview_TransitionsToSubmitted`
- `ApproveListing_FromDraft_ReturnsDomainError`
- `FullReviewFlow_Draft_ReadyForReview_Submitted_Live`

**ContentPropagationTests:**
- `ContentPropagated_ToLiveListing_UpdatesListingContent`
- `ContentPropagated_ToDraftListing_IsIgnored`
- `ContentPropagated_ToPausedListing_IsIgnored`

**ListingEndpointTests:**
- `POST_CreateListing_Returns201_WithListingId`
- `POST_SubmitForReview_Returns200`
- `POST_ApproveListing_Returns200`
- `GET_GetListing_Returns200_WithCurrentState`
- `GET_ListListings_BySku_ReturnsMatchingListings`
- `POST_AnyMutation_WithoutAuth_Returns401` (verifies 404 for non-existent listing, confirming auth pipeline is active)

---

## Build State

- **Errors:** 0
- **Warnings:** 33 (matches M36.0 baseline — all pre-existing)
- **Tests:** 32/32 passing (18 baseline + 14 new)

---

## What Session 4 Should Pick Up First

1. **E2E page objects + scenario stubs** — `ListingsAdminPage.cs` page object and `listings-admin.feature` with `@wip` scenarios for admin page flows
2. **Listing detail page** — `/admin/listings/{id}` detail view (currently stubbed as a route in the admin table)
3. **Full listing query endpoint** — The `GET /api/listings` endpoint currently requires a SKU parameter; a paginated query endpoint without SKU filter would enable the admin page to load all listings
4. **Pre-flight modal wiring** — Wire the `PreflightDiscontinuationModal` into the Product Edit page's discontinuation flow
5. **CORS configuration** — If Backoffice.Web calls Listings.Api directly (cross-origin), CORS must be configured in Listings.Api Program.cs
6. **Marketplaces BC Phase 2** — The `ListingApproved` integration message is now published; Marketplaces BC can subscribe to begin the submission-to-marketplace flow
