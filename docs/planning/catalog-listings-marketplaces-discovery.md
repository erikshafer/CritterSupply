# Product Catalog Evolution + Listings & Marketplaces Bounded Contexts
## Discovery Session: Product Owner Analysis

**Document Owner:** Product Owner  
**Status:** 🟡 Draft — Awaiting UX Engineer output and Owner/Erik decisions  
**Date:** 2026-03-10  
**Session Type:** PO Discovery (parallel to UX Engineer session; outputs will be merged)  
**Triggered by:** Owner request to evolve Product Catalog toward multi-channel marketplace selling  
**Companion Documents (pending merge):**
- UX Engineer session output *(in progress)*
- Principal Engineer synthesis *(pending PO + UX merge)*

---

## Table of Contents

1. [Review of Current Product Catalog BC](#1-review-of-current-product-catalog-bc)
2. [Discovery Questions — Product Catalog Evolution](#2-discovery-questions--product-catalog-evolution)
3. [Discovery Questions — Listings BC](#3-discovery-questions--listings-bc)
4. [Discovery Questions — Marketplaces BC](#4-discovery-questions--marketplaces-bc)
5. [Business Workflow Sketches](#5-business-workflow-sketches)
6. [Risks and Business Concerns](#6-risks-and-business-concerns)
7. [PO Recommendations](#7-po-recommendations)
8. [Decisions Needed](#8-decisions-needed)

---

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

> **🔴 Owner/Erik Decision Required:** What is the variant model for CritterSupply? This decision has cascading effects on Listings, Inventory, Pricing, and the storefront experience.

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

> **🔴 Owner/Erik Decision Required:** Does category-to-marketplace mapping live in Product Catalog BC (as a subdomain) or in Marketplaces BC (as a marketplace-owned concern)?

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

> **🔴 Owner/Erik Decision Required:** What is the expected "rate of change" for marketplace configuration? If we add a marketplace once every two years and never change credentials in the system, an enum is fine. If we're actively managing credentials, rate limits, and schema versions across six marketplaces simultaneously, we need a proper aggregate.

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

| # | Decision | Context | Impact if Deferred |
|---|----------|---------|-------------------|
| D1 | Variant model (parent-child vs. standalone with family link vs. embedded) | Section 2.1 Q1 | Blocks Listings BC design entirely |
| D2 | Is our own website a formal "channel" in the Listings model? | Section 3.2 Q6 | Determines Listings BC scope for Phase 1 |
| D3 | Does Listings BC own marketplace API integration, or is there an adapter layer? | Section 3.4 Q12 | Determines failure handling architecture |
| D4 | Is a Marketplace an aggregate (event-sourced or document) or configuration/enum? | Section 4.1 Q1 | Determines Marketplaces BC complexity |
| D5 | Does category-to-marketplace mapping live in Product Catalog BC or Marketplaces BC? | Section 2.4 Q12 | Critical for taxonomy change cascade workflow |
| D6 | Are credentials managed in Marketplaces BC or infrastructure (Vault)? | Section 4.2 Q5 | Security posture and operational responsibility |
| D7 | Is lot/batch tracking in scope for Inventory BC? | Section 5 Workflow 3 | Determines precision of product recall scope |
| D8 | Compliance metadata — required at Listings BC launch or deferrable? | Section 2.3 Q9 | Amazon will reject listings without hazmat data |
| D9 | Automated seasonal product reactivation vs. manual | Section 2.2 Q5 | Operational overhead for seasonal catalog |
| D10 | Schema versioning for marketplace attribute definitions | Section 4.2 Q8 | Risk: breaking listings on marketplace API version changes |

---

*Document prepared by: Product Owner*  
*Pending merge with: UX Engineer discovery session output*  
*Next step: Principal Engineer synthesis into architecture evolution plan*
