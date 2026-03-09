# Product Catalog Evolution · Listings BC · Marketplaces BC
## Discovery Session: Product Owner + UX Engineer

**Document Owner:** Product Owner (primary), UX Engineer (addendum)
**Status:** 🟡 Active — D1 ✅ decided (2026-03-09); D4 ✅ decided (PSA); D5 ✅ decided (PSA); D2, D3, D6, D7, D8, D9, D10 awaiting Owner/Erik
**Date:** 2026-03-09
**Last Updated:** 2026-03-09 (PO + UX outputs merged; D1 Owner decision recorded)
**Triggered by:** Owner request to evolve Product Catalog BC toward multi-channel marketplace selling
**Source documents:**
- [`CONTEXTS.md`](../../CONTEXTS.md) — Product Catalog BC section
- Current implementation: `src/Product Catalog/`
- Reference format: [`docs/planning/fulfillment-evolution-plan.md`](fulfillment-evolution-plan.md)

**Companion documents:**
- Principal Engineer synthesis: [`catalog-listings-marketplaces-evolution-plan.md`](catalog-listings-marketplaces-evolution-plan.md) ✅
- Ubiquitous language glossary: [`catalog-listings-marketplaces-glossary.md`](catalog-listings-marketplaces-glossary.md) ✅
- D1 variant model decision + all sign-offs: [`catalog-variant-model.md`](catalog-variant-model.md) ✅
- ADR candidates *(to be authored after Owner decisions)*

---

## Table of Contents

**Part A — Product Owner Analysis**
1. [Review of Current Product Catalog BC](#1-review-of-current-product-catalog-bc)
2. [Discovery Questions — Product Catalog Evolution](#2-discovery-questions--product-catalog-evolution)
3. [Discovery Questions — Listings BC](#3-discovery-questions--listings-bc)
4. [Discovery Questions — Marketplaces BC](#4-discovery-questions--marketplaces-bc)
5. [Business Workflow Sketches](#5-business-workflow-sketches)
6. [Risks and Business Concerns](#6-risks-and-business-concerns)
7. [PO Recommendations](#7-po-recommendations)
8. [Decisions Needed from Owner/Erik](#8-decisions-needed)

**Part B — UX Engineer Perspective**
9. [Grounding Note: Where We Are Starting](#9-grounding-note-where-we-are-starting)
10. [UX Perspective on the Four Missing Catalog Concepts](#10-ux-perspective-on-the-four-missing-catalog-concepts)
11. [UX Review of the Listings Lifecycle](#11-ux-review-of-the-listings-lifecycle)
12. [UX Mapping of Marketplace Identity (ChannelCode)](#12-ux-mapping-of-marketplace-identity-channelcode)
13. [Information Architecture Proposal](#13-information-architecture-proposal)
14. [UX Risk Register](#14-ux-risk-register)
15. [UX Questions for the Product Owner](#15-ux-questions-for-the-product-owner)
16. [UX Recommendations by Priority](#16-ux-recommendations-by-priority)
17. [Summary for Erik and the Principal Engineer](#17-summary-for-erik-and-the-principal-engineer)

---

# Part A — Product Owner Analysis

## 1. Review of Current Product Catalog BC

### 1.1 What the Current BC Gets Right

Let me be direct: **for a Phase 1, the current Product Catalog BC is a solid foundation.** The team made pragmatic, correct decisions. Here's what I'd defend to any e-commerce skeptic:

**✅ SKU as the canonical identity.**  
SKU-as-identifier is exactly how real retailers think. Warehouse staff, buyers, vendors, and customer service all refer to products by SKU. Making SKU the primary key — not some opaque internal GUID — is a business-first decision I fully endorse. Every integration event, every external query, every listing reference should anchor on SKU. This is right.

**✅ ProductStatus captures the actual selling lifecycle.**  
`Active`, `Discontinued`, `ComingSoon`, and `OutOfSeason` map to real e-commerce states that actually drive business behavior. `Discontinued` is one-way (can't reactivate a discontinued product — hard rule, correctly enforced). `OutOfSeason` for holiday or seasonal items (like a Halloween-themed cat costume we'd sell in October) is a concept most systems get wrong by lumping it with discontinued. The team got this right.

**✅ Soft delete only — products are never truly destroyed.**  
This is essential for historical integrity. If a customer ordered a product five years ago and we destroyed it from the system, we'd break order history, returns processing, and vendor audit trails. Soft delete is the correct call.

**✅ Document store, not event sourcing, for master product data.**  
Product data is highly read-heavy and changes infrequently. The current state of a product is what matters for 95% of queries. The team correctly identified that event sourcing would add complexity without proportional benefit here. I agree with this call. The integration events (`ProductAdded`, `ProductUpdated`, `ProductDiscontinued`) give us the downstream notification we need without requiring the full event stream.

**✅ Separation of pricing from catalog.**  
This is a discipline decision that will pay dividends. Pricing is volatile — promotional pricing, MAP pricing, regional pricing, channel-specific pricing. Keeping pricing out of the Product document means we can change pricing aggressively without touching master product data. Good call.

---

### 1.2 What Is Missing — Business Concepts Not Captured

The current BC captures "what we sell." What it doesn't capture is "who makes it, how it's configured per channel, what rules apply, or how products relate to each other." These are the gaps that will block Listings and Marketplaces work.

**❌ Variant / Parent-Child Relationships**  
Today, if we sell a dog collar in Small, Medium, and Large, those are three completely separate SKU records with no relationship to each other. That's not how customers shop and it's not how vendors supply. A customer searches for "Blue Nylon Dog Collar" and expects to see ONE product listing with a size selector — not three separate entries. A vendor supplies one item description with a size chart. This gap is the most urgent business concept missing from the model.

**❌ Vendor Association**  
Which vendor supplies `DOG-BOWL-001`? We don't know. The Product record has no supplier. This matters for:  
- Vendor portal ("show me all MY products")
- Purchase order generation ("reorder DOG-BOWL-001 from vendor X")
- Recall and compliance ("vendor Y had a recall — which of our SKUs are affected?")
- Listing content accuracy ("vendor submitted updated images — apply to SKU")  

The Vendor Portal feature files show us vendor-scoped views of products, which means the association exists conceptually in the system today — it's just not formally captured in the Product Catalog domain model.

**❌ Regulatory and Compliance Metadata**  
This is a significant gap that only grows as we expand to marketplaces and more SKUs. What's missing:
- **Hazmat classification** — the CONTEXTS.md already flags this: "Hazmat classification is maintained in the Product Catalog SKU master." But the actual `Product` record has no hazmat field today. Fulfillment's routing engine would check this at routing time, but where does the authoritative value live?
- **Age restrictions** — pet medications, some reptile supplies, and certain food items have regulatory age gate requirements on some marketplaces
- **Country restrictions** — some flea treatments cannot be sold in California; some food ingredients are prohibited in certain countries if we ever sell internationally
- **Proposition 65 warnings** (California) — many pet products need these disclosures
- **Organic/natural certification claims** — FDA-regulated language for pet food

**❌ Category Taxonomy (Structured)**  
A plain string `"Dogs"` as a category was the right Phase 1 compromise. But it cannot support:
- Subcategory drill-down: `Dogs > Food > Dry Food > Large Breed Adult`
- Marketplace category mapping: what CritterSupply calls "Dogs > Bowls" is Amazon ASIN category `Pet Supplies > Dogs > Feeding & Watering Supplies > Bowls & Feeders`
- SEO-optimized category pages
- Category-specific attribute requirements (a product in "Fish > Aquariums" requires voltage and gallons; a product in "Dogs > Food" requires protein percentage and life stage)

The team already flagged this in CONTEXTS.md as a future vision item. It needs to move from future vision to planned work before Listings is viable.

**❌ Channel-Specific Product Attributes**  
Every marketplace has its own attribute schema. Amazon requires `breed_recommendation`, `material_type`, `item_form` for dog bowls. Walmart requires `primary_material` and `color_category`. eBay requires `compatible_breed` and `bowl_type`. These attributes live nowhere in the current model. They can't reasonably live in the core Product record either — they're listing-level, not product-level. But some source attributes (what IS the primary material? what IS the breed recommendation?) do belong in the catalog as shared raw data that gets mapped per channel.

**❌ Product Lifecycle Events Not Captured**  
The system knows a product was discontinued (status change) but it doesn't capture richer lifecycle moments:
- **Launch date / go-live scheduled date** — for ComingSoon products, when do they go live?
- **Phase-out date** — products often have a planned end-of-life date before they actually discontinue
- **Seasonal return date** — an OutOfSeason product like a Christmas stocking cat toy: when does it come back next year?
- **Safety recall** — this is a distinct status event from "discontinued," with different downstream effects (remove from all active listings immediately, notify recent purchasers)

---

### 1.3 Workflows the Current Model Fails to Capture

**Workflow Gap 1: "New product with variants goes live"**  
Today: Create SKU for DOG-COLLAR-S, create separate SKU for DOG-COLLAR-M, create separate SKU for DOG-COLLAR-L. No parent relationship. Customer sees three entries. Vendor supplier sees three separate line items. This is broken for multi-size products.

**Workflow Gap 2: "Product is recalled"**  
Today: We'd manually change status to Discontinued. There's no concept of a "recall" as a distinct business event. Downstream: no automated notification to customers who purchased, no mechanism to pause all marketplace listings, no audit trail of the recall reason and regulatory notification date.

**Workflow Gap 3: "Vendor updates product specifications"**  
The Vendor Portal feature files show vendors submitting change requests. But the approval workflow for vendor-submitted content has no home in the current domain model. Who approves? What fields can vendors change vs. what is curated internally? What happens to active listings when a product description is updated?

**Workflow Gap 4: "Seasonal product re-activation"**  
An OutOfSeason product like a holiday dog costume — when next October arrives, how does it go back to Active? Does someone manually flip the status? Is there a scheduled activation? The current model has no concept of a planned future status change.

---

## 2. Discovery Questions — Product Catalog Evolution

These questions must be answered before Product Catalog can evolve to support Listings.

---

### 2.1 Variant / Parent-Child Products

**Q1: What is the source of truth for "this is a parent product with variants"?**  
When a vendor supplies a dog collar in five colors and three sizes (15 combinations), do we model that as:
- (a) One "parent" product with 15 child SKUs, where the parent holds shared content (name, description, brand, images) and each child holds the differentiating attribute (color + size)?
- (b) 15 standalone products that are loosely linked by a shared "product family" identifier?
- (c) One product record with embedded variant data?

Option (a) is the Amazon/Shopify model. Option (b) is simpler but loses the shared content benefit. Option (c) creates a very complex document.

> **✅ Decision Made (2026-03-09):** **Option A — One parent `ProductFamily` + N child variant SKU records (parent/child hierarchy).** Example: Parent "AquaPaws Fountain" → Children: SKU-SM, SKU-MD, SKU-LG. See [`catalog-variant-model.md`](catalog-variant-model.md) for full PSA technical design, PO business validation, and UX sign-off.

**Q2: Can variants have different prices, weights, and images?**  
A 5-lb bag of dog food and a 20-lb bag of the same dog food are variants. But they have different prices, different weights (critical for shipping), potentially different primary images, and possibly different inventory levels. How much do variants diverge before they become their own unrelated products?

**Q3: Who creates variants — CritterSupply staff or vendors?**  
If vendors submit a new size through the Vendor Portal, does that create a variant automatically, or does it go through a catalog manager review? What prevents a vendor from creating spurious variants that pollute the catalog?

**Q4: What happens to a listing on Amazon when a new variant is added to a parent product?**  
Amazon grouping (parent ASIN / child ASIN) is a specific API concept. If we add a new size to an existing dog collar product, does the Amazon listing need to be updated automatically? Who is responsible for triggering that update — Listings BC reacting to a Product Catalog event?

---

### 2.2 Product Lifecycle Events

**Q5: What triggers a seasonal product to return to Active?**  
Options:
- Manual status change by a catalog manager
- Scheduled activation date stored on the product (e.g., "activate this OutOfSeason product on October 1 every year")
- An integration event from a future Promotions or Merchandising BC  

> **🔴 Owner/Erik Decision Required:** Do we want automated seasonal scheduling, or is manual reactivation acceptable for Phase 1 of the expanded catalog?

**Q6: Is a safety recall a distinct product status, or a special kind of discontinuation?**  
From a regulatory standpoint, a recall is fundamentally different from a business decision to discontinue. A recall requires:
- Immediate removal from all active channels (including removing live listings, not just marking a product)
- Regulatory notification dates captured
- Customer notification for past purchasers
- Potential lot/batch tracking (only certain production batches affected, not all inventory)

Should `Recalled` be a `ProductStatus` enum value, or does it warrant its own event type (`ProductRecalled`) with richer metadata? I lean toward a distinct event, not just a status change, because the downstream cascade is so different.

**Q7: For ComingSoon products, how does the transition to Active happen?**  
Is there a `PlannedLaunchDate` on the product? Does a catalog manager manually flip to Active on launch day? Does a scheduled job run at midnight? And critically: if we list a `ComingSoon` product on Amazon's "pre-order" feature, do we need Listings BC to know about the planned launch date?

**Q8: Who can change product status, and what are the authorization rules?**  
From the feature files, we see that CopyWriter role can edit descriptions but shouldn't access pricing or inventory. What roles can:
- Activate a ComingSoon product?
- Mark a product as OutOfSeason?
- Discontinue a product?
- Initiate a recall?

> **🟡 PO Can Decide:** Suggest a simple RBAC model: CopyWriter (content only), MerchandisingManager (status changes), ComplianceOfficer (recalls), SystemAdmin (all).

---

### 2.3 Regulatory and Compliance Attributes

**Q9: What is the minimum compliance metadata we need per product?**  
At minimum, I'd expect:
- `IsHazmat` (boolean) — flagged for shipping restrictions
- `HazmatClass` (string/enum) — DOT hazard class if applicable
- `AgeRestriction` (integer or null) — minimum customer age
- `PropSixtyFiveWarning` (boolean) — California Proposition 65 disclosure required
- `RestrictedStates` (list of state codes) — states where sale is prohibited

Are there other compliance dimensions we know we'll need? Does the pet food regulatory landscape (AAFCO standards) require any specific fields?

> **🔴 Owner/Erik Decision Required:** Do we need compliance metadata at launch of Listings BC, or can we defer? Some marketplaces (Amazon, Walmart) will refuse listings that don't include hazmat classification for applicable products.

**Q10: How do compliance requirements change over time?**  
California adds a new restriction on a pet food ingredient in 2027. We have 500 SKUs in that category. Who is responsible for updating compliance metadata — a compliance officer updating products one at a time, a bulk import from a regulatory database, or an automated feed? This is an operational workflow question as much as a domain question.

---

### 2.4 Category Taxonomy

**Q11: Who owns the CritterSupply internal category taxonomy?**  
The internal categories (Dogs, Cats, Fish, Birds, etc.) need to be managed somewhere. Options:
- A catalog manager maintains the category tree in Product Catalog BC (most natural)
- A separate "Taxonomy BC" manages categories independently (probably overkill for Phase 1)
- Categories are a hardcoded configuration (practical but inflexible)

> **🟡 PO Recommendation:** Categories as a managed entity within Product Catalog BC — with its own CRUD, hierarchy, and event when a category is renamed or reorganized. This aligns with CONTEXTS.md's note that category management is "complex enough to warrant its own subdomain within Product Catalog."

**Q12: How does internal categorization map to marketplace category trees?**  
Amazon has over 10,000 browse nodes. Walmart has a different hierarchy. eBay has yet another. Our internal "Dogs > Bowls" needs to map to Amazon `2975448011`, Walmart `pet_bowls`, eBay `66792`. Who maintains these mappings? When Amazon reorganizes their taxonomy (which they do regularly), how do we detect the change and update the mapping?

> **✅ Decided (PSA — no Owner input required):** Category-to-marketplace mapping lives in **Marketplaces BC**. Change rate follows the marketplace, not the catalog. See `catalog-listings-marketplaces-evolution-plan.md` §4.3 for rationale.

---

### 2.5 Vendor-Product Relationships

**Q13: Is the vendor-product association one-to-one or many-to-many?**  
Today, we can assume one vendor supplies one product (simple case). But in real retail, multiple vendors can supply the same product (competition, redundancy, regional suppliers). Does CritterSupply need to track all vendors who can supply a SKU, or only the primary supplier?

**Q14: What data does the vendor-product association carry?**  
When we know that Vendor "Acme Pet Supplies" supplies `DOG-BOWL-001`, what else do we track?
- Vendor's internal product code (their catalog number, which may differ from our SKU)
- Minimum order quantity
- Lead time
- Cost price (for margin calculations — note: this is sensitive and probably not part of Product Catalog)

---

## 3. Discovery Questions — Listings BC

The Listings BC is conceptually the **act of presenting a product on a specific channel** — with channel-specific content, attributes, and status.

---

### 3.1 What Is a Listing?

**Q1: How do we formally define a Listing?**  
My working definition: A **Listing** is the representation of a CritterSupply product on a specific marketplace or channel, at a specific point in time, with channel-specific content, pricing, and attributes.

A Listing is **not** the product itself — it's the product's face on a specific channel. One Product can have many Listings (one per channel). One Listing belongs to exactly one Product and one Channel/Marketplace.

Does the team agree with this definition? Are there edge cases where one Listing might reference multiple internal products (e.g., a "bundle" sold on Amazon)?

> **🔴 Owner/Erik Decision Required:** Do we plan to support bundle listings (multiple SKUs in one listing) in scope? This significantly complicates the model.

**Q2: What is the lifecycle of a Listing?**  
My proposed Listing lifecycle:

```
Draft → ReadyForReview → Submitted → Live → Paused → Ended
                                        ↑              ↓
                                   (Unpaused)      (Ended is terminal)
```

- **Draft** — being built; not submitted to marketplace API yet
- **ReadyForReview** — internal review before submission (compliance check, image check, content review)
- **Submitted** — sent to marketplace API, awaiting acceptance
- **Live** — active and selling on that channel
- **Paused** — temporarily suspended (e.g., out of stock, price investigation, voluntary pause)
- **Ended** — permanently removed from the channel (distinct from product discontinuation)

Is this lifecycle realistic? Is `ReadyForReview` a necessary step, or do we trust internal teams to submit directly?

**Q3: What makes a Listing "complete" and eligible for submission?**  
Each marketplace has required fields. Before we submit to Amazon, do we validate:
- All required Amazon attributes are present
- Images meet Amazon's minimum resolution and count requirements
- Price is set for this channel (Pricing BC)
- Inventory is available (Inventory BC)
- The parent product is in Active status (Product Catalog BC)

Who performs this validation — is it a pre-submission check in Listings BC, or does the marketplace API tell us what's wrong?

**Q4: Is a Listing tied to one variant, one parent product, or can it span variants?**  
On Amazon, a "parent listing" groups all variants under one ASIN. You don't list each size separately — you list the parent and its children together. On eBay, you might list each variant separately. How does Listings BC handle this channel-specific difference in how variants group?

---

### 3.2 Who Manages Listings?

**Q5: Who creates and manages listings — CritterSupply staff or vendors?**  
Options:
- CritterSupply's merchandising team creates all listings (full control, slower)
- Vendors create listings through the Vendor Portal and CritterSupply approves (faster, more vendor responsibility)
- Hybrid: vendors can create draft listings, CS team reviews before submission

> **🟡 PO Recommendation:** For Phase 1, CritterSupply staff creates listings. Vendor self-service listing creation is a Phase 2 feature — it requires more trust infrastructure and marketplace API credential management.

**Q6: What does the listing creation workflow look like for CritterSupply's own website?**  
Our own storefront (crittersupply.com) is also a "channel." A product is listed on our site when it's Active in the catalog — or is it? Do we need an explicit Listing record for our own site, or is "Active in Product Catalog" sufficient to make a product appear on the storefront?

> **🔴 Owner/Erik Decision Required:** Is our own website a "channel" in the Listings model, or is Product Catalog status the sole gating mechanism for storefront visibility? This is a fundamental architectural question. If we model our own site as a channel, we gain consistency ("everything goes through Listings") but add complexity. If we keep it as-is ("Active = visible on site"), we have a simpler storefront but an asymmetry with external channels.

**Q7: What happens to a listing when the underlying product is updated?**  
A vendor submits new, better product images. The catalog manager approves them. The Product Catalog BC fires a `ProductUpdated` event. What happens next?
- Does Listings BC automatically push the new images to all active listings on all channels?
- Does it create a "pending update" that requires manual approval before pushing to marketplaces?
- Does it do nothing, and channel updates are manual operations?

This is a significant operational question. Auto-pushing changes to Amazon without review has burned real retailers when they accidentally published incomplete updates.

---

### 3.3 Listing Invariants and Business Rules

**Q8: What are the hard invariants around listings?**  
Proposed invariants:
- A listing cannot be submitted for a product in `Discontinued` or `Recalled` status
- A listing cannot be Live without a price being set for that channel
- A listing cannot be Live without at least one image meeting the marketplace's minimum spec
- If a product is recalled, ALL active listings for that product across ALL channels must transition to `Ended` or `Paused` immediately
- A listing cannot reference a marketplace that is in a `Suspended` or `Offboarded` state

**Q9: Does a listing have its own price, or does it reference the Pricing BC?**  
Channel-specific pricing is a real business concept. The same dog bowl might be:
- $19.99 on our own website
- $22.99 on Amazon (to account for Amazon's referral fee)
- $21.49 on eBay

Does Listings BC hold the channel-specific price, or does it reference Pricing BC with a channel dimension? I suspect Pricing BC should own channel pricing, but Listings BC needs to know "has a price been set for this channel" as a precondition for going Live.

**Q10: What happens if inventory drops to zero on a live listing?**  
On Amazon, if inventory hits zero, the listing stays live but shows "Temporarily Out of Stock." On eBay, if quantity hits zero, the listing ends automatically. These are channel-specific behaviors. Does Listings BC react to inventory events and update listing status accordingly? Or does the marketplace API handle this automatically?

---

### 3.4 Listing Integration and Events

**Q11: What events does Listings BC publish, and who consumes them?**  
Proposed events:
- `ListingCreated(ListingId, Sku, MarketplaceId, CreatedAt)` — merchandising analytics
- `ListingWentLive(ListingId, Sku, MarketplaceId, ListingUrl, LiveAt)` — analytics, Customer Experience (show "also available on Amazon")
- `ListingPaused(ListingId, Sku, MarketplaceId, Reason, PausedAt)` — operational alerting
- `ListingEnded(ListingId, Sku, MarketplaceId, Reason, EndedAt)` — analytics

**Q12: How does the Listings BC communicate with actual marketplace APIs?**  
Does Listings BC directly call Amazon's Selling Partner API, eBay's Trading API, and Walmart's Marketplace API? Or does it publish events that a separate "Marketplace Integration" adapter layer handles? This matters for failure handling: marketplace APIs are notoriously unreliable.

> **🔴 Owner/Erik Decision Required:** Does Listings BC own the marketplace API integration directly, or is there an anti-corruption layer / adapter between Listings BC and each marketplace's external API? The latter is more resilient but adds another moving part.

---

## 4. Discovery Questions — Marketplaces BC

The Marketplaces BC is the **definition and configuration of sales channels** — what they require, how they're connected, what their rules are.

---

### 4.1 What Is a Marketplace in Our System?

**Q1: Is a Marketplace an aggregate, a configuration file, a database record, or an enum?**  
This is the most fundamental design question for this BC. Let me think through the options:

- **Hardcoded enum** (`Amazon`, `eBay`, `Walmart`, `OwnSite`): Simple. Works fine if we never add new channels or if adding a new channel requires a code deployment. Doesn't capture marketplace-specific configuration (API credentials, rate limits, required fields).
- **Configuration file** (YAML/JSON): Stores marketplace schemas and rules externally. Easy to update without deployment. But doesn't capture lifecycle changes or audit trail.
- **Database record** (document or relational): Full lifecycle management. We can add, suspend, or retire marketplaces through API operations. Supports audit trail. Appropriate if marketplaces are expected to come and go.
- **Event-sourced aggregate**: Captures every configuration change, credential rotation, and status transition over time. Probably overkill unless we need full audit history of marketplace configuration changes.

> **✅ Decided (PSA — no Owner input required):** Marketplace is a **Marten document entity** with a stable `ChannelCode` string (`AMAZON_US`, `EBAY_US`, `WALMART_US`, `OWN_WEBSITE`). Not an enum (too rigid), not an event-sourced aggregate (overkill for config). See `catalog-listings-marketplaces-evolution-plan.md` §4.1 for rationale.

**Q2: What is the lifecycle of a Marketplace?**  
Proposed lifecycle:
```
Configured → Active → Suspended → Offboarded (terminal)
```

- **Configured** — API credentials registered, schema defined, not yet accepting listings
- **Active** — listings can be submitted to this channel
- **Suspended** — temporarily not accepting new listings (e.g., we're renegotiating terms with Walmart)
- **Offboarded** — we've exited this channel; all listings end, no new listings accepted

Does this model a real business scenario? Yes — retailers periodically exit channels (Temu relationship sours, we exit), add new channels (Kroger launches online marketplace, we onboard), or suspend a channel for renegotiation.

**Q3: What does Marketplace identity look like across bounded contexts?**  
When Inventory BC needs to know "how many units are reserved for Amazon vs. eBay vs. our own site" — what identifier does it use? Options:
- A `MarketplaceId` (GUID) from the Marketplaces BC
- A `ChannelCode` (short string like `"AMZN"`, `"EBAY"`, `"WALM"`) that's stable across systems
- An enum value (if marketplace identity is code-level)

I'd argue for a **stable `ChannelCode` string** — short, human-readable, stable across deployments, doesn't require a lookup table in every BC. Similar to how ISO currency codes (`USD`, `EUR`) are stable identifiers that travel across systems without requiring a database join.

> **🟡 PO Recommendation:** Define a canonical `ChannelCode` (e.g., `"AMZN"`, `"EBAY"`, `"WALM"`, `"SITE"`) as the cross-BC marketplace identifier, similar to how `Sku` is the cross-BC product identifier. Each BC uses this code directly without needing a Marketplaces BC lookup.

---

### 4.2 Marketplace Configuration and Rules

**Q4: Who is responsible for knowing what Amazon requires for a pet food listing?**  
Amazon requires specific attributes for pet food: `item_form` (dry, wet, semi-moist), `breed_recommendation`, `life_stage`, `primary_ingredient`, `caloric_content_unit`. Walmart has a different set. eBay has different required attributes still.

This "schema knowledge" must live somewhere. Options:
- In the Marketplaces BC as managed configuration per category (flexible but complex to maintain)
- In code as hard-coded validators per channel (simple but requires deployment for every change)
- In an external marketplace attribute database that we sync from (most realistic for production — Amazon publishes category-specific attribute lists)

**Q5: How do marketplace API credentials get stored and rotated?**  
Amazon SP-API credentials, eBay OAuth tokens, Walmart API keys — these are sensitive and expire. Who manages credential rotation? Is that part of the Marketplaces BC (a `RotateCredentials` command)? Is credential storage a concern for Marketplaces BC or for a secrets management layer? This is partly an infrastructure question but the business process matters: who does the rotation, how often, and what happens to active listings during a credential rotation?

> **🔴 Owner/Erik Decision Required:** Credentials management strategy — is that in scope for Marketplaces BC, or handled by secrets management infrastructure (Vault, AWS Secrets Manager)?

**Q6: How do marketplace fee structures change over time?**  
Amazon's referral fees change annually (sometimes mid-year). Walmart negotiates fees per seller. eBay has both fixed fees and percentage fees that vary by category. These fee structures affect pricing decisions in the Pricing BC. Does Marketplaces BC own fee configuration, or does Pricing BC own it?

> **🟡 PO Recommendation:** Marketplaces BC owns the canonical fee structure as reference data. Pricing BC consumes it when calculating channel-specific prices. This keeps the pricing logic in the right place while keeping marketplace knowledge in the right place.

**Q7: How does Marketplaces BC communicate marketplace category taxonomy changes?**  
Amazon reorganizes its browse node tree periodically. When Amazon renames a category or deprecates a browse node, our category mappings (internal category → Amazon category) may break, causing listings to fail validation. How do we detect this? How do we communicate it to teams managing listings? Does Marketplaces BC publish a `MarketplaceTaxonomyChanged` event?

**Q8: Is the marketplace schema (required attributes, image specs, title character limits) versioned?**  
Amazon SP-API has versioned schemas. If Amazon publishes a new version of the product type definition for "pet_food," our existing listings built against the old schema may need migration. Does Marketplaces BC track schema versions? Does Listings BC know which schema version each listing was built against?

> **🔴 Owner/Erik Decision Required:** Schema versioning for marketplace attribute definitions — is this in scope for the first iteration of Marketplaces BC, or deferred?

---

### 4.3 Marketplace Identity Across the System

**Q9: What other bounded contexts need marketplace identity, and how do they get it?**  
Candidate consumers of marketplace identity:
- **Listings BC** — core consumer (a listing is for a specific marketplace)
- **Pricing BC** — channel-specific pricing (`AMZN` price vs. `SITE` price)
- **Inventory BC** — channel-specific inventory allocation (Amazon FBA vs. own warehouse)
- **Orders BC** — orders come in from different channels; order source matters for fulfillment routing and fee calculation
- **Analytics** — revenue per channel, conversion per channel
- **Fulfillment BC** — marketplace SLAs differ (Amazon has 1-day delivery promises; eBay has seller-set estimates)

Does each of these BCs need a live relationship with Marketplaces BC, or can they use the stable `ChannelCode` string and be done?

> **🟡 PO Recommendation:** Use `ChannelCode` as a dumb, stable identifier across all BCs. Only Listings BC has a live dependency on Marketplaces BC (for schema validation, submission). Other BCs carry `ChannelCode` as context without needing to query Marketplaces BC. This avoids a distributed dependency web.

**Q10: What is the relationship between "marketplace" and "fulfillment channel"?**  
Amazon has two fulfillment models: **FBA** (Fulfillment by Amazon, where Amazon warehouses our product) and **FBM** (Fulfilled by Merchant, where we ship directly). These are the same marketplace but different fulfillment channels. Does `AMZN` as a marketplace need sub-channels (`AMZN-FBA`, `AMZN-FBM`)? Or is fulfillment channel a Fulfillment BC concept, not a Marketplaces BC concept?

---

## 5. Business Workflow Sketches

These are plain-English workflow descriptions — no code, no framework references. The intent is to validate the business logic before engineers build it.

---

### Workflow 1: "CritterSupply Adds a New Product and Lists It on Amazon and eBay"

**Business context:** Our buyer sources a new premium dog water fountain from vendor "AquaPaws Inc." We want it live on our website, Amazon, and eBay simultaneously within one week.

**Step-by-step:**

1. **Vendor submission.** The AquaPaws sales rep submits product information through the Vendor Portal: product name, description, item dimensions, weight, images, their internal item number (AP-WF-001). They map it to our taxonomy as best they can. A "product submission" enters review queue.

2. **Catalog manager review.** A CritterSupply catalog manager reviews the submission. They:
   - Assign the CritterSupply SKU: `DOG-FOUNTAIN-001`
   - Confirm the category: `Dogs > Feeding & Watering`
   - Edit the vendor-submitted description for CritterSupply brand voice
   - Add regulatory metadata: `IsHazmat = false`, no age restriction, no state restrictions
   - Add searchable tags: `water fountain, dog hydration, automatic, quiet`
   - Confirm dimensions and weight for shipping calculation
   - Approve and publish. **Product `DOG-FOUNTAIN-001` enters Product Catalog as `ComingSoon`.**

3. **Pricing is set.** The pricing team sets:
   - Site price: $49.99
   - Amazon price: $54.99 (accounts for ~15% referral fee, still maintains margin)
   - eBay price: $52.99 (accounts for eBay fees, slightly lower than Amazon to be competitive)
   
   _(This happens in Pricing BC — Listings BC won't let a listing go Live without a price being confirmed for that channel.)_

4. **Listing creation — Amazon.** A merchandising specialist creates a Listing for `DOG-FOUNTAIN-001` on Amazon:
   - Fills Amazon-required attributes: `breed_recommendation = All Breeds`, `item_form = Electric`, `material_type = BPA-free plastic`, `wattage = 3.5W`
   - Selects 5 images that meet Amazon's minimum 1000x1000px requirement
   - Selects Amazon browse node: `Pet Supplies > Dogs > Feeding & Watering Supplies > Water Fountains`
   - Maps to our internal category-to-Amazon-category mapping
   - Saves as Draft.

5. **Listing creation — eBay.** Simultaneously, another specialist (or the same one) creates a Listing for `DOG-FOUNTAIN-001` on eBay:
   - eBay required attributes differ: `compatible_breed = All`, `power_source = Electric`, `features = Quiet, Filtered`
   - eBay condition: `New`
   - eBay listing duration: `Good Till Cancelled`
   - Saves as Draft.

6. **Internal review (ReadyForReview).** Both draft listings go through internal compliance check:
   - Product is not discontinued or recalled ✅
   - Price is set for each channel ✅
   - Required attributes filled per marketplace schema ✅
   - Images meet marketplace specs ✅
   - Compliance metadata complete ✅
   
   Both listings advance to `ReadyForReview`. Compliance officer confirms. Listings advance to `Submitted`.

7. **Submission to marketplace APIs.** Listings BC submits:
   - Amazon: Creates new ASIN via SP-API Product Type Definition schema for `PET_WATERER_AND_FEEDER` *(or the most appropriate product type for water fountains)*
   - eBay: Creates new listing via Trading API

8. **Marketplace confirmation.** Amazon responds (can take minutes to hours): "ASIN B098XXXXXXX created." Listing status → `Live`. eBay responds: "Item 123456789012 created." Listing status → `Live`.

9. **Product goes Active.** Once at least one listing is Live, the catalog manager flips `DOG-FOUNTAIN-001` from `ComingSoon` to `Active`. The CritterSupply storefront now shows the product (assuming our own site is a listing or Catalog Active = visible on site).

10. **Customer Experience updates.** Real-time UI receives `ProductStatusChanged` event. Storefront shows the new product. If "also available on Amazon" is a feature, Customer Experience BC knows from `ListingWentLive` events that we have an Amazon ASIN.

**Key questions this workflow surfaces:**
- Steps 4 and 5 assume a human fills in marketplace attributes manually. At scale (100 new products/month), this is unsustainable. Is there a longer-term plan for AI-assisted attribute population or vendor-provided attribute data?
- Step 7 assumes Listings BC calls marketplace APIs directly. What is the retry/failure model if Amazon's API is down?
- Step 9 (flipping to Active) — should this be automatic when the first listing goes Live, or always manual?

---

### Workflow 2: "A Marketplace Changes Its Category Taxonomy — What Downstream Effects Occur?"

**Business context:** Amazon announces that effective March 1, the browse node `Pet Supplies > Dogs > Feeding & Watering Supplies` (node ID `2975448011`) is being reorganized. All dog bowls will move to `Pet Supplies > Dogs > Bowls & Feeders` (new node ID `9876543210`). Any listings using the old node after March 1 will fail Amazon's listing quality check and may be suppressed.

**Step-by-step:**

1. **Detection.** Amazon publishes taxonomy change notes in their developer bulletin. How do we detect this? Options:
   - Manual: someone on the team reads Amazon's bulletin and updates the mapping manually
   - Automated: we poll Amazon's Browse Node API and diff against our stored taxonomy
   - Reactive: Amazon's listing quality alerts flag our listings as using a deprecated node

   *(This is an open question about tooling — but the business workflow exists regardless.)*

2. **Marketplace taxonomy update.** The Marketplaces BC is updated to reflect the new Amazon browse node mapping. `MarketplaceTaxonomyChanged` event is published. The old node `2975448011` is marked deprecated with an effective date.

3. **Affected listing identification.** Listings BC handles `MarketplaceTaxonomyChanged`. It queries: "Which of our Live Amazon listings use deprecated browse node `2975448011`?" Answer: potentially many — DOG-BOWL-001, DOG-BOWL-002, DOG-FOUNTAIN-001, etc.

4. **Listing status degradation.** Each affected listing transitions from `Live` to a new sub-state: perhaps `Live (ActionRequired)` — it's still live and selling but flagged. An alert goes to the merchandising team: "17 Amazon listings need category remapping before March 1."

5. **Remapping.** Catalog manager updates the internal category-to-Amazon-category mapping for `Dogs > Feeding & Watering` → new Amazon node. This propagates to all affected listings automatically (if we store the internal-to-marketplace mapping centrally) — or must be updated listing by listing (bad).

6. **Re-submission.** Each affected listing is re-submitted to Amazon with the updated browse node. Listings return to `Live`.

7. **Aftermath.** If we miss the March 1 deadline, Amazon may suppress listings. `ListingSuppressed` event from Amazon → Listings BC transitions to `Paused` and sends an urgent operational alert.

**Key insight:** This workflow demonstrates why the **internal-to-marketplace category mapping must be centralized**. If each listing stores its Amazon browse node directly, we have to update 17 listings individually. If the mapping lives in a shared table (internal category → marketplace category), updating the mapping cascades automatically. This is a critical design decision.

---

### Workflow 3: "A Product Is Recalled — How Does That Cascade Across Marketplace Listings?"

**Business context:** CritterSupply receives an urgent notification from AquaPaws Inc. on a Friday afternoon: the `DOG-FOUNTAIN-001` dog water fountain has a manufacturing defect. Batch numbers AQ-2025-Q3-001 through AQ-2025-Q3-045 have a pump motor issue that could cause overheating. The vendor is initiating a voluntary recall with the CPSC (Consumer Product Safety Commission). CritterSupply must act immediately.

**Step-by-step:**

1. **Recall initiation.** A compliance officer (or senior catalog manager) issues a `RecallProduct` command against `DOG-FOUNTAIN-001`. This is NOT the same as discontinuing the product — it's a distinct business event with legal implications.

2. **Product Catalog reacts immediately.** `ProductRecalled` event is persisted. The Product record's status transitions to `Recalled` (distinct from `Discontinued`). Integration event `ProductRecalled(Sku, RecallReason, AffectedBatchNumbers, RecalledAt, RegulatoryNotificationDate)` is published.

3. **ALL listings end immediately.** Listings BC handles `ProductRecalled`. Every listing for `DOG-FOUNTAIN-001` across every channel transitions to `Ended` or `Paused` — immediately, not eventually. This is a zero-tolerance rule: no delay, no batch processing window.
   - Amazon listing: de-listed via SP-API (product removed from detail pages)
   - eBay listing: ended via Trading API
   - Own site: product becomes invisible on storefront

4. **Marketplace APIs are called.** Within minutes (not days), Listings BC fires API calls to Amazon SP-API and eBay to remove or suppress the listing. If the API call fails (network issue, rate limit), it goes to a retry queue with maximum urgency — this is not a low-priority background job.

5. **Inventory BC notified.** Inventory BC handles `ProductRecalled`. Any reserved or uncommitted inventory for `DOG-FOUNTAIN-001` is flagged. If there's a concept of "quarantine hold" in the warehouse, this triggers it.

6. **Orders BC notified.** Orders BC handles `ProductRecalled`. Any active orders containing `DOG-FOUNTAIN-001` that haven't shipped yet need evaluation:
   - Orders in `Fulfilling` state: fulfillment is halted immediately
   - Orders already shipped: customer notification must go out
   - Orders in payment/inventory stage: cancelled and refunded

7. **Customer notifications.** For any customer who purchased `DOG-FOUNTAIN-001` — particularly if their order's batch number falls within the affected range — a notification must go out. This is probably an email/notification workflow triggered by the recall event.

8. **Audit trail is preserved.** Because this is a legal matter, the `ProductRecalled` event (with timestamp, officer identity, regulatory notification date, and batch numbers) is permanently preserved. This is not something we can undo or soft-delete.

**Key questions this workflow surfaces:**
- Lot/batch tracking: if only batches AQ-2025-Q3-001 through AQ-2025-Q3-045 are recalled (not later batches), can we leave listings active for inventory we know came from non-affected batches? This requires lot tracking at the inventory level. Does CritterSupply plan to track lot numbers?
- Immediate vs. eventual: for a recall, the cascade to marketplace listings **must be immediate** (not eventual). Does the architecture support synchronous cascade for this specific event type?
- Customer notification ownership: which BC owns sending customer recall notifications? Customer Experience? A dedicated Notifications BC?

> **🔴 Owner/Erik Decision Required:** Is lot/batch tracking in scope for CritterSupply? It fundamentally changes the Inventory BC model.

---

## 6. Risks and Business Concerns

These are the top business risks if we get the Listings and Marketplaces BC design wrong.

---

### Risk 1: Tight Coupling Between Listings and Product Catalog Blocks Independent Evolution

**The risk:** If Listings BC queries Product Catalog synchronously at listing creation time (and at every listing update), we create a runtime dependency that means both services must be up simultaneously. Worse, if Product Catalog changes its data model, Listings BC breaks.

**Business impact:** Marketplace listing operations are blocked every time we upgrade or restart Product Catalog. At peak catalog update times (vendor batch imports), listing operations grind to a halt.

**Mitigation:** Listings BC should maintain its own read-model of product data it needs (name, category, dimensions, compliance metadata), updated via integration events from Product Catalog. This is the anti-corruption layer pattern. The Listings BC is not a thin wrapper around Catalog — it has its own projection of catalog data relevant to listings.

---

### Risk 2: Getting the Variant Model Wrong Before Listings BC Is Built

**The risk:** If we build Listings BC before resolving the variant/parent-child product model in Product Catalog, we'll make assumptions about "one listing = one SKU" that break when variants arrive. Amazon requires grouping variants under a single parent listing — which assumes the parent-child relationship exists in our product model.

**Business impact:** Retrofitting variant support into Listings BC after it's built is expensive and error-prone. It's not a feature addition — it changes the fundamental unit of listing.

**Mitigation:** **Variants must be resolved in Product Catalog BC before Listings BC is designed.** This is a hard prerequisite.

---

### Risk 3: Compliance Requirements Not Enforced at Listing Submission

**The risk:** If Listings BC doesn't validate compliance metadata (hazmat classification, age restriction, country restrictions) before submitting to a marketplace, we may successfully create a listing that violates marketplace rules or regulatory law.

**Business impact:** 
- Amazon can suspend our seller account for repeated compliance violations
- Selling a product in a state where it's prohibited can create legal liability
- Pet food with unverified AAFCO claims can trigger FDA enforcement

**Mitigation:** Listings BC must perform a compliance gate check before advancing to `Submitted`. Marketplace-specific compliance rules must be encoded in Marketplaces BC configuration (e.g., "Amazon requires hazmat class for any product with batteries").

---

### Risk 4: Category Taxonomy Mapping Maintained Manually at Scale

**The risk:** If internal-to-marketplace category mappings are maintained manually (one mapping per product, not one mapping per category), a single Amazon taxonomy reorganization requires updating hundreds of listings individually.

**Business impact:** At 500+ SKUs across 3 marketplaces, a taxonomy change event creates a multi-day remediation project. Listings get suppressed. Revenue drops. Staff bandwidth is consumed.

**Mitigation:** Category-to-marketplace mappings must be maintained at the **category level**, not the listing level. When CritterSupply internal category `Dogs > Bowls` maps to Amazon node `XYZ`, that mapping applies to ALL products in that internal category. Changing the mapping updates all affected listings.

---

### Risk 5: Recall Cascade Is "Eventually Consistent" Instead of Immediate

**The risk:** If a product recall event enters a message queue and the listing de-activation is processed "eventually" (within a few minutes or hours), that's legally and reputationally unacceptable. During that window, customers can add a recalled product to their cart and purchase it.

**Business impact:** Legal exposure for selling a recalled product after the recall is known. Brand damage. Potential regulatory penalty (CPSC takes recall compliance seriously).

**Mitigation:** Product recall must trigger a synchronous or near-synchronous cascade. The listing de-activation path must be designated as a high-priority, non-batched operation. This is one of the few places where we might accept the complexity of a synchronous multi-BC operation — or at minimum, a very high-priority, low-SLA message channel.

---

## 7. PO Recommendations

### Should Listings and Marketplaces Be One BC or Two?

**Recommendation: Two separate bounded contexts.**

The lifecycles, ownership, and change rates are fundamentally different:

| Concern | Listings BC | Marketplaces BC |
|---------|------------|-----------------|
| **Core question** | "Is product X listed on channel Y?" | "What does channel Y require?" |
| **Change frequency** | High — listings are created, updated, paused constantly | Low — marketplace schemas change quarterly at most |
| **Ownership** | Merchandising team | Integration/Platform team |
| **Aggregate** | Listing (product × channel) | Marketplace (channel definition + schema) |
| **Event volume** | High | Low |

Merging them would create a BC that merchandising managers and platform engineers would both need to touch — violating Conway's Law. Keep them separate.

The Marketplaces BC is a supporting subdomain — relatively stable configuration and schema. The Listings BC is a core subdomain — active, transactional, high-value business operations.

---

### What Should the MVP Listings Capability Look Like?

**Recommendation: Start with our own storefront as the first "channel," then one external marketplace.**

**MVP Phase 1 — Own Website as a Channel:**
- Model our own storefront as a channel (`ChannelCode = "SITE"`)
- A `SiteListing` is created when a product is approved in Product Catalog
- Listing lifecycle on the site: Draft → Live → Paused → Ended (maps to product status changes)
- This establishes the pattern without the complexity of external marketplace APIs
- No external API integration required — the Listings BC coordinates with Product Catalog

**MVP Phase 2 — One External Marketplace (Amazon recommended):**
- Integrate with Amazon SP-API as the first external channel
- Implement the full Listing lifecycle for Amazon: Draft → ReadyForReview → Submitted → Live → Paused/Ended
- Build the category mapping layer (internal category → Amazon browse node)
- Validate marketplace attribute requirements
- Learn from the Amazon integration before building eBay/Walmart

**Why Amazon first?** Largest revenue potential, most complex API (so we learn from the hardest case), most mature SP-API documentation, and most common marketplace for pet supply retailers.

---

### What Must Product Catalog BC Evolve to Support Before Listings BC Is Viable?

In priority order:

1. **Variant / parent-child product model** — Cannot build Listings BC without this. Amazon's listing model is built around parent/child relationships. This is the hard prerequisite.

2. **Structured category taxonomy** — Internal category hierarchy + category-to-marketplace mapping. Without this, every listing requires manual browse node selection. Non-scalable.

3. **Regulatory / compliance metadata** — `IsHazmat`, `HazmatClass`, `AgeRestriction`, `RestrictedStates`, `PropSixtyFiveWarning`. Listings BC validation gate requires these. Amazon will reject listings without hazmat classification for applicable products.

4. **Vendor-product association** — Which vendor supplies this product? Required for: Vendor Portal scoping, purchase order generation, recall tracing.

5. **Richer product lifecycle events** — `ProductRecalled` as a distinct event (not just a status change). `PlannedLaunchDate` for `ComingSoon` products.

6. **Integration events actually published** — The `ProductAdded`, `ProductUpdated`, `ProductDiscontinued` integration events exist in `Messages.Contracts` but are not confirmed to be published by RabbitMQ transport (per workflow docs noting "RabbitMQ: ❌ Not configured"). Listings BC needs these events to maintain its read-model of product data.

---

## 8. Decisions Needed

The following decisions are flagged **Owner/Erik must decide** throughout this document. Consolidated here for convenience:

| # | Decision | Context | Status | Impact if Deferred |
|---|----------|---------|--------|-------------------|
| D1 | Variant model (parent-child vs. standalone with family link vs. embedded) | Section 2.1 Q1 | ✅ **Decided** — Option A: parent/child hierarchy ([details](catalog-variant-model.md)) | ~~Blocks Listings BC design entirely~~ — unblocked |
| D2 | Is our own website a formal "channel" in the Listings model? | Section 3.2 Q6 | 🔴 **Owner decision required** | Determines Listings BC scope for Phase 1 |
| D3 | Does Listings BC own marketplace API integration, or is there an adapter layer? | Section 3.4 Q12 | 🔴 **Owner decision required** | Determines failure handling architecture |
| D4 | Is a Marketplace an aggregate (event-sourced or document) or configuration/enum? | Section 4.1 Q1 | ✅ **Decided** (PSA) — Marten document entity; no Owner input needed | ~~Determines Marketplaces BC complexity~~ — resolved |
| D5 | Does category-to-marketplace mapping live in Product Catalog BC or Marketplaces BC? | Section 2.4 Q12 | ✅ **Decided** (PSA) — Marketplaces BC owns it; no Owner input needed | ~~Critical for taxonomy change cascade workflow~~ — resolved |
| D6 | Are credentials managed in Marketplaces BC or infrastructure (Vault)? | Section 4.2 Q5 | 🟡 **Owner decision required** | Security posture and operational responsibility |
| D7 | Is lot/batch tracking in scope for Inventory BC? | Section 5 Workflow 3 | 🟡 **Owner decision required** | Determines precision of product recall scope |
| D8 | Compliance metadata — required at Listings BC launch or deferrable? | Section 2.3 Q9 | 🔴 **Owner decision required** | Amazon will reject listings without hazmat data |
| D9 | Automated seasonal product reactivation vs. manual | Section 2.2 Q5 | 🟡 **Owner decision required** | Operational overhead for seasonal catalog |
| D10 | Schema versioning for marketplace attribute definitions | Section 4.2 Q8 | 🟡 **Owner decision required** | Risk: breaking listings on marketplace API version changes |

---

---

# Part B — UX Engineer Perspective

## 9. Grounding Note: Where We Are Starting

Before diving into recommendations, one fact shapes everything that follows: **CritterSupply has zero admin UI today.** Product data is managed through raw REST API calls. This means we are not redesigning a UI — we are building one from scratch. That is an opportunity and a risk. The opportunity is that we can design the admin experience correctly from the beginning, aligned with the bounded context boundaries we are designing now. The risk is that whoever currently manages product data (presumably a developer or technical ops person using Postman or curl) has developed workarounds and mental models around the API shape — not around any user-centered workflow. We need to surface those workflows before we build.

**Immediate action needed (UX-owned):** Schedule a 1-hour contextual inquiry with whoever currently manages catalog data. I need to see their actual workflow, not just hear about it.

---

## 10. UX Perspective on the Four Missing Catalog Concepts

---

### 1.1 — Variant / Parent-Child Product Model

**Who uses this?**

| User | Relationship to Variants |
|---|---|
| **Catalog Manager** | Creates and maintains variant groups; sets which attributes define variants |
| **Merchandiser** | Decides which variants are featured, sets display order |
| **Customer** (indirectly) | Selects a variant on the PDP; must understand what they are choosing |
| **Inventory team** | Tracks stock per-variant, not per-parent product |
| **Operations / Fulfillment** | Picks and ships a specific variant (the SKU), not the parent concept |

**Primary workflow:** A catalog manager receives a new product line — say, the *AquaPaws Self-Cleaning Dog Fountain* in Small, Medium, and Large. Today they create three unrelated products. The correct workflow is: create a parent product, define the variant axis (Size), then create or link the three child variants, each with its own SKU, weight, and image, but inheriting shared description, brand, and category from the parent.

**Pain points if this stays unresolved:**

- **Search and discovery break.** A shopper searching for the AquaPaws fountain sees three results instead of one. Choice overload, confusion about whether these are really different products.
- **Catalog manager busywork triples.** Update the description → update it three times. Change the brand name → three records to fix. Miss one → data inconsistency in production.
- **Pricing BC cannot model "S/M/L at different price points" correctly** without variant identity. You end up with three price entries with no shared lineage.
- **Inventory reporting is meaningless at the product level.** "How many AquaPaws fountains do we have?" becomes a manual sum of three separate product queries.
- **Amazon and Walmart expect the parent/child structure in their product APIs.** Building Listings BC without variants means we cannot correctly publish to any major channel.

**UX pattern recommendation:**

A two-panel form flow:

```
┌─────────────────────────────────────────────────────────┐
│  PRODUCT FAMILY: AquaPaws Self-Cleaning Dog Fountain    │
│  SKU Prefix: AQUAPAWS-FOUNTAIN  Brand: AquaPaws         │
│  Category: Dog > Water & Feeding > Fountains            │
│                                                         │
│  ┌──────────────────┐  ┌──────────────────────────────┐ │
│  │ SHARED DETAILS   │  │ VARIANTS (3)                 │ │
│  │ Description      │  │  ┌─────┬───────┬──────────┐  │ │
│  │ Brand            │  │  │ SKU │ Size  │ Weight   │  │ │
│  │ Category         │  │  ├─────┼───────┼──────────┤  │ │
│  │ Tags             │  │  │ -SM │ Small │ 1.2 lbs  │  │ │
│  │ Images (shared)  │  │  │ -MD │ Med   │ 1.8 lbs  │  │ │
│  └──────────────────┘  │  │ -LG │ Large │ 2.4 lbs  │  │ │
│                        │  └─────┴───────┴──────────┘  │ │
│                        │  [+ Add Variant]              │ │
│                        └──────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

Key interaction decisions:
- Variant-defining axes (Size, Color, Flavor) should be configurable per category, not hardcoded
- Inherited fields should visually indicate inheritance (grayed with an "override" affordance)
- Bulk-editing variants (e.g., change all images at once) should be possible from the parent view
- **Owned by PO/Engineer to decide:** Which fields are inheritable vs. always variant-specific

---

### 1.2 — Vendor-Product Association

**Who uses this?**

| User | Relationship to Vendors |
|---|---|
| **Purchasing / Procurement** | Needs to know which vendor(s) supply a product and at what cost |
| **Catalog Manager** | Associates new products with the correct vendor; resolves duplicates when two vendors supply the same item |
| **Operations** | Needs vendor contact info when a product has a quality issue or recall |
| **Finance** | Cost-of-goods calculations depend on vendor pricing |

**Primary workflow:** A new vendor (PawsFirst Supplies) onboards and provides a product feed of 40 SKUs. The catalog manager needs to match those 40 SKUs against existing catalog products (some already exist, some are new). For each match: associate the vendor. For each new product: create it and associate. Then later, when PawsFirst raises their unit cost on 6 items, Purchasing needs to see those 6 products and update cost-of-goods accordingly.

**Pain points if this stays unresolved:**

- **Recall response becomes a manual hunt.** If PawsFirst issues a recall on a product line, someone has to manually search through the catalog trying to remember which products came from them.
- **No way to answer "who do I call?"** If a product ships damaged repeatedly, there is no system-of-record for the vendor relationship — it lives in someone's email or spreadsheet.
- **Multi-vendor products are invisible.** Some products may be sourceable from multiple vendors (a resilience strategy). There is no way to model "we can get this from Vendor A or Vendor B, and we prefer A."

**UX pattern recommendation:**

- Vendor association is a **secondary panel on the product form**, not a top-level screen. It should never require leaving the product being edited.
- For the vendor portal (future Blazor WASM app), the inverse view is the primary one: "all products I supply" is the vendor's home screen.
- For the recall scenario: a **vendor product list view** with bulk status-change capability (select 12 products → Discontinue all → confirm) is a non-negotiable workflow.

```
┌─ Product: AQUAPAWS-FOUNTAIN-MD ────────────────────────┐
│  [Details] [Variants] [Vendors] [Compliance] [Listings]│
│                                                        │
│  VENDOR ASSOCIATIONS                                   │
│  ┌──────────────────┬────────┬──────────┬───────────┐  │
│  │ Vendor           │ Cost   │ Lead Time │ Preferred │  │
│  ├──────────────────┼────────┼──────────┼───────────┤  │
│  │ PawsFirst Supply │ $12.40 │ 14 days  │    ●      │  │
│  │ MegaPet Dist.    │ $13.10 │  7 days  │    ○      │  │
│  └──────────────────┴────────┴──────────┴───────────┘  │
│  [+ Associate Vendor]                                  │
└────────────────────────────────────────────────────────┘
```

**⚠ Owned by PO/Engineer to decide:** Does Vendor belong in Product Catalog BC, or in a dedicated Vendor Management BC? This is a bounded context boundary question with real UX consequences. If vendors are in a separate BC, then the product form is consuming a cross-BC read model, and eventual consistency means a newly-added vendor might not appear in the product form dropdown for a few seconds. That latency must be designed for.

---

### 1.3 — Regulatory / Compliance Metadata

**Who uses this?**

| User | Relationship to Compliance |
|---|---|
| **Compliance Officer / Legal** | Defines and audits what classifications apply to which products |
| **Catalog Manager** | Fills in compliance fields when adding products; is blocked if they don't have the information |
| **Listings Submitter** | Cannot submit a listing to Amazon or Walmart without correct compliance tags |
| **Operations / Shipping** | Needs hazmat classification to route shipments correctly |
| **Customer** (indirectly) | Sees age restriction warnings on PDPs; affected by blocked shipment to restricted regions |

**Primary workflow:** A catalog manager is adding a new flea-and-tick treatment product. The product form should guide them through: Is this a hazardous material? (Yes → select classification) Does it have age restrictions? (Yes → 18+) Are there state-level restrictions? (Yes → California, Hawaii → explain why). The form should not let them complete the product record until compliance fields are resolved — but it also should not silently block them without explaining what is needed and why.

**Pain points if this stays unresolved:**

- **Listing rejection with no clear explanation.** A catalog manager submits to Amazon, gets rejected for "compliance data missing." They have no idea what that means because the catalog form never collected it.
- **Legal exposure from silent omission.** If a product ships to a restricted jurisdiction because the restriction was never recorded, the company is liable.
- **Compliance becomes a tribal knowledge problem.** One person "knows" which products are hazmat; when they leave, that knowledge leaves too.
- **The PO correctly identified recall cascade as eventually consistent.** From a UX angle: compliance is the data that makes a recall cascade *deterministic* — you can only recall the right products if you know which ones share a compliance characteristic.

**UX pattern recommendation:**

A **progressive disclosure form** — simple for compliant products, revealing additional fields only when a triggering condition is met:

```
COMPLIANCE & REGULATORY
  Is this product a hazardous material?  ○ Yes  ● No

  Does this product have an age restriction?  ○ Yes  ● No

  Are there geographic sales restrictions?  ○ Yes  ● No

  ────────── when "Yes" is selected for any above ──────────

  [Hazmat Classification]  [Primary Regulation Ref]
  ┌──────────────────────────────────────────────────┐
  │ ORM-D  ▼        │  CFR 49 Part 173  ____________ │
  └──────────────────────────────────────────────────┘

  Restricted Jurisdictions:
  [California ✕] [Hawaii ✕]  [+ Add Restriction]

  ⚠ These restrictions will block listing to Amazon US
    for customers with CA/HI shipping addresses.
    [Learn more]
```

**Critical UX principle for compliance fields:** Never silently enforce. Always tell the user *what* is being blocked and *why*, with a reference to the rule or requirement. Compliance frustration almost always comes from invisible enforcement.

---

### 1.4 — Structured Category Taxonomy

**Who uses this?**

| User | Relationship to Taxonomy |
|---|---|
| **Catalog Manager** | Assigns products to the correct category; relies on taxonomy for consistency |
| **Merchandiser** | Creates category landing pages and navigation; needs the hierarchy to be meaningful |
| **Customer** (indirectly) | Browses by category; "Dog > Feeding > Fountains" is a navigable path |
| **Channel Mapper** | Maps CritterSupply's category hierarchy to Amazon's, Walmart's, etc. |

**Primary workflow — two distinct workflows, not one:**

1. **Taxonomy management** (done rarely, by a category/ops admin): Define the hierarchy. "Dog → Water & Feeding → Fountains." Add, rename, merge, retire categories.
2. **Product categorization** (done frequently, by catalog manager): Assign a product to its place in the hierarchy. This must be a tree-picker or autocomplete, not a free-text string.

**Pain points if the string-based category stays:**

- **"Dog" vs "Dogs" vs "dog" all coexist.** Free text means inconsistency at data entry time, compounding over hundreds of products.
- **Category-level marketplace mapping is impossible.** If Amazon requires us to map "Dog > Feeding > Fountains" to their `pet_supplies/dogs/bowls_and_feeders/water_fountains`, we cannot do that mapping once and apply it to all products in that category — because the category is not a structured entity, it is just a string on each product record.
- **Faceted filtering on the storefront is unreliable.** Filter by "Dog" and miss everything filed as "Dogs."
- **The PO correctly called out per-listing category mapping as a maintenance nightmare.** This is the root cause — fix the taxonomy, and the marketplace mapping problem becomes tractable.

**UX pattern recommendation:**

- **Taxonomy editor:** A tree view component (MudBlazor `MudTreeView`) for managing the hierarchy. Drag-to-reorder. Click-to-rename. "Merge with…" action for consolidating duplicates during the migration from flat strings.
- **Product categorization:** A cascading picker or type-ahead search that resolves to a taxonomy node ID, not a string. Show the full path: `Dog > Water & Feeding > Fountains`.
- **Migration path:** This is a UX-critical one-time workflow — migrating the existing flat-string categories into the new taxonomy. The UI needs a "bulk recategorize" tool: show me all products where `category = 'Dog'`, let me select them all, and assign them to `Dog > Uncategorized` (a temporary holding node) in one action.

---

## 11. UX Review of the Listings Lifecycle

### 2.1 — The Lifecycle from the User's Perspective

The PO's proposed states are technically accurate. But users do not think about state machines — they think about *what they are trying to do right now*. Let me reframe the lifecycle in task-oriented language:

| Technical State | User's Mental Model | "I am..." |
|---|---|---|
| `Draft` | **Working on it** | "Building out this listing, it's not ready yet" |
| `ReadyForReview` | **Waiting for approval** | "I submitted it for review, it's out of my hands" |
| `Submitted` | **Waiting for the channel** | "We sent it to Amazon, waiting for them to accept it" |
| `Live` | **Active, earning** | "This listing is live and selling" |
| `Paused` | **Intentionally off** | "I turned this off temporarily, I can turn it back on" |
| `Ended` | **Permanently closed** | "This listing is done — either we chose to close it, or the channel removed it" |

**⚠ UX concern — "Ended" is doing too much work.** From a user perspective, there is a meaningful difference between:
- "We chose to end this listing" (intentional, controlled)
- "Amazon removed this listing" (external, unexpected — requires action)
- "The listing expired due to a time-limited promotion" (system-triggered, expected)

All three look like `Ended` in the proposed model. The user needs to know *why* something ended to know what to do next. I recommend surfacing this as a **reason/cause attribute on the Ended state**, not necessarily a separate state in the domain model. This is a question for the Principal Engineer on whether that belongs on the aggregate or as a separate event payload.

---

### 2.2 — Actions, Information, and Feedback at Each Stage

```
╔══════════════════════════════════════════════════════════════════════╗
║  DRAFT                                                               ║
╠══════════════════════════════════════════════════════════════════════╣
║  User actions:   Edit all listing fields, Upload channel-specific   ║
║                  images, Set pricing, Map category, Preview          ║
║                                                                      ║
║  Must see:       Completion checklist ("7 of 9 required fields       ║
║                  complete"), Which fields are required by THIS        ║
║                  channel (Amazon requires more than our own site),   ║
║                  Draft auto-save indicator, Preview of how it        ║
║                  will appear on the channel                          ║
║                                                                      ║
║  Primary CTA:    [Submit for Review]  (disabled until checklist OK)  ║
║  Secondary CTA:  [Save Draft] [Preview] [Discard]                   ║
║                                                                      ║
║  Error states:   Catalog product was discontinued while draft        ║
║                  was in progress → banner warning, block submit      ║
╚══════════════════════════════════════════════════════════════════════╝

╔══════════════════════════════════════════════════════════════════════╗
║  READY FOR REVIEW                                                    ║
╠══════════════════════════════════════════════════════════════════════╣
║  User actions:   View only (submitter); Approve/Reject (reviewer)   ║
║                  Add review notes, Request changes                   ║
║                                                                      ║
║  Must see:       Who submitted, When submitted, Time in review       ║
║                  queue, Reviewer comments, Side-by-side diff if      ║
║                  this is an update to an existing Live listing       ║
║                                                                      ║
║  Primary CTA:    [Approve → Submit to Channel] [Request Changes]    ║
║                  [Reject]                                            ║
║                                                                      ║
║  Error states:   Compliance check fails during review →             ║
║                  reviewer sees specific failures highlighted,         ║
║                  cannot approve until resolved                       ║
╚══════════════════════════════════════════════════════════════════════╝

╔══════════════════════════════════════════════════════════════════════╗
║  SUBMITTED (Awaiting Channel Acceptance)                             ║
╠══════════════════════════════════════════════════════════════════════╣
║  User actions:   View only; Withdraw submission (if channel allows) ║
║                                                                      ║
║  Must see:       Submitted timestamp, Estimated processing time      ║
║                  for this channel (Amazon: 24–72hrs), Real-time      ║
║                  status from channel API if available, Any channel   ║
║                  errors or rejection reasons returned                ║
║                                                                      ║
║  Primary CTA:    [Withdraw] (where channel supports it)             ║
║                                                                      ║
║  Error states:   ⚠ HIGH RISK — Channel rejects the listing.        ║
║                  User must see the rejection reason IN PLAIN         ║
║                  ENGLISH, not the raw API error code from Amazon.   ║
║                  → Return to Draft with pre-populated rejection log  ║
╚══════════════════════════════════════════════════════════════════════╝

╔══════════════════════════════════════════════════════════════════════╗
║  LIVE                                                                ║
╠══════════════════════════════════════════════════════════════════════╣
║  User actions:   Pause, End, Edit (triggers new Draft/Review cycle  ║
║                  for the updated version), View performance data     ║
║                                                                      ║
║  Must see:       Channel listing URL (deep link to the live page),   ║
║                  Current price on channel, Inventory level shown     ║
║                  on channel, Last sync timestamp, Performance KPIs   ║
║                  (views, clicks, conversion — if channel provides)  ║
║                                                                      ║
║  Primary CTA:    [Pause Listing] [Edit Listing] [View on Channel]  ║
║                                                                      ║
║  Error states:   Channel reports listing as suppressed (Amazon      ║
║                  suppresses for policy violations) → alert badge,   ║
║                  reason surfaced, action required                    ║
╚══════════════════════════════════════════════════════════════════════╝

╔══════════════════════════════════════════════════════════════════════╗
║  PAUSED                                                              ║
╠══════════════════════════════════════════════════════════════════════╣
║  User actions:   Resume (→ Live), End permanently                   ║
║                                                                      ║
║  Must see:       Why it was paused (user note field — mandatory     ║
║                  when pausing), Paused since date, Last Live date   ║
║                                                                      ║
║  Primary CTA:    [Resume Listing] [End Listing]                     ║
║                                                                      ║
║  ⚠ UX note: Paused on our site vs. Paused on Amazon are different  ║
║  concepts. "Paused" on our site might mean we stopped syncing;      ║
║  the Amazon listing may still be live in Amazon's system. The UI    ║
║  must be unambiguous about which system is paused.                  ║
╚══════════════════════════════════════════════════════════════════════╝

╔══════════════════════════════════════════════════════════════════════╗
║  ENDED                                                               ║
╠══════════════════════════════════════════════════════════════════════╣
║  User actions:   View history only; Clone to new Draft (for         ║
║                  re-listing); Export listing data                    ║
║                                                                      ║
║  Must see:       Why it ended (our choice / channel removed /        ║
║                  expired), Ended date, Full history log,            ║
║                  Performance summary for the listing's lifetime      ║
║                                                                      ║
║  Primary CTA:    [Clone as New Draft] (do not call it "Relist" —    ║
║                  that implies reactivation, but this creates a new   ║
║                  listing that goes through review again)             ║
╚══════════════════════════════════════════════════════════════════════╝
```

---

### 2.3 — Multi-Channel State Complexity

This is the most significant UX challenge in the Listings BC. A single CritterSupply product can have multiple listings — one per channel — each in a different state. The user must be able to answer these questions at a glance:

1. "Is the AquaPaws fountain listed on Amazon?" → Yes/No
2. "If yes, is it live?" → State badge
3. "What about Walmart? Our own site?" → Same, per channel

**Recommended pattern: Channel Status Matrix**

On a product's listings overview panel:

```
LISTINGS FOR: AquaPaws Dog Fountain (AQUAPAWS-FOUNTAIN-MD)
┌────────────────┬──────────┬────────────┬─────────────┐
│ Channel        │ Status   │ Price      │ Last Updated│
├────────────────┼──────────┼────────────┼─────────────┤
│ 🟢 Own Website │ LIVE     │ $34.99     │ 2 days ago  │
│ 🟡 Amazon US   │ IN REVIEW│ $36.99     │ 4 hours ago │
│ 🔴 Walmart US  │ REJECTED │ —          │ Yesterday   │
│ ── eBay US     │ Not listed│ —         │ —           │
└────────────────┴──────────┴────────────┴─────────────┘
[+ Create Listing for eBay US]
```

- Color-coded status badges, not just text labels
- Each row is clickable → goes to the full listing detail for that channel
- "Not listed" channels are shown greyed-out at the bottom, with a direct CTA to create a listing
- A `REJECTED` status shows an inline explanation tooltip with the channel's rejection reason

**⚠ Question for PO (preview of Part 6):** Is a "Listing" in our domain model one record that has multiple channel destinations, or one record *per channel*? The answer profoundly changes the UI. If it's one-per-channel (which I suspect from the BC description), then the matrix view above is an aggregate read model, not a single document view.

---

## 12. UX Mapping of Marketplace Identity (ChannelCode)

### 3.1 — How ChannelCode Should Surface in the Admin UI

The `ChannelCode` is a backend identifier. Users should never need to type `AMAZON_US` into a form field. From a UX standpoint, a ChannelCode always resolves to a **marketplace entity** with:

- A **display name** ("Amazon US")
- A **logo/icon** (for visual scanning in lists and badges)
- A **status indicator** (connected / degraded / disconnected)
- A **short code** displayed only in technical contexts (exports, logs): `AMAZON_US`

**How ChannelCode appears across the UI:**

| Context | Appearance |
|---|---|
| Listing status matrix | Logo + display name + status badge |
| Filter/facet panel | Checkbox list with logos |
| Listing form header | "Creating listing for: [Amazon logo] Amazon US" |
| Bulk action confirmation | "You are pausing 3 listings on Amazon US" |
| Error messages | "Amazon US rejected this listing because…" |
| API logs / export | `AMAZON_US` (technical context only) |
| Navigation tabs | "Amazon US" tab label |

**Never expose the raw `ChannelCode` string in user-facing UI.** It is a system identifier. Users think "Amazon," not "AMAZON_US."

---

### 3.2 — Seeing All Channels a Product Is Listed On

This is answered by the Channel Status Matrix from Part 2. But there is a second entry point: the **Marketplace dashboard**, where the user approaches from the channel side rather than the product side.

```
AMAZON US — Listings Overview
Status: 🟢 Connected  |  API Quota: 847/1000 remaining  |  Fees last updated: 3 days ago

┌──────────────────────────────────────────────────────────────────────┐
│  LIVE (127)  │  IN REVIEW (4)  │  PAUSED (12)  │  REJECTED (3)  │  │
├──────────────────────────────────────────────────────────────────────┤
│  [search]  [filter by category ▼]  [filter by status ▼]  [export]  │
├──────────────────────────────────────────────────────────────────────┤
│  Product            │ SKU           │ Status  │ Price  │ Actions    │
├─────────────────────┼───────────────┼─────────┼────────┼────────────┤
│  AquaPaws Fountain  │ AQUA-FOUNT-MD │ 🟢 Live │ $36.99 │ Edit | ⋮  │
│  PawsFirst Collar S │ PAWSFST-COL-S │ 🔴 Rej  │   —    │ Fix | ⋮  │
└─────────────────────────────────────────────────────────────────────┘
```

Two navigational entry points for the same data, different user intent:
- **Product-first navigation:** "I know which product I want to manage, show me its listings"
- **Channel-first navigation:** "I'm doing my Amazon morning check, show me everything on Amazon"

Both are valid primary workflows for different users (or the same user on different days). The IA must support both.

---

### 3.3 — Adding a New Marketplace Channel

This is an **infrequent, high-stakes, operations-owned workflow** — not something a catalog manager does. It should live under a `Settings > Marketplace Channels` area, not in the daily merchandising UI.

Proposed workflow as a wizard (3–4 steps):

```
STEP 1: IDENTIFY                       STEP 2: CONNECT
┌─────────────────────────┐            ┌─────────────────────────┐
│ Which marketplace?      │            │ Amazon US Credentials   │
│                         │            │                         │
│ ○ Amazon US             │  →  Next   │ Seller ID: [__________] │
│ ○ Walmart US            │            │ API Key:  [__________]  │
│ ○ eBay US               │            │ MWS Auth: [__________]  │
│ ○ Target                │            │                         │
│ ○ Other (custom)        │            │ [Test Connection]       │
└─────────────────────────┘            │ ✓ Connection successful │
                                       └─────────────────────────┘

STEP 3: CONFIGURE                      STEP 4: CONFIRM
┌─────────────────────────┐            ┌─────────────────────────┐
│ Category Mappings       │            │ Amazon US is ready.     │
│                         │            │                         │
│ Dog > Feeding >         │  →  Save   │ ChannelCode: AMAZON_US  │
│   Fountains             │            │ Status: 🟢 Connected    │
│   → Amazon: pet_sup..   │            │ 0 listings              │
│                         │            │                         │
│ [+ Add mapping]         │            │ [Go to Amazon Listings] │
└─────────────────────────┘            └─────────────────────────┘
```

**⚠ Owned by PO/Engineer to decide:** What happens to existing products when a new channel is added? Does the system automatically create Draft listings for all Active products? Or does the user manually choose which products to list? This is a business workflow decision with significant UX implications — auto-creation could flood the review queue; manual selection requires tooling to find relevant products.

---

### 3.4 — Marketplace Health Status Indicators

A marketplace channel can fail silently. API credentials expire, rate limits hit, fee structures change without warning. The UI must surface these proactively — not after a listing submission fails mysteriously.

**Health indicators needed per channel:**

| Signal | Normal State | Warning State | Critical State |
|---|---|---|---|
| API connectivity | 🟢 Connected | 🟡 Intermittent | 🔴 Disconnected |
| API rate limit remaining | 🟢 > 50% remaining | 🟡 10–50% remaining | 🔴 < 10% remaining |
| Credential expiry | 🟢 > 30 days | 🟡 7–30 days | 🔴 < 7 days or expired |
| Fee schedule freshness | 🟢 Updated < 7 days | 🟡 7–30 days old | 🔴 > 30 days old |
| Pending channel notifications | 🟢 None | 🟡 Policy update | 🔴 Account warning |

**Where these appear:**
- **Persistent banner in the Marketplace settings page** for any 🔴 state
- **Status badge on the channel card** in the channel list view
- **Top-of-screen alert banner** for 🔴 Disconnected or 🔴 Expired credentials — this affects all listings on that channel
- **Notification in a global notification center** for any state transition from 🟢 to 🟡 or 🔴

**Key principle:** Credential expiry and disconnection warnings must arrive *early*, not at the moment of failure. A 🟡 warning 30 days before credential expiry means someone has time to act during business hours. A 🔴 alert on a Monday morning because credentials expired over the weekend is a preventable bad day.

---

## 13. Information Architecture Proposal

### 4.1 — What Is the "Primary Object" for a Merchandising User?

This is the most important IA decision, and it depends on role:

| User Role | Primary Daily Object | Secondary Object |
|---|---|---|
| **Catalog Manager** | **Product** | Listing (is this product available on channels?) |
| **Merchandiser** | **Listing** | Product (what's the source content?) |
| **Channel Ops** | **Marketplace** | Listing (what's the health of this channel?) |
| **Compliance Officer** | **Product** | Compliance flags across the catalog |

The MVP recommendation is to design for **Catalog Manager as the primary user**, since they are the one who will use the system from Day 1 (once an admin UI exists). Merchandiser and Channel Ops roles can be layered in as channels go live.

---

### 4.2 — Top-Level Navigation

```
┌─────────────────────────────────────────────────────────────────┐
│  🐾 CritterSupply Admin                    [🔔 3]  [👤 Admin]  │
├──────────────────────┬──────────────────────────────────────────┤
│                      │                                          │
│  📦 CATALOG          │  (main workspace)                        │
│     Products         │                                          │
│     Categories       │                                          │
│     Brands           │                                          │
│                      │                                          │
│  📋 LISTINGS         │                                          │
│     All Listings     │                                          │
│     Pending Review   │                                          │
│     Rejected    🔴3  │                                          │
│                      │                                          │
│  🌐 MARKETPLACES     │                                          │
│     Own Website 🟢   │                                          │
│     Amazon US   🟢   │                                          │
│     Walmart US  🟡   │                                          │
│     + Add Channel    │                                          │
│                      │                                          │
│  ── SETTINGS         │                                          │
│     Team             │                                          │
│     Vendors          │                                          │
│     Compliance Rules │                                          │
│                      │                                          │
└──────────────────────┴──────────────────────────────────────────┘
```

**Navigation design principles:**
- Each top-level section maps directly to a bounded context (Catalog, Listings, Marketplaces)
- Status indicators (🔴3, 🟡) are persistent in the nav — the user should not have to navigate somewhere to discover a problem
- "Pending Review" is a shortcut into the review queue — this is a daily-use link for a reviewer role
- Marketplace health is visible in the nav without clicking in

---

### 4.3 — The Catalog Manager's Workday Start View

When the catalog manager arrives in the morning, their first question is: **"What needs my attention today?"** Not "let me browse all 3,000 products." The workday-start view (think of it as a dashboard, but I prefer to call it a **Focus View** to avoid data-for-data's-sake dashboard thinking) should answer:

- What has changed in the catalog since I was last here?
- Are there listings in a state that requires my action?
- Are there any channel problems I need to know about?
- What tasks are specifically assigned to me?

```
GOOD MORNING, [USER]  ·  Tuesday, [Date]

┌─────────────────── YOUR ATTENTION QUEUE ───────────────────────────┐
│                                                                     │
│  🔴 3 listings rejected by Amazon — action required               │
│     [View Rejected Listings →]                                      │
│                                                                     │
│  🟡 Walmart US API credentials expire in 12 days                  │
│     [Renew Credentials →]                                           │
│                                                                     │
│  📋 4 listings awaiting your review (you are assigned reviewer)   │
│     [Go to Review Queue →]                                          │
│                                                                     │
│  ✅ No compliance issues flagged today                             │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘

┌─────────── RECENT CATALOG ACTIVITY ────────────────────────────────┐
│  Yesterday                                                          │
│  • PawsFirst added 12 new products via feed upload                 │
│  • AquaPaws Fountain (LG) status changed to Discontinued           │
│  • 3 products re-categorized by Jordan                             │
└─────────────────────────────────────────────────────────────────────┘

┌─────────── CHANNEL SNAPSHOT ───────────────────────────────────────┐
│  Own Website   🟢 247 Live   4 Paused   0 Issues                   │
│  Amazon US     🟢 183 Live  12 Paused   3 Rejected                 │
│  Walmart US    🟡 94 Live    6 Paused   0 Issues   ⚠ Creds expiring│
└─────────────────────────────────────────────────────────────────────┘
```

**Design rationale:** This view is task-driven, not metric-driven. Every item in the Attention Queue has a direct action link. The catalog manager should be able to clear their attention queue and know exactly what to work on. This is very different from a KPI dashboard — it is an **inbox**, not a report.

---

### 4.4 — How the Three BCs Relate Navigationally

```
CATALOG (Product Catalog BC)
    │
    │  A Product can have zero or more Listings
    │
    ▼
LISTINGS (Listings BC)
    │
    │  A Listing targets one Marketplace (by ChannelCode)
    │
    ▼
MARKETPLACES (Marketplaces BC)
```

The navigation reflects this hierarchy:
- From a **Product** → see all its Listings → click a Listing → see the Marketplace it targets
- From a **Marketplace** → see all its Listings → click a Listing → see the underlying Product
- From a **Listing** → see both the Product (source content) and the Marketplace (destination)

The Listing is the join entity between Product and Marketplace, and the UI must make that visible without confusing the user about which BC they are "in."

---

## 14. UX Risk Register

### 5.1 — PO-Identified Risks, UX Lens Added

---

**Risk 1: Tight Coupling Between Listings and Product Catalog**

*PO concern:* Changes in Product Catalog cascade unexpectedly into Listings.

*User-facing consequence:* A catalog manager changes a product's description for a legitimate reason (new regulatory wording required), and the change is automatically reflected on a Live Amazon listing without any review step. The customer on Amazon sees "updated 2 minutes ago" on a listing that just had its copy changed by someone who may not know Amazon's content policies. Worst case: the update triggers Amazon's listing suppression algorithm.

*UX guardrail:* When a product field that feeds a live listing is edited, surface an explicit warning modal:

```
⚠ This product has 3 live listings.

  Changing the description will affect:
  • Amazon US (Live)
  • Walmart US (Live)
  • Own Website (Live)

  How would you like to apply this change?

  ○ Update all listings immediately (no review)
  ○ Create a pending update for each listing
    (current live version stays until approved)

  [Proceed]  [Cancel]
```

This is a pattern from content management systems (CMS "publish" vs. "save draft"). The user must consciously choose the propagation behavior.

---

**Risk 2: Building Listings Before Variant Model is Resolved**

*PO concern:* Hard prerequisite — variants must come first.

*User-facing consequence:* If we build the Listings UI before variants exist, catalog managers will create listings at the individual-SKU level (e.g., a separate Amazon listing for AquaPaws Small, Medium, and Large). Amazon actually *penalizes* sellers for this — Amazon expects a parent listing with child variations. We will build the wrong abstraction in the UI and have to redesign it once variants arrive, confusing users who have already formed habits around the wrong model.

*UX guardrail:* Do not build a Listings UI until the variant model is in place. This is not a UX guardrail — it is a hard prerequisite. If we build the Listings UI pre-variant, we are designing it around a broken domain model and will waste the investment.

*Recommendation to PO and Principal Engineer:* Gate the Listings UI behind a feature flag that requires "variant model complete" to be true. This is as much a product management decision as a UX one.

---

**Risk 3: Compliance Not Enforced at Listing Submission**

*PO concern:* A listing can be submitted to a channel without compliance metadata on the product.

*User-facing consequence:* The listing gets rejected by the channel with an opaque error code, or worse — it gets accepted and then pulled down later for a compliance violation, which can result in seller account warnings on Amazon or Walmart. The user's experience is: "I did everything right and the system let me, but now we have a problem I don't understand."

*UX guardrail:* Enforce compliance completeness as a **blocking gate on the "Submit for Review" action**, not as an async validation after submission. The listing form should show:

```
⛔ Compliance check failed — cannot submit

  Product: AquaPaws Flea & Tick Treatment
  
  Missing required fields for Amazon US:
  • Hazmat classification (required for all pesticide products)
  • CA Prop 65 warning status
  
  [Complete compliance data →]
```

This tells the user specifically what is missing and links directly to where they fix it. No guessing, no waiting for rejection.

---

**Risk 4: Per-Listing Category Mappings (Maintenance Nightmare)**

*PO concern:* If each listing has its own category mapping to the channel's taxonomy, updating that mapping across 200 listings becomes 200 edits.

*User-facing consequence:* A catalog manager discovers that CritterSupply has been mapping Dog Fountain products to the wrong Amazon category (using "Dog Bowls" instead of "Water Fountains"). To fix it, they currently must open and edit 47 individual listings. This is a half-day of clicking that shouldn't exist.

*UX guardrail:* The category mapping UI must be at the **taxonomy node level**, not the listing level. The edit workflow is: go to `Catalog > Categories > Dog > Water & Feeding > Fountains`, click "Channel Mappings," update the Amazon mapping once. All 47 listings automatically inherit the new mapping on their next sync.

Surface this clearly in the listing detail view: "Category mapping inherited from taxonomy: `Dog > Water & Feeding > Fountains → Amazon: Water Fountains`. [Override for this listing only]"

The "override" escape hatch is important — sometimes one product in a category genuinely belongs in a different channel category — but it should be the exception, not the default.

---

**Risk 5: Recall Cascade Eventually Consistent (Legal Risk)**

*PO concern:* A product recall might propagate to channel takedowns with a delay, creating a window where a recalled product is still purchasable.

*User-facing consequence:* A shopper on Amazon buys a recalled product 4 minutes after the recall was issued, because the Listings BC hasn't yet processed the `ProductDiscontinued` event from Catalog BC. This is not just a bad experience — it is a legal and safety issue.

*UX guardrail:* This is primarily an engineering decision (make the cascade synchronous or near-synchronous for recalls), but UX can support it with:

1. A **Recall workflow** that is separate from the normal `Discontinue` flow. Instead of just changing status, "Initiate Recall" is its own high-urgency command, with a confirmation that forces the user to explicitly acknowledge they want immediate channel takedown.
2. A **post-recall status screen** that shows, in near-real-time, which channels have confirmed the takedown and which are still pending. This is an audit trail as much as a UX feature.
3. **Do not rely on eventual consistency for recall.** This is my recommendation to the Principal Engineer: the recall command should be a synchronous saga or a high-priority message queue path, not a standard background projection.

---

### 5.2 — UX-Specific Risks the PO Did Not Identify

---

**UX Risk A: Role Ambiguity — Who Can Do What, and Can the UI Enforce It?**

*Description:* The PO describes a review/approval workflow (Draft → ReadyForReview requires an approver). But today there is no admin user model, no roles, no permissions. If we build a review workflow in the UI without an underlying RBAC model, either: (a) anyone can approve anything (the review is theater), or (b) we hard-code reviewer logic that becomes unmaintainable.

*User-facing consequence:* A catalog manager accidentally approves their own listing because the "Approve" button is visible to them. Or a reviewer cannot act because the system doesn't know who the reviewers are.

*UX guardrail:* Before building any listing management UI, define at minimum three roles: `CatalogManager`, `ListingReviewer`, `ChannelAdmin`. These roles gate which actions are visible in the UI. This is a P0 prerequisite — **raise this with the Principal Engineer immediately.** Without RBAC, we cannot build a review workflow.

---

**UX Risk B: No Admin UI Means Entrenched API Habits That Will Fight the UI**

*Description:* Because there is no admin UI today, whoever currently manages products has built workflows around the API (Postman collections, scripts, direct HTTP calls). When we introduce a UI, those users will compare every action to their current API workflow. If the UI is slower or less capable than their current workarounds, they will abandon it.

*User-facing consequence:* The admin UI is built but not adopted. Products continue to be managed via API. The UI atrophies and becomes inaccurate about what the system actually contains.

*UX guardrail:* Before designing any admin UI screens, conduct a **contextual inquiry** with whoever currently manages catalog data (even if that person is a developer). Document their actual workflow, their pain points with the API approach, and the cases where the API is actually *better* for them (e.g., bulk imports via script). The UI must match or exceed their current workflow efficiency for adoption to happen. This is the **#1 UX research priority before any UI work begins**.

---

**UX Risk C: Eventual Consistency Creates Invisible Stale State**

*Description:* The system uses event sourcing and RabbitMQ for inter-BC messaging. This means that when a catalog manager updates a product description, the Listings BC might not reflect that change for seconds, minutes, or (in degraded states) longer. If the UI does not communicate this, users will assume the update is immediate and be confused when the listing detail still shows the old description.

*User-facing consequence:* "I just changed the description. Why does the Amazon listing still show the old one? Did my save work? Should I try again?" → User saves again → duplicate event or race condition.

*UX guardrail:* Every cross-BC data display needs a **data freshness indicator** and a clear mental model. When a user updates a product description:
1. Show an optimistic confirmation: "Saved. Listings will update shortly."
2. Show a "Last synced" timestamp on listing data from the Catalog, not just "last updated."
3. If sync has not occurred in > 5 minutes after an edit, show a subtle indicator: "⏳ Listing reflects catalog as of 6 minutes ago."
4. Never show a spinner indefinitely — set a timeout and show "Sync taking longer than expected. [Refresh]"

---

## 15. UX Questions for the Product Owner

The following questions need PO decisions before UX can finalize direction. I've flagged the **urgency** of each.

---

**Q1 — Listing Definition (URGENT)**  
Is a "Listing" in our domain model one record per channel, or one record per product that targets multiple channels? For example: when the AquaPaws fountain is listed on both Amazon and Walmart, are those two separate Listing entities with separate lifecycles, or one Listing entity with channel-specific attributes?

*Why it matters:* This determines whether the Channel Status Matrix view (showing one row per channel) is an aggregate projection over multiple listing records, or a single multi-channel listing form. The data model and the UI are fundamentally different in each case. Getting this wrong means rebuilding the UI.

---

**Q2 — Review Workflow Ownership (URGENT)**  
Who reviews listings before they go live — is this an internal CritterSupply role, a vendor self-approval, or a combination? Is the review workflow required for every channel, or only for external channels (not our own website)?

*Why it matters:* If the own website channel bypasses review, the workflow branches significantly. If vendors can self-approve for their own products, we need vendor-facing UI much earlier than expected.

---

**Q3 — Vendor Portal Timeline**  
You mentioned a future Blazor WASM Vendor Portal. What is the earliest we expect vendors to directly submit products or listings? Are vendors in scope for the MVP, or are they managed entirely by CritterSupply staff for the foreseeable future?

*Why it matters:* If vendors are in MVP scope, the Vendor Portal IA and the admin IA must be designed in parallel so they use the same mental model. If vendors are future-scope, I can focus entirely on the internal admin experience.

---

**Q4 — Multi-Variant Listing Behavior on Channels**  
When CritterSupply lists the AquaPaws fountain on Amazon, does each size variant get its own Amazon listing (ASIN), or do they form a parent-child variation set under one ASIN? Does this vary by channel (Amazon does parent-child; some channels don't)?

*Why it matters:* The answer determines whether the listing creation flow starts at the product family level (create one listing for the family, variants are children) or at the variant level (create individual listings per SKU). These are very different user workflows.

---

**Q5 — Compliance Enforcement Timing**  
When compliance data is missing, should the system block the listing from being *created*, block it from being *submitted for review*, or block the *reviewer from approving* it? Can a listing exist in Draft state without full compliance data?

*Why it matters:* If Draft is allowed without compliance, the form is more forgiving during content creation (better for catalog managers). But the reviewer then has to catch compliance gaps — is that the right workflow, or should the system catch it earlier?

---

**Q6 — The "Own Website" Channel**  
Is the CritterSupply own website (Storefront.Web) a first-class marketplace channel with its own ChannelCode (`OWN_WEBSITE`), meaning a product must have a Live listing to appear on the storefront? Or is the storefront driven directly from the Product Catalog status (Active = visible on storefront)?

*Why it matters:* If the storefront consumes Listings BC, then making a product visible on the website requires creating and approving a listing — even for internal catalog managers. That adds friction to a workflow that currently has zero friction (just mark as Active). If the storefront consumes the Catalog directly, we have two different systems for visibility (Listings for external channels, Catalog status for own site) that users will confuse.

---

**Q7 — Category Taxonomy Migration**  
The current category field is a free-text string. When we introduce the structured taxonomy, how do we handle the migration? Is this a data migration (one-time script), a user-driven recategorization workflow, or a combination? Who owns that migration work — catalog team, engineering, or a temporary project team?

*Why it matters:* The migration UI is the first admin workflow catalog managers will use on the new system. If it is confusing or slow, it poisons their perception of the whole admin experience before they've even used the catalog management tools. This needs to be designed with the same care as any production user-facing feature.

---

**Q8 — Channel-Specific Content Overrides**  
Can a listing on Amazon have a *different* product title, description, or images than the canonical catalog content? If yes, how much can be overridden — title only, or all content? If a catalog manager changes the canonical description, does that override the Amazon-specific title?

*Why it matters:* Channel-specific content override is a common e-commerce need (Amazon limits titles to 200 chars and has specific keyword requirements) but it creates a content management complexity that the UI must model carefully. Without a clear rule, users will be confused about which content they are editing and where it appears.

---

## 16. UX Recommendations by Priority

---

### P0 — Must Address Before Any UI is Built

**P0.1 — Conduct a Contextual Inquiry Before Building Anything**

*Owned by: UX*  
Interview and observe whoever currently manages product data. Document their actual workflow, their tools (Postman, scripts, spreadsheets), and the 3–5 jobs they do most often. Any admin UI that doesn't support these core jobs will be abandoned on arrival. This takes one afternoon of calendar time and will prevent weeks of rework. I will write the discussion guide and facilitate the session.

**P0.2 — Define Admin User Roles and Permissions Before Designing Any Workflow**

*Owned by: PO + Principal Engineer (UX will implement once decided)*  
At minimum: `CatalogManager`, `ListingReviewer`, `ChannelAdmin`, `ComplianceOfficer`, `Administrator`. Each role gates what actions are visible in the UI. Without this, the review workflow and the approval gates are security theater. The role model does not need to be fully built — but it needs to be decided before the first admin screen is designed, because role-sensitive UI is much harder to add retroactively.

**P0.3 — Resolve the Variant Model Before Designing Any Listing UI**

*Owned by: Principal Engineer (prerequisite) + UX (design, once resolved)*  
The Listings UI is built on top of the variant model. Building Listings UI before variants means designing around a temporary, wrong abstraction. This is the PO's prerequisite and I am reinforcing it from the UX side. The listing form's fundamental structure (does it start with a product family or an individual SKU?) cannot be designed without knowing how variants will be modeled.

---

### P1 — Should Address in the First UI Iteration

**P1.1 — Design the Catalog Manager Focus View (Workday Start Screen)**

*Owned by: UX*  
The Focus View (attention queue + channel snapshot + recent activity) is the anchor for the entire admin experience. It should be the first screen designed and prototyped, because it forces decisions about what data is available, what projections are needed, and what constitutes "needs attention." Design this first, then work backward to the individual management screens.

**P1.2 — Design the Channel Status Matrix as a Core Component**

*Owned by: UX*  
The cross-channel status summary (which channels is a product listed on, at what state) is the connective tissue between the Catalog, Listings, and Marketplaces BCs in the UI. It should be a reusable component that appears on the product detail page, in search results (as a compact indicator), and in the Listings views. Building this as a shared component enforces consistency and drives a conversation about the underlying projection structure needed to power it.

**P1.3 — Establish a Channel-Agnostic Ubiquitous Language for the Admin UI**

*Owned by: UX + PO*  
Before writing any button labels, error messages, or page headings, we need to agree on the terminology for the admin UI. Some examples that need a decision:

| Concept | Option A | Option B | Recommendation |
|---|---|---|---|
| The listing you submit to Amazon | "Listing" | "Channel Listing" | "Listing" (simpler) |
| Putting a listing live | "Publish" | "Go Live" | "Go Live" (matches user mental model) |
| Removing from a channel | "End" | "Delist" | "Delist" (clearer intent) |
| The internal product record | "Product" | "Catalog Item" | "Product" (simpler) |
| Parent product with variants | "Product Family" | "Product Group" | "Product Family" (clearer for pet retail) |

This is a one-hour workshop with the PO and one catalog manager to reach consensus. Once decided, it must be documented and treated as the ubiquitous language for this bounded context — every screen, every error message, every API response label (for admin-facing endpoints) must use these terms.

**P1.4 — Build Compliance as a Gate, Not an Afterthought**

*Owned by: UX + Principal Engineer*  
The compliance check must run as a **pre-submission validation** that produces specific, actionable error messages — not a channel rejection that the user has to interpret. This requires: (a) a compliance rule model (what fields are required for what channels and product categories), and (b) a UI pattern that surfaces failures in plain language with direct links to resolution. Design the compliance validation UX before implementing the listing submission flow.

---

### P2 — Future Iteration / Nice to Have

**P2.1 — Listing Performance Dashboard Per Channel**

*Owned by: UX (design) + Principal Engineer (projection design)*  
Once Live listings are generating traffic, catalog managers and merchandisers need to see channel-level performance: views, clicks, conversion, returns rate (where available from the channel API). Design this as a per-listing read model projection — not a BI tool — surfaced inline on the listing detail page. Keep it simple: trend line (last 30 days), compare to previous period, comparison to category average. No charts for their own sake.

**P2.2 — Bulk Operations Tooling**

*Owned by: UX*  
Catalog managers managing thousands of products need bulk actions: bulk recategorize, bulk status change, bulk listing creation ("list all Active products in the 'Dog > Feeding' category on Amazon"), bulk compliance review. These are table-stakes for operational efficiency at scale but are not MVP features. Design the bulk operation confirmation pattern early (so the single-item and bulk flows are consistent) even if implementation comes later.

**P2.3 — Vendor Self-Service Feed Upload (Vendor Portal)**

*Owned by: UX (design, when Vendor Portal is in scope)*  
When vendors can submit their own products, the workflow is an upload-and-review cycle: vendor uploads a CSV or connects their feed, the system maps their fields to our catalog model, mismatches surface as validation errors, catalog manager reviews and approves. This is a substantial UX effort (field mapping UI, validation error display, approval workflow) and should be treated as a separate project. Flag now so it is not scope-crept into the initial catalog admin UI.

---

## 17. Summary for Erik and the Principal Engineer

The most important UX observation from this session: **we are designing for users we have not yet met.** The catalog management user today is almost certainly a developer or technical ops person using the API directly. Before we build a UI around these three bounded contexts, we need to spend time with that person (or those people) and understand their actual work. Every P0 recommendation above is oriented toward getting that foundation right.

The bounded context structure the PO recommends — separate Catalog, Listings, and Marketplaces BCs — is correct from a UX standpoint. It maps cleanly to three different user concerns (what we sell, how we present it on channels, which channels we operate on). The seams between them are navigable in the UI if we design the connective tissue carefully (the Channel Status Matrix, the Focus View, and the role model are the three load-bearing pieces).

The two non-negotiable prerequisites from the UX side:
1. **Contextual inquiry with current catalog users before any UI screen is finalized**
2. **Variant model resolved before Listings UI is designed**

Everything else can be iterated.

---

*Document prepared by: UX Engineer — merged into combined PO + UX discovery document.*  
*UX questions and research plan available on request.*  
*Next suggested touchpoint: 30-minute sync with PO to align on terminology (Section 16 P1.3).*  
*Next step after Owner/Erik decisions: Principal Engineer synthesis into architecture evolution plan.*
