# M33 + M34 Engineering Proposal — 2026-03-21

> **Prepared by:** PSA (Principal Software Architect) + UXE (UX Engineer)
> **Context:** Post-audit prioritization session. Owner has directed that these milestones be engineering-led, not product-driven. References the audit (`docs/audits/CODEBASE-AUDIT-2026-03-21.md`) and the post-audit Top 10 (`docs/audits/POST-AUDIT-DISCUSSION-2026-03-21.md`).
> **Premise:** M32 (M32.4 and its E2E fixture stabilization) must close before M33 begins. These proposals describe what happens *after* the Backoffice series completes.

---

## Joint PSA + UXE Positioning Statement

After reading each other's proposals independently and comparing priorities, PSA and UXE are in strong agreement on three foundational points that drive both milestones:

1. **INV-3 → F-8 → three Marten projections is a sequenced dependency chain, not three parallel work items.** The `LowStockAlerts` tile on the executive dashboard cannot be made honest by building the projections first. INV-3 must fix the event path; F-8 must instrument the test harness to verify it; then and only then do the projections have a truthful event stream to consume. Milestone boundary enforces this sequencing structurally.

2. **`CheckoutCompleted` with two incompatible payloads is a live 🔴 risk, not a naming preference.** A consumer binding the wrong record silently drops fields or throws a deserialization exception at runtime during checkout — the highest-value user action in the system. Fix cost: S (one rename, update consumers). PSA, QAE, and UXE all escalated this during the post-audit discussion.

3. **The Returns BC refactor and the UXE event renames for Returns must ship as one changeset.** The `ReturnEvents.cs` file is the target of both PSA's R-2 (explode to individual files) and UXE's identified synonym pairs (`ReturnDenied`/`ReturnRejected`, `ExchangeDenied`/`ExchangeRejected`, `InspectionMixed`). Splitting them creates a window where a test assertion can break invisibly. One PR.

**The one resolved discrepancy:** PSA proposed deferring the three missing Marten projections to M34; UXE proposed building them in M33 (immediately after INV-3 + F-8). The panel settled on UXE's framing: the projections are not a new feature — they are two stub zeros on a screen that executives use today. M33 fixes every broken feedback loop in the currently-shipped codebase. The projections are part of that definition. M34 completes the experience and aligns the vocabulary.

---

## M33: Code Correction + Broken Feedback Loop Repair

**Theme:** No live bug persists; no currently-broken screen remains broken; no structural violation survives that would corrupt future feature development.

**Duration estimate:** 10–13 sessions

**Primary owners:** PSA (structural refactors, correctness bugs, ADR), UXE (naming coordination, user-facing completeness), QAE (test fixture instrumentation, cascade timing)

---

### Exit Criteria

M33 closes when **all** of the following are true:

1. **Dashboard truthfulness:** A warehouse operator makes an inventory adjustment through Backoffice InventoryEdit; the `LowStockAlerts` KPI tile updates and/or an alert appears in the Operations Alerts feed — no restart, no manual Marten append required. An integration test using `ExecuteAndWaitAsync()` proves the full `AdjustInventory → LowStockDetected → AlertFeedView` chain is green in CI.

2. **`PendingReturns` is live:** The `ReturnMetricsView` projection is built; the `PendingReturns` tile shows the real count of in-flight returns; `// STUB` is not present in `GetDashboardSummary.cs`.

3. **`FulfillmentPipelineView` and `CorrespondenceMetricsView` are built:** Both projections are populated from real event streams; both are tested via the updated `BackofficeTestFixture`.

4. **Direct order lookup works:** A user with the CustomerService role navigates to `/orders/search`, types an order number, and reaches the Order Detail page in three interactions or fewer.

5. **Return queue exists:** `/returns` shows the active return queue; its count matches the `PendingReturns` dashboard tile.

6. **Returns BC is in vertical slice conformance:** Every command, handler, validator, query, and event in the Returns BC lives in its own file; the folder is renamed; all 11 commands have `AbstractValidator<T>`; `Returns.Api.IntegrationTests` uses `TrackedHttpCall()` for all HTTP-triggered cascade tests.

7. **Vendor Portal structural violations are resolved:** Every VP command + handler pair is a single slice file; all 7 commands have validators; `CatalogResponseHandlers.cs` is exploded to 7 files; `VendorHubMessages.cs` is split; feature-level `@ignore` tags are removed (scenario-level `@ignore` with comments accepted for unbound steps).

8. **Backoffice folder structure uses feature-named folders:** `Commands/` and `Queries/` folders replaced with capability-named counterparts in `Backoffice.Api`; `AcknowledgeAlert` lives in `AlertManagement/`; manual `session.SaveChangesAsync()` replaced with Wolverine auto-transaction.

9. **ADR 003x is published** declaring the canonical validator placement convention for all BCs.

10. **`CheckoutCompleted` collision is resolved.** One `CheckoutCompleted` record in `Messages.Contracts` with one payload.

11. **Quick wins batch is shipped:** INV-1/INV-2 consolidated; PR-1 merged; CO-1 split; PAY-1/FUL-1/ORD-1 renamed; F-9 raw string literal fixed.

12. **Build: 0 errors; all previously-passing tests pass; no net new warnings.**

---

### In Scope

#### Phase 1 — Correctness + Regression Foundation (Sessions 1–2)
*Must ship first. Every downstream item in M33 depends on this sequence being complete.*

| Item | Finding(s) | Effort | Notes |
|---|---|---|---|
| Fix `AdjustInventoryEndpoint` to dispatch via `IMessageBus.InvokeAsync(new AdjustInventory(...))` | INV-3 | S | Single call site; verify `InventoryAdjusted` and `LowStockDetected` now publish |
| Add `ExecuteAndWaitAsync()` + `TrackedHttpCall()` to `BackofficeTestFixture`; refactor `EventDrivenProjectionTests` to dispatch through Wolverine (not direct Marten append) | F-8 | S | Must ship in the same session as INV-3; this is the regression test for the fix |
| Write canonical ADR for validator placement (top-level `AbstractValidator<T>`, same file as command + handler; not nested, not extracted to separate file, not bulk file) | XC-1 | S | Policy must exist before any validator normalization work begins |
| Rename `CheckoutCompleted` in `Messages.Contracts/Shopping` to `CartCheckoutCompleted`; rename Orders-internal `CheckoutCompleted` to `OrderCreated`; update all consumers | Top 10 #4 | S | ≤1 hour per rename; highest severity/effort ratio in the entire audit |

---

#### Phase 2 — Quick Wins Batch (Sessions 2–3, parallelizable)
*All independent of each other; batch into a single mechanical PR.*

| Item | Finding(s) | Effort | Notes |
|---|---|---|---|
| Consolidate `AdjustInventory*` 4-file shatter → `AdjustInventory.cs`; repeat for `ReceiveInboundStock`; rename `Commands/` and `Queries/` folders in Inventory.Api to feature-named folders | INV-1, INV-2 | S | Follow XC-1 ADR pattern for validator placement |
| Merge Pricing three-way split: `SetInitialPrice.cs` + handler + validator → one file; repeat for `ChangePrice` | PR-1 | S | Two commands; 30 minutes |
| Explode `MessageEvents.cs` into 4 individual event files | CO-1 | S | File split only; no logic changes |
| Move isolated `Queries/` folder files in Payments.Api, Fulfillment.Api, Orders.Api into feature-named siblings | PAY-1, FUL-1, ORD-1 | S | Single-file moves |
| Fix 3 raw string collection literals → `[Collection(IntegrationTestCollection.Name)]` in Orders | F-9 | S | Typo-prevention |

---

#### Phase 3 — Returns BC Full Structural Refactor (Sessions 3–6)
*Treated as a single atomic changeset. UXE event renames MUST ship in the same PR as R-2.*

| Item | Finding(s) | Effort | Notes |
|---|---|---|---|
| Explode `ReturnCommandHandlers.cs` (387 lines, 5 handlers) → 5 files, one per handler | R-4 | M | Start here — highest merge-conflict risk file in the codebase |
| Explode `ReturnCommands.cs` (11 commands) → 11 files; each file: command record + colocated validator + handler | R-1 | M | |
| Explode `ReturnEvents.cs` → individual event files; rename in the same changeset: `ReturnRejected` → `ReturnFailedInspection`, `ExchangeRejected` → `ExchangeFailedInspection`, `InspectionMixed` → `InspectionPartiallyPassed`, `ReturnRequested` → `ReturnInitiated` | R-2 + UXE renames | M | **One PR.** See QAE cross-review: test assertion clarity requires rename and refactor to be atomic |
| Dissolve `ReturnValidators.cs` — move each validator into the matching command file following XC-1 ADR | R-3 | S | Follows naturally from R-1 |
| Explode `ReturnQueries.cs` → `GetReturn.cs` + `GetReturnsForOrder.cs` | R-5 | S | Query record + response DTO + handler in each file |
| Add `AbstractValidator<T>` for `ApproveReturn`, `ReceiveReturn`, `StartInspection`, `ExpireReturn` (the four high-risk un-validated commands) | R-6 | S | Following XC-1 ADR pattern |
| Add `TrackedHttpCall()` to `Returns.Api.IntegrationTests/TestFixture.cs`; migrate approve-return → `RefundRequested` cascade tests to use `TrackedHttpCall()` | F-7 | S | Ship as part of the Returns PR |
| Rename `Returns/Returns/Returns/` → `Returns/Returns/ReturnProcessing/` | R-7 | S | Last step; after all files are placed |

---

#### Phase 4 — Vendor Portal Structural Refactor + E2E Phase A (Sessions 6–8)

| Item | Finding(s) | Effort | Notes |
|---|---|---|---|
| Remove feature-level `@ignore` tags from all Vendor Portal E2E files; add scenario-level `@ignore` with blocking-reason comments for unbound steps; document 3 unbound feature files as GitHub Issues | F-2 Phase A | S | Phase A only. Step definition work is M34 (Phase B). Do this first so the structural refactor ships with some CI signal |
| Flatten `ChangeRequests/Commands/` + `ChangeRequests/Handlers/` → single slice files (`SubmitChangeRequest.cs` etc.) | VP-1 | M | |
| Flatten `VendorAccount/Commands/` + `VendorAccount/Handlers/` → single slice files | VP-2 | M | |
| Explode `CatalogResponseHandlers.cs` (7 handlers, 189 lines) → 7 files; coordinate `MoreInfoRequestedForChangeRequest` → `AdditionalInfoRequested` rename in the same changeset | VP-4 + UXE ❌ #2 | S | VP-4 refactor and the rename must be one changeset — the handler file is the primary consumer |
| Split `VendorHubMessages.cs` → one file per message record | VP-5 | S | |
| Flatten `Analytics/Handlers/` → place handlers directly in `Analytics/` | VP-3 | S | |
| Add `AbstractValidator<T>` to all 7 VP commands following XC-1 ADR | VP-6 | M | `SubmitChangeRequest` is highest-risk; user-supplied payload with no guard |
| Rename `DeliveryFailed` → `MessageDeliveryFailed` in Correspondence BC internal events; update all internal handlers and the `CorrespondenceMetricsView` projection definition | UXE ❌ #1 | S | Sets the canonical internal name that M34's contract alignment follows |

---

#### Phase 5 — Backoffice Folder Restructure + Transaction Fix (Sessions 8–9)

| Item | Finding(s) | Effort | Notes |
|---|---|---|---|
| Move `Backoffice/Commands/AcknowledgeAlert.cs` → `Backoffice/AlertManagement/AcknowledgeAlert.cs`; remove manual `session.SaveChangesAsync()`; replace `throw InvalidOperationException` with `Before()` guard returning `ProblemDetails` | XC-3 + BO-2 | S | One file; folder move bundled with correctness fix |
| Restructure `Backoffice.Api/Commands/` + `Backoffice.Api/Queries/` → feature-named folders (`AlertManagement/`, `OrderManagement/`, `CustomerManagement/`, etc.) | BO-1 | M | File moves; update using directives |
| Colocate `Backoffice/Projections/` files with the capability they serve (`AdminDailyMetrics` → `DashboardMetrics/`, `AlertFeedView` → `AlertManagement/`) | BO-3 | S | |

---

#### Phase 6 — Backoffice Completion: Missing Projections + Missing Pages (Sessions 9–12)
*Begins only after Phase 1 (INV-3 + F-8) is merged and green in CI.*

| Item | Finding(s) | Effort | Notes |
|---|---|---|---|
| Build `ReturnMetricsView` projection consuming `ReturnInitiated` (renamed), `ReturnApproved`, `ReturnDenied`, `ReturnReceived`, `InspectionStarted`, `InspectionPassed`, `InspectionFailed`, `InspectionPartiallyPassed`, `ReturnCompleted`, `ReturnExpired`; remove `PendingReturns` stub; wire to `BackofficeHub` for real-time push | Top 10 #2 | M | Projection data model: active count, pipeline stage breakdown, avg time to resolution, mixed-inspection count |
| Build `CorrespondenceMetricsView` projection consuming `MessageQueued`, `MessageDelivered`, `MessageDeliveryFailed` (renamed), `MessageSkipped`; wire delivery success rate KPI | Top 10 #2 | M | |
| Build `FulfillmentPipelineView` projection consuming `StockReserved`, `ReservationCommitted`, `WarehouseAssigned`, `ShipmentDispatched`, `ShipmentDelivered`, `ShipmentDeliveryFailed`; implement stage-distribution breakdown; longest-unresolved-assignment signal | Top 10 #2 + ADR 0036 TODO | L | Largest of the three; ADR 0036 has the data model sketch |
| Build Order Search page at `/orders/search`: search by order number (primary), customer email, customer name; results table with status; single-click navigation to Order Detail; role-gated to CustomerService / OperationsManager / SystemAdmin | Top 10 #3 | M | BFF proxy pattern (Backoffice.Api → Orders.Api) already established |
| Build Return Management page at `/returns`: active return queue defaulting to Pending stage; filterable by stage; count badge matching `PendingReturns` tile; single-click to return detail; role-gated to CustomerService / OperationsManager / SystemAdmin | Top 10 #3 | M | Depends on `ReturnMetricsView` projection above |
| Create `Backoffice.Web.UnitTests` bUnit project; initial coverage: new Order Search page, new Return Management page, role-gated NavMenu, Dashboard KPI tile rendering | F-3 | M | UXE argument: establish bUnit alongside new pages, not after. Template: `Storefront.Web.UnitTests` |

---

### Sequencing Diagram

```
[Phase 1] INV-3 fix ──→ F-8 fixture ──→ [Phase 6] Three projections + pages
              └───────────────────────────────────────────────────────────────┘
                           MUST be sequential. No Phase 6 before Phase 1.

[Phase 1] XC-1 ADR ──→ R-6 validators ──→ VP-6 validators
                   └──→ (all future validator normalization has a reference)

[Phase 3] R-4/R-1/R-2 (UXE renames with R-2) / R-5 ──→ R-3 ──→ R-6 ──→ F-7 ──→ R-7
                       └── R-2 and UXE Returns renames: ONE PR

[Phase 4] VP E2E @ignore lift (F-2 Phase A) ──→ VP structural refactor (VP-1/2/3/4/5/6)
          └── VP-4 refactor and MoreInfoRequested rename: ONE changeset

[Phase 5] XC-3 + BO-2 ──→ BO-1 ──→ BO-3     [independent; parallelizable with Phase 4]

[Phase 2] Quick wins: independent of all phases except XC-1 ADR for validator decisions
```

**Can be parallelized:** Phase 2 quick wins are independent of all phases. Phase 5 (Backoffice folder work) is independent of Phases 3 and 4 — a second contributor can run it concurrently.

**Cannot be parallelized:** INV-3 → F-8 → Phase 6 must be sequential in that order. R-2 and UXE Returns renames must be one PR. VP-4 refactor and `MoreInfoRequestedForChangeRequest` rename must be one changeset.

---

## M34: Architecture Completion + Vocabulary Alignment

**Theme:** Complete every experience the architecture already supports but users cannot yet access; align the event vocabulary across BC boundaries so the codebase reads as one coherent system.

**Duration estimate:** 8–12 sessions

**Primary owners:** UXE (event naming alignment, Customer Experience E2E, Vendor Portal E2E Phase B, untapped value), QAE (bUnit for Vendor Portal, Promotions test infrastructure, Correspondence fixture), PSA (SH-1 Shopping ADR, persisted event rename migration research, `.Api` naming normalization, `Correspondence` contract alignment)

**Prerequisite:** M33 must be fully closed. Track A (Backoffice projections and pages) begins the moment M33 closes — do not start M34 Track A before M33 exit criteria are all green.

---

### Exit Criteria

M34 closes when **all** of the following are true:

1. **Shopping dual-handler pattern resolved:** A single architectural decision, documented in an ADR, governs how HTTP endpoints in the Shopping BC interact with domain handlers. No divergent patterns remain.

2. **Transient command-masquerade events renamed:** All non-persisted integration messages using "Requested" suffix that function as commands are renamed following the alignment convention. An ADR documents the persisted event migration strategy for the ones that cannot be renamed without a Marten stream alias migration.

3. **Backoffice / Vendor Portal bUnit coverage established:** Both `Backoffice.Web.UnitTests` and `VendorPortal.Web.UnitTests` bUnit projects exist with meaningful initial coverage (role-gated UI, primary workflow components).

4. **Vendor Portal E2E Phase B complete:** Step definitions exist for all 3 previously-unbound feature files; all previously-bound Vendor Portal E2E scenarios are running in CI (not `@ignore`).

5. **Customer Experience E2E gaps filled:** `cart-real-time-updates.feature` and `product-browsing.feature` have step definitions; `order-history.feature` scenarios are running.

6. **Promotions test infrastructure is correct:** `Promotions.UnitTests` project exists; `ICollectionFixture` pattern replaces `IClassFixture` (1 PostgreSQL container per test run, not 4).

7. **Vocabulary alignment shipped:** `PriceChanged`/`PriceUpdated`, `CheckoutStarted`/`CheckoutReceived`, Correspondence contract alignment, VP transient command renames — all resolved.

8. **`Correspondence.Api.IntegrationTests` `TrackedHttpCall()`** added.

9. **`Returns → Correspondence` integration wired** (untapped value item 1) OR explicitly deferred to M35 with a documented ADR.

10. **Build: 0 errors; all previously-passing tests pass.**

---

### In Scope

#### Track A — Untapped Architectural Value: Live Order Tracking + Correspondence Integration (Sessions 1–3)
*These represent capabilities the architecture fully supports that users cannot yet access.*

| Item | Finding(s) | Effort | Notes |
|---|---|---|---|
| Wire `ReturnMetricsView` updates → Backoffice SignalR hub push (`PendingReturnIncremented`/`PendingReturnDecremented`); CS operators see live count without refresh | Untapped value | S | Hub infrastructure exists; projection is live after M33 |
| Wire `Returns → Correspondence`: when `ReturnApproved` or `ReturnDenied` fires, emit a `CorrespondenceQueued` message; template: `OrderPlaced → CorrespondenceQueued` handler pattern | Untapped value | M | Customers currently receive zero automated notification of return decisions |
| `AdminDailyMetrics` projection: add `ReturnedRevenue` field sourced from `ReturnCompleted` events; rounds out the executive dashboard's financial picture | Untapped value | S | Now that `ReturnMetricsView` is live (M33), this is a one-field addition |
| Wire `LowStockDetected` → Correspondence notification for warehouse managers (email on low-stock threshold breach) | Untapped value | M | `LowStockDetected` events are now correctly published (after M33's INV-3 fix); the Correspondence handler pattern is established |
| **Storefront: Customer-visible order status timeline** — `OrderTrackingView` Marten projection consuming `ShipmentDispatched`, `ShipmentDelivered`, `ShipmentDeliveryFailed`; Storefront.Api endpoint; order detail timeline UI showing past-tense fulfillment events | Untapped value | L | Highest-value untapped Storefront capability: every order generates fulfillment events; zero of them are visible to the customer today |

---

#### Track B — Test Infrastructure Completion (Sessions 2–6, partially parallelizable)

| Item | Finding(s) | Effort | Notes |
|---|---|---|---|
| Upgrade `BackofficeTestFixture` existing tests: refactor `EventDrivenProjectionTests` to dispatch through Wolverine bus; verify full RabbitMQ → handler → projection chain for all three new projections from M33 | F-8 (completion) | S | Uses the helpers added in M33's Phase 1 |
| Create `Promotions.UnitTests` project (template: `Returns.UnitTests`); unit tests for coupon validation rules, discount calculation, usage limits, optimistic concurrency guard | F-1 | M | |
| Fix Promotions `IClassFixture` → `ICollectionFixture`; reduces CI from 4 PostgreSQL containers → 1 | F-6 | S | Bundle with F-1 in one session — zero cost alongside new project creation |
| Create `VendorPortal.Web.UnitTests` bUnit project; initial coverage: `LoginPage`, `ChangeRequestForm` (validation feedback, success/error states), `VendorDashboard`, `VendorHubConnector` | F-4 | M | Template: `Storefront.Web.UnitTests`; depends on VP structural refactor from M33 |
| Vendor Portal E2E Phase B: step definitions for `product-management.feature`, `vendor-analytics-dashboard.feature`, `vendor-hub-connection.feature`; POMs for missing pages; all previously-bound `@ignore` scenarios running | F-2 Phase B | L | M per feature file; depends on M33 VP structural refactor |
| Add `TrackedHttpCall()` to `Correspondence.Api.IntegrationTests/TestFixture.cs` | F-10 | S | Trivial; batch with any Correspondence session |

---

#### Track C — Vocabulary Alignment (Sessions 4–7)
*Ordered by blast radius: narrow-scope renames first; the Marten migration research item last.*

| Item | Finding(s) | Effort | Notes |
|---|---|---|---|
| Shopping dual-handler pattern (SH-1): establish one canonical pattern, written as ADR; HTTP endpoint dispatches to domain handler via `IMessageBus` OR owns the aggregate workflow exclusively | SH-1 | M | This is primarily an architectural decision + ADR; implementation follows the decision |
| Rename VP transient command-masquerade events: `DataCorrectionRequested` → `DataCorrectionSubmitted`, `DescriptionChangeRequested` → `DescriptionChangeSubmitted`, `ImageUploadRequested` → `ImageUploadSubmitted` | UXE VP events | S | Integration messages, not persisted Marten events; update `Messages.Contracts` + all consumers |
| Rename `CheckoutStarted` (Orders-internal) → `CheckoutReceived`; resolves `CheckoutStarted`/`CheckoutInitiated` verb conflict without requiring Shopping changes | UXE ⚠️ | S | Orders aggregate + internal projections/handlers only |
| Align Pricing vocabulary: `PriceUpdated` (contract) → `PriceChanged`; update Shopping `CurrentPriceView` consumer, Storefront.Api pricing display | UXE ⚠️ | S | Integration message; not a persisted Marten domain event. Coordinate with Shopping BC consumer |
| Align Correspondence contract vocabulary to internal names: `CorrespondenceQueued` → `MessageQueued`, `CorrespondenceDelivered` → `MessageDelivered`, `CorrespondenceFailed` → `MessageDeliveryFailed` (set by M33); sets the contract after the internal anchor is established | UXE ⚠️ | S | Sequencing dependency: M33's `DeliveryFailed` rename must be live before this contract alignment ships |
| Normalize `.Api` folder naming across 6 deviating BCs (XC-2): single-file moves; one PR | XC-2 | M | Batch pass; no logic changes |
| Investigation sprint: query `mt_events` for `mt_dotnet_type` containing `RefundRequested`, `FulfillmentRequested`, `ReservationCommitRequested`, `ReservationReleaseRequested`; determine whether each is persisted in Marten aggregate streams; publish ADR documenting Marten stream alias migration approach | UXE #19 + PSA cross-review | S | If rows exist across persisted Order streams, rename requires `AddEventTypeAlias` migration. Do not rename until investigation determines scope. |
| Rename non-persisted command-masquerade events (those confirmed transient by investigation above): `FulfillmentRequested` → `FulfillOrderRequested`, `ReservationCommitRequested` → `CommitInventoryReservation`, `ReservationReleaseRequested` → `ReleaseInventoryReservation` | UXE #19 | S–M each | Contingent on investigation confirming they are transient. Coordinate Orders + Fulfillment + Inventory BC consumers. |

---

#### Track D — Customer Experience E2E Completion (Sessions 7–8)

| Item | Finding(s) | Effort | Notes |
|---|---|---|---|
| `cart-real-time-updates.feature` step definitions: cart badge SignalR update on add-to-cart, price freeze at cart entry, coupon reflection live | F-5 | M | Highest-value Customer Experience E2E gap; SignalR regressions are invisible to unit/integration layers |
| `product-browsing.feature` step definitions: search result display, category navigation, product detail page | F-5 | M | Highest-traffic Storefront page with no E2E coverage |
| Lift `@wip @ignore` from `order-history.feature` scenarios; implement step definitions | F-5 | S | 4 scenarios already written; step definitions only |

---

### Untapped Value Items (M34)

These represent architectural capabilities that are fully designed and partially built today, whose value users cannot yet access.

**1. Customer-visible order status timeline (Storefront BC)**
Every order generates `ShipmentDispatched`, `ShipmentDelivered`, `ShipmentDeliveryFailed` events from the Fulfillment BC. Zero of those events currently produce anything visible to the customer on the Storefront. The Correspondence BC sends email notifications — but if the email fails (`CorrespondenceMetricsView`, live after M33) or the customer ignores it, there is no in-app fallback. The architecture fully supports an `OrderTrackingView` Marten projection → Storefront.Api endpoint → order detail timeline UI.

**2. Customer-visible coupon state (Promotions BC)**
`CouponIssued`, `CouponRedeemed`, `CouponExpired`, `CouponRevoked` events are emitted per customer. No Storefront page shows a customer "you have an active coupon." If a coupon was issued as a return apology, the customer cannot discover it without an out-of-band notification. A `CustomerCouponView` projection (per customer, active codes) → Storefront.Api endpoint → "My Offers" section is a natural, low-friction addition.

**3. Returns → Correspondence integration**
`ReturnApproved` and `ReturnDenied` fire but produce no automated customer email. The Correspondence BC already handles order and payment lifecycle events using the same integration pattern. A `ReturnApproved → CorrespondenceQueued` handler is a one-session implementation.

**4. Vendor change request visibility in Backoffice**
The full change request lifecycle flows through `Messages.Contracts` events. Backoffice operators have no queue view for pending change requests. The Backoffice.Api proxy pattern and client infrastructure fully support adding a change request queue page. This is a day-one operational visibility gap the architecture has been ready to close since the Vendor Portal shipped.

**5. `AdminDailyMetrics` net revenue accuracy**
Returns affect net revenue. After M33 ships `ReturnMetricsView`, the `AdminDailyMetrics` daily snapshot can be updated with a `ReturnedRevenue` field sourced from `ReturnCompleted` events. Effort: S (one projection field addition).

---

### ADRs Accompanying M34

- **Shopping canonical handler ADR (SH-1):** Documents the resolution: HTTP endpoints dispatch to domain handlers via `IMessageBus` OR own the aggregate workflow exclusively. Every future Shopping feature follows this decision.
- **Persisted event rename migration ADR:** Documents the Marten `AddEventTypeAlias` approach for `RefundRequested` and any others confirmed as persisted. Makes the migration path explicit so future sprints can execute it confidently without re-researching.
- **Returns → Correspondence integration ADR:** Documents triggering conditions and message contract if the untapped value item is pursued; keeps Correspondence BC-boundary-clean.

---

## Summary View

| Milestone | Theme | Sessions | Key Outcome |
|---|---|---|---|
| **M33** | Code Correction + Broken Feedback Loop Repair | 10–13 | No live bugs; no broken screens; every BC in vertical slice conformance; test timing guarantees on all cascade paths; dashboard tells the truth |
| **M34** | Architecture Completion + Vocabulary Alignment | 8–12 | Dashboard has live real-time updates; CS workflows complete; test pyramid filled at every layer; event vocabulary coherent across BC boundaries; untapped integration value realized |

**The through-line:** M33 fixes the foundation so nothing M34 builds on a lie. The sequenced dependency chain — INV-3 → F-8 → three projections — is the structural spine of M33. M34 assumes a codebase where every currently-broken feedback loop has been repaired and every BC follows the same structural conventions. If M33 closes cleanly, M34 is expansion and completion work on a stable base. If M33 is shortcut, M34 inherits compounding risk on every track.

---

*Prepared by PSA + UXE on 2026-03-21.*
*References: `docs/audits/CODEBASE-AUDIT-2026-03-21.md` · `docs/audits/POST-AUDIT-DISCUSSION-2026-03-21.md`*
