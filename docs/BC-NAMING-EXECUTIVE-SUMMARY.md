# Bounded Context Naming - Executive Summary

**Date:** 2026-02-16  
**Issue:** Overuse of "Management" suffix across bounded contexts  
**Goal:** Simplify naming to follow industry standards and reduce cognitive load

---

## TL;DR - Recommended Changes

| Change "Management" To... | Why?                                  |
|---------------------------|---------------------------------------|
| **Orders**                | Industry standard (Shopify, Amazon)   |
| **Payments**              | Industry standard (Stripe, PayPal)    |
| **Shopping**              | Clearer, allows future expansion      |
| **Inventory**             | Simpler (though "Management" OK here) |
| **Fulfillment**           | Industry standard (Amazon FBA)        |

**Status:** Documentation updated (this PR), folder renaming deferred (future PR)

---

## The Problem

**4 of 5 core BCs use "Management":**
- Order **Management**
- Shopping **Management**
- Inventory **Management**
- Fulfillment **Management**

**Issues:**
1. Vague - "Management" doesn't convey what makes each BC unique
2. Verbose - Longer names, more typing, harder to discuss
3. Non-standard - Industry uses simpler names (Shopify Orders, Stripe Payments)

---

## The Solution

**Drop "Management" suffix, use simple nouns:**
- Orders (not Order Management)
- Payments (not Payment Processing)
- Shopping (not Shopping Management)
- Inventory (not Inventory Management)
- Fulfillment (not Fulfillment Management)

**Benefits:**
- ✅ Shorter, clearer names
- ✅ Industry-aligned (easier onboarding)
- ✅ Reduces cognitive load
- ✅ Emphasizes domain concepts (Orders, Payments) over technical jargon (Management, Processing)

---

## Current State Assessment

### ✅ Keep As-Is (Already Excellent)
- **Customer Identity** - Clear, domain-specific
- **Product Catalog** - Industry term, well-understood
- **Customer Experience** - BFF pattern, clear scope
- **Vendor Identity** (planned) - Parallel to Customer Identity
- **Vendor Portal** (planned) - Industry convention
- **Returns** (planned) - Simple, clear

### ⚠️ Rename (High Priority)
- **Order Management** → **Orders** (industry standard)
- **Payment Processing** → **Payments** (shorter, clearer)

### ⚠️ Rename (Medium Priority)
- **Shopping Management** → **Shopping** (allows expansion)

### ⚠️ Rename (Low Priority)
- **Inventory Management** → **Inventory** (though "Management" is more justified here)
- **Fulfillment Management** → **Fulfillment** (industry standard)

---

## What This PR Does

**✅ Completed:**
1. Comprehensive analysis document (`docs/BC-NAMING-ANALYSIS.md` - 20+ pages)
2. Quick reference summary (`docs/BC-NAMING-SUMMARY.md` - 3 pages)
3. Visual comparison (`docs/BC-NAMING-VISUAL.md` - 5 pages)
4. Updated CONTEXTS.md with:
   - Architectural pattern emphasis (saga, two-phase reservation, stateless BFF)
   - Folder name references with links to analysis
   - Improved BC summaries
5. Updated README.md with proposed names and notes
6. Updated CLAUDE.md with folder mapping table

**❌ Deferred (Future PR):**
- Actual folder/namespace renaming
- `.csproj` file updates
- `.sln` and `.slnx` solution file updates
- Test project renaming
- Docker Compose service name changes

**Why deferred?**
- Large-scale refactoring = high risk of build/test breaks
- Better to align on naming **conceptually** first
- Separate PR allows focused review of technical changes

---

## Architectural Highlights Added to CONTEXTS.md

### Orders (formerly "Order Management")
> "The Orders context **orchestrates** the order lifecycle across Payments, Inventory, and Fulfillment using a **stateful saga**..."

**Emphasis:** Saga orchestration pattern is THE defining characteristic

---

### Inventory (formerly "Inventory Management")
> "It implements a **two-phase reservation pattern** (soft holds → committed allocations) to prevent overselling..."

**Emphasis:** Sophisticated reservation workflow, not just "management"

---

### Product Catalog
> "**Unlike Orders, Payments, and Inventory, this BC uses Marten as a document store (NOT event sourced)**..."

**Emphasis:** Architectural divergence - document store vs event sourcing

---

### Customer Experience
> "The Customer Experience context is a **stateless BFF** that composes views from multiple domain BCs..."

**Emphasis:** No domain logic, no persistence - pure composition

---

## Naming Philosophy

**5 Core Principles:**

1. **Use Domain Language**
   - "Orders" (customer-facing term) > "Order Management" (technical)

2. **Be Specific, Not Generic**
   - "Payments" (clear) > "Payment Processing" (verbose)

3. **Follow Industry Conventions**
   - Shopify Orders, Stripe Payments, Amazon Fulfillment

4. **Reserve "Management" for True Coordination**
   - Use only when orchestration/coordination is PRIMARY responsibility
   - Even then, simpler is often better

5. **Be Consistent**
   - Orders/Returns (both lifecycle BCs)
   - Customer Identity/Vendor Identity (both auth BCs)

---

## Industry Comparison

| Platform | Their Terms                                        | CritterSupply (Proposed) |
|----------|---------------------------------------------------|--------------------------|
| Shopify  | Orders, Payments, Inventory, Products, Customers  | ✅ Matches               |
| Stripe   | Payments, Checkout, Customers, Products           | ✅ Matches               |
| Amazon   | Orders, Payments, Inventory (FBA), Fulfillment    | ✅ Matches               |

**Result:** Proposed names align with industry leaders

---

## Decision: Keep "Management" for Inventory?

**Arguments FOR keeping "Inventory Management":**
- Two-phase reservation pattern is complex (Reserved → Committed → Released)
- Manages stock levels, reservations, allocations across warehouses
- "Management" reflects coordination sophistication

**Arguments AGAINST keeping "Inventory Management":**
- "Inventory" alone is industry standard (Shopify Inventory, WooCommerce Inventory)
- Shorter is clearer when meaning isn't lost
- Other complex BCs don't use "Management" (Orders saga is complex too)

**Recommendation:** Drop "Management", use "Inventory"  
**Rationale:** Consistency > edge case justification

---

## Special Case: Order Management

**Could argue for keeping "Order Management" because:**
- Orders BC is the most coordinated (saga across 4 BCs)
- "Management" emphasizes orchestration role
- It's the hub connecting Payments, Inventory, Fulfillment

**But "Orders" is still better because:**
- Industry standard (Shopify Orders API, Amazon Orders)
- Simpler = clearer (6 chars vs 16 chars)
- Parallel to "Returns" (both lifecycle BCs)
- Documentation can explain saga role without name verbosity

**Decision:** Use "Orders"  
**Rationale:** Simplicity wins when industry convention supports it

---

## Implementation Roadmap

### Phase 1: Conceptual Alignment (This PR) ✅
- Document analysis and recommendations
- Update CONTEXTS.md, README.md, CLAUDE.md with proposed names
- Add references linking folder names to conceptual names
- No code changes, no build risk

### Phase 2: Folder/Namespace Renaming (Future PR) 📋
- Rename folders in `src/` and `tests/`
- Update namespace declarations in all `.cs` files
- Update `.csproj` file references
- Update `.sln` and `.slnx` solution files
- Update Docker Compose service names
- Update all documentation paths
- Run full test suite to verify
- High risk, dedicated PR with focused review

---

## Bottom Line

**Problem:** Overuse of "Management" across BCs (4 of 5)  
**Solution:** Use simple nouns aligned with industry standards  
**Benefit:** Shorter names, clearer intent, easier onboarding  
**Status:** Conceptual names documented (this PR), folder renaming deferred (future PR)  
**Quality:** BC architecture is excellent, only naming needs improvement  

**Recommendation:** Approve this PR, schedule folder renaming for later cycle

---

## Quick Reference

- **Full Analysis (20+ pages):** `docs/BC-NAMING-ANALYSIS.md`
- **Summary (3 pages):** `docs/BC-NAMING-SUMMARY.md`
- **Visual Comparison (5 pages):** `docs/BC-NAMING-VISUAL.md`
- **Executive Summary (this doc):** `docs/BC-NAMING-EXECUTIVE-SUMMARY.md`
