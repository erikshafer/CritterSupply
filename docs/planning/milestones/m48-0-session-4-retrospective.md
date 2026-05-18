# M48.0 Session 4 Retrospective — Cross-BC Workflow Tracing

**Date:** 2026-05-18
**Milestone:** M48.0 — CritterSupply Business Architecture Extraction
**Session:** Session 4 — Cross-BC workflow tracing

## Outcome (executive summary)

S4 closed cleanly: **15 cross-BC workflow traces landed** under `docs/extraction/workflows/`, covering every cross-BC business workflow surfaced across the 18 S2-full BC dossiers. Every workflow follows the prompt's standard template (Status / Type / Initiating actor / BCs involved / Most recent material milestone — then Purpose, Actors and triggers, Trace, Projections, Compensation, Variants, BCs and roles, Tests, ADRs, Declared vs. implemented, Source citations). All claims source-cite dossier sections under `docs/extraction/bcs/`; descriptive register only (no should/good/bad). No fallback to S4b was needed.

The traces consolidate the workflow-level findings forwarded from S3 + S3b and add a new layer of cross-BC drift / declared-not-wired observations that will feed S5's structural-observations pass — most prominently the vendor change-request choreography (10 inbound + outbound routes that are not paired with producers / consumers anywhere in `src/`), the vendor activation flow (`VendorUserActivated` declared but no producer), three Backoffice SignalR types declared with no instantiator, `MessageSkipped` declared with no production emitter, and the 4 mis-spelled customer-service auth policy strings on Backoffice that operatively block `CustomerService`-role access.

## Baseline

- Build at session open: no code changed in S3b close (S3b touched only `docs/`).
- Build at session close: identical — S4 touched only `docs/`.
- Files changed: 19 — 15 new workflow files, `docs/extraction/README.md` (status table + new Workflows index), this retrospective, `docs/planning/CURRENT-CYCLE.md`.

## Workflows landed

| # | Workflow | File | Type | Initiating actor | Most recent material milestone |
|---|----------|------|------|------------------|--------------------------------|
| 1 | Cart to checkout | `cart-to-checkout.md` | Choreography → Orchestration handoff | Customer | M45.x — Storefront BFF + Orders saga maturity |
| 2 | Coupon + discount application | `coupon-and-discount-application.md` | Query-only (synchronous HTTP) | Customer | M42.x — Promotions Phase-1 |
| 3 | Coupon redemption recording | `coupon-redemption-recording.md` | Choreography + DCB write | System (Orders → Promotions) | M42.x — DCB on Promotions |
| 4 | Recall cascade | `recall-cascade.md` | Choreography (fan-out) | Operator (Catalog Manager) | M3x — Product Catalog recall semantics |
| 5 | Marketplace listing submission | `marketplace-listing-submission.md` | Choreography (sync HTTP to adapters) | Vendor / scheduler | M37.0 — Walmart / eBay adapters |
| 6 | Order saga | `order-saga.md` | Orchestration (Orders) | Customer (checkout) | M45.1 — saga hardening |
| 7 | Standard return + refund | `standard-return-refund.md` | Orchestration (Returns) | Customer | M44.x — return RMA lifecycle |
| 8 | Cross-product exchange | `cross-product-exchange.md` | Orchestration (Returns) + Choreography (Payments / Inventory) | Customer | M47.0 (5 slices) — cross-product exchange end-to-end |
| 9 | Vendor onboarding | `vendor-onboarding.md` | Choreography | Operator (Backoffice admin) | M44.0 — Vendor Portal hardening |
| 10 | Vendor change request | `vendor-change-request.md` | Choreography (declared-not-wired cross-BC) | Vendor user (Admin / CatalogManager) | M44.0 — Vendor Portal hardening |
| 11 | Backoffice fan-in dashboards | `backoffice-fan-in-dashboards.md` | Read-only fan-in (sub → projection → SignalR) | System (upstream BCs) | M46.0 — Operations health dashboard |
| 12 | Backoffice customer service | `backoffice-customer-service.md` | Synchronous BFF composition | Operator (Customer Service) | M43–M45 — composition view rollouts |
| 13 | Backoffice operations health | `backoffice-operations-health.md` | Cross-schema read aggregation | Operator (Operations Manager) | M46.0/D — Dead-letter aggregator |
| 14 | Transactional communication | `transactional-communication.md` | Choreography (Correspondence) | System (upstream BC events) | M31.0 — Phase 2 (7 events + SMS infra) |
| 15 | Storefront real-time updates | `storefront-real-time-updates.md` | SignalR fan-out | System (upstream BC events) | M47.0 / Slice 5 — in-session timeline |

## Cross-cutting observations surfaced for S5

The workflow pass made several patterns visible that were not obvious at the per-BC dossier level:

1. **Declared-not-wired choreographies.** The vendor change-request workflow is the largest single instance: 10 cross-BC routes (3 outbound submission + 7 inbound decision contracts) are subscribed / produced by Vendor Portal but the counter-side does not exist in `src/`. The submission contracts round-trip through the local Wolverine bus on the Vendor Portal API host only; the 7 inbound decision queues are silent in production. This is a documented `bcs/vendor-portal.md#routes-without-instantiator` finding restated at the workflow level.

2. **One-sided vendor lifecycle event.** `VendorUserActivated` has a Vendor Identity contract and a Vendor Portal subscriber but no producer in the current code — the user-acceptance flow that would publish it is not yet implemented. The vendor onboarding workflow can complete operationally because login → JWT issuance happens via `POST /api/vendor-identity/auth/login` regardless, but the activation event itself never fires.

3. **Declared SignalR types with no instantiator.** Three of Backoffice's 5 `IBackofficeWebSocketMessage` types (`ActiveOrderIncremented`, `ActiveOrderDecremented`, `PendingReturnIncremented`) are wired into the hub routing but have no producer in code. The dashboards workflow operates on the 2 wired types only.

4. **Backoffice auth-policy regressions.** Four customer-service endpoints carry `[Authorize(Policy="CustomerService")]` policy strings that are mis-spelled (the dossier flags them "broken"). Operators with the `CustomerService` role cannot access `GET /api/backoffice/customers/{customerId}`, `GET /api/backoffice/customers/{customerId}/correspondence`, `GET /api/backoffice/customers`, or `GET /api/backoffice/orders/search` until the policy strings are corrected. The operations-health workflow is unaffected (`OperationsManager` is correctly registered).

5. **CONTEXTS.md drift visible per-workflow.** Several workflows surfaced CONTEXTS.md directional or membership drift: Vendor Portal listed as publisher of Inventory events when it is in fact subscriber; Customer Experience's Inventory + Returns subscriptions absent from the integration table; Backoffice's Payments subscription absent from the composition table; Backoffice's Fulfillment relationship documented as "queries" when it is in fact subscription-driven with an unused typed client; Backoffice's Pricing typed client registered with no consumer (the WASM `PriceEdit.razor` page calls `/api/pricing/...` directly against the wrong base URL).

6. **Marten DCB usage is BC-specific.** Already captured at the dossier level: Promotions uses DCB end-to-end (tag types, boundary query, retry policy); Pricing does not (plain `Guid` UUID v5 streams). The coupon redemption workflow is the only place in S4 where DCB is operationally relevant.

7. **Deferred persistence (storefront).** The M47.0 / Slice 5 OrderConfirmation timeline is in-memory only; persistence across page refreshes is deferred to the (currently-active) M48.0 milestone. The storefront-real-time-updates workflow captures this as an explicit declared-vs-implemented gap.

8. **Choreography vs orchestration mix is consistent.** Orders + Returns are the only saga-orchestrators; everywhere else (Inventory, Payments, Fulfillment, Correspondence, Customer Experience, Backoffice, Vendor Identity, Vendor Portal) operates by choreography. The BFF workflows (storefront real-time, backoffice customer-service, backoffice fan-in) are subscriber + composer surfaces with no orchestration state of their own.

## Process notes

- **Template fit.** The prompt's standard template scaled cleanly across all 15 workflows including the BFF-style fan-ins (no Trace ambiguity) and the declared-not-wired vendor change-request (the template explicitly accommodates this via the Declared vs. implemented section and the Status field).
- **Scratch inventory.** A `_session-4-workflow-inventory.md` file was used during discovery to consolidate cross-BC edge inventory across the 18 dossiers and was deleted before this retrospective commit per the prompt's housekeeping note.
- **No code changes.** S4 was a documentation-synthesis session; no `src/` or `tests/` files were touched.
- **No fallback to S4b.** The session completed all 15 workflows within the budget. The prompt's pre-identified S4b deferral candidates (`backoffice-operations-health`, `transactional-communication`, `storefront-real-time-updates`) were each landed in S4.

## Next session

**S5 — Structural observations across the system.** Inputs are now in place:

- 18 S2-full BC dossiers under `docs/extraction/bcs/`.
- 15 cross-BC workflow traces under `docs/extraction/workflows/`.
- The accumulated drift / declared-not-wired / declared-but-unemitted register: 12 CONTEXTS.md drift items + 13 routes-without-instantiator items + 9 declared-but-unemitted events from S3/S3b, plus the 8-item workflow-level cross-cutting list above from S4.

S5 should aggregate, classify, and where appropriate prioritise these for the synthesis brief that S6 will produce.
