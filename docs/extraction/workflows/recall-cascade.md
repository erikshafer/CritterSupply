# Recall cascade

> **Status:** Active
> **Type:** Choreography
> **Initiating actor:** Operator (Product Catalog / Backoffice operator)
> **BCs involved:** Product Catalog, Listings, Marketplaces (downstream via Listings outcome events)
> **Most recent material milestone:** M36.1 — Listings BC build-out

## Purpose

When a catalog product is discontinued with the recall flag set, every active listing of that SKU across every sales channel must be taken down immediately. The recall cascade traces the publication of a single `ProductDiscontinued(IsRecall=true)` integration event from Product Catalog through Listings' bulk force-down handler to one terminal summary event per recall, plus one per-listing force-down event for each affected listing.

## Actors and triggers

- **Initiating actor:** Operator (via Product Catalog HTTP discontinue command on the affected `CatalogProduct`)
- **Trigger:** `ProductCatalog.ProductDiscontinued` integration event with `IsRecall == true` (default `false` short-circuits the cascade)
- **Prerequisite state:** A `CatalogProduct` aggregate exists in Product Catalog; one or more `Listing` aggregates exist for the SKU in non-terminal states (`Draft`, `ReadyForReview`, `Submitted`, `Live`, or `Paused`)

## Trace

| Step | BC | Command / Event / Edge | Resulting state / output | Dossier reference |
|------|----|------------------------|--------------------------|-------------------|
| 1 | Product Catalog | `CatalogProduct.Discontinue(IsRecall=true, Reason)` command | Stream event `ProductDiscontinued`; `Messages.Contracts.ProductCatalog.ProductDiscontinued` published | `bcs/product-catalog.md#published-by-product-catalog` |
| 2 | Listings | `RecallCascadeHandler` consumes from RabbitMQ queue `listings-product-recall`; short-circuits if `IsRecall == false` | Loads `ListingsActiveView` for the SKU (the set of non-terminal listing stream IDs) | `bcs/listings.md#inbound-subscribed` (item 2) |
| 3 | Listings | For each active listing, the handler appends `ListingForcedDown(RecallReason)` to the `Listing` stream | One per-listing event per affected listing; per-listing `ListingStatus` flips to terminal `Ended` | `bcs/listings.md#aggregates` |
| 4 | Listings | The handler publishes `Messages.Contracts.Listings.ListingForcedDown(ListingId, Sku, ChannelCode, RecallReason, OccurredAt)` for each affected listing | One outbound integration message per listing | `bcs/listings.md#outbound-published` |
| 5 | Listings | The handler emits a single `Messages.Contracts.Listings.ListingsCascadeCompleted(Sku, AffectedCount, OccurredAt)` summary (always — including `AffectedCount == 0`) | Terminal cascade event | `bcs/listings.md#outbound-published` |
| 6 | Product Catalog / consumer side | `ProductSummaryHandlers.ProductDiscontinuedSummaryHandler` (also on a separate `listings-product-catalog-events` queue subscription) sets the Listings ACL `ProductSummaryView` status to `Discontinued` | ACL reflects discontinued status independently of the cascade itself | `bcs/listings.md#inbound-subscribed` (item 1) |

Note that Marketplaces does **not** subscribe to `ProductDiscontinued` directly (see `bcs/marketplaces.md` — only 4 of Product Catalog's 9 outbound events are consumed by Marketplaces; `ProductDiscontinued` is not among them). External marketplace deactivation flows separately through the per-listing `ListingForcedDown` if any future Marketplaces handler is wired against it; today there is no Marketplaces consumer for `ListingForcedDown`.

## Projections and views

- `ListingsActiveView` (owned by Listings) — keyed by SKU; tracks the set of `ListingStreamId` values in non-terminal states. Updated on `ListingDraftCreated` (add) and `ListingEnded` / `ListingForcedDown` (remove). The RecallCascadeHandler's authoritative source for "which listings to force down." Dossier: `bcs/listings.md#projections`.
- `ProductSummaryView` (owned by Listings; ACL over Product Catalog) — updated by `ProductDiscontinuedSummaryHandler` independently of the cascade. Dossier: `bcs/listings.md#aggregates`.

## Compensation paths

### Failure: `IsRecall == false` short-circuit
- **Compensating action:** Not a failure — the handler returns immediately when `IsRecall == false`. No events appended; no outbound integration messages emitted.
- **Resulting state:** Catalog product transitions to `Discontinued` but listings remain in their current states.
- **BCs involved in compensation:** Product Catalog and Listings (no compensation needed).

### Failure: A per-listing `ListingForcedDown` append fails mid-cascade
- **Compensating action:** Wolverine handler retries the entire `RecallCascadeHandler` invocation per its retry policy. The handler's per-listing `IsTerminal` guard makes the operation idempotent — listings already in terminal `Ended` state are skipped on re-run.
- **Resulting state:** Eventual consistency; on re-run, only the remaining non-terminal listings are force-downed; the terminal `ListingsCascadeCompleted` event reflects the cumulative count from the successful run that closes the handler.
- **BCs involved in compensation:** Listings.

### Failure: Inconsistency between Catalog status and per-channel external state
- **Compensating action:** None currently wired. `ListingForcedDown` is published but no Marketplaces handler subscribes to it; external channel listings (Amazon, eBay, Walmart) remain active until the Backoffice operator manually deactivates via Marketplaces management endpoints.
- **Resulting state:** Internal listings reflect `Ended`; external channel states drift until manual intervention.
- **BCs involved in compensation:** None at the system layer — operator intervention via Backoffice + Marketplaces admin endpoints.

## Variants and edge cases

### `AffectedCount == 0`
`ListingsCascadeCompleted` is published unconditionally — even when zero listings exist for the SKU. Downstream consumers can observe "recall acknowledged, no listings affected" without inferring from the absence of events. Dossier: `bcs/listings.md#outbound-published`.

### Recall cascade fires on a SKU with only `Draft` and `ReadyForReview` listings
Per the implemented `IsTerminal` guard at `RecallCascadeHandler` (`src/Listings/Listings/Listing/RecallCascadeHandler.cs#L43-L48`), draft and ready-for-review listings are also force-downed. The CONTEXTS.md description of "all Live and Paused listings" is a narrower summary than what code does — see Declared vs. implemented below.

## BCs and roles

- **Product Catalog** — Publishes `ProductDiscontinued` with the recall flag; the cascade's only origin. Dossier: `bcs/product-catalog.md`.
- **Listings** — Owns the cascade: `RecallCascadeHandler` reacts to the recall, iterates `ListingsActiveView`, emits per-listing `ListingForcedDown` events and a single terminal `ListingsCascadeCompleted`. Dossier: `bcs/listings.md`.
- **Marketplaces** — Not currently a downstream subscriber of `ListingForcedDown`; the cascade does not propagate to external channels through Marketplaces today. Dossier: `bcs/marketplaces.md`.

## Tests as behavioral evidence

- **Gherkin features:** none specific to recall cascade.
- **Integration tests (Alba + Testcontainers):**
  - `tests/Listings/Listings.Api.IntegrationTests/RecallCascadeTests.cs` — 4 tests exercising `RecallCascadeHandler` against multiple Live + Paused + Draft listings, confirming the per-listing `ListingForcedDown` plus the summary `ListingsCascadeCompleted` and the `IsRecall == false` short-circuit.
- **Unit / projection tests:** `ProductSummaryViewTests.cs` (4 tests) covers the parallel `ProductDiscontinued` → ACL status flip.

## ADRs

No dedicated recall-cascade ADR; the cascade is documented through ADR 0050 (Marketplaces ProductSummaryView ACL), ADR 0048 (Marketplaces document design), and the catalog-listings-marketplaces evolution plan in `docs/planning/`. Dossier: `bcs/listings.md#adrs`.

## Declared vs. implemented

- **Declared shape (CONTEXTS.md lines 170–174):** The recall cascade force-downs "all Live and Paused listings for the affected SKU."
- **Implemented shape:** `RecallCascadeHandler` force-downs every non-terminal listing — any state other than `Ended` (`Draft`, `ReadyForReview`, `Submitted`, `Live`, `Paused`). The implementation follows from `ListingsActiveView` adding stream IDs on `ListingDraftCreated` and removing them only on `ListingEnded` / `ListingForcedDown`, combined with the `RecallCascadeHandler` per-listing `IsTerminal` guard.
- **Gap:** CONTEXTS.md sentence is a narrower summary than implementation. Dossier source: `bcs/listings.md#contextsmd-drift`.

- **Declared shape:** External marketplace recall flow (Amazon, eBay, Walmart deactivation) implied by the existence of `Marketplaces` BC integration.
- **Implemented shape:** No Marketplaces handler subscribes to `ListingForcedDown`; external channel state does not deactivate automatically on recall.
- **Gap:** Recall-driven external-channel deactivation is not wired.

## Source citations

- Dossier sections referenced: `bcs/product-catalog.md#published-by-product-catalog`, `bcs/listings.md#inbound-subscribed`, `bcs/listings.md#outbound-published`, `bcs/listings.md#aggregates`, `bcs/listings.md#projections`, `bcs/listings.md#contextsmd-drift`, `bcs/marketplaces.md#inbound-subscribed`.
- Handler citations (where dossier already source-cites): `src/Listings/Listings/Listing/RecallCascadeHandler.cs` (full handler), `src/Shared/Messages.Contracts/ProductCatalog/ProductDiscontinued.cs#L12-L13` (`IsRecall` field).
- Tests: `tests/Listings/Listings.Api.IntegrationTests/RecallCascadeTests.cs`.
- ADRs cited via dossier: `docs/decisions/0050-marketplaces-product-summary-acl.md`.
