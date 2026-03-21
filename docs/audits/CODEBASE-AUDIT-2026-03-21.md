# CritterSupply Codebase Audit — 2026-03-21

> **Participants:** PSA (Principal Software Architect) · QAE (Quality Assurance Engineer) · UXE (UX Engineer)
> **Scope:** All bounded contexts under `src/` and all test projects under `tests/`
> **Date:** 2026-03-21

---

## PSA Findings

> **Audit scope:** All bounded contexts under `src/` · **Reference standards:** `vertical-slice-organization.md`, `wolverine-message-handlers.md`, `marten-event-sourcing.md`, `event-sourcing-projections.md`

---

### Returns BC

**Severity: 🔴 CRITICAL**

The Returns BC is explicitly called out in `vertical-slice-organization.md` as the canonical anti-pattern. Every file in `src/Returns/Returns/Returns/` violates the standard.

#### Finding R-1 — `ReturnCommands.cs`: 11 commands in one file

**Path:** `src/Returns/Returns/Returns/ReturnCommands.cs`

**Issue:** Eleven separate command records (`RequestReturnExchangeRequest`, `RequestReturn`, `RequestReturnItem`, `ApproveReturn`, `DenyReturn`, `ReceiveReturn`, `StartInspection`, `SubmitInspection`, `ExpireReturn`, `ApproveExchange`, `DenyExchange`, `ShipReplacementItem`) are defined in a single file.

**Correct pattern:** One file per operation. `ApproveReturn.cs` contains only the `ApproveReturn` record (and its colocated validator and handler).

**Risk:** High. Navigability cost compounds with every sprint. Merge conflict surface grows with every new command.

---

#### Finding R-2 — `ReturnEvents.cs`: 17 event records in one file

**Path:** `src/Returns/Returns/Returns/ReturnEvents.cs`

**Issue:** Two value objects and 15 domain events are packed into a single 3 KB file.

**Correct pattern:** Each event in its own file named identically to the record.

**Risk:** Medium-high. When an event shape needs to change, the developer must open a 140-line omnibus file and reason about 14 neighbouring types.

---

#### Finding R-3 — `ReturnValidators.cs`: 4 validators separated from their commands

**Path:** `src/Returns/Returns/Returns/ReturnValidators.cs`

**Issue:** `RequestReturnValidator`, `RequestReturnItemValidator`, `SubmitInspectionValidator`, and `DenyReturnValidator` are extracted into a standalone file. The full picture of any single operation is spread across three files.

**Correct pattern:** The validator for `RequestReturn` belongs in the same file as the `RequestReturn` command and its handler.

**Risk:** High. Direct violation of vertical slice discipline.

---

#### Finding R-4 — `ReturnCommandHandlers.cs`: 5 handlers in 387 lines

**Path:** `src/Returns/Returns/Returns/ReturnCommandHandlers.cs`

**Issue:** `ApproveReturnHandler`, `DenyReturnHandler`, `ReceiveReturnHandler`, `SubmitInspectionHandler`, and `ExpireReturnHandler` are all in a single 387-line file. `SubmitInspectionHandler` alone runs 200 lines.

**Correct pattern:** Each handler in its own file named after the operation it handles.

**Risk:** Critical. This is a merge conflict magnet and is untestable in isolation.

---

#### Finding R-5 — `ReturnQueries.cs`: 2 query handlers + response DTOs in one file

**Path:** `src/Returns/Returns/Returns/ReturnQueries.cs`

**Issue:** `GetReturnHandler`, `GetReturnsForOrderHandler`, and response DTOs are all in a single file.

**Correct pattern:** `GetReturn.cs` (query record + response DTO + handler), `GetReturnsForOrder.cs`.

**Risk:** Medium.

---

#### Finding R-6 — Missing validators on high-risk commands

**Path:** `src/Returns/Returns/Returns/`

**Issue:** Of 11 commands, only 4 have validators. `ApproveReturn`, `ReceiveReturn`, `StartInspection`, and `ExpireReturn` carry zero validation.

**Risk:** Medium. Missing validators allow malformed inputs to reach the aggregate guard and event store.

---

#### Finding R-7 — Triple-nested same-name folder `Returns/Returns/Returns/`

**Path:** `src/Returns/Returns/Returns/`

**Issue:** The feature folder should be renamed to reflect the domain concept (e.g., `ReturnProcessing/`).

**Risk:** Low.

---

### Vendor Portal BC

**Severity: 🔴 HIGH**

#### Finding VP-1 — `ChangeRequests/Commands/` + `ChangeRequests/Handlers/` folder split

**Path:** `src/Vendor Portal/VendorPortal/ChangeRequests/Commands/` and `.../Handlers/`

**Issue:** Commands and handlers for change requests are split across technical sub-folders within a feature folder. This is the technically-layered anti-pattern applied inside a feature folder.

**Correct pattern:** `ChangeRequests/SubmitChangeRequest.cs` containing the command record, its validator, and its handler.

**Risk:** High. Establishes wrong precedent for every future ChangeRequest type.

---

#### Finding VP-2 — `VendorAccount/Commands/` + `VendorAccount/Handlers/` folder split

**Path:** `src/Vendor Portal/VendorPortal/VendorAccount/Commands/` and `.../Handlers/`

**Issue:** Same structural anti-pattern as VP-1.

**Risk:** Medium-high.

---

#### Finding VP-3 — `Analytics/Handlers/` folder

**Path:** `src/Vendor Portal/VendorPortal/Analytics/Handlers/`

**Issue:** The `Handlers/` sub-folder is superfluous and technically named.

**Correct pattern:** Place handler files directly in `Analytics/`.

**Risk:** Low.

---

#### Finding VP-4 — `CatalogResponseHandlers.cs`: 7 handlers in one file

**Path:** `src/Vendor Portal/VendorPortal/ChangeRequests/Handlers/CatalogResponseHandlers.cs`

**Issue:** Seven static handler classes for catalog approval events are defined in a single 189-line file.

**Correct pattern:** Seven files named after the integration event they handle.

**Risk:** High. Maintenance trap; every new approval event will add to this file.

---

#### Finding VP-5 — `VendorHubMessages.cs`: 7 real-time message records in one file

**Path:** `src/Vendor Portal/VendorPortal/RealTime/VendorHubMessages.cs`

**Correct pattern:** One file per message record.

**Risk:** Medium.

---

#### Finding VP-6 — No validators on any Vendor Portal commands

**Path:** `src/Vendor Portal/VendorPortal/ChangeRequests/Commands/`, `VendorAccount/Commands/`

**Issue:** None of the seven Vendor Portal commands have an `AbstractValidator<T>`.

**Risk:** High. `SubmitChangeRequest` has no validation guard on its user-supplied payload.

---

### Backoffice BC

**Severity: 🟠 HIGH**

#### Finding BO-1 — `Backoffice.Api/Commands/` and `Backoffice.Api/Queries/` folders

**Path:** `src/Backoffice/Backoffice.Api/Commands/` and `Queries/`

**Issue:** Technically-named top-level folders in the API project. Feature-oriented folders (`AlertManagement/`, `OrderManagement/`, `ReturnManagement/`) should replace them.

**Risk:** High. Navigating to "everything related to order management" requires cross-referencing two technical folders.

---

#### Finding BO-2 — `Backoffice/Commands/AcknowledgeAlert.cs` — sole occupant of a technically-named folder

**Path:** `src/Backoffice/Backoffice/Commands/AcknowledgeAlert.cs`

**Correct pattern:** `Backoffice/AlertManagement/AcknowledgeAlert.cs`.

**Risk:** Medium. Seed of the Commands-folder anti-pattern.

---

#### Finding BO-3 — `Backoffice/Projections/` — technically-named folder

**Path:** `src/Backoffice/Backoffice/Projections/`

**Issue:** Files should be colocated with the business capability they serve (`DashboardMetrics/`, `AlertManagement/`).

**Risk:** Medium.

---

### Inventory.Api BC

**Severity: 🟠 HIGH**

#### Finding INV-1 — Shattered vertical slices: 4 files per operation

**Paths:**
- `src/Inventory/Inventory.Api/Commands/AdjustInventoryRequest.cs`
- `src/Inventory/Inventory.Api/Commands/AdjustInventoryRequestValidator.cs`
- `src/Inventory/Inventory.Api/Commands/AdjustInventoryEndpoint.cs`
- `src/Inventory/Inventory.Api/Commands/AdjustInventoryResult.cs`
- *(Same 4-file pattern for `ReceiveInboundStock`)*

**Issue:** A single operation is shattered across four files. `AdjustInventoryRequest.cs` is 9 lines; `AdjustInventoryResult.cs` is 11 lines.

**Correct pattern:** One file, `AdjustInventory.cs`, containing the request record, validator, endpoint handler, and response record.

**Risk:** High. Every new operation will replicate this 4-file explosion.

---

#### Finding INV-2 — `Inventory.Api/Commands/` and `Inventory.Api/Queries/` — technically-named folders

**Risk:** Medium.

---

#### Finding INV-3 — `AdjustInventoryEndpoint.cs` bypasses Wolverine aggregate workflow entirely

**Path:** `src/Inventory/Inventory.Api/Commands/AdjustInventoryEndpoint.cs`

**Issue:** The endpoint manually loads the aggregate via `session.LoadAsync<ProductInventory>`, manually appends events via `session.Events.Append`, and manually calls `session.SaveChangesAsync`. This circumvents Wolverine's `[WriteAggregate]` compound handler and the decider pattern. The domain-layer `AdjustInventory` handler with its `OutgoingMessages` is completely bypassed.

**Correct pattern:** Dispatch the `AdjustInventory` domain command via `IMessageBus.InvokeAsync(new AdjustInventory(...))`, or use Wolverine HTTP endpoint conventions with `[WriteAggregate]` and a `Before()` guard.

**Risk:** Critical. Integration events that the domain handler emits via `OutgoingMessages` will silently not be published when the API endpoint is invoked. This is a correctness bug.

---

### Shopping BC

**Severity: 🟡 MEDIUM**

#### Finding SH-1 — Dual-handler pattern in same file (command bus handler + HTTP endpoint)

**Paths:** `src/Shopping/Shopping/Cart/AddItemToCart.cs`, `ApplyCouponToCart.cs`, `ClearCart.cs`, `RemoveCouponFromCart.cs`

**Issue:** Each file contains two separate handler classes: one message-bus handler and one HTTP endpoint handler. The HTTP endpoint re-implements the validation pipeline independently. They can diverge silently.

**Correct pattern:** One canonical handler per command. The HTTP endpoint should dispatch to the domain handler, or own the aggregate workflow exclusively.

**Risk:** Medium-high.

---

#### Finding SH-2 — Validators nested inside command records

**Issue:** Validators in Shopping (and Inventory domain) use nested classes inside command records. This conflicts with three other validator placement conventions across the codebase.

**Risk:** Low in isolation; medium cross-cutting.

---

### Pricing BC

**Severity: 🟡 MEDIUM**

#### Finding PR-1 — Command/Handler/Validator three-way split

**Paths:**
- `src/Pricing/Pricing/Products/SetInitialPrice.cs` (command only)
- `src/Pricing/Pricing/Products/SetInitialPriceHandler.cs` (handler only)
- `src/Pricing/Pricing/Products/SetInitialPriceValidator.cs` (validator only)
- *(Same for `ChangePrice`)*

**Correct pattern:** `SetInitialPrice.cs` (command + validator) and `SetInitialPriceHandler.cs` (handler), or all three in one file.

**Risk:** Medium.

---

### Correspondence BC

**Severity: 🟡 MEDIUM**

#### Finding CO-1 — `MessageEvents.cs`: 4 domain events in one file

**Path:** `src/Correspondence/Correspondence/Messages/MessageEvents.cs`

**Correct pattern:** One file per event.

**Risk:** Low-medium.

---

#### Finding CO-2 — `Correspondence.Api/Queries/` — technically-named folder

**Correct pattern:** `CustomerMessages/` or `MessageHistory/`.

**Risk:** Low.

---

### Cross-Cutting Concerns

#### Finding XC-1 — Four different validator placement conventions

| BC | Validator pattern |
|---|---|
| Shopping, Inventory | Nested class inside command record |
| Promotions | Top-level class in same file as command |
| Pricing | Separate `XxxValidator.cs` file |
| Returns | Bulk `ReturnValidators.cs` file |
| Vendor Portal | None |

**Correct pattern:** Top-level `AbstractValidator<T>` in the same file as the command record and handler (not nested, not separate file, not bulk).

**Risk:** Medium. Inconsistency is a latent onboarding and correctness risk.

---

#### Finding XC-2 — `.Api` project folder naming inconsistency

| BC | `.Api` folders |
|---|---|
| Orders | `Checkout/`, `Placement/` ✅ |
| Shopping | `Cart/`, `Clients/` ✅ |
| Pricing | `Pricing/` ✅ |
| Product Catalog | `Products/` ✅ |
| Payments | `Processing/` ✅, `Queries/` ❌ |
| Fulfillment | `Queries/` ❌ |
| Backoffice | `Commands/`, `Queries/`, `Clients/` ❌ |
| Inventory | `Commands/`, `Queries/` ❌ |
| Correspondence | `Queries/` ❌ |

**Risk:** Medium.

---

#### Finding XC-3 — `AcknowledgeAlert` manual transaction + exception pattern

**Path:** `src/Backoffice/Backoffice/Commands/AcknowledgeAlert.cs`

**Issue:** Handler calls `session.SaveChangesAsync()` explicitly (opting out of `AutoApplyTransactions()`), and throws `InvalidOperationException` rather than returning `ProblemDetails` from a `Before()` method.

**Risk:** Medium. If `AcknowledgeAlert` ever needs to publish an integration event, the manual `SaveChangesAsync` will cause double-commit or ordering issues.

---

### BCs with No Findings

| BC | Assessment | Notable strengths |
|---|---|---|
| **Promotions** | ✅ Exemplary | One file per operation, validators correctly colocated, events in own files |
| **Product Catalog** | ✅ Clean | Feature folders, proper value objects |
| **Orders (domain)** | ✅ Clean | Decider pattern, feature folders, compound handler lifecycle |
| **Customer Identity** | ✅ Clean | Feature folders, EF Core integration handled correctly |

---

### PSA Priority Summary

| Finding | BC | Severity | Est. Effort |
|---|---|---|---|
| R-4 `ReturnCommandHandlers.cs` (5 handlers, 387 lines) | Returns | 🔴 CRITICAL | 2–3 days |
| INV-3 Endpoint bypasses Wolverine aggregate workflow | Inventory.Api | 🔴 CRITICAL | 1 day |
| R-1/R-2/R-3 Bulk commands/events/validators | Returns | 🔴 HIGH | 2 days |
| VP-1/VP-2 Commands/Handlers folder split | Vendor Portal | 🔴 HIGH | 1–2 days |
| VP-4 `CatalogResponseHandlers.cs` (7 handlers) | Vendor Portal | 🔴 HIGH | 0.5 days |
| VP-6 No validators on Vendor Portal commands | Vendor Portal | 🔴 HIGH | 1 day |
| R-6 Missing validators on Returns commands | Returns | 🔴 HIGH | 0.5 days |
| BO-1 `Commands/`+`Queries/` in Backoffice.Api | Backoffice | 🟠 HIGH | 2 days |
| INV-1 Shattered slices in Inventory.Api | Inventory.Api | 🟠 HIGH | 0.5 days |
| XC-3 `AcknowledgeAlert` manual transaction | Backoffice | 🟠 HIGH | 0.5 days |
| R-5 `ReturnQueries.cs` | Returns | 🟡 MEDIUM | 0.5 days |
| PR-1 Validator separation in Pricing | Pricing | 🟡 MEDIUM | 0.5 days |
| SH-1 Dual-handler pattern in Shopping | Shopping | 🟡 MEDIUM | 1 day |
| CO-1 `MessageEvents.cs` bulk events | Correspondence | 🟡 MEDIUM | 0.5 days |
| XC-1 Four validator placement conventions | Cross-cutting | 🟡 MEDIUM | ADR only |
| XC-2 `.Api` folder naming inconsistency | Cross-cutting | 🟡 MEDIUM | 2 days |
| VP-5 `VendorHubMessages.cs` bulk messages | Vendor Portal | 🟡 MEDIUM | 0.5 days |
| BO-2/BO-3 `Commands/` + `Projections/` in Backoffice | Backoffice | 🟡 MEDIUM | 1 day |
| PAY-1/FUL-1/ORD-1 Isolated `Queries/` folders | Payments, Fulfillment, Orders.Api | 🟢 LOW | 1 day total |
| R-7 Triple-nested `Returns/Returns/Returns/` | Returns | 🟢 LOW | 1 hour |
| VP-3 `Analytics/Handlers/` sub-folder | Vendor Portal | 🟢 LOW | 0.5 hours |

---

## QAE Findings

### 1. Coverage Matrix

| Bounded Context | Unit Tests | Integration Tests | E2E (Playwright + Reqnroll) | BUnit (Blazor) |
|---|:---:|:---:|:---:|:---:|
| **Shopping** | ✅ `Shopping.UnitTests` | ✅ `Shopping.Api.IntegrationTests` | N/A | N/A |
| **Orders** | ✅ `Orders.UnitTests` | ✅ `Orders.Api.IntegrationTests` | N/A | N/A |
| **Payments** | ✅ `Payments.UnitTests` | ✅ `Payments.Api.IntegrationTests` | N/A | N/A |
| **Inventory** | ✅ `Inventory.UnitTests` | ✅ `Inventory.Api.IntegrationTests` | N/A | N/A |
| **Fulfillment** | ✅ `Fulfillment.UnitTests` | ✅ `Fulfillment.Api.IntegrationTests` | N/A | N/A |
| **Product Catalog** | ✅ `ProductCatalog.UnitTests` | ✅ `ProductCatalog.Api.IntegrationTests` | N/A | N/A |
| **Promotions** | ❌ **MISSING** | ✅ `Promotions.IntegrationTests` | N/A | N/A |
| **Returns** | ✅ `Returns.UnitTests` | ✅ `Returns.Api.IntegrationTests` | N/A | N/A |
| **Pricing** | ✅ `Pricing.UnitTests` | ✅ `Pricing.Api.IntegrationTests` | N/A | N/A |
| **Correspondence** | ✅ `Correspondence.UnitTests` | ✅ `Correspondence.Api.IntegrationTests` | N/A | N/A |
| **Customer Experience** | ✅ `Storefront.Web.UnitTests` (bUnit) | ✅ `Storefront.Api.IntegrationTests` | ✅ `Storefront.E2ETests` | ✅ |
| **Backoffice** | ✅ `Backoffice.UnitTests` (domain only) | ✅ `Backoffice.Api.IntegrationTests` | ✅ `Backoffice.E2ETests` | ❌ **MISSING** |
| **Vendor Portal** | ✅ `VendorPortal.UnitTests` (domain only) | ✅ `VendorPortal.Api.IntegrationTests` | ⚠️ `VendorPortal.E2ETests` (**all `@ignore`**) | ❌ **MISSING** |
| **Customer Identity** | N/A (identity — exempt) | ✅ `CustomerIdentity.Api.IntegrationTests` | N/A | N/A |
| **Backoffice Identity** | N/A (identity — exempt) | ✅ `BackofficeIdentity.Api.IntegrationTests` | N/A | N/A |
| **Vendor Identity** | N/A (identity — exempt) | ✅ `VendorIdentity.Api.IntegrationTests` | N/A | N/A |

**Legend:** ✅ Present · ❌ Missing · ⚠️ Exists but effectively disabled

---

### 2. Per-BC Findings

#### F-1 — Promotions: Unit Test Project Absent

**Severity: High**
**Path:** `tests/Promotions/` (no `Promotions.UnitTests`)

Promotions has domain logic including coupon validation rules, discount calculation, usage limits, and optimistic concurrency guards — all well-suited to pure unit tests. The only test project is `Promotions.IntegrationTests`, which spins up a full PostgreSQL container per run.

**Risk:** Slow feedback loop for discount domain logic bugs; no isolated tests for guard invariants or discount calculation edge cases.

**Fix:** New project — `Promotions.UnitTests` following the `Returns.UnitTests` or `Shopping.UnitTests` patterns.

---

#### F-2 — Vendor Portal E2E: All Feature Files Tagged `@ignore` at the Feature Level

**Severity: Critical**
**Path:** `tests/Vendor Portal/VendorPortal.E2ETests/Features/`

All three Vendor Portal E2E feature files carry `@ignore` at the **Feature** tag level, not at individual scenarios:

```gherkin
@vendor-portal @auth @ignore
Feature: Vendor Portal Authentication
```

In Reqnroll with xUnit, a feature-level `@ignore` maps to xUnit `[Fact(Skip="...")]` on every generated test method. **100% of Vendor Portal E2E tests are silently skipped in CI** — including P0 scenarios like "Admin logs in with valid credentials."

Additionally missing from the test suite (present in `docs/features/vendor-portal/` but not bound):
- `product-management.feature`
- `vendor-analytics-dashboard.feature`
- `vendor-hub-connection.feature`

**Risk:** Zero automated browser-level coverage. Auth regressions, navigation breaks, SignalR disconnects, and change request workflow failures can ship undetected.

**Fix:**
1. Remove `@ignore` from feature-level tags. Move `@ignore` to individual Scenario level (with comments) only where infrastructure blockers are documented.
2. Create GitHub Issues for the three missing feature files.

---

#### F-3 — Backoffice: Missing BUnit Component Test Project

**Severity: Medium**
**Path:** `tests/Backoffice/` (no `Backoffice.Web.UnitTests`)

`Backoffice.Web` contains 15+ Blazor Server components. The existing `Backoffice.UnitTests` targets the backend domain project (`Backoffice.csproj`), not the WASM UI. Customer Experience's `Storefront.Web.UnitTests` covers 7 components with 40+ tests under 2 seconds — Backoffice has nothing equivalent.

**Risk:** Blazor rendering bugs in admin-facing components go undetected until the heavyweight Playwright layer.

**Fix:** New `tests/Backoffice/Backoffice.Web.UnitTests/` using `<Project Sdk="Microsoft.NET.Sdk.Razor">` with `bunit`. Priority: `NavMenu` (role-gated links), `ProductList`, `UserList`, `PriceEdit`.

---

#### F-4 — Vendor Portal: Missing BUnit Component Test Project

**Severity: Medium**
**Path:** `tests/Vendor Portal/` (no `VendorPortal.Web.UnitTests`)

`VendorPortal.Web` has Blazor WASM components. Combined with F-2 (all E2E `@ignore`), there is currently **zero automated test coverage** for the Vendor Portal's Blazor UI layer.

**Fix:** New `tests/Vendor Portal/VendorPortal.Web.UnitTests/` following the `Storefront.Web.UnitTests` structure.

---

#### F-5 — Customer Experience E2E: Missing Feature Files

**Severity: Medium**
**Path:** `tests/Customer Experience/Storefront.E2ETests/Features/`

Two feature files in `docs/features/customer-experience/` have no corresponding test file: `cart-real-time-updates.feature` and `product-browsing.feature`. Additionally, `order-history.feature` exists in the test suite but all 4 scenarios carry `@wip @ignore` with deferred step definitions.

**Risk:** Cart real-time update (SSE/SignalR) regressions won't be caught. Product browsing (highest-traffic Storefront page) has no E2E coverage.

---

#### F-6 — Promotions: Non-Standard Collection Fixture Pattern

**Severity: Low-Medium**
**Path:** `tests/Promotions/Promotions.IntegrationTests/`

All four test classes use `IClassFixture<TestFixture>` combined with `[Collection("Sequential")]`. Every other BC uses `ICollectionFixture<TestFixture>` with `[CollectionDefinition]`. Result: Promotions spins up **4 PostgreSQL containers** instead of 1 per test run.

**Fix:** Add `IntegrationTestCollection.cs` with `[CollectionDefinition]`/`ICollectionFixture<TestFixture>`, update all four test classes.

---

#### F-7 — Returns: Missing `TrackedHttpCall()` in TestFixture

**Severity: Medium**
**Path:** `tests/Returns/Returns.Api.IntegrationTests/TestFixture.cs`

Returns tests call `_fixture.Host.Scenario(...)` directly for HTTP-triggered operations. Nine other BC fixtures expose `TrackedHttpCall()` to wrap HTTP calls in `Host.ExecuteAndWaitAsync()`, guaranteeing Wolverine cascades complete before assertions.

**Risk:** Tests asserting on downstream effects of HTTP-triggered state transitions may have timing gaps (e.g., approve-return → publish `RefundRequested` → assertions run before cascade completes).

**Fix:** Add `TrackedHttpCall()` to the Returns TestFixture. Migrate HTTP calls in lifecycle endpoint tests.

---

#### F-8 — Backoffice: TestFixture Lacks Wolverine Message Tracking Helpers

**Severity: Low-Medium**
**Path:** `tests/Backoffice/Backoffice.Api.IntegrationTests/BackofficeTestFixture.cs`

`BackofficeTestFixture` exposes only `CleanAllDocumentsAsync()` and `GetDocumentSession()`. `EventDrivenProjectionTests` bypass Wolverine entirely, appending events directly to Marten. No test verifies the full RabbitMQ → WolverineHandler → Marten projection path.

**Fix:** Add `ExecuteAndWaitAsync()` and `TrackedHttpCall()` to `BackofficeTestFixture`. Refactor `EventDrivenProjectionTests` to dispatch through Wolverine.

---

#### F-9 — Orders: Mixed Collection Reference Style

**Severity: Low**
**Path:** `tests/Orders/Orders.Api.IntegrationTests/Placement/`

Three test classes use `[Collection("orders-integration")]` (raw string literal) while others use `[Collection(IntegrationTestCollection.Name)]` (constant). Functional at runtime; typo risk on refactor.

**Fix:** Replace 3 raw literals with `[Collection(IntegrationTestCollection.Name)]`.

---

#### F-10 — Correspondence: Missing `TrackedHttpCall()` in TestFixture

**Severity: Low**
**Path:** `tests/Correspondence/Correspondence.Api.IntegrationTests/TestFixture.cs`

`ExecuteAndWaitAsync()` is present; `TrackedHttpCall()` is absent. Risk is low given Correspondence's minimal HTTP surface.

---

### 3. Pattern Consistency Summary

| Pattern | Standard | Non-Conforming |
|---|---|---|
| `ICollectionFixture` + `[CollectionDefinition]` | Shopping, Orders, Pricing, Fulfillment, Returns, Inventory, Payments, ProductCatalog | **Promotions** |
| `TrackedHttpCall()` in TestFixture | Shopping, Orders, Pricing, Fulfillment, Inventory, Payments, ProductCatalog, Promotions, Storefront | **Returns**, **Backoffice**, **Correspondence** |
| `[Collection(IntegrationTestCollection.Name)]` constant | Most BCs | **Orders** (3 files use raw string) |
| BUnit project for Blazor BCs | **Customer Experience** | **Backoffice**, **Vendor Portal** (missing) |
| E2E feature files with runnable scenarios | Customer Experience (partial), Backoffice | **Vendor Portal** (all `@ignore`) |

---

## UXE Findings

### Event Naming Review

> **Legend:** ✅ Well-named · ⚠️ Minor issue · ❌ Clear naming problem requiring correction

#### Shopping BC

| Event Name | BC | File Path | Verdict | Notes |
|---|---|---|---|---|
| `CartInitialized` | Shopping | `Shopping/Cart/CartInitialized.cs` | ✅ | Clear lifecycle start |
| `ItemAdded` | Shopping | `Shopping/Cart/ItemAdded.cs` | ✅ | Precise |
| `ItemRemoved` | Shopping | `Shopping/Cart/ItemRemoved.cs` | ✅ | Precise |
| `ItemQuantityChanged` | Shopping | `Shopping/Cart/ItemQuantityChanged.cs` | ⚠️ | "Changed" is generic; `CartItemQuantityAdjusted` is more precise |
| `CouponApplied` | Shopping | `Shopping/Cart/CouponApplied.cs` | ✅ | Clear |
| `CouponRemoved` | Shopping | `Shopping/Cart/CouponRemoved.cs` | ✅ | Clear |
| `CartAbandoned` | Shopping | `Shopping/Cart/CartAbandoned.cs` | ✅ | Important business event |
| `CartCleared` | Shopping | `Shopping/Cart/CartCleared.cs` | ✅ | Clear |
| `CheckoutInitiated` | Shopping → Contracts | `Messages.Contracts/Shopping/CheckoutInitiated.cs` | ✅ | Correct integration event name |

#### Orders BC

| Event Name | BC | File Path | Verdict | Notes |
|---|---|---|---|---|
| `CheckoutStarted` | Orders | `Orders/Checkout/CheckoutStarted.cs` | ⚠️ | **Cross-BC naming clash** with Shopping's `CheckoutInitiated`. Two BCs use different verbs for the same checkout handoff. Candidate: `CheckoutReceived`. |
| `ShippingAddressProvided` | Orders | `Orders/Checkout/ShippingAddressProvided.cs` | ✅ | Precise |
| `ShippingMethodSelected` | Orders | `Orders/Checkout/ShippingMethodSelected.cs` | ✅ | Precise |
| `PaymentMethodProvided` | Orders | `Orders/Checkout/PaymentMethodProvided.cs` | ✅ | Precise |
| `CheckoutCompleted` | Orders | `Orders/Checkout/CheckoutCompleted.cs` | ⚠️ | **Duplicate name across BCs.** `Messages.Contracts/Shopping/CheckoutCompleted.cs` also exists with a different payload. Differentiate: Orders-internal could be `OrderCreated`; Shopping's could be `CartCheckoutCompleted`. |
| `OrderPlaced` | Orders | `Orders/Placement/OrderPlaced.cs` | ✅ | Canonical |
| `ReturnWindowExpired` | Orders | `Orders/Placement/ReturnWindowExpired.cs` | ✅ | Precise lifecycle event |
| `AppliedDiscount` | Orders | `Orders/Placement/AppliedDiscount.cs` | ⚠️ | This is a **value object**, not a standalone event. Rename to `DiscountSnapshot` or `OrderDiscount` to prevent confusion. |

#### Payments BC

| Event Name | BC | File Path | Verdict | Notes |
|---|---|---|---|---|
| `PaymentInitiated` | Payments | `Payments/Processing/PaymentInitiated.cs` | ✅ | Clear lifecycle start |
| `PaymentAuthorized` | Payments | `Payments/Processing/PaymentAuthorized.cs` | ✅ | Precise |
| `PaymentCaptured` | Payments | `Payments/Processing/PaymentCaptured.cs` | ✅ | Precise |
| `PaymentFailed` | Payments | `Payments/Processing/PaymentFailed.cs` | ✅ | Precise |
| `PaymentRefunded` | Payments | `Payments/Processing/PaymentRefunded.cs` | ⚠️ | Internal event is `PaymentRefunded`; integration contract is `RefundCompleted`. Two names for same outcome. Align on one. |
| `PaymentRequested` | Payments | (Integration) | ⚠️ | **Reads like a command.** `ProcessPayment` or `AuthorizePaymentRequested` preferred. |
| `RefundRequested` | Shared | `Messages.Contracts/Payments/RefundRequested.cs` | ⚠️ | **Command disguised as event.** This is a command sent from Orders to Payments. `InitiateRefund` is more accurate. |
| `RefundCompleted` | Shared | `Messages.Contracts/Payments/RefundCompleted.cs` | ✅ | Clear |
| `RefundFailed` | Shared | `Messages.Contracts/Payments/RefundFailed.cs` | ✅ | Clear |

#### Inventory BC

| Event Name | BC | File Path | Verdict | Notes |
|---|---|---|---|---|
| `InventoryInitialized` | Inventory | `Inventory/Management/InventoryInitialized.cs` | ✅ | Lifecycle start |
| `InventoryAdjusted` | Inventory | `Inventory/Management/InventoryAdjusted.cs` | ✅ | Precise |
| `StockReceived` | Inventory | `Inventory/Management/StockReceived.cs` | ✅ | Clear |
| `StockReserved` | Inventory | `Inventory/Management/StockReserved.cs` | ✅ | Precise |
| `ReservationCommitted` | Inventory | `Inventory/Management/ReservationCommitted.cs` | ✅ | Clear |
| `ReservationReleased` | Inventory | `Inventory/Management/ReservationReleased.cs` | ✅ | Clear |
| `StockRestocked` | Inventory | `Inventory/Management/StockRestocked.cs` | ⚠️ | **Domain ambiguity.** Used specifically for returned-item restock. `ReturnedStockRestored` more precisely reflects the trigger. |
| `ReservationCommitRequested` | Shared | `Messages.Contracts/Orders/ReservationCommitRequested.cs` | ⚠️ | **Command, not event.** `CommitInventoryReservation` preferred. |
| `ReservationReleaseRequested` | Shared | `Messages.Contracts/Orders/ReservationReleaseRequested.cs` | ⚠️ | Same — `ReleaseInventoryReservation` preferred. |
| `ReservationConfirmed` | Shared | `Messages.Contracts/Inventory/ReservationConfirmed.cs` | ✅ | Clear |
| `ReservationFailed` | Shared | `Messages.Contracts/Inventory/ReservationFailed.cs` | ✅ | Clear |
| `InventoryAdjusted` | Shared | `Messages.Contracts/Inventory/InventoryAdjusted.cs` | ✅ | Clear integration event |
| `LowStockDetected` | Shared | `Messages.Contracts/Inventory/LowStockDetected.cs` | ✅ | Precise |
| `StockReplenished` | Shared | `Messages.Contracts/Inventory/StockReplenished.cs` | ⚠️ | **Overlaps with `StockReceived` and `StockRestocked`.** Three names for "stock quantity increased." Consider explicit vocabulary: `SupplierStockReceived`, `ReturnStockRestored`, `StockReplenished`. |

#### Fulfillment BC

| Event Name | BC | File Path | Verdict | Notes |
|---|---|---|---|---|
| `WarehouseAssigned` | Fulfillment | `Fulfillment/Shipments/WarehouseAssigned.cs` | ✅ | Precise |
| `ShipmentDispatched` | Fulfillment | `Fulfillment/Shipments/ShipmentDispatched.cs` | ✅ | Precise |
| `ShipmentDelivered` | Fulfillment | `Fulfillment/Shipments/ShipmentDelivered.cs` | ✅ | Precise |
| `ShipmentDeliveryFailed` | Fulfillment | `Fulfillment/Shipments/ShipmentDeliveryFailed.cs` | ✅ | Compound but unambiguous |
| `FulfillmentRequested` | Shared | `Messages.Contracts/Fulfillment/FulfillmentRequested.cs` | ⚠️ | **Command, not event.** `RequestFulfillment` (command) or `FulfillmentOrderReceived` (event) preferred. |

#### Product Catalog BC

| Event Name | BC | File Path | Verdict | Notes |
|---|---|---|---|---|
| `ProductAdded` | Shared | `Messages.Contracts/ProductCatalog/ProductAdded.cs` | ✅ | Clear |
| `ProductUpdated` | Shared | `Messages.Contracts/ProductCatalog/ProductUpdated.cs` | ⚠️ | **Overly generic.** What was updated? Consider `ProductDescriptionUpdated`, `ProductDisplayNameUpdated`, or a discriminated union. |
| `ProductDiscontinued` | Shared | `Messages.Contracts/ProductCatalog/ProductDiscontinued.cs` | ✅ | Clear lifecycle event |
| `VendorProductAssociated` | Shared | `Messages.Contracts/ProductCatalog/VendorProductAssociated.cs` | ✅ | Precise |
| `DataCorrectionApproved` | Shared | `Messages.Contracts/ProductCatalog/DataCorrectionApproved.cs` | ✅ | Clear |
| `DataCorrectionRejected` | Shared | `Messages.Contracts/ProductCatalog/DataCorrectionRejected.cs` | ✅ | Clear |
| `DescriptionChangeApproved` | Shared | `Messages.Contracts/ProductCatalog/DescriptionChangeApproved.cs` | ✅ | Precise |
| `DescriptionChangeRejected` | Shared | `Messages.Contracts/ProductCatalog/DescriptionChangeRejected.cs` | ✅ | Precise |
| `ImageChangeApproved` | Shared | `Messages.Contracts/ProductCatalog/ImageChangeApproved.cs` | ✅ | Precise |
| `ImageChangeRejected` | Shared | `Messages.Contracts/ProductCatalog/ImageChangeRejected.cs` | ✅ | Precise |
| `MoreInfoRequestedForChangeRequest` | Shared | `Messages.Contracts/ProductCatalog/MoreInfoRequestedForChangeRequest.cs` | ❌ | **Verbose, command-like, inconsistent.** 33 characters, contains "Requested" (command signal), embeds aggregate type. Suggested: `AdditionalInfoRequested` or `ChangeRequestAdditionalInfoRequired`. |

#### Returns BC

| Event Name | BC | File Path | Verdict | Notes |
|---|---|---|---|---|
| `ReturnRequested` | Returns | `Returns/Returns/ReturnEvents.cs` | ⚠️ | "Requested" blurs the command/event line. `ReturnInitiated` less ambiguous. |
| `ReturnApproved` | Returns | `Returns/Returns/ReturnEvents.cs` | ✅ | Clear |
| `ReturnDenied` | Returns | `Returns/Returns/ReturnEvents.cs` | ✅ | Clear — CS-initiated denial |
| `ReturnReceived` | Returns | `Returns/Returns/ReturnEvents.cs` | ✅ | Clear — physical receipt |
| `InspectionStarted` | Returns | `Returns/Returns/ReturnEvents.cs` | ✅ | Acceptable |
| `InspectionPassed` | Returns | `Returns/Returns/ReturnEvents.cs` | ✅ | Clear |
| `InspectionFailed` | Returns | `Returns/Returns/ReturnEvents.cs` | ✅ | Clear |
| `InspectionMixed` | Returns | `Returns/Returns/ReturnEvents.cs` | ⚠️ | **Ambiguous.** "Mixed" is not a past-tense verb. `InspectionPartiallyPassed` or `MixedInspectionRecorded` more precise. |
| `ReturnExpired` | Returns | `Returns/Returns/ReturnEvents.cs` | ✅ | Clear lifecycle event |
| `ExchangeApproved` | Returns | `Returns/Returns/ReturnEvents.cs` | ✅ | Clear |
| `ExchangeDenied` | Returns | `Returns/Returns/ReturnEvents.cs` | ✅ | Clear — staff-initiated denial |
| `ExchangeReplacementShipped` | Returns | `Returns/Returns/ReturnEvents.cs` | ✅ | Compound but unambiguous |
| `ExchangeCompleted` | Returns | `Returns/Returns/ReturnEvents.cs` | ✅ | Clear |
| `ExchangeRejected` | Returns | `Returns/Returns/ReturnEvents.cs` | ⚠️ | **Near-synonym collision with `ExchangeDenied`.** `ExchangeRejected` = inspection failure; `ExchangeDenied` = staff decision. Names don't communicate the difference. Suggest: `ExchangeFailedInspection`. |
| `ReturnRejected` | Shared | `Messages.Contracts/Returns/ReturnRejected.cs` | ⚠️ | Same — `ReturnDenied` = staff turns it down; `ReturnRejected` = fails inspection. Suggest: `ReturnFailedInspection`. |
| `ReturnCompleted` | Shared | `Messages.Contracts/Returns/ReturnCompleted.cs` | ✅ | Clear terminal event |

#### Correspondence BC

| Event Name | BC | File Path | Verdict | Notes |
|---|---|---|---|---|
| `MessageQueued` | Correspondence | `Correspondence/Messages/MessageEvents.cs` | ✅ | Clear internal event |
| `MessageDelivered` | Correspondence | `Correspondence/Messages/MessageEvents.cs` | ✅ | Clear |
| `DeliveryFailed` | Correspondence | `Correspondence/Messages/MessageEvents.cs` | ❌ | **Missing subject noun.** All other "Failed" events prefix the subject. Should be `MessageDeliveryFailed`. |
| `MessageSkipped` | Correspondence | `Correspondence/Messages/MessageEvents.cs` | ⚠️ | **Ambiguous reason.** `MessageSuppressed` or `NotificationOptedOut` more precise. |
| `CorrespondenceQueued` | Shared | `Messages.Contracts/Correspondence/CorrespondenceQueued.cs` | ⚠️ | **BC-level terminology mismatch.** Internal events say "Message"; contracts say "Correspondence". Pick one and align. |
| `CorrespondenceDelivered` | Shared | `Messages.Contracts/Correspondence/CorrespondenceDelivered.cs` | ✅ | Clear |
| `CorrespondenceFailed` | Shared | `Messages.Contracts/Correspondence/CorrespondenceFailed.cs` | ⚠️ | Truncated — `CorrespondenceDeliveryFailed` is more consistent. |

#### Pricing BC

| Event Name | BC | File Path | Verdict | Notes |
|---|---|---|---|---|
| `ProductRegistered` | Pricing | `Pricing/Products/ProductRegistered.cs` | ✅ | Lifecycle start |
| `InitialPriceSet` | Pricing | `Pricing/Products/InitialPriceSet.cs` | ✅ | Excellent — distinct from `PriceChanged` |
| `PriceChanged` | Pricing | `Pricing/Products/PriceChanged.cs` | ✅ | Clear internal event |
| `PriceCorrected` | Pricing | `Pricing/Products/PriceCorrected.cs` | ✅ | Precise retroactive audit record |
| `PriceDiscontinued` | Pricing | `Pricing/Products/PriceDiscontinued.cs` | ✅ | Clear |
| `FloorPriceSet` | Pricing | `Pricing/Products/FloorPriceSet.cs` | ✅ | Precise |
| `CeilingPriceSet` | Pricing | `Pricing/Products/CeilingPriceSet.cs` | ✅ | Precise |
| `PriceChangeScheduled` | Pricing | `Pricing/Products/PriceChangeScheduled.cs` | ✅ | Precise |
| `ScheduledPriceActivated` | Pricing | `Pricing/Products/ScheduledPriceActivated.cs` | ✅ | Precise |
| `ScheduledPriceChangeCancelled` | Pricing | `Pricing/Products/ScheduledPriceChangeCancelled.cs` | ✅ | Precise |
| `PricePublished` | Shared | `Messages.Contracts/Pricing/PricePublished.cs` | ✅ | Clear |
| `PriceUpdated` | Shared | `Messages.Contracts/Pricing/PriceUpdated.cs` | ⚠️ | **Cross-BC inconsistency.** Internal: `PriceChanged`; contract: `PriceUpdated`. Align on `PriceChanged`. |
| `VendorPriceSuggestionSubmitted` | Shared | `Messages.Contracts/Pricing/VendorPriceSuggestionSubmitted.cs` | ✅ | Precise |

#### Promotions BC

| Event Name | BC | File Path | Verdict | Notes |
|---|---|---|---|---|
| `PromotionCreated` | Promotions | `Promotions/Promotion/PromotionCreated.cs` | ✅ | Clear |
| `PromotionActivated` | Promotions | `Promotions/Promotion/PromotionActivated.cs` | ✅ | Clear |
| `PromotionPaused` | Promotions | `Promotions/Promotion/PromotionPaused.cs` | ✅ | Clear |
| `PromotionResumed` | Promotions | `Promotions/Promotion/PromotionResumed.cs` | ✅ | Clear |
| `PromotionCancelled` | Promotions | `Promotions/Promotion/PromotionCancelled.cs` | ✅ | Clear |
| `PromotionExpired` | Promotions | `Promotions/Promotion/PromotionExpired.cs` | ✅ | Clear |
| `CouponBatchGenerated` | Promotions | `Promotions/Promotion/CouponBatchGenerated.cs` | ✅ | Precise |
| `CouponIssued` | Promotions | `Promotions/Coupon/CouponIssued.cs` | ✅ | Clear |
| `CouponRedeemed` | Promotions | `Promotions/Coupon/CouponRedeemed.cs` | ✅ | Clear |
| `CouponRevoked` | Promotions | `Promotions/Coupon/CouponRevoked.cs` | ✅ | Clear |
| `CouponExpired` | Promotions | `Promotions/Coupon/CouponExpired.cs` | ✅ | Clear |
| `PromotionRedemptionRecorded` | Promotions | `Promotions/Promotion/PromotionRedemptionRecorded.cs` | ⚠️ | Verbose. `PromotionRedeemed` reads more naturally. |

#### Vendor Identity BC

| Event Name | BC | File Path | Verdict | Notes |
|---|---|---|---|---|
| `VendorTenantCreated` | Vendor Identity | `Messages.Contracts/VendorIdentity/VendorTenantCreated.cs` | ✅ | Precise |
| `VendorTenantSuspended` | Vendor Identity | `Messages.Contracts/VendorIdentity/VendorTenantSuspended.cs` | ✅ | Clear |
| `VendorTenantTerminated` | Vendor Identity | `Messages.Contracts/VendorIdentity/VendorTenantTerminated.cs` | ✅ | Clear terminal event |
| `VendorTenantReinstated` | Vendor Identity | `Messages.Contracts/VendorIdentity/VendorTenantReinstated.cs` | ✅ | Clear recovery |
| `VendorUserInvited` | Vendor Identity | `Messages.Contracts/VendorIdentity/VendorUserInvited.cs` | ✅ | Clear |
| `VendorUserActivated` | Vendor Identity | `Messages.Contracts/VendorIdentity/VendorUserActivated.cs` | ✅ | Clear |
| `VendorUserDeactivated` | Vendor Identity | `Messages.Contracts/VendorIdentity/VendorUserDeactivated.cs` | ✅ | Clear |
| `VendorUserReactivated` | Vendor Identity | `Messages.Contracts/VendorIdentity/VendorUserReactivated.cs` | ✅ | Clear |
| `VendorUserInvitationResent` | Vendor Identity | `Messages.Contracts/VendorIdentity/VendorUserInvitationResent.cs` | ✅ | Precise |
| `VendorUserInvitationRevoked` | Vendor Identity | `Messages.Contracts/VendorIdentity/VendorUserInvitationRevoked.cs` | ✅ | Precise |
| `VendorUserRoleChanged` | Vendor Identity | `Messages.Contracts/VendorIdentity/VendorUserRoleChanged.cs` | ⚠️ | "Changed" generic; acceptable in context. Low priority. |

#### Vendor Portal BC

| Event Name | BC | File Path | Verdict | Notes |
|---|---|---|---|---|
| `DataCorrectionRequested` | Vendor Portal | `Messages.Contracts/VendorPortal/DataCorrectionRequested.cs` | ⚠️ | **Command-like.** `DataCorrectionSubmitted` more clearly signals a completed action. |
| `DescriptionChangeRequested` | Vendor Portal | `Messages.Contracts/VendorPortal/DescriptionChangeRequested.cs` | ⚠️ | Same. `DescriptionChangeSubmitted` preferred. |
| `ImageUploadRequested` | Vendor Portal | `Messages.Contracts/VendorPortal/ImageUploadRequested.cs` | ⚠️ | Same. `ImageUploadSubmitted` preferred. Also inconsistency: ProductCatalog uses "Change" while this uses "Upload". |

#### Backoffice BC

| Event Name | BC | File Path | Verdict | Notes |
|---|---|---|---|---|
| `OrderNoteAdded` | Backoffice | `Backoffice/OrderNote/OrderNoteEvents.cs` | ✅ | Clear domain event |
| `OrderNoteEdited` | Backoffice | `Backoffice/OrderNote/OrderNoteEvents.cs` | ⚠️ | "Edited" informal; `OrderNoteUpdated` or `OrderNoteRevised` more consistent. |
| `OrderNoteDeleted` | Backoffice | `Backoffice/OrderNote/OrderNoteEvents.cs` | ✅ | Clear |
| `LiveMetricUpdated` | Backoffice | `Backoffice/RealTime/BackofficeEvent.cs` | ⚠️ | UI concept, not domain concept. Acceptable as SignalR message name. |
| `AlertCreated` | Backoffice | `Backoffice/RealTime/BackofficeEvent.cs` | ✅ | Acceptable for SignalR push |
| `ActiveOrderIncremented` | Backoffice | `Backoffice/RealTime/BackofficeEvent.cs` | ⚠️ | **Technical counter mechanics exposed as events.** `OrderActivated` preferred. |
| `ActiveOrderDecremented` | Backoffice | `Backoffice/RealTime/BackofficeEvent.cs` | ⚠️ | Same. `OrderFulfilled` preferred. |
| `PendingReturnIncremented` | Backoffice | `Backoffice/RealTime/BackofficeEvent.cs` | ⚠️ | `ReturnBecamePending` would be more expressive. |

---

#### Event Naming — Priority Issues

1. **❌ `DeliveryFailed` (Correspondence)** — Missing subject noun. Fix immediately: `MessageDeliveryFailed`.
2. **❌ `MoreInfoRequestedForChangeRequest` (ProductCatalog)** — Verbose, command-like, structurally inconsistent. Fix: `AdditionalInfoRequested`.
3. **⚠️ `ReturnDenied` vs. `ReturnRejected` / `ExchangeDenied` vs. `ExchangeRejected`** — Near-synonym pairs where the distinction (staff decision vs. inspection failure) is opaque from the names alone. Fix: `ReturnFailedInspection` / `ExchangeFailedInspection` for inspection-triggered outcomes.
4. **⚠️ `CheckoutStarted` (Orders) vs. `CheckoutInitiated` (Shopping)** — Ubiquitous language divergence. Align on one verb.
5. **⚠️ `CheckoutCompleted` exists in both Orders BC and Shopping contracts** with different payloads — a subtle collision waiting to cause deserialisation bugs.
6. **⚠️ `PaymentRequested` / `RefundRequested` / `FulfillmentRequested` / `ReservationCommitRequested` / `ReservationReleaseRequested`** — Commands dressed in "Requested" past-tense clothing. Name as commands (`InitiateRefund`, `CommitInventoryReservation`) or as true outcome events.
7. **⚠️ `PriceChanged` (internal) vs. `PriceUpdated` (contract)** — Align on `PriceChanged`.
8. **⚠️ `MessageQueued`/`MessageDelivered` (internal) vs. `CorrespondenceQueued`/`CorrespondenceDelivered` (contracts)** — Align on contract vocabulary.

---

### Backoffice Status Report

#### What Was Planned

| Milestone | Goal |
|---|---|
| M32.0 (Phase 1) | Read-only dashboards, CS tooling, alert feed, warehouse tools |
| M32.1 (Phase 2) | Blazor WASM frontend, domain BC endpoint gap closure, E2E infrastructure |
| M32.2 (Phase 3A) | E2E test stabilization |
| M32.3 (Phase 3B) | Write operations depth (Product, Pricing, Inventory, User Management) |
| M32.4 (Phase 4) | E2E fixture stabilization, UX polish |

Planned Marten projections for the executive dashboard:
- `AdminDailyMetrics` — orders, revenue, AOV, payment failure rate
- `AlertFeedView` — operations alert feed
- `FulfillmentPipelineView` — order distribution by saga state
- `ReturnMetricsView` — active return rate KPI
- `CorrespondenceMetricsView` — delivery success rate KPI

#### What Has Actually Been Implemented

**Infrastructure — ✅ Complete**
- BFF project structure (`Backoffice`, `Backoffice.Api`, `Backoffice.Web`)
- Marten document store, Wolverine message handling, multi-issuer JWT validation
- Authorization policies for 7 roles (CopyWriter → SystemAdmin)
- SignalR hub at `/hub/backoffice` with role-based groups
- 9 HTTP proxy client interfaces across domain BCs

**Customer Service Workflows — ✅ Complete**
- Customer search, order detail, order cancellation, return lookup, return approval/denial, correspondence history, OrderNote aggregate (add/edit/delete)

**Projections — ⚠️ Partially Complete**
- `AdminDailyMetrics` ✅ — orders, cancelled orders, revenue, payment failure rate
- `AlertFeedView` ✅ — 4 alert types from 4 integration events
- `FulfillmentPipelineView` ❌ — **never built.** Referenced in ADR 0036 with TODO comment.
- `ReturnMetricsView` ❌ — **never built.** Listed in M32.0 scope.
- `CorrespondenceMetricsView` ❌ — **never built.** Same.

> **Critical:** `GetDashboardSummary` returns hard-coded `0` stubs for `PendingReturns` and `LowStockAlerts`, with `// STUB` comments. Two of five executive dashboard KPIs are inert placeholders.

**Blazor WASM Frontend — ✅ Substantially Complete**

| Page | Route | Status |
|---|---|---|
| Login | `/login` | ✅ |
| Dashboard | `/dashboard` | ✅ (2 of 5 KPIs are stubs) |
| Customer Search | `/customers/search` | ✅ |
| Operations Alerts | `/alerts` | ✅ |
| Product List/Edit | `/products`, `/products/{sku}/edit` | ✅ |
| Price Edit | `/products/{sku}/price` | ✅ |
| Inventory List/Edit | `/inventory`, `/inventory/{sku}/edit` | ✅ |
| User List/Create/Edit | `/users/*` | ✅ |

**Notable absent pages:**
- No dedicated **Order List/Search** page — CS agents can only reach orders via Customer Search flow
- No dedicated **Return Management** page — returns accessible only via customer search composition
- No dedicated **Correspondence History** page

**E2E Testing — ⚠️ 65% Complete**
- 34 BDD Gherkin scenarios created; 22/34 passing
- 12 scenarios blocked by a Blazor WASM app-loading timeout in fixture (deferred to M32.4)

#### Gap Analysis

| Feature | Planned In | Status | Gap Detail |
|---|---|---|---|
| `AdminDailyMetrics` projection | M32.0 | ✅ Shipped | — |
| `AlertFeedView` projection | M32.0 | ✅ Shipped | — |
| `FulfillmentPipelineView` projection | M32.0 | ❌ Not built | ADR 0036 TODO comment; absent from codebase |
| `ReturnMetricsView` projection | M32.0 | ❌ Not built | Listed in M32-0 session prep |
| `CorrespondenceMetricsView` projection | M32.0 | ❌ Not built | Same |
| PendingReturns KPI (live) | M32.0 | ❌ Stubbed `0` | Explicit `// STUB` in `GetDashboardSummary.cs` |
| LowStockAlerts KPI (live) | M32.0 | ❌ Stubbed `0` | Same |
| Return management dedicated page | M32.1/M32.3 | ❌ Absent | Returns approachable only via customer search |
| Order list/search standalone page | M32.1/M32.3 | ❌ Absent | No direct order search for CS agents |
| E2E test suite fully green | M32.3 | ⚠️ 65% | 12/34 scenarios blocked by fixture issue → M32.4 |
| Promotions read-only view | M32.3+ | ❌ Deferred | Explicitly deferred |

#### Honest Assessment

**Status: Late-stage, approximately 80% complete.**

The infrastructure is solid and consistent with the BFF pattern. The Blazor WASM application with 12 functional pages, real-time SignalR, JWT auth, and 7-role RBAC is production-ready. What keeps it from "complete":

1. Three Marten projections were scoped and never built — the executive dashboard ships with two permanently-zero KPI tiles.
2. Two critical CS workflows have no dedicated UI surface (return management, direct order lookup).
3. E2E test coverage is at 65% — 12 scenarios exist in BDD spec form but cannot run.

The next milestone (M32.4) is scoped as "stabilization" — completing it requires the E2E fixture fix, three missing projections, and a Returns queue page plus direct Order search page. The architecture fully supports this work; the gaps are tractable.

---

## Convergence Discussion Summary

The panel convened after independent analysis and identified the following cross-cutting themes and compounding risks:

### Where Findings Overlap

**1. Returns BC is the highest-priority single target (PSA + QAE)**

PSA flagged every structural pattern in `src/Returns/Returns/Returns/` as a critical violation. QAE independently flagged that `Returns.Api.IntegrationTests` lacks `TrackedHttpCall()`, meaning there are no timing guarantees on Wolverine cascades (e.g., approve-return → emit `RefundRequested`). Together: the BC with the messiest code organisation is also the BC where test reliability is most questionable. Regressions in the return approval flow — the highest-stakes CS workflow — could ship undetected.

**2. Vendor Portal has both structural violations AND zero UI test coverage (PSA + QAE)**

PSA flagged the Commands/Handlers folder split, no validators, and `CatalogResponseHandlers.cs` (7 handlers, 1 file). QAE found that all three E2E feature files are globally `@ignore` and no bUnit project exists. Combined risk: every future change to the Vendor Portal is structurally hard to make AND entirely untested at the UI layer. This is the most dangerous combination in the codebase.

**3. `AdjustInventoryEndpoint.cs` bypasses Wolverine aggregate workflow (PSA + UXE)**

PSA flagged this as a correctness bug (INV-3): integration events from the domain handler are silently not published when the API endpoint is invoked. UXE noted that `InventoryAdjusted` and `LowStockDetected` integration events are the data source for the Backoffice executive dashboard's real-time alert feed. If the endpoint bypass suppresses these events, the Backoffice `AlertFeedView` projection will never receive the inventory signals it depends on — compounding the already-noted stub KPI problem.

**4. Backoffice projection gaps connect to UX dead ends (UXE + QAE)**

UXE found three missing Marten projections and two stub KPIs on the executive dashboard. QAE found that `BackofficeTestFixture` has no Wolverine message tracking helpers, meaning the projection pipelines driven by integration events have no end-to-end tests. These gaps reinforce each other: the missing projections don't get built partly because there's no established test harness to verify them.

**5. Event naming "command masquerades" compound integration traceability (UXE + PSA)**

UXE identified six integration messages that use "Requested" suffix but semantically function as commands (`RefundRequested`, `FulfillmentRequested`, `ReservationCommitRequested`, etc.). PSA noted that these messages flow through the Wolverine bus as integration messages, and their command-like semantics can make it harder to reason about event stream causality during debugging. Not a correctness bug, but a consistent friction point across six inter-BC integrations.

### Disagreements

**Disagreement on SH-1 priority (PSA vs. QAE):** PSA rated the Shopping dual-handler pattern as Medium-High risk; QAE did not flag it as a test gap because both handler paths have integration test coverage. The panel settled on Medium: it is a latent divergence risk but not an immediate correctness problem.

**Disagreement on event naming urgency (UXE vs. PSA):** PSA rates event contract changes as medium-high risk (requires Marten stream migration for events already persisted). UXE rates the ambiguity cost as growing each sprint. Consensus: triage `❌`-rated events (2 items) as immediate; `⚠️`-rated cross-BC naming clashes (CheckoutStarted/CheckoutInitiated, PriceChanged/PriceUpdated) as medium.

---

## Aggregated Priority List

> **Key:** 🔴 High risk (likely bugs, data issues, near-term) · 🟡 Medium (technical debt that compounds) · 🟢 Low (polish, consistency)
> **Effort:** S = half day or less · M = 1–2 days · L = 3–5 days · XL = 5+ days

| # | Finding | Agent(s) | BC | Risk | Effort | Notes |
|---|---|---|---|---|---|---|
| 1 | INV-3: `AdjustInventoryEndpoint` bypasses Wolverine aggregate workflow; integration events silently suppressed | PSA + UXE | Inventory.Api | 🔴 | M | **Correctness bug.** Fix first. Affects Backoffice alert feed. |
| 2 | F-2: All Vendor Portal E2E feature files globally `@ignore`; 100% of browser tests silently skipped | QAE | Vendor Portal | 🔴 | S | Remove feature-level `@ignore`; move to scenario level where blocked. |
| 3 | R-4: `ReturnCommandHandlers.cs` — 5 handlers, 387 lines | PSA | Returns | 🔴 | L | Highest merge-conflict risk file in the codebase. |
| 4 | R-1/R-2/R-3: Bulk commands/events/validators in Returns BC | PSA | Returns | 🔴 | L | Must accompany #3 as a combined Returns refactor sprint. |
| 5 | VP-1/VP-2/VP-6: Commands/Handlers split + zero validators in Vendor Portal | PSA | Vendor Portal | 🔴 | L | Structural + correctness risk on every SubmitChangeRequest call. |
| 6 | VP-4: `CatalogResponseHandlers.cs` — 7 handlers in one file | PSA | Vendor Portal | 🔴 | S | Quick win alongside VP-1/VP-2. |
| 7 | R-6: Missing validators on high-risk Returns commands | PSA | Returns | 🔴 | S | Accompany Returns refactor sprint. |
| 8 | F-3/F-4: Missing bUnit component test projects for Backoffice and Vendor Portal | QAE | Backoffice, Vendor Portal | 🟡 | M each | Provides fast-feedback safety net before E2E. |
| 9 | BO-1/BO-2/BO-3: Technically-named folders in Backoffice API and domain | PSA | Backoffice | 🟡 | M | Growing anti-pattern; address before M32.4 adds more endpoints. |
| 10 | XC-3: `AcknowledgeAlert` manual transaction + exception pattern | PSA | Backoffice | 🟡 | S | Fix before `AcknowledgeAlert` gains outbox-driven side effects. |
| 11 | UXE: `FulfillmentPipelineView`, `ReturnMetricsView`, `CorrespondenceMetricsView` projections never built; dashboard KPIs stub `0` | UXE + QAE | Backoffice | 🟡 | L | Two KPIs visibly broken to executives. Addressable in M32.4. |
| 12 | UXE: No dedicated Order Search or Return Management page in Backoffice | UXE | Backoffice | 🟡 | M | Critical CS workflow gap; deferred to M32.4. |
| 13 | F-7: Returns `TestFixture` missing `TrackedHttpCall()` | QAE | Returns | 🟡 | S | Accompany Returns refactor sprint; adds timing guarantees. |
| 14 | INV-1/INV-2: Shattered 4-file slices in Inventory.Api | PSA | Inventory.Api | 🟡 | S | Quick consolidation; address alongside INV-3. |
| 15 | PR-1: Validator/Command/Handler three-way split in Pricing | PSA | Pricing | 🟡 | S | Two commands affected; low friction to fix. |
| 16 | SH-1: Dual-handler pattern in Shopping | PSA | Shopping | 🟡 | M | Latent divergence risk; medium priority. |
| 17 | UXE event naming ❌ items: `DeliveryFailed` → `MessageDeliveryFailed`; `MoreInfoRequestedForChangeRequest` → `AdditionalInfoRequested` | UXE | Correspondence, ProductCatalog | 🟡 | S each | Breaking contract change; coordinate with all consumers before deploying. |
| 18 | UXE event naming ⚠️ cross-BC clash: `CheckoutStarted` vs. `CheckoutInitiated`; `CheckoutCompleted` duplicate payload | UXE | Orders, Shopping | 🟡 | M | Requires consumer coordination. |
| 19 | UXE: Command-masquerade events (`RefundRequested`, `FulfillmentRequested`, etc.) | UXE | Payments, Fulfillment, Inventory | 🟡 | M | Align naming convention; breaking changes. |
| 20 | F-6: Promotions uses `IClassFixture` (4 containers) instead of `ICollectionFixture` (1 container) | QAE | Promotions | 🟡 | S | CI performance fix. |
| 21 | F-1: Promotions missing unit test project | QAE | Promotions | 🟡 | M | New project required; template from Returns.UnitTests. |
| 22 | F-5: Customer Experience E2E missing `cart-real-time-updates` and `product-browsing` feature files | QAE | Customer Experience | 🟡 | M | SignalR cart updates are highest-value E2E scenario. |
| 23 | XC-1: Four validator placement conventions (ADR needed) | PSA | Cross-cutting | 🟢 | S | Write ADR 003x defining canonical pattern. |
| 24 | XC-2: `.Api` folder naming inconsistency (6 BCs deviate) | PSA | Cross-cutting | 🟢 | L | Normalization pass; low urgency, high breadth. |
| 25 | CO-1: `MessageEvents.cs` bulk events in Correspondence | PSA | Correspondence | 🟢 | S | Quick split. |
| 26 | F-8: Backoffice TestFixture lacks message tracking helpers | QAE | Backoffice | 🟢 | S | Needed before Backoffice gets RabbitMQ-triggered projection tests. |
| 27 | VP-5: `VendorHubMessages.cs` bulk messages | PSA | Vendor Portal | 🟢 | S | Quick split; accompany VP-1/VP-2 sprint. |
| 28 | UXE: `PriceChanged` vs. `PriceUpdated` alignment | UXE | Pricing | 🟢 | S | Breaking contract change; coordinate first. |
| 29 | UXE: Correspondence internal/contract terminology mismatch (`Message*` vs. `Correspondence*`) | UXE | Correspondence | 🟢 | S | Internal cleanup. |
| 30 | F-9/F-10: Raw string literal in Orders; missing `TrackedHttpCall` in Correspondence | QAE | Orders, Correspondence | 🟢 | S | Trivial mechanical fixes. |
| 31 | PAY-1/FUL-1/ORD-1: Isolated `Queries/` folders in Payments, Fulfillment, Orders.Api | PSA | Payments, Fulfillment, Orders.Api | 🟢 | S | Single-file moves. |
| 32 | R-7: Triple-nested `Returns/Returns/Returns/` folder | PSA | Returns | 🟢 | S | Rename feature folder. |

---

*Audit conducted by PSA · QAE · UXE on 2026-03-21.*

---

## UXE Cross-Review

### 1. What surprised me in the PSA findings

**INV-3 is worse for the dashboard than my section conveyed.** I framed the stub KPIs as a build gap: three projections are missing, ergo two tiles show `0`. That implies a straightforward fix: build the projections. INV-3 breaks that assumption. `LowStockDetected` is published by the domain handler that `AdjustInventoryEndpoint` bypasses entirely. So even after the Backoffice team builds the missing projections in M32.4, the `LowStockAlerts` tile will *still* read zero — suppressed by a correctness bug a floor below. A product manager scoping M32.4 will write "build the three projections — done." INV-3 must ship first, or one KPI remains broken for reasons no one will immediately trace. That dependency is absent from the priority list.

**XC-3 (`AcknowledgeAlert` manual transaction) has a present-tense UX failure mode.** If the handler fails mid-transaction, the CS agent's acknowledgment silently disappears — the alert stays active on screen, the write never committed, no error surfaced. PSA frames this as future ordering risk. It is also an immediate feedback integrity risk.

---

### 2. What surprised me in the QAE findings

**F-2 confirms the Vendor Portal workflow is untestable by design, not accident.** Feature-level `@ignore` tags are a deliberate suppression decision. The change request submission flow — the primary job vendors come to the portal to do — has no browser-level verification, no bUnit component coverage (F-4), and no validator on the command payload (VP-6). I flagged `DescriptionChangeRequested` and `DataCorrectionRequested` as command-masquerades with traceability problems. What I underweighted is that the workflow behind those events is dark end-to-end: if the submission UI silently fails, nothing automated detects it.

**Missing E2E coverage compounds the SignalR gap more than the Order Search or Return Management absences.** Those are Backoffice pages — F-2 doesn't directly touch them. But F-5's missing `cart-real-time-updates.feature` is the sharper concern: real-time cart state is the highest-value Storefront interaction, and a SignalR regression is precisely the class of failure that is invisible to unit and integration tests and immediately obvious to the first shopper who adds an item and watches the count freeze.

---

### 3. Priority list re-ranking from a user-facing lens

**Item #11 (missing projections + stub KPIs, 🟡) should carry a user-visibility callout.** Executives looking at the dashboard today see two tiles permanently displaying `0`. That is not future risk — it is a currently-broken screen. The 🟡 colour communicates "can wait," but the user experience is already degraded.

**Item #12 (no Order Search or Return Management page, 🟡) should be elevated to 🔴.** CS agents cannot directly look up an order — every order access requires navigating through Customer Search first. In a high-volume CS environment, that is not a UX inconvenience; it is a measurable increase in average handle time on every call. Returns-related calls are highest-stakes and most time-sensitive. I would re-rank this above the structural refactor items that have no user-facing symptom until a merge conflict materialises.

---

### 4. The one cross-agent observation

The Backoffice executive dashboard's two broken KPI tiles look like one problem from the outside. They are actually three independent failure modes converging on the same screen:

1. **Three projections were never built** (UXE — structural gap)
2. **INV-3 suppresses the upstream events those projections would consume** (PSA — correctness bug)
3. **`BackofficeTestFixture` has no cascade-aware message tracking helpers**, so even after #1 and #2 are fixed, no test in the suite can verify the full event → projection → dashboard pipeline is working (QAE/F-8)

You only see all three when you read across sections. The danger is that M32.4 ships the projection fixes (#1), closes the milestone, and the dashboard still shows partial zeroes because #2 and #3 were scoped as separate concerns. An executive sees broken KPIs and draws conclusions about system reliability. INV-3 must ship before projection work begins; F-8 must ship before that work is considered verified. The priority list treats these as independent items. They are a sequenced dependency chain.

---

## QAE Cross-Review

### 1. What surprised me in the PSA findings

**INV-3 exposed a blind spot in my own coverage assessment.** Inventory showed green in my coverage matrix — both unit and integration tests present. But if `Inventory.Api.IntegrationTests` drives `AdjustInventoryEndpoint` directly, those tests are verifying a code path that silently suppresses `InventoryAdjusted` and `LowStockDetected`. Passing tests. Wrong behavior. That's worse than a test gap: it's false confidence. I was evaluating test *presence*, not test *fidelity to the production path*. INV-3 is the first fix on this list for exactly that reason.

**XC-3 and F-8 together describe the same future failure from opposite ends.** PSA flagged that `AcknowledgeAlert`'s manual `session.SaveChangesAsync()` will cause ordering issues the moment it needs to publish an outbox message. I flagged F-8 — `BackofficeTestFixture` has no `ExecuteAndWaitAsync()` or `TrackedHttpCall()`. The handler has a transaction anti-pattern that hasn't triggered yet; the test fixture has no ability to detect the cascade when it does. Each finding is medium severity alone. Together they are a silent failure waiting for a single new integration event.

---

### 2. What surprised me in the UXE findings

**The `ReturnDenied`/`ReturnRejected` and `ExchangeDenied`/`ExchangeRejected` pairs are a concrete test assertion problem, not just a readability issue.** When I assert on `mt_events` after a return inspection failure, I need to know whether to expect `ReturnRejected` or `ReturnDenied`. If a developer asserts the wrong one, the test can pass against a broken handler or fail against a correct one, with no type-system guard to catch the mistake. These names live in `ReturnEvents.cs` — already an omnibus 140-line file (R-2). The UXE rename and PSA's R-2 refactor need to ship as one changeset.

**`CheckoutCompleted` with two different payloads in `Messages.Contracts`** should be pulled out of `⚠️` and treated as a correctness risk. Two records with the same name and divergent shapes is a deserialization bug waiting to fire when a consumer binds the wrong one. That is not a cosmetic rename.

---

### 3. Priority list items I'd re-rank

**F-8 (item #26, 🟢 LOW) needs to be 🟡 MEDIUM.** The Convergence Summary notes the missing Backoffice projections are partly unbuilt because there's no test harness to verify them. Green-low signals it can wait until *after* the projections are written. It needs to come *before* them — that's the whole point.

**XC-1 (item #23, ADR only)** is under-weighted from a test-writing perspective. Four validator placement conventions means four different locations a developer might search when writing a sad-path test. The resolution should explicitly include normalization in Returns and Vendor Portal, not just an ADR.

**Item #18 (`CheckoutCompleted` duplicate payload) should be 🔴**, not 🟡. See above.

---

### 4. The one cross-agent observation

Reading all three sections together, the Vendor Portal's failure mode is more complete than any section alone shows. PSA: structurally fragmented, no validators on any command. QAE: all E2E tests globally `@ignore`, no bUnit project. UXE: all three outbound integration events are command-masquerades.

The result: `SubmitChangeRequest` accepts user-supplied payload with zero validation, flows through a fragmented handler, emits a command-named integration event into Product Catalog, and **not a single automated test at any layer — unit, integration, bUnit, or E2E — would fail if the entire flow broke silently**. VP-6 is listed as a correctness risk in the PSA section. But without F-2 and F-4 resolved, no test exists to surface it until a vendor submits a malformed payload in production. That compounding is invisible unless you read all three sections together.
