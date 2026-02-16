# Bounded Context Naming - Visual Comparison

## Current State (Folder Names)

```
┌─────────────────────────────────────┐
│     Orders/               │  🎯 → Orders
│     (saga orchestration)            │
└─────────────────────────────────────┘
           ↓ orchestrates
    ┌──────┴──────┬──────────┬──────────┐
    ↓             ↓          ↓          ↓

┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
│  Payment     │ │  Inventory   │ │ Fulfillment  │ │  Shopping    │
│  Processing/ │ │  Management/ │ │ Management/  │ │ Management/  │
│              │ │              │ │              │ │              │
│ 🎯→ Payments │ │ 🎯→ Inventory│ │🎯→Fulfillment│ │ 🎯→ Shopping │
└──────────────┘ └──────────────┘ └──────────────┘ └──────────────┘

┌──────────────┐ ┌──────────────┐ ┌──────────────┐
│   Customer   │ │   Product    │ │   Customer   │
│   Identity/  │ │   Catalog/   │ │ Experience/  │
│              │ │              │ │              │
│   ✅ Keep    │ │   ✅ Keep    │ │   ✅ Keep    │
└──────────────┘ └──────────────┘ └──────────────┘
```

---

## Proposed Conceptual Names (This PR)

```
┌─────────────────────────────────────┐
│           Orders                    │  ← Simpler, industry standard
│     (saga orchestration)            │
└─────────────────────────────────────┘
           ↓ orchestrates
    ┌──────┴──────┬──────────┬──────────┐
    ↓             ↓          ↓          ↓

┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
│   Payments   │ │  Inventory   │ │ Fulfillment  │ │   Shopping   │
│              │ │              │ │              │ │              │
│ (auth/       │ │ (two-phase   │ │ (picking/    │ │ (cart        │
│  capture/    │ │  reservation)│ │  packing/    │ │  lifecycle)  │
│  refunds)    │ │              │ │  shipping)   │ │              │
└──────────────┘ └──────────────┘ └──────────────┘ └──────────────┘

┌──────────────┐ ┌──────────────┐ ┌──────────────┐
│   Customer   │ │   Product    │ │   Customer   │
│   Identity   │ │   Catalog    │ │  Experience  │
│              │ │              │ │              │
│ (addresses/  │ │ (master      │ │ (BFF/        │
│  profiles)   │ │  product     │ │  Blazor/     │
│              │ │  data)       │ │  SSE)        │
└──────────────┘ └──────────────┘ └──────────────┘
```

---

## Naming Pattern Analysis

### "Management" Usage (Current)

```
✅ Appropriate:
   - Inventory Management (manages stock, reservations, allocations)
   
⚠️ Questionable:
   - Order Management (orders + orchestration, but "Orders" is clearer)
   
❌ Overused/Vague:
   - Shopping Management (just "Shopping" is clearer)
   - Fulfillment Management (just "Fulfillment" is clearer)
   - Payment Processing (not "Management" but still verbose)
```

### Proposed Pattern

```
Simple Nouns:
   Orders, Payments, Shopping, Inventory, Fulfillment
   ↓
   Clear, concise, industry standard
   
Domain-Specific Terms:
   Customer Identity, Product Catalog, Customer Experience
   ↓
   Two words maximum, domain-meaningful
   
Multi-Tenancy Contexts:
   Vendor Identity, Vendor Portal
   ↓
   Parallel to customer-facing equivalents
```

---

## Evolution of Naming

### Phase 1: Generic "Management" ❌
```
Cart Management
Order Management
Payment Management
Inventory Management
Fulfillment Management
↓
Problem: Every BC has "Management", loses meaning
```

### Phase 2: Process-Specific Suffixes ⚠️
```
Payment Processing
Order Orchestration
Inventory Allocation
Cart Handling
↓
Problem: Still verbose, not industry standard
```

### Phase 3: Simple Domain Nouns ✅
```
Payments
Orders
Inventory
Shopping
Fulfillment
↓
Solution: Clear, concise, industry-aligned
```

---

## Industry Comparison

### Shopify
```
Orders API
Payments API
Inventory API
Products API
Customers API
```

### Stripe
```
Payments
Checkout
Invoices
Customers
Products
```

### Amazon
```
Orders
Payments
Inventory (FBA)
Fulfillment
Products
```

### CritterSupply (Proposed)
```
Orders           ← Matches industry
Payments         ← Matches industry
Inventory        ← Matches industry
Shopping         ← Our term (cart-focused)
Fulfillment      ← Matches industry
Product Catalog  ← Our term (emphasizes master data)
```

---

## Key Insights

1. **Industry Standard = Cognitive Load Reduction**
   - Developers familiar with e-commerce expect "Orders" not "Order Management"
   - Onboarding is faster when names match external conventions

2. **Simplicity = Clarity**
   - "Orders" is 6 characters, "Order Management" is 16 characters
   - Shorter names are easier to type, remember, discuss

3. **Reserve "Management" for True Coordination**
   - Only use when orchestration/coordination is THE defining trait
   - Even then, simpler is often better ("Orders" vs "Order Management")

4. **Consistency Matters**
   - Orders/Returns (both lifecycle BCs)
   - Customer Identity/Vendor Identity (both auth BCs)
   - Payments/Fulfillment/Inventory (all single-word nouns)

---

## Folder Renaming Impact (Deferred)

**Current State:**
```
src/Orders/Orders/
src/Payments/Payments/
src/Shopping/Shopping/
```

**Proposed State (Future PR):**
```
src/Orders/Orders/
src/Payments/Payments/
src/Shopping/Shopping/
```

**Breaking Changes:**
- `.sln` and `.slnx` solution file paths
- `.csproj` file references between projects
- Namespace declarations in `.cs` files
- Test project folders and namespaces
- Docker Compose service names
- Documentation paths (CLAUDE.md, README.md)

**Risk:** High (many files touched, build/test breaks possible)  
**Strategy:** Defer to dedicated refactoring PR after conceptual alignment

---

## Decision Matrix

| Factor                  | "Orders" | "Order Management" | Winner        |
|-------------------------|----------|-------------------|---------------|
| Industry Standard       | ✅ Yes   | ❌ No             | Orders        |
| Simplicity              | ✅ Short | ❌ Verbose        | Orders        |
| Emphasizes Saga Role    | ⚠️ No    | ✅ Yes            | Management    |
| Consistency (w/Returns) | ✅ Yes   | ❌ No             | Orders        |
| Cognitive Load          | ✅ Low   | ⚠️ Medium         | Orders        |

**Recommendation:** "Orders" wins 4-1

---

## References

- **Full Analysis:** `BC-NAMING-ANALYSIS.md`
- **Quick Summary:** `BC-NAMING-SUMMARY.md`
- **CONTEXTS.md:** Updated BC summaries with architectural emphasis
- **README.md:** Updated with proposed names
