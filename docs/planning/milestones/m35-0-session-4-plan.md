# M35.0 Session 4 Plan — Prerequisite Resolution & Event Modeling

**Date:** 2026-03-27
**Session Type:** Prerequisite resolution and event modeling (not feature implementation)
**Objective:** Clear Track 3 items for Session 5 implementation by resolving event modeling gaps and Vendor Identity prerequisites

---

## Context

Session 3 assessed all Track 3 items and found none ready for implementation. Every item requires either event modeling work or Vendor Identity architectural prerequisites before feature code can be written.

This session resolves those blockers. The deliverables are:
1. Event modeling artifacts for each Track 3 item (named events, commands, read models, Given/When/Then scenarios, slice boundaries)
2. ASIE determination of which items are blocked by Vendor Identity issues #254 and #255
3. A revised sequencing of Track 3 work that Session 5 can implement without re-modeling

---

## Track 3 Items Under Assessment

### Item 1: Vendor Portal Team Management

**Session 3 finding:** Blocked by Vendor Identity architectural work (issues #254, #255)

**ASIE assessment required:**
- Is issue #254 (EF Core project structure) a prerequisite?
- Is issue #255 (CreateVendorTenant command) a prerequisite?
- What is the minimum Vendor Identity work needed to unblock?

**EMF modeling required:**
- Team management user flows (invite, deactivate, role change)
- Read models for team roster display
- Given/When/Then scenarios for admin team operations

### Item 2: Exchange v2 — Cross-Product Exchanges

**Session 3 finding:** Requires cross-product exchange modeling

**Current state:** Same-SKU exchanges fully implemented in Returns BC (ApproveExchange, DenyExchange, ExchangeApproved/Denied/Completed/Rejected events). Phase 1 constraint: `ReplacementSku == OriginalSku`.

**EMF modeling required:**
- Cross-product exchange flow (different SKU replacement)
- Price difference handling (refund if replacement cheaper, payment if more expensive)
- Inventory availability check for replacement SKU
- Given/When/Then scenarios for cross-product exchange paths

### Item 3: Product Catalog Evolution

**Session 3 finding:** Requires variants/listings/marketplaces design

**Current state:** Document store with Marten. Evolution plan exists at `docs/planning/catalog-listings-marketplaces-evolution-plan.md` with approved decision to migrate to event sourcing.

**EMF modeling required:**
- Event sourcing migration slice (ProductMigrated bootstrap event)
- Granular domain events replacing coarse ProductUpdated
- Variant model (parent product + variant children)
- Given/When/Then scenarios for migration and variant operations

### Item 4: Search BC

**Session 3 finding:** Requires full-text product search design

**Assessment:** Out of scope for M35.0 Track 3. No existing code, no evolution plan, no BC boundary defined. Deferred to future milestone.

---

## ASIE Prerequisite Assessment Results

### Issue #254: Vendor Identity EF Core Project Structure

**Finding:** ALREADY IMPLEMENTED in the codebase.

Evidence:
- `src/Vendor Identity/VendorIdentity/Identity/VendorIdentityDbContext.cs` — EF Core DbContext exists
- `src/Vendor Identity/VendorIdentity/Migrations/` — Two migrations exist (InitialCreate + AddTerminationReason)
- `src/Vendor Identity/VendorIdentity.Api/Program.cs` — Full EF Core + Wolverine + RabbitMQ wiring
- Entity models exist: `VendorTenant.cs`, `VendorUser` (in UserManagement/), `VendorUserInvitation` (in UserInvitations/)
- Port 5240 assigned per CLAUDE.md
- Both projects in solution

**Determination:** Issue #254 is NOT a blocker. The work described in the issue is complete in code. The GitHub issue should be closed.

### Issue #255: CreateVendorTenant Command

**Finding:** ALREADY IMPLEMENTED in the codebase.

Evidence:
- `src/Vendor Identity/VendorIdentity/TenantManagement/CreateVendorTenant.cs` — Command record exists
- `src/Vendor Identity/VendorIdentity/TenantManagement/CreateVendorTenantHandler.cs` — Handler exists
- `src/Vendor Identity/VendorIdentity/TenantManagement/CreateVendorTenantValidator.cs` — Validator exists
- `src/Shared/Messages.Contracts/VendorIdentity/VendorTenantCreated.cs` — Integration event exists
- Additional tenant lifecycle commands exist: Suspend, Reinstate, Terminate (with handlers and validators)

**Determination:** Issue #255 is NOT a blocker. The work described in the issue is complete in code. The GitHub issue should be closed.

### Impact on Track 3 Items

| Track 3 Item | Blocked by #254? | Blocked by #255? | Identity Prerequisites Met? |
|---|---|---|---|
| Vendor Portal Team Management | No (implemented) | No (implemented) | ✅ Yes — VendorIdentity BC infrastructure exists |
| Exchange v2 | No (not related) | No (not related) | ✅ N/A — no identity dependency |
| Product Catalog Evolution | No (not related) | No (not related) | ✅ N/A — no identity dependency |
| Search BC | N/A (deferred) | N/A (deferred) | N/A |

**ASIE summary:** All Track 3 items that were assessed as blocked by Vendor Identity prerequisites (#254, #255) are in fact NOT blocked. The VendorIdentity BC has the EF Core project structure, entity models, tenant lifecycle commands, user management infrastructure, invitation workflow, and integration event contracts already implemented. The GitHub issues are stale — the work was completed but the issues were never closed.

---

## Event Modeling Results

### Exchange v2: Cross-Product Exchange

**Scope:** Extend existing same-SKU exchange flow to support cross-product (different SKU) replacement.

**Named Domain Events:**
| Event | Description |
|---|---|
| `CrossProductExchangeRequested` | Customer requests exchange with different replacement SKU |
| `ExchangePriceDifferenceCalculated` | System calculates price delta between original and replacement |
| `ExchangeAdditionalPaymentRequired` | Replacement costs more than original; customer must pay difference |
| `ExchangeAdditionalPaymentCaptured` | Customer paid the price difference |
| `ExchangePartialRefundIssued` | Replacement costs less; customer receives partial refund |

**Named Commands:**
| Command | Initiator | Description |
|---|---|---|
| `RequestCrossProductExchange` | Customer (via CS agent) | Initiate exchange with different SKU |
| `CalculateExchangePriceDifference` | System | Compute price delta |
| `CaptureExchangeAdditionalPayment` | System → Payments BC | Collect additional payment |
| `IssueExchangePartialRefund` | System → Payments BC | Refund price difference |

**Read Models:**
| Read Model | Contents | Consumer |
|---|---|---|
| `ExchangeOptionsView` | Available replacement SKUs, price differences, stock status | CS agent / customer UI |
| `ExchangeStatusView` (extended) | Current exchange status including price difference details | CS agent / customer UI |

**Slice Table:**

| # | Slice Name | Command | Events | View | BC | Priority |
|---|---|---|---|---|---|---|
| 1 | Request cross-product exchange | `RequestCrossProductExchange` | `CrossProductExchangeRequested`, `ExchangePriceDifferenceCalculated` | ExchangeStatusView (pending, price delta shown) | Returns | P0 |
| 2 | Approve cross-product exchange (replacement cheaper) | `ApproveExchange` | `ExchangeApproved`, `ExchangePartialRefundIssued` | ExchangeStatusView (approved, refund pending) | Returns + Payments | P0 |
| 3 | Approve cross-product exchange (replacement more expensive) | `ApproveExchange` | `ExchangeApproved`, `ExchangeAdditionalPaymentRequired` | ExchangeStatusView (approved, payment required) | Returns + Payments | P0 |
| 4 | Capture additional payment | `CaptureExchangeAdditionalPayment` | `ExchangeAdditionalPaymentCaptured` | ExchangeStatusView (payment captured) | Payments → Returns | P1 |
| 5 | Deny cross-product exchange | `DenyExchange` | `ExchangeDenied` | ExchangeStatusView (denied, reason) | Returns | P0 |

**Given/When/Then Scenarios:**

```
Scenario 1: Request cross-product exchange with cheaper replacement
Given:  OrderPlaced { orderId: "ord-1", items: [{ sku: "SKU-100", unitPrice: 29.99 }] }
        ShipmentDelivered { orderId: "ord-1" }
When:   RequestCrossProductExchange { orderId: "ord-1", originalSku: "SKU-100", replacementSku: "SKU-200", reason: "Prefer different color" }
Then:   CrossProductExchangeRequested { returnId: "ret-1", originalSku: "SKU-100", replacementSku: "SKU-200" }
        ExchangePriceDifferenceCalculated { returnId: "ret-1", originalPrice: 29.99, replacementPrice: 19.99, difference: -10.00 }

Scenario 2: Approve cross-product exchange — replacement cheaper, partial refund issued
Given:  CrossProductExchangeRequested { returnId: "ret-1", replacementSku: "SKU-200" }
        ExchangePriceDifferenceCalculated { difference: -10.00 }
        StockAvailable { sku: "SKU-200", quantity: 5 }
When:   ApproveExchange { returnId: "ret-1" }
Then:   ExchangeApproved { returnId: "ret-1", replacementSku: "SKU-200" }
        ExchangePartialRefundIssued { returnId: "ret-1", refundAmount: 10.00 }

Scenario 3: Approve cross-product exchange — replacement more expensive, payment required
Given:  CrossProductExchangeRequested { returnId: "ret-1", replacementSku: "SKU-300" }
        ExchangePriceDifferenceCalculated { originalPrice: 19.99, replacementPrice: 39.99, difference: 20.00 }
When:   ApproveExchange { returnId: "ret-1" }
Then:   ExchangeApproved { returnId: "ret-1", replacementSku: "SKU-300" }
        ExchangeAdditionalPaymentRequired { returnId: "ret-1", amount: 20.00 }

Scenario 4: Deny cross-product exchange — replacement out of stock
Given:  CrossProductExchangeRequested { returnId: "ret-1", replacementSku: "SKU-200" }
        StockUnavailable { sku: "SKU-200" }
When:   DenyExchange { returnId: "ret-1", reason: "Replacement item out of stock" }
Then:   ExchangeDenied { returnId: "ret-1", reason: "Replacement item out of stock" }

Scenario 5: Cross-product exchange — 30-day eligibility window expired
Given:  OrderPlaced { orderId: "ord-1" }
        ShipmentDelivered { orderId: "ord-1", deliveredAt: "2026-02-01" }
When:   RequestCrossProductExchange { orderId: "ord-1", requestedAt: "2026-03-15" }
Then:   ExchangeDenied { reason: "Exchange window expired (30 days post-delivery)" }
```

**Boundary:** Cross-product exchange extends the existing Returns BC exchange flow. It does NOT create a new aggregate — it extends the existing Return aggregate with additional event types. Price difference calculation queries Pricing BC. Payment collection/refund routes through Payments BC (same as existing refund flow).

**Not in this slice:** Multi-item exchanges (exchanging multiple items in one return), exchange for items from different orders, exchange without return (advance ship).

---

### Vendor Portal Team Management

**Scope:** Admin users manage their vendor team — view roster, invite new members, change roles, deactivate/reactivate users.

**Named Domain Events (already defined in Messages.Contracts):**
| Event | Status |
|---|---|
| `VendorUserInvited` | ✅ Contract exists |
| `VendorUserActivated` | ✅ Contract exists |
| `VendorUserDeactivated` | ✅ Contract exists |
| `VendorUserReactivated` | ✅ Contract exists |
| `VendorUserRoleChanged` | ✅ Contract exists |
| `VendorUserInvitationResent` | ✅ Contract exists |
| `VendorUserInvitationRevoked` | ✅ Contract exists |

**Named Commands:**
| Command | Initiator | Status |
|---|---|---|
| `InviteVendorUser` | Vendor Admin | Exists in VendorIdentity/UserInvitations/ |
| `AcceptInvitation` | Invited user | Exists in VendorIdentity/UserInvitations/ |
| `DeactivateVendorUser` | Vendor Admin | Exists in VendorIdentity/UserManagement/ |
| `ReactivateVendorUser` | Vendor Admin | Exists in VendorIdentity/UserManagement/ |
| `ChangeVendorUserRole` | Vendor Admin | Exists in VendorIdentity/UserManagement/ |
| `ResendInvitation` | Vendor Admin | Exists in VendorIdentity/UserInvitations/ |
| `RevokeInvitation` | Vendor Admin | Exists in VendorIdentity/UserInvitations/ |

**Read Models (needed for Vendor Portal BFF):**
| Read Model | Contents | Consumer |
|---|---|---|
| `TeamRosterView` | List of vendor users with roles, statuses, last login | Vendor Portal team management page |
| `PendingInvitationsView` | Active invitations with status, expiry | Vendor Portal team management page |

**Slice Table:**

| # | Slice Name | Command | Events | View | BC | Priority |
|---|---|---|---|---|---|---|
| 1 | View team roster | *(query)* | — | TeamRosterView | Vendor Portal (BFF) → Vendor Identity | P0 |
| 2 | Invite team member | `InviteVendorUser` | `VendorUserInvited` | PendingInvitationsView (new entry) | Vendor Identity | P0 |
| 3 | Accept invitation | `AcceptInvitation` | `VendorUserActivated` | TeamRosterView (new active member) | Vendor Identity | P0 |
| 4 | Change user role | `ChangeVendorUserRole` | `VendorUserRoleChanged` | TeamRosterView (role updated) | Vendor Identity | P1 |
| 5 | Deactivate user | `DeactivateVendorUser` | `VendorUserDeactivated` | TeamRosterView (status: deactivated) | Vendor Identity | P1 |
| 6 | Reactivate user | `ReactivateVendorUser` | `VendorUserReactivated` | TeamRosterView (status: active) | Vendor Identity | P1 |
| 7 | Resend invitation | `ResendInvitation` | `VendorUserInvitationResent` | PendingInvitationsView (resend count++) | Vendor Identity | P2 |
| 8 | Revoke invitation | `RevokeInvitation` | `VendorUserInvitationRevoked` | PendingInvitationsView (entry removed) | Vendor Identity | P2 |

**Given/When/Then Scenarios:**

```
Scenario 1: Admin invites new team member
Given:  VendorTenantCreated { tenantId: "vendor-1", organizationName: "Paws & Claws" }
        VendorUserActivated { userId: "admin-1", role: "Admin" }
When:   InviteVendorUser { tenantId: "vendor-1", email: "alice@pawsandclaws.com", role: "CatalogManager", invitedBy: "admin-1" }
Then:   VendorUserInvited { userId: "user-2", email: "alice@pawsandclaws.com", role: "CatalogManager", expiresAt: "+72h" }
        PendingInvitationsView includes { email: "alice@pawsandclaws.com", status: "Pending", role: "CatalogManager" }

Scenario 2: Invited user accepts invitation and joins team
Given:  VendorUserInvited { userId: "user-2", email: "alice@pawsandclaws.com", token: "hashed-token" }
When:   AcceptInvitation { token: "raw-token", password: "SecurePass123!", firstName: "Alice", lastName: "Smith" }
Then:   VendorUserActivated { userId: "user-2", tenantId: "vendor-1" }
        TeamRosterView includes { name: "Alice Smith", role: "CatalogManager", status: "Active" }

Scenario 3: Admin changes team member role
Given:  VendorUserActivated { userId: "user-2", role: "CatalogManager" }
When:   ChangeVendorUserRole { userId: "user-2", newRole: "ReadOnly", changedBy: "admin-1" }
Then:   VendorUserRoleChanged { userId: "user-2", previousRole: "CatalogManager", newRole: "ReadOnly" }
        TeamRosterView shows { userId: "user-2", role: "ReadOnly" }

Scenario 4: Admin deactivates team member
Given:  VendorUserActivated { userId: "user-2", role: "CatalogManager" }
When:   DeactivateVendorUser { userId: "user-2", deactivatedBy: "admin-1" }
Then:   VendorUserDeactivated { userId: "user-2" }
        TeamRosterView shows { userId: "user-2", status: "Deactivated" }
        SignalR pushes force-logout to user:{user-2}

Scenario 5: Non-admin cannot invite users
Given:  VendorUserActivated { userId: "user-2", role: "CatalogManager" }
When:   InviteVendorUser { invitedBy: "user-2" }
Then:   Rejected { reason: "Insufficient permissions — Admin role required" }

Scenario 6: Invitation expires after 72 hours
Given:  VendorUserInvited { userId: "user-2", expiresAt: "2026-03-25T00:00:00Z" }
When:   System clock reaches "2026-03-28T00:00:01Z"
Then:   VendorUserInvitationExpired { userId: "user-2" }
        PendingInvitationsView shows { userId: "user-2", status: "Expired" }
```

**Boundary:** Team management operations live in Vendor Identity BC. Vendor Portal BFF exposes read-only roster views via HTTP queries to Vendor Identity. Real-time updates (user deactivation, invitation acceptance) push to SignalR groups.

**Not in this slice:** Bulk user import, custom roles (beyond Admin/CatalogManager/ReadOnly), cross-tenant user transfer, SSO/SAML integration.

---

### Product Catalog Evolution — Event Sourcing Migration

**Scope:** Migrate Product Catalog from Marten document store to event sourcing. This is a foundational slice that must complete before variants, listings, or marketplaces can be built.

**Named Domain Events:**
| Event | Description |
|---|---|
| `ProductMigrated` | Bootstrap event for existing products during migration |
| `ProductCreated` | New product added to catalog (replaces document Store) |
| `ProductNameChanged` | Product display name updated |
| `ProductDescriptionChanged` | Product description updated |
| `ProductCategoryChanged` | Product category/subcategory changed |
| `ProductImagesUpdated` | Product images added/removed/reordered |
| `ProductDimensionsChanged` | Product physical dimensions updated |
| `ProductStatusChanged` | Product lifecycle status changed (Active/Discontinued/etc.) |
| `ProductTagsUpdated` | Product tags modified |
| `ProductSoftDeleted` | Product marked as deleted (soft delete) |
| `ProductRestored` | Soft-deleted product restored |

**Named Commands:**
| Command | Initiator | Description |
|---|---|---|
| `CreateProduct` | Admin / Vendor | Add new product (replaces AddProduct) |
| `ChangeProductName` | Admin / Vendor | Update display name (replaces UpdateProductDisplayName) |
| `ChangeProductDescription` | Admin / Vendor | Update description (replaces UpdateProductDescription) |
| `ChangeProductCategory` | Admin | Change category assignment |
| `UpdateProductImages` | Admin / Vendor | Modify image set |
| `ChangeProductDimensions` | Admin / Vendor | Update physical dimensions |
| `ChangeProductStatus` | Admin | Change lifecycle status |
| `UpdateProductTags` | Admin / Vendor | Modify tag set |
| `SoftDeleteProduct` | Admin | Mark product as deleted |
| `RestoreProduct` | Admin | Restore soft-deleted product |
| `MigrateProduct` | System (one-time) | Bootstrap event for migration |

**Read Models:**
| Read Model | Contents | Consumer |
|---|---|---|
| `ProductCatalogView` | Full product data (replaces current document) | All existing consumers |
| `ProductListView` | Summary for list pages (SKU, name, status, category) | Backoffice, Vendor Portal |

**Slice Table:**

| # | Slice Name | Command | Events | View | BC | Priority |
|---|---|---|---|---|---|---|
| 1 | Migrate existing products | `MigrateProduct` | `ProductMigrated` | ProductCatalogView (identical to current) | Product Catalog | P0 |
| 2 | Create product (event-sourced) | `CreateProduct` | `ProductCreated` | ProductCatalogView (new entry) | Product Catalog | P0 |
| 3 | Change product name | `ChangeProductName` | `ProductNameChanged` | ProductCatalogView (name updated) | Product Catalog | P0 |
| 4 | Change product status | `ChangeProductStatus` | `ProductStatusChanged` | ProductCatalogView (status updated) | Product Catalog | P0 |
| 5 | Soft delete product | `SoftDeleteProduct` | `ProductSoftDeleted` | ProductCatalogView (marked deleted) | Product Catalog | P1 |

**Given/When/Then Scenarios:**

```
Scenario 1: Create new product via event sourcing
Given:  (empty stream)
When:   CreateProduct { sku: "SKU-500", name: "Premium Dog Leash", category: "Dog Supplies", status: "Active" }
Then:   ProductCreated { sku: "SKU-500", name: "Premium Dog Leash", category: "Dog Supplies" }
        ProductCatalogView includes { sku: "SKU-500", name: "Premium Dog Leash", status: "Active" }

Scenario 2: Change product name emits granular event
Given:  ProductCreated { sku: "SKU-500", name: "Premium Dog Leash" }
When:   ChangeProductName { sku: "SKU-500", newName: "Deluxe Dog Leash" }
Then:   ProductNameChanged { sku: "SKU-500", previousName: "Premium Dog Leash", newName: "Deluxe Dog Leash" }
        ProductCatalogView shows { sku: "SKU-500", name: "Deluxe Dog Leash" }

Scenario 3: Migrate existing document to event stream
Given:  Product document exists { sku: "SKU-100", name: "Parakeet Perch", status: "Active" }
When:   MigrateProduct { sku: "SKU-100" }
Then:   ProductMigrated { sku: "SKU-100", name: "Parakeet Perch", status: "Active", ... (full snapshot) }
        ProductCatalogView identical to previous document

Scenario 4: Discontinue product emits status change event
Given:  ProductCreated { sku: "SKU-500", status: "Active" }
When:   ChangeProductStatus { sku: "SKU-500", newStatus: "Discontinued", reason: "Supplier discontinued" }
Then:   ProductStatusChanged { sku: "SKU-500", previousStatus: "Active", newStatus: "Discontinued", reason: "Supplier discontinued" }
        ProductDiscontinued integration event published
```

**Boundary:** This slice covers the core event sourcing migration and basic CRUD operations. It does NOT include variants, listings, marketplaces, or structured category taxonomy. Those are Phase 2+ and depend on this foundational work.

**Migration strategy:** Run `MigrateProduct` for each existing Marten document as a one-time bootstrap. After migration, all writes go through event-sourced handlers. Read side uses `SingleStreamProjection<ProductCatalogView>` that produces the same shape as the current document store.

---

## Clearance Status Summary

| Track 3 Item | EMF Cleared? | ASIE Cleared? | Ready for Session 5? | Notes |
|---|---|---|---|---|
| **Exchange v2 (cross-product)** | ✅ Yes | ✅ Yes (no identity dependency) | ✅ **Ready** | Extends existing Returns BC exchange flow |
| **Vendor Portal Team Management** | ✅ Yes | ✅ Yes (#254/#255 already implemented) | ✅ **Ready** | Backend commands exist; needs BFF endpoints + Blazor page |
| **Product Catalog Evolution** | ✅ Yes | ✅ Yes (no identity dependency) | ✅ **Ready** | Migration-first approach; foundational for variants/listings |
| **Search BC** | ❌ Deferred | N/A | ❌ **Not in scope** | No existing design; deferred to future milestone |

## Session 5 Recommended Sequencing

1. **Product Catalog Evolution — Migration slice (P0)** — Foundational; all future catalog work depends on this. Start with `ProductMigrated` bootstrap, then convert existing CRUD handlers to event-sourced equivalents.

2. **Exchange v2 — Cross-product exchange (P0)** — Extends existing, well-tested Returns BC flow. Smallest delta from current state. Requires coordination with Payments BC for price difference handling.

3. **Vendor Portal Team Management — Roster + Invite (P0)** — Backend infrastructure exists; needs BFF proxy endpoints in VendorPortal.Api and Blazor WASM team management page. Larger frontend surface area.

---

## Session Plan Updates

*This section will be updated as work progresses during the session.*

### Phase 1: Assessment & Modeling (completed)
- [x] Read Session 3 retrospective — understood Track 3 blockers
- [x] ASIE: Assessed issues #254 and #255 — both already implemented in code
- [x] ASIE: Determined no Track 3 items are blocked by Vendor Identity prerequisites
- [x] EMF: Modeled Exchange v2 cross-product exchange (5 slices, 5 scenarios)
- [x] EMF: Modeled Vendor Portal team management (8 slices, 6 scenarios)
- [x] EMF: Modeled Product Catalog Evolution migration (5 slices, 4 scenarios)
- [x] Deferred Search BC to future milestone (no existing design)

### Phase 2: Feature File Commits
- [x] Committed cross-product exchange feature file
- [x] Committed team management feature file
- [x] Committed catalog evolution feature file

### Phase 3: Session Bookends
- [x] Session plan committed (this document)
- [x] Session retrospective committed
- [x] CURRENT-CYCLE.md updated

---

*Plan Created: 2026-03-27*
*Last Updated: 2026-03-27*
