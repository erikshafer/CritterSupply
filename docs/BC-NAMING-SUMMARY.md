# Bounded Context Naming - Quick Reference

**Date:** 2026-02-16  
**Full Analysis:** See `BC-NAMING-ANALYSIS.md`

---

## Proposed Name Changes

| Current Folder             | Proposed Name   | Why Change?                                               |
|----------------------------|-----------------|-----------------------------------------------------------|
| `Order Management/`        | **Orders**      | Simpler, industry standard (Shopify Orders, Amazon Orders) |
| `Payment Processing/`      | **Payments**    | Shorter, clearer (Stripe Payments, PayPal Payments)      |
| `Shopping Management/`     | **Shopping**    | Allows future expansion, removes vague "Management"       |
| `Inventory Management/`    | **Inventory**   | Simpler (though "Management" somewhat justified)          |
| `Fulfillment Management/`  | **Fulfillment** | Industry standard (Amazon Fulfillment)                    |

---

## Contexts That Should Keep Current Names

✅ **Customer Identity** - Perfect as-is  
✅ **Product Catalog** - Perfect as-is  
✅ **Customer Experience** - Perfect as-is  
✅ **Vendor Identity** (planned) - Perfect as-is  
✅ **Vendor Portal** (planned) - Perfect as-is  
✅ **Returns** (planned) - Perfect as-is  

---

## Naming Philosophy

1. **Use domain language** - Terms customers/business users understand
2. **Be specific, not generic** - "Orders" > "Order Management"
3. **Follow industry conventions** - Makes onboarding easier
4. **Reserve "Management" for truly managerial contexts** - Use sparingly
5. **Parallel naming** - Orders/Returns, Customer Identity/Vendor Identity

---

## Current vs Proposed Quick Map

```
Current Folder Name          → Proposed Conceptual Name
─────────────────────────────────────────────────────────
Order Management            → Orders
Payment Processing          → Payments
Shopping Management         → Shopping
Inventory Management        → Inventory
Fulfillment Management      → Fulfillment
Customer Identity           → Customer Identity (no change)
Product Catalog             → Product Catalog (no change)
Customer Experience         → Customer Experience (no change)
```

---

## Implementation Status

**Phase 1 (This PR):** ✅ Documentation updated with conceptual names  
**Phase 2 (Future PR):** Folder/namespace renaming (deferred due to scale)

---

## Why "Management" is Overused

**Problem:** 4 of 5 core BCs use "Management" suffix
- Order **Management**
- Payment **Processing** (at least different from "Management")
- Shopping **Management**
- Inventory **Management**
- Fulfillment **Management**

**Issue:** Generic, doesn't convey what makes each BC unique
- Orders orchestrates a saga across 4 BCs
- Payments integrates with external gateways
- Shopping manages cart lifecycle
- Inventory implements two-phase reservation
- Fulfillment coordinates physical operations

**Solution:** Drop "Management", let the noun speak for itself
- **Orders** - Clearly about orders
- **Payments** - Clearly about payments
- **Shopping** - Clearly about shopping/cart
- **Inventory** - Clearly about stock
- **Fulfillment** - Clearly about shipping

---

## Special Case: Order Management

**Could keep "Order Management"** IF you want to emphasize:
- This BC does the most coordination (saga orchestration)
- It's the hub connecting Payments, Inventory, Fulfillment
- "Management" reflects orchestration complexity

**But "Orders" is simpler** and industry standard:
- Shopify: "Orders API"
- Amazon: "Orders"
- WooCommerce: "Orders"

**Recommendation:** Use "Orders" (simpler is better when it doesn't lose meaning)

---

## When to Use "Management"

**Good use cases:**
- **Inventory Management** - Could argue this BC "manages" stock levels, reservations, allocations
- Context that truly manages other concerns (rare)

**Bad use cases:**
- Generic suffix added to every BC ("Cart Management", "Payment Management")
- When the noun alone is clear ("Orders" not "Order Management")

**Rule of thumb:** If 50%+ of your BCs use "Management", you're overusing it

---

## Reference

For detailed analysis, rationale, and critique of all BC responsibilities:
- **Full Analysis:** `docs/BC-NAMING-ANALYSIS.md` (20+ pages)
- **CONTEXTS.md:** Updated with architectural emphasis
- **README.md:** Updated with proposed names
- **CLAUDE.md:** Updated with folder mapping
