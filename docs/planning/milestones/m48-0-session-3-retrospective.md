# M48.0 Session 3 Retrospective — Channels / Vendor / Admin Deep Dive (partial)

**Date:** 2026-05-15
**Milestone:** M48.0 — CritterSupply Business Architecture Extraction
**Session:** Session 3 — Promote 9 channels / vendor / admin stub dossiers to S2 — full depth

## Outcome (executive summary)

S3 closed in a partial state: **5 of the 9 planned dossiers were promoted to S2 — full depth in place** (Listings, Marketplaces, Vendor Identity, Vendor Portal, Backoffice Identity). The remaining four — **Backoffice (Variant C-hybrid; heaviest of S3), Pricing (Variant A — DCB), Promotions (Variant A — two aggregates, two stream-ID strategies, DCB), and Correspondence (Variant A — lightest)** — were deferred. They will be picked up in a follow-up session **S3b** before S4 begins, mirroring the S2 → S2b precedent.

The 5 dossiers that landed surfaced **substantive code-vs-S1 deviations and CONTEXTS.md drift items** that were not visible at stub depth — the per-BC notes below capture each, and the cross-reference forward to S5 at the end of this document consolidates them.

## Baseline

- Build at session open: **0 errors, 456 warnings** (incremental). Note: the S2b retrospective recorded 359 warnings on incremental and 475 on clean rebuild; the 456 figure on this session's incremental open lies between the two and is consistent with no code change. Composition of warnings is unchanged.
- Build at session close: **No code changed** — incremental rebuild expected to be identical.
- Files changed: 8 — 5 BC dossiers, this retrospective, `docs/extraction/README.md` status table, `docs/planning/CURRENT-CYCLE.md`.

## Items Completed

| Item | Description | File | Counts |
|------|-------------|------|--------|
| S3a | Listings dossier (Variant A) | `docs/extraction/bcs/listings.md` | 1 aggregate (Listing) / 9 events / 7 commands / 2 inline projections + 1 ACL document / 6 outbound + 11 inbound integration events |
| S3b | Marketplaces dossier (Variant D — new) | `docs/extraction/bcs/marketplaces.md` | 4 Marten document types / 5 commands / 4 outbound + 5 inbound integration events / 6 adapter implementations (3 production + 3 stub) |
| S3c | Vendor Identity dossier (Variant B — JWT issuer) | `docs/extraction/bcs/vendor-identity.md` | 3 EF Core entities / 10 commands / 11 outbound integration events / 0 inbound |
| S3d | Vendor Portal dossier (Variant D + Blazor WASM frontend) | `docs/extraction/bcs/vendor-portal.md` | 7 Marten document types (S1 had 9 — 2 are JSONB sub-properties) / 7 commands / 7 `.razor` pages / 21 inbound queues / 6 SignalR realtime message types / 16 HTTP endpoints |
| S3e | Backoffice Identity dossier (Variant B — JWT issuer + 7-role RBAC) | `docs/extraction/bcs/backoffice-identity.md` | 1 EF Core entity (BackofficeUser) / 7 commands + 1 read query / 7 roles enumerated / 0 integration events |

## Items Deferred (not completed in this session)

| Item | Description | File | Status |
|------|-------------|------|--------|
| S3f | Backoffice dossier (Variant C-hybrid: BFF + 1 ES aggregate) | `docs/extraction/bcs/backoffice.md` | **Deferred to S3b.** Heaviest dossier in S3 (~350-450 lines target). Requires `.razor` page enumeration, 5 BFF projection direct-enumeration, EM reconciliation across three prior artifacts (`backoffice-event-modeling.md`, `backoffice-event-model-critique.md`, `backoffice-event-modeling-revised.md`), and operations-health surface (M46.0 dead-letter summary). Two principal-architect dispatches reported insufficient remaining session budget to begin. Stub unchanged. |
| S3g | Pricing dossier (Variant A — DCB) | `docs/extraction/bcs/pricing.md` | **Deferred to S3b.** Stub unchanged. |
| S3h | Promotions dossier (Variant A — two aggregates / two stream-ID strategies / DCB) | `docs/extraction/bcs/promotions.md` | **Deferred to S3b.** Stub unchanged. |
| S3i | Correspondence dossier (Variant A — lightest) | `docs/extraction/bcs/correspondence.md` | **Deferred to S3b.** Stub unchanged. |

## Per-BC Dossier Notes

### Listings (S3a — Variant A)

- Aggregate: 1 (`Listing`). Events: 9. Commands: 7. Projections: 2 inline registrations (`Listing` snapshot + `ListingsActiveView` `MultiStreamProjection`) + 1 ACL document (`ProductSummaryView`, mutated imperatively by 9 handlers — not registered as a Marten projection). Reconciliation against S1: counts match exactly.
- Stream-ID verification: confirmed UUID v5 (RFC 4122 URL namespace, SHA-1 hash of `listing:{sku}:{channelCode}`, version/variant bits forced) at `src/Listings/Listings/Listing/ListingStreamId.cs:21-66`. S1 claim accurate.
- Saga participation: not a saga orchestrator. Participates in the recall cascade by reacting to Product Catalog `ProductDiscontinued` with `IsRecall=true`.
- Integration events: 6 outbound (`ListingCreated`, `ListingApproved`, `ListingActivated`, `ListingEnded`, `ListingForcedDown`, `ListingsCascadeCompleted`) — only `ListingActivated` and `ListingEnded` have explicit `PublishMessage(...).ToRabbitExchange(...)` routes; the other four rely on Wolverine `.AutoProvision()` conventions. 11 inbound: 9 `ProductCatalog.*` events (ACL) + 2 `Marketplaces.*` (state-machine inputs).
- Notable ADRs cited: ADR 0042 (catalog UUID v5 namespace).
- Routes-without-instantiator: none found.
- **CONTEXTS.md drift (forward-note for S5):** CONTEXTS.md line 174 says recall force-downs apply to "all Live and Paused listings"; the code force-downs **all non-terminal listings** (Draft / ReadyForReview / Submitted / Live / Paused) because `ListingsActiveView` only removes on `ListingEnded` / `ListingForcedDown`. Documented in dossier with code as authoritative.

### Marketplaces (S3b — Variant D, new)

- Document types: **4** — `Marketplace`, `CategoryMapping`, `ProductSummaryView` (ACL), `OrphanedEbayDraft`. **+2 vs S1's 2.** Cause: S1 listed `ProductSummaryView` under "Projections" (it is a Marten document mutated by Wolverine handlers, not a `Projections.*` registration) and did not enumerate `OrphanedEbayDraft` (the M38.1 follow-up to ADR 0055). S3 enumerates all four `opts.Schema.For<...>().Identity(...)` registrations in `Program.cs#L48-L59`.
- Commands: 5 — `RegisterMarketplace`, `UpdateMarketplace`, `SetCategoryMappingRequest`, `CheckWalmartFeedStatus` (internal scheduled), `SweepOrphanedEbayDrafts` (internal scheduled). S1 deferred the count.
- Integration events: 4 outbound + 5 inbound (`Listings.ListingApproved` triggering adapter submission + 4 `ProductCatalog.*` events feeding the ACL).
- Adapter implementations enumerated: **6** — `AmazonMarketplaceAdapter`, `WalmartMarketplaceAdapter`, `EbayMarketplaceAdapter` (production, gated by `Marketplaces:UseRealAdapters`); `StubAmazonAdapter`, `StubWalmartAdapter`, `StubEbayAdapter` (Dev/CI default). Resolved via `IReadOnlyDictionary<string, IMarketplaceAdapter>` keyed case-insensitively by `ChannelCode` (`Program.cs#L157-L159`).
- Notable ADRs cited: ADR 0048 (doc entity), ADR 0049 (category mapping), ADR 0050 (independent ACL), ADR 0051 (vault), ADR 0052-0054 (per-marketplace auth strategies), ADR 0055 (status polling), ADR 0056 (resilience), ADR 0057 (Walmart deactivation IDs).
- Routes-without-instantiator: none. Marketplaces does not subscribe to 5 of Product Catalog's 9 published events (`ProductImagesUpdated`, `ProductDimensionsChanged`, `ProductDeleted`, `ProductRestored`, `ProductDiscontinued`) — this is a one-direction subscription gap, not a Marketplaces-declared route lacking a handler.
- **CONTEXTS.md drift (forward-notes for S5):**
  1. `OrphanedEbayDraft` Marten document and the `SweepOrphanedEbayDraftsHandler` 24-hour sweep are unmentioned in CONTEXTS.md lines 178-189.
  2. The Marketplaces communicates-with table at CONTEXTS.md lines 184-187 shows only inbound arrows; the outbound publishes of `MarketplaceListingActivated` / `MarketplaceSubmissionRejected` to Listings are described in inline note rather than as row directions (presentation drift only — the Listings row narrates both directions).

### Vendor Identity (S3c — Variant B, JWT issuer)

- Entities: 3 (`VendorTenant`, `VendorUser`, `VendorUserInvitation`). DbSet → CLR: `Tenants` → `VendorTenant`, `Users` → `VendorUser`, `Invitations` → `VendorUserInvitation` (`VendorIdentityDbContext.cs:18-20`). Schema: `vendoridentity`.
- Commands: 10 — exact match to S1.
- Integration events: 11 outbound — exact match to S1. **One declared-not-emitted item:** `VendorUserActivated` has a contract record and a `PublishMessage<>().ToRabbitQueue(...)` route at `Program.cs:104` but **no handler in current code emits it** — the user-acceptance flow (Invited → Active) is not implemented (`InviteVendorUserHandler.cs:67-69` flags the email/raw-token side as out-of-scope). This is the same routes-without-instantiator pattern surfaced in S2b for Fulfillment.
- Anonymous endpoints: 3 (`POST /auth/login`, `/auth/refresh`, `/auth/logout`) — all expected. **All 10 administrative endpoints are `[Authorize]`** — no surprise (in contrast to Customer Identity's `AddAddress` / `GetCustomerAddresses` finding from S2).
- JWT claims set issued (`JwtTokenService.cs:25-32`, exactly six claims): `VendorUserId`, `VendorTenantId`, `VendorTenantStatus`, `ClaimTypes.Email`, `ClaimTypes.Role`, `JwtRegisteredClaimNames.Jti`. Signing: HMAC-SHA256, symmetric key from `Jwt:SigningKey`; access-token lifetime 15 min, refresh cookie 7 days; **no key rotation implemented**.
- Notable ADRs cited: ADR 0024 (SHA-256 invitation tokens), ADR 0028 (JWT for Vendor Identity), ADR 0032 (multi-issuer JWT).
- **CONTEXTS.md drift (forward-notes for S5):**
  1. CONTEXTS.md line 201 lists Vendor Portal as the only peer ("queried by"); actual topology is publish-only over RabbitMQ to 11 dedicated queues — synchronous query relationship is not documented and the 11 events are not enumerated.
  2. CONTEXTS.md cites ADRs 0024 / 0028 only for this BC — ADR 0032 (multi-issuer JWT) is not referenced even though it materially governs cross-API token acceptance.
  3. CONTEXTS.md does not mention the tenant-lifecycle commands or the `VendorTenantStatus` machine.
- **Other declared-but-unused / declared-but-unimplemented items (forward-notes for S5):**
  - Refresh tokens are **not persisted server-side** — no `RefreshTokens` DbSet/entity. Cookie-only.
  - `InvitationStatus.Expired` enum value exists but **no code path transitions invitations into it** — no expiry-sweep job is registered.
  - Prior EM doc (`vendor-portal-event-modeling.md:68`) prescribed Argon2id; implementation uses framework-default `PasswordHasher<T>` (PBKDF2 / HMACSHA512).

### Vendor Portal (S3d — Variant D + Blazor WASM frontend)

- Document types: **7** (S1 said 9). Cause: `NotificationPreferences` and `SavedDashboardView` are `sealed record` types embedded inside `VendorAccount` (`NotificationPreferences` as a single property; `SavedDashboardViews` as `IReadOnlyList<SavedDashboardView>`) — they persist as JSONB sub-properties of the parent VendorAccount document, not as independent Marten collections. Reconciled inline in dossier.
- Commands: 7 — match S1.
- HTTP endpoints: 16 + `/health` + `/` redirect.
- Razor pages: 7 (`Login`, `Dashboard`, `TeamManagement`, `Settings`, `SubmitChangeRequest`, `ChangeRequests`, `ChangeRequestDetail`).
- SignalR: hub at `/hub/vendor-portal`. Two groups per connection: `vendor:{VendorTenantId}` and `user:{VendorUserId}` (`VendorPortalHub.cs:47-48`). 6 realtime message types — 4 tenant-scoped via `IVendorTenantMessage` + 2 user-scoped via `IVendorUserMessage`. Status `Suspended` / `Terminated` triggers `Context.Abort()` (`VendorPortalHub.cs`). Bearer carried via `?access_token=` query string for WebSocket upgrade.
- Inbound RabbitMQ listener queues: 21 (`grep -c ListenToRabbitQueue Program.cs`).
- Permission policies: **none registered** — `Program.cs` has zero named authorization policies. Every endpoint method carries bare `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]` and performs per-handler claim checks (tenant claim → `Unauthorized`; status `Suspended` / `Terminated` → `Forbid`; role gate (e.g. Admin or CatalogManager for change-request drafting at `DraftChangeRequestEndpoint.cs:48`); cross-tenant guards inside command handlers).
- Notable ADRs cited: ADR 0021 (Blazor WASM), ADR 0025 (POC learnings).
- **Routes-without-instantiator (forward-notes for S5):**
  1. **Outbound declared, no consumer in any other BC:** `DescriptionChangeRequested`, `ImageUploadRequested`, `DataCorrectionRequested` — no `[WolverineHandler]` exists for these in `src/`. No explicit `PublishMessage<T>().ToRabbitExchange(...)` route is registered either; they round-trip the local Wolverine bus only.
  2. **Inbound subscribed, no producer in any other BC:** 7 change-request decision contracts (`Description / Image / DataCorrection × Approved / Rejected` + `AdditionalInfoRequested`) — handlers and queues exist; `grep` for the constructor calls returns zero matches outside the contract definitions.
  3. **Realtime declared but unused:** `ForceLogout` — no producer (`new ForceLogout(`) and no client `ReceiveMessage` branch in `Dashboard.razor`.
- **CONTEXTS.md drift (forward-notes for S5):**
  1. **Direction inversion** — CONTEXTS.md line 73 lists Vendor Portal as the **publisher** of `InventoryAdjusted`, `LowStockDetected`, `StockReplenished`. Implementation has them as inbound from Inventory.
  2. **Missing neighbours** — Lines 213-219 omit Inventory and Product Catalog (both have substantial inbound contract flow); list Fulfillment, Payments, Pricing as neighbours but no listener queues, handlers, or contract subscriptions exist for any of those three at M44.0.
  3. **Workflow contracts undocumented** — None of the 3 outbound or 7 change-request decision contracts appear in CONTEXTS.md.

### Backoffice Identity (S3e — Variant B, JWT issuer + 7-role RBAC; first formal modeling artifact)

- Entity: 1 (`BackofficeUser`). Schema: `backofficeidentity`. DbSet `Users` → CLR `BackofficeUser`.
- Commands: 7 + 1 read query — match S1. Caveat: `ResetBackofficeUserPassword` is implemented as endpoint-resident logic (`ResetBackofficeUserPasswordEndpoint.cs:157-192`) rather than a Wolverine handler; the command record itself exists at `UserManagement/ResetBackofficeUserPassword.cs:12`.
- 7 roles enumerated (from `BackofficeRole` enum, `BackofficeUser.cs:43-52`): CopyWriter, PricingManager, WarehouseClerk, CustomerService, OperationsManager, Executive, SystemAdmin. Count verified.
- JWT claims set (`JwtTokenGenerator.cs:54-61`): `sub` (user Id), `email`, `name` (FirstName + LastName), `role` (kebab-case via `ToRoleString()`), `iat`. Plus standard `exp` / `iss` / `aud`. HMAC-SHA256, symmetric key from `Jwt:SecretKey`, access 15 min, refresh 7 days. **Refresh token is persisted server-side** on `BackofficeUser.RefreshToken` — divergence from Vendor Identity (cookie-only).
- Anonymous endpoints: 3 (`/auth/login`, `/auth/refresh`, `/auth/logout`). All five `users/*` endpoints carry `[Authorize(Policy = "SystemAdmin")]`.
- Integration events: 0 — confirmed (no `BackofficeIdentity` folder under `src/Shared/Messages.Contracts`; zero `IMessageBus|PublishAsync|PublishMessage` matches in handlers). Matches S1.
- Domain events: 0 — confirmed by grep.
- Notable ADRs cited: ADR 0031 (admin RBAC), ADR 0032 (multi-issuer JWT).
- Prior event modeling: explicitly acknowledged as the first formal modeling artifact for this BC (no prior EM file exists).
- **CONTEXTS.md drift / surprising findings (forward-notes for S5):**
  1. **Role claim case mismatch** — Role claim is emitted in **kebab-case** (`JwtTokenGenerator.cs:59`) but authorization policies are registered against **PascalCase** role names (`Program.cs:65-83`); inconsistency also between `LoginResponse.User.Role` (kebab) and `RefreshTokenResponse.User.Role` plus user-management responses (PascalCase). Functional impact is unverified at dossier depth — may or may not break consumer-side policy evaluation. Worth direct verification in S5 / S4.
  2. Password hashing uses framework-default `PasswordHasher<BackofficeUser>` (PBKDF2-SHA256); prescribed direction elsewhere in the system is Argon2id (same pattern as Vendor Identity).

## Confirmation Checks (over the 5 dossiers that were written)

- **Banned evaluative language:** `grep -wEi 'good|bad|awkward|elegant|should|nicely|ugly|better|worse|properly|unfortunately'` over the 5 newly-written dossiers returns **no matches**. (Each sub-agent verified its own file before reporting back.)
- **Sibling-project / project-level successor framing:** `grep -wEi 'critterbids|crittercab'` returns **no matches**. The word "successor" is used only in the in-BC code-history sense explicitly permitted by the prompt.
- **Source-cited file paths for behavioral claims:** every behavioral claim in every written dossier source-cites a specific file (with line range where applicable). Structural lists cite the folder.
- **Routes-without-instantiator:** all such items discovered (Vendor Identity `VendorUserActivated`; Vendor Portal 3 outbound + 7 inbound + 1 realtime) are documented descriptively in the relevant dossier sections.
- **CONTEXTS.md drift:** every drift discovered is recorded descriptively in the relevant dossier (with code as authoritative) and forwarded for S5 in the per-BC notes above.
- **Source-enumeration scratch file:** `docs/extraction/_session-3-source-enumeration.md` was created at session open and **deleted** before this retrospective was committed.

## Cross-Reference Forward to S4

S3 work surfaced these non-obvious S4 workflow dependencies:

- **Vendor Identity → Vendor Portal flow** — Vendor Identity publishes 11 lifecycle events to dedicated queues; Vendor Portal subscribes to a subset for Team Management. The `VendorUserActivated` contract is published to a queue but never emitted — surfaces a gap in the user-acceptance subflow of vendor onboarding.
- **Vendor Portal change-request workflow** — 3 outbound contracts (`DataCorrectionRequested`, `DescriptionChangeRequested`, `ImageUploadRequested`) and 7 inbound decision contracts have no producer in any other BC. The change-request → review → decision workflow is therefore single-BC-internal at present, despite being modeled as cross-BC contracts. S4 should document the workflow as it exists rather than as the contracts imply.
- **Recall cascade** — Listings reacts to Product Catalog `ProductDiscontinued` with `IsRecall=true` by force-down across all non-terminal listings, then publishes `ListingsCascadeCompleted`. S4 should trace the cascade end-to-end through Listings → Marketplaces.
- **Marketplace adapter outbound** — Listings approval triggers Marketplaces submission via the adapter set; the adapter outcome flows back as `MarketplaceListingActivated` / `MarketplaceSubmissionRejected`. S4 should trace this submission round-trip.

## Cross-Reference Forward to S5

Combined with the S2 + S2b forward-notes, the S5 starting inventory now contains:

**From S2 + S2b (carried forward):**
- CONTEXTS.md Customer Experience integration table omits Inventory and Returns edges.
- CONTEXTS.md line 158 description of `AssignProductToVendor` as the "sole remaining document-store write path" is stale.
- CONTEXTS.md and `Fulfillment.Api/README.md` narrative diagrams still reference retired `ShipmentDispatched` / `ShipmentDeliveryFailed` events.
- Fulfillment integration contracts with no instantiator (`DeliveryAttemptFailed`, `GhostShipmentDetected`, `ItemPicked`).
- Returns `ReturnStatus` declared-but-unused values (`LabelGenerated`, `InTransit`).

**New from S3 (this session):**
- Listings recall cascade scope mismatch: CONTEXTS.md line 174 says "all Live and Paused listings"; code force-downs all non-terminal listings.
- Marketplaces document type undercount in CONTEXTS.md: `OrphanedEbayDraft` and `SweepOrphanedEbayDraftsHandler` are unmentioned; `ProductSummaryView` is misclassified as a projection in CONTEXTS.md (it is a Marten document mutated by handlers).
- Marketplaces communicates-with table at CONTEXTS.md lines 184-187 shows only inbound arrows for the Listings edge.
- Vendor Identity `VendorUserActivated` route-without-instantiator (publish-route declared, never emitted).
- Vendor Identity refresh tokens not persisted server-side (cookie-only).
- Vendor Identity `InvitationStatus.Expired` declared-but-unused (no expiry-sweep job).
- Vendor Identity password hashing uses PBKDF2 framework default; prior EM prescribed Argon2id.
- Vendor Identity CONTEXTS.md entry omits the 11 published events, the `VendorTenantStatus` machine, and ADR 0032.
- Vendor Portal document type overcount in S1: `NotificationPreferences` and `SavedDashboardView` are JSONB sub-properties of `VendorAccount`, not independent Marten collections.
- Vendor Portal CONTEXTS.md direction inversion: `InventoryAdjusted` / `LowStockDetected` / `StockReplenished` are listed as Vendor Portal publishes; they are inbound from Inventory.
- Vendor Portal CONTEXTS.md neighbour list omits Inventory and Product Catalog, lists Fulfillment / Payments / Pricing without supporting code.
- Vendor Portal change-request contracts: 3 outbound + 7 inbound + 1 realtime (`ForceLogout`) declared without producers / consumers in any other BC.
- Vendor Portal has zero named authorization policies; all gating is per-handler claim-check code.
- Backoffice Identity role-claim case mismatch: claim emitted kebab-case, policies registered PascalCase. Worth functional verification.
- Backoffice Identity password hashing uses PBKDF2 framework default; same Argon2id-prescribed-but-not-implemented pattern as Vendor Identity.

## Build State at Session Close

- Errors: 0 (delta from baseline: 0)
- Warnings: 456 incremental (delta from baseline: 0; warning composition unchanged — no code touched)
- Files changed: 8 — 5 BC dossiers, this retrospective, `docs/extraction/README.md` status table, `docs/planning/CURRENT-CYCLE.md`. No code changes; no project file changes.

## Verification Checklist

- [x] 5 of 9 channel/vendor/admin dossiers exist at full S2 depth in place.
- [ ] All 9 channel/vendor/admin dossiers exist at full S2 depth — **partial** (Backoffice / Pricing / Promotions / Correspondence deferred to S3b).
- [x] Listings, Pricing\* (\*pending), Promotions\* (\*pending), Correspondence\* (\*pending) follow Variant A; Marketplaces and Vendor Portal follow Variant D; Vendor Identity and Backoffice Identity follow Variant B; Backoffice\* (\*pending) follows Variant C-hybrid.
- [x] Every stream-ID claim in the 5 written dossiers is verified against source (Listings UUID v5 confirmed at `ListingStreamId.cs:21-66`; Vendor Identity / Backoffice Identity have no aggregate stream IDs — EF Core entities; Marketplaces / Vendor Portal use document store with explicit identity fields).
- [x] Every command count in the 5 written dossiers is direct-enumerated.
- [x] Every behavioral claim in every written dossier source-cites a specific file.
- [x] Routes-without-instantiator items discovered are documented descriptively (Vendor Identity `VendorUserActivated`; Vendor Portal 11 such items).
- [x] CONTEXTS.md drift discovered is recorded descriptively (code authoritative) and consolidated for S5 above.
- [x] No written dossier contains evaluative language (verified by grep).
- [x] No written dossier references CritterBids, CritterCab, or any project-level successor framing (verified by grep).
- [x] No written dossier frames itself as preparation for a downstream operation.
- [x] `docs/extraction/README.md` status table reflects 5-of-9 S3 partial state and 4-deferred-to-S3b state.
- [x] This retrospective committed.
- [x] `CURRENT-CYCLE.md` updated.
- [x] Build at session open recorded (0 errors, 456 warnings incremental).
- [x] S3 event / command / document counts per BC reconciled against S1 stub counts; deviations explained per BC above (Marketplaces: +2 doc types; Vendor Portal: -2 doc types).
- [x] Source-enumeration scratch file (`docs/extraction/_session-3-source-enumeration.md`) deleted before this retrospective commit.

## What Remains / Next Session

- **Complete S3 by promoting Backoffice, Pricing, Promotions, and Correspondence dossiers in S3b** before starting S4. Backoffice is the heaviest of the four (Variant C-hybrid; ~350-450 line target; requires `.razor` page enumeration and EM reconciliation across 3 prior artifacts). Correspondence is the lightest (Variant A; 1 aggregate, 4 events, 1 command). Pricing and Promotions are moderate (Variant A; both DCB; Promotions has two aggregates / two stream-ID strategies and a known cross-BC integration with Orders for `RecordPromotionRedemption`).
- **Then S4 — cross-BC workflow tracing.**

The next session is **S3b — Channels / vendor / admin closeout (4 deferred dossiers) → then S4**.
