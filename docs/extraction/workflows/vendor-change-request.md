# Vendor change-request review

> **Status:** Declared-not-wired (outbound + decision-inbound are both unproduced/unconsumed)
> **Type:** Choreography (single-BC internal in code; cross-BC in design)
> **Initiating actor:** Vendor user (Admin or CatalogManager role)
> **BCs involved:** Vendor Portal (originator + decision recipient); Product Catalog (intended reviewer, **not wired**)
> **Most recent material milestone:** M44.0 — Vendor Portal hardening

## Purpose

A vendor user with `Admin` or `CatalogManager` role drafts and submits a change request against their assigned products — a description edit, an image upload, or a data correction. Vendor Portal owns the `ChangeRequest` document and the per-request state machine (`Draft → Submitted → AdditionalInfoRequested → Approved/Rejected/Withdrawn`). On submission, it emits one of three per-type outbound integration contracts (`DescriptionChangeRequested`, `ImageUploadRequested`, `DataCorrectionRequested`); on a reviewer decision it consumes one of seven decision contracts (`DescriptionChangeApproved`, `DescriptionChangeRejected`, `ImageChangeApproved`, `ImageChangeRejected`, `DataCorrectionApproved`, `DataCorrectionRejected`, `AdditionalInfoRequested`).

**In the M44.0 codebase, none of the 10 cross-BC routes are wired end-to-end** — there is no producer for the seven decision contracts and no consumer for the three submission contracts in any other BC under `src/`. The workflow is therefore documented as a declared-not-wired choreography; the intra-BC commands and the local Wolverine routing are fully functional.

## Actors and triggers

- **Initiating actor:** Vendor user with role `Admin` or `CatalogManager` (per `DraftChangeRequestEndpoint.cs:48` authorisation gate)
- **Trigger:** `POST /api/vendor-portal/change-requests/draft` on Vendor Portal BC
- **Prerequisite state:** Authenticated session (Vendor Identity JWT bound to a non-`Suspended` / non-`Terminated` tenant); the SKU is associated with the vendor's tenant (per `VendorProductCatalogEntry`)

## Trace (intra-BC + intended cross-BC steps)

| Step | BC | Command / Event / Edge | Resulting state / output | Dossier reference |
|------|----|------------------------|--------------------------|-------------------|
| 1 | Vendor Portal | `DraftChangeRequest` command — handler validates tenant + role gate; persists new `ChangeRequest` document at `Status = Draft` | `ChangeRequest` document created | `bcs/vendor-portal.md#commands` |
| 2 | Vendor Portal | `SubmitChangeRequest` command — handler verifies tenant match (`SubmitChangeRequest.cs:88-94`); transitions `Status: Draft → Submitted`; emits one of three per-type contracts based on `ChangeRequest.Type` | One of `DescriptionChangeRequested`, `ImageUploadRequested`, `DataCorrectionRequested` published to the local Wolverine bus | `bcs/vendor-portal.md#commands`, `bcs/vendor-portal.md#outbound` |
| 3 | (Intended Reviewer — declared-not-wired) | Reviewer service would consume the submission contract from RabbitMQ | **No producer / consumer pair exists in `src/`** — the contracts round-trip through the local Wolverine bus on the Vendor Portal API host only | `bcs/vendor-portal.md#routes-without-instantiator` (item 1) |
| 4 | (Intended Reviewer — declared-not-wired) | Reviewer would emit a decision: `{Description,Image,DataCorrection}ChangeApproved` or `…Rejected`, or `AdditionalInfoRequested` | **No producer exists in `src/`** for any of the seven decision contracts | `bcs/vendor-portal.md#routes-without-instantiator` (item 2) |
| 5 | Vendor Portal | Decision handlers (`DescriptionChangeApprovedHandler`, etc.) subscribe to dedicated queues and would transition `Status` accordingly | Handlers are coded and ready; never invoked in M44.0 | `bcs/vendor-portal.md#inbound` (from Product Catalog) |
| 5a | Vendor Portal | If `AdditionalInfoRequested` received: `Status → AdditionalInfoRequested`; `ProvideAdditionalInfo` command (`POST /api/vendor-portal/change-requests/{id}/additional-info`) re-emits the same per-type submission contract; cycle continues | Same contract emitted as on initial submit | `bcs/vendor-portal.md#commands`, `bcs/vendor-portal.md#outbound` |
| 5b | Vendor Portal | `WithdrawChangeRequest` command — `Status → Withdrawn` | Terminal | `bcs/vendor-portal.md#commands` |

## Projections and views

- `ChangeRequest` (Vendor Portal) — Marten document; identity strategy and storage per `bcs/vendor-portal.md#aggregates-and-documents`. Holds `VendorTenantId`, `Sku`, `Type`, `Status`, and the per-type payload.
- `VendorProductCatalogEntry` (Vendor Portal) — per-SKU tenant ownership lookup; used by submission validators.
- Read endpoints: `GET /api/vendor-portal/change-requests?status=…` (list + filter); `GET /api/vendor-portal/change-requests/{id}` (single); `GET /api/vendor-portal/change-requests/image-upload-url` (pre-signed image upload).

## Compensation paths

### Failure: Tenant role gate rejects submit
- **Compensating action:** Endpoint returns 4xx; no `ChangeRequest` document is mutated; no integration contract emitted.
- **Resulting state:** Caller unauthorised.

### Failure: Cross-tenant guard rejects submit
- **Compensating action:** `SubmitChangeRequest.cs:88-94` document-level check rejects; same 4xx outcome.
- **Resulting state:** Caller unauthorised.

### Failure: Operator withdraws
- **Compensating action:** `WithdrawChangeRequest` transitions `Status → Withdrawn`; no compensating outbound contract.
- **Resulting state:** Terminal.

### Failure: Submission emitted but no reviewer ever decides
- **Compensating action:** None — in the current M44.0 implementation, the submission contracts are unconsumed; the `ChangeRequest` document remains in `Submitted` indefinitely.
- **Resulting state:** Stuck-at-`Submitted`. The dossier captures this as the primary declared-vs-implemented gap.

## Variants and edge cases

### Three change-request types use three distinct contract pairs
- `Description` → `DescriptionChangeRequested` (out) / `DescriptionChangeApproved` + `DescriptionChangeRejected` (in)
- `Image` → `ImageUploadRequested` (out) / `ImageChangeApproved` + `ImageChangeRejected` (in)
- `DataCorrection` → `DataCorrectionRequested` (out) / `DataCorrectionApproved` + `DataCorrectionRejected` (in)
- Plus the shared `AdditionalInfoRequested` (in) which restarts the submission cycle on the relevant request

### Image upload uses a pre-signed URL
`GET /api/vendor-portal/change-requests/image-upload-url` provides a pre-signed URL for the image-upload variant; the actual upload bypasses Vendor Portal's command surface.

### SignalR `ForceLogout` is declared-but-unused
Per `bcs/vendor-portal.md#routes-without-instantiator` (item 3), `ForceLogout` implements `IVendorUserMessage` and is wired into the SignalR routing convention, but no producer exists in `src/Vendor Portal/`. `Dashboard.razor` (the only client subscriber) does not branch on this type discriminator. Unrelated to the change-request flow itself but co-located.

## BCs and roles

- **Vendor Portal** — Originator + decision recipient. Owns the `ChangeRequest` document and the state machine. Emits 3 outbound contracts; subscribes to 7 inbound decision contracts. Dossier: `bcs/vendor-portal.md`.
- **Product Catalog (intended)** — In design, Product Catalog (or a reviewer service) would consume the per-type submission contracts and emit the matching decision contract. In code, **no producer / consumer pair exists**.

## Tests as behavioral evidence

- **Gherkin features:** none specific to the cross-BC review choreography; Vendor Portal command-level tests cover the intra-BC state transitions.
- **Integration tests:** `tests/Vendor Portal/VendorPortal.Api.IntegrationTests/` exercises each command end-to-end; cross-BC tests cannot exist because there is no producer for the decision contracts.

## ADRs

No dedicated ADR for the change-request choreography is cited in `bcs/vendor-portal.md`.

## Declared vs. implemented

- **Declared shape (CONTEXTS.md):** Vendor Portal communicates with Vendor Identity, Orders, Fulfillment, Payments, and Pricing.
- **Implemented shape (M44.0):** Vendor Portal listener wiring shows traffic with Vendor Identity (9 contracts), Orders (1), Inventory (3), and Product Catalog (8). Fulfillment, Payments, and Pricing have no listener queues, no integration handlers, and no contract subscriptions. Inventory and Product Catalog are absent from the CONTEXTS.md neighbour list.
- **Gap:** Three CONTEXTS.md-declared neighbours are not wired; two wired neighbours are not in CONTEXTS.md. Dossier source: `bcs/vendor-portal.md#contextsmd-drift`.

- **Declared shape:** Three outbound submission contracts (`DescriptionChangeRequested`, `ImageUploadRequested`, `DataCorrectionRequested`) exist in `Messages.Contracts/VendorPortal/`.
- **Implemented shape:** Emitted from Vendor Portal handlers but **no other BC in `src/` declares a handler for any of these types**. The contracts round-trip through the local Wolverine bus on the Vendor Portal API host.
- **Gap:** Outbound contracts produced but not consumed. Dossier source: `bcs/vendor-portal.md#routes-without-instantiator` (item 1).

- **Declared shape:** Seven inbound decision contracts (`DescriptionChangeApproved`, `DescriptionChangeRejected`, `ImageChangeApproved`, `ImageChangeRejected`, `DataCorrectionApproved`, `DataCorrectionRejected`, `AdditionalInfoRequested`) are subscribed via dedicated queues with dedicated handlers.
- **Implemented shape:** `grep` for `new {Contract}(` constructors across `src/` returns zero matches outside the contract definitions themselves. **No producer exists in M44.0.**
- **Gap:** Inbound contracts consumed but not produced. Dossier source: `bcs/vendor-portal.md#routes-without-instantiator` (item 2).

- **Declared shape:** `CONTEXTS.md:73` lists Vendor Portal as the **publisher** of `InventoryAdjusted`, `LowStockDetected`, and `StockReplenished`.
- **Implemented shape:** These three are inbound queues from Inventory (producers in `src/Inventory/`, not `src/Vendor Portal/`).
- **Gap:** CONTEXTS.md inverts the direction. Dossier source: `bcs/vendor-portal.md#contextsmd-drift`.

## Source citations

- Dossier sections referenced: `bcs/vendor-portal.md#commands`, `bcs/vendor-portal.md#outbound`, `bcs/vendor-portal.md#inbound`, `bcs/vendor-portal.md#routes-without-instantiator`, `bcs/vendor-portal.md#contextsmd-drift`, `bcs/vendor-portal.md#aggregates-and-documents`.
- Tests: `tests/Vendor Portal/VendorPortal.Api.IntegrationTests/`.
