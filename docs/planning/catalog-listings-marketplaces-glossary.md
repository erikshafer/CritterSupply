# CritterSupply Ubiquitous Language Glossary
## Product Catalog · Listings · Marketplaces

**Document Owners:** Product Owner + UX Engineer  
**Status:** 🟢 Authoritative — adopted as team standard  
**Date:** 2026-03-10  
**Scope:** Product Catalog BC, Listings BC, Marketplaces BC  
**Audience:** Engineering, UX, QA, Documentation, Team Communication

---

## How to Use This Document

This glossary is **normative**. Every term listed here is the canonical name for the concept it describes.
When a term appears in code, a UI label, an API response, an event type name, or a team conversation,
it must use the canonical spelling and casing shown here. Aliases and rejected terms are listed so
developers can recognize them in the existing codebase and migrate them on contact.

**Casing conventions used throughout:**
- Domain events: `PascalCase` (e.g., `ProductDiscontinued`)
- Commands: `PascalCase` verb-noun (e.g., `SubmitListing`)
- C# classes/records: `PascalCase`
- JSON properties / API fields: `camelCase`
- UI copy: Sentence case for labels, Title Case for page headings

---

## Table of Contents

1. [Product Catalog Vocabulary](#1-product-catalog-vocabulary)
   - 1.1 [Core Entities](#11-core-entities)
   - 1.2 [Compliance and Regulatory Terms](#12-compliance-and-regulatory-terms)
   - 1.3 [Lifecycle Actions](#13-lifecycle-actions)
2. [Listings Vocabulary](#2-listings-vocabulary)
   - 2.1 [Core Entities](#21-core-entities)
   - 2.2 [Listing States](#22-listing-states)
   - 2.3 [Listing Actions](#23-listing-actions)
   - 2.4 [Ended Reasons](#24-ended-reasons)
3. [Marketplaces Vocabulary](#3-marketplaces-vocabulary)
   - 3.1 [Core Entities](#31-core-entities)
   - 3.2 [Lifecycle Actions](#32-lifecycle-actions)
4. [Cross-Cutting Terms](#4-cross-cutting-terms)
5. [Ambiguous Terms — Handle With Care](#5-ambiguous-terms--handle-with-care)

---

## 1. Product Catalog Vocabulary

The Product Catalog BC is the **source of truth for what CritterSupply sells**. It owns the master product
record — identity, content, structure, compliance metadata, and lifecycle status. It does not own pricing,
inventory levels, or marketplace presence. Those are separate concerns.

---

### 1.1 Core Entities

---

#### Product

**Definition:** The master record representing a single sellable item in CritterSupply's catalog. A Product
is identified by its SKU, holds authoritative content (name, description, images, dimensions, compliance
metadata), and owns its own lifecycle status. A Product is the source of truth for "what this thing is."
It does not contain price or stock level — those live in Pricing BC and Inventory BC respectively.

**Bounded Context:** Product Catalog BC (authoritative). Referenced (read-only) by Listings, Inventory,
Pricing, Fulfillment, Shopping.

**Aliases / Rejected Terms:**
- ~~Catalog Item~~ — rejected; "Item" is overloaded (also used for cart line items and order line items).
  "Product" is what vendors, buyers, and customers call it.
- ~~Article~~ — rejected; European retail jargon, not natural in North American e-commerce.
- ~~Item~~ — rejected when referring to the catalog record; reserve "item" for order/cart line items.

**Code Usage:**
- Class: `Product`
- Stream type (event sourcing): `Product`
- API route: `GET /api/catalog/products/{sku}`
- Integration event payload field: `sku` (identifies the product)
- UI label: "Product", "Products" (plural)

**Example:** `DOG-BOWL-001` is a Product — a 32 oz stainless steel dog feeding bowl manufactured by
PetSupreme. It has a name, description, images, weight dimensions, and a compliance note. Its price
($14.99) lives in Pricing BC. Its stock level (142 units in Warehouse A) lives in Inventory BC.

---

#### SKU (Stock Keeping Unit)

**Definition:** The stable, human-readable, immutable identifier for a Product. A SKU is assigned at
the time of product creation and never changes. It is the canonical identity anchor across all bounded
contexts — Inventory, Pricing, Listings, and Fulfillment all reference products by SKU. SKUs follow
the pattern `CATEGORY-NAME-NNN` (e.g., `DOG-BOWL-001`, `CAT-TOY-047`, `FLEA-TX-003`).

**Bounded Context:** Product Catalog BC (assigned and owned). Used as a foreign key reference in all
other BCs.

**Aliases / Rejected Terms:**
- ~~Product ID~~ — rejected; "ID" implies an opaque surrogate key (like a UUID). SKU is human-readable
  and semantically meaningful. Use "SKU" everywhere — in logs, API responses, error messages.
- ~~Part Number~~ — rejected; manufacturing jargon, not appropriate for retail catalog context.
- ~~Item Code~~ — rejected; vague; "code" is ambiguous with status codes, channel codes, etc.

**Code Usage:**
- Type: `Sku` (value object wrapping `string`, validated format)
- Stream ID: UUID v5 derived from `"catalog:{sku}"` per ADR 0016
- API path parameter: `{sku}` (e.g., `GET /api/catalog/products/DOG-BOWL-001`)
- Property name: `sku` (JSON), `Sku` (C#)
- Domain event field: `Sku` (always present on all Product Catalog events)

**Example:** `FLEA-TX-003` is the SKU for Frontline Plus Flea & Tick Treatment (6-count, large dogs).
This same SKU appears in Inventory (stock level), Pricing (MAP price and promotional price), Listings
(the Amazon and Walmart listings for this product), and Fulfillment (hazmat routing rules).

---

#### Product Family

**Definition:** A named grouping of related Product Variants that share a common identity — the same
product concept in different configurations. A Product Family holds shared content (display name,
brand, base description, category) that applies to all its variants. Each variant within the family
has its own SKU and differentiating attributes (size, color, scent, etc.). The Product Family is
the unit customers browse; the individual SKU/variant is the unit they add to cart.

**Bounded Context:** Product Catalog BC (owned). Referenced by Listings BC (a listing may be created
for a family, grouping all variants under one marketplace parent listing).

**Aliases / Rejected Terms:**
- ~~Product Group~~ — rejected; "Group" is too generic and implies administrative categorization rather
  than a product-first concept. "Family" communicates shared lineage, which is the correct mental model.
- ~~Parent Product~~ — acceptable as informal shorthand in technical discussions, but "Product Family"
  is the canonical term in UI, events, and documentation. Use "parent" only when explicitly contrasting
  with "child" in a technical explanation.
- ~~Product Bundle~~ — rejected; a "Bundle" is a set of distinct products sold together (e.g., "Starter
  Kit"). A Family is one product concept in multiple configurations.
- ~~Style~~ — rejected; apparel jargon.

**Code Usage:**
- Class: `ProductFamily`
- Event: `ProductFamilyCreated`, `ProductVariantAddedToFamily`
- Property: `familyId` (on a Product Variant record)
- UI heading: "Product Family", "Manage Product Family"

**Example:** "PetSupreme Nylon Dog Collar" is a Product Family. It contains six variants:
`DOG-COLLAR-S-BLK`, `DOG-COLLAR-M-BLK`, `DOG-COLLAR-L-BLK`, `DOG-COLLAR-S-RED`,
`DOG-COLLAR-M-RED`, `DOG-COLLAR-L-RED`. Each is a distinct SKU with its own inventory and price.
On the Amazon listing, they appear as a single parent ASIN with a size+color selector.

---

#### Product Variant

**Definition:** A specific, purchasable configuration of a Product Family. A Variant has its own SKU
(it is a full Product record), and it carries the differentiating attributes — the properties that
distinguish it from sibling variants in the same family (size, color, flavor, weight, count, scent).
Every purchasable unit in the catalog is a Variant; not every product has siblings.

**Bounded Context:** Product Catalog BC (owned). Inventory BC tracks stock per Variant SKU. Listings
BC creates listings at the Variant level (or groups them under a family-level listing).

**Aliases / Rejected Terms:**
- ~~Child Product~~ — acceptable informally, but "Variant" is the canonical term. It names the concept,
  not the relationship.
- ~~Option~~ — rejected; Shopify/storefront jargon for the attribute (e.g., "Size" is an option, "Large"
  is the option value). We use "variant" for the product record and "attribute" for the differentiating
  property.
- ~~Sub-product~~ — rejected; implies a subset, which misrepresents the relationship (a variant is a
  full, independent purchasable unit).

**Code Usage:**
- No separate class initially — a Variant IS a `Product` record with `familyId` set
- Property: `variantAttributes` (e.g., `{ "size": "Large", "color": "Black" }`)
- Event: `ProductVariantAdded` (Phase 2)
- UI label: "Variant", "Add variant", "Variants (6)"

**Example:** `DOG-COLLAR-L-RED` is a Variant of the "PetSupreme Nylon Dog Collar" Product Family.
Its differentiating attributes are `size: Large` and `color: Red`. It has its own stock level (23 units),
its own price ($12.99), and its own Listings (it may be live on Amazon while its medium sibling is paused).

---

#### Product Status

**Definition:** The current lifecycle state of a Product, representing CritterSupply's internal selling
intent. Product Status governs whether a product is available for listing, purchasing, and fulfillment.
This is distinct from Listing State (which governs a product's presence on a specific channel) — a
product may be `Active` in the catalog while its listing on Amazon is `Paused`.

**Bounded Context:** Product Catalog BC (owned and authoritative).

**Valid values:**

| Status | Meaning |
|---|---|
| `Active` | Normal selling state. Listable, purchasable, fulfillable. |
| `ComingSoon` | Pre-launch. Visible for pre-order on supported channels; not yet fulfillable. |
| `OutOfSeason` | Planned temporary pause. Listings should be Paused, not Ended. Product will return. |
| `Discontinued` | Permanent end of life. Terminal state — cannot be reversed. All listings must End. |
| `Recalled` | Regulatory/safety recall. Emergency terminal state. All listings force-downed immediately. |

**Aliases / Rejected Terms:**
- ~~IsActive (boolean)~~ — rejected; a boolean collapses five meaningful business states into two.
  The nuance of `OutOfSeason` vs. `Discontinued` drives different downstream behaviors.
- ~~Archived~~ — rejected; implies the record is hidden/inaccessible. Products are never deleted from
  the catalog (historical integrity). Use `Discontinued` or `IsDeleted` (soft delete) instead.

**Code Usage:**
- Enum: `ProductStatus` with values `Active`, `ComingSoon`, `OutOfSeason`, `Discontinued`, `Recalled`
- Property: `status` (JSON), `Status` (C#)
- Events: `ProductActivated`, `ProductSetToComingSoon`, `ProductSetToOutOfSeason`,
  `ProductDiscontinued`, `ProductRecallInitiated`

**Example:** "Snowflake Holiday Cat Stocking" (`CAT-STKG-012`) is `OutOfSeason` from January through
September. Its Amazon listing is Paused during that period. In October, a catalog manager activates it
(`ProductActivated` event), the listing resumes (`Live`), and it sells through the holiday season.

---

#### Product Category

**Definition:** The structured taxonomy node that classifies a Product's type within CritterSupply's
internal catalog hierarchy. Categories form a tree: `Dogs > Food > Dry Food > Large Breed Adult`.
A Product is assigned to exactly one leaf-level category node. Product Category is CritterSupply's
internal classification — it is NOT the same as a Marketplace Category Mapping, which translates
our internal category to a specific marketplace's taxonomy.

**Bounded Context:** Product Catalog BC (owned). Marketplaces BC consumes category data to determine
Marketplace Category Mappings.

**Aliases / Rejected Terms:**
- ~~Tag~~ — rejected; tags are supplementary, non-hierarchical keywords. Category is the primary
  structural classification.
- ~~Department~~ — rejected; brick-and-mortar retail language.

**Code Usage:**
- Value object: `ProductCategory` (slug-based, e.g., `dogs.food.dry.large-breed-adult`)
- Property: `category` (leaf node), `categoryPath` (full path as array)
- Event: `ProductCategoryAssigned`
- UI label: "Category", breadcrumb display: `Dogs > Food > Dry Food > Large Breed Adult`

**Example:** "Frontline Plus Flea & Tick" (`FLEA-TX-003`) is assigned to
`Dogs > Health & Wellness > Flea & Tick > Topical Treatments`. This maps to Amazon's
`Pet Supplies > Dogs > Health Supplies > Flea & Tick Remedies > Topical`.

---

#### Category Taxonomy

**Definition:** The complete hierarchical tree of Product Categories used by CritterSupply for internal
catalog organization. The taxonomy defines all valid category paths and their parent-child relationships.
It is managed by the Merchandising team and evolves deliberately — adding a new node is a planned action,
not an ad-hoc edit.

**Bounded Context:** Product Catalog BC (owned). Consumed by Marketplaces BC for mapping construction.

**Aliases / Rejected Terms:**
- ~~Category Tree~~ — acceptable in informal technical discussion; "Taxonomy" is the canonical term.
- ~~Navigation~~ — rejected; navigation is a storefront/UX concept derived from taxonomy, not the
  taxonomy itself.

**Code Usage:**
- Class: `CategoryTaxonomy`, `CategoryTaxonomyNode`
- Event: `CategoryTaxonomyNodeAdded`, `CategoryTaxonomyNodeRenamed`
- API: `GET /api/catalog/taxonomy`

---

#### Brand

**Definition:** The manufacturer or brand name under which a product is sold or marketed. Brand is an
optional attribute on a Product. It appears in marketplace listings (Amazon requires brand for most
categories), in storefront filters, and in vendor association logic (vendor X supplies all products
under brand "PetSupreme").

**Bounded Context:** Product Catalog BC (owned).

**Aliases / Rejected Terms:**
- ~~Manufacturer~~ — a related but distinct concept. Manufacturer makes the product; Brand is the
  name under which it's sold. For private-label products, CritterSupply is the brand but not the
  manufacturer. Use "Brand" in the domain; capture manufacturer separately if needed for compliance.
- ~~Vendor~~ — rejected; Vendor is the supplier relationship, not the consumer-facing brand name.

**Code Usage:**
- Property: `brand` (JSON), `Brand` (C#, nullable string)
- Event field: included in `ProductAdded`, `ProductContentUpdated`
- UI label: "Brand", filter label: "Filter by brand"

**Example:** A 6-oz can of "Wellness CORE" wet cat food has `brand: "Wellness"`. CritterSupply
does not manufacture it; the brand owner (WellPet LLC) does. CritterSupply is the retailer/reseller.

---

#### Product Image

**Definition:** A digital image asset associated with a Product, including its CDN URL, display order,
and descriptive alt text. A Product typically has multiple images: a primary image, lifestyle photos,
and angle shots. Each marketplace may impose its own image requirements (Amazon requires a white
background for the primary; Walmart requires minimum 1000px). Images live in the Product record;
marketplace-specific rendering constraints are enforced at listing submission time.

**Bounded Context:** Product Catalog BC (owned).

**Code Usage:**
- Class: `ProductImage` with properties `url`, `altText`, `sortOrder`, `isPrimary`
- Event: `ProductImagesUpdated`
- API field: `images[]`

---

#### Product Dimensions

**Definition:** The physical measurements and weight of a Product as packaged for sale or shipment.
Includes length, width, height (in inches), and weight (in pounds). Dimensions are required for
shipping rate calculation and for triggering Fulfillment BC's hazmat routing rules. They may differ
between product variants (a 5-lb vs. 20-lb bag of dog food).

**Bounded Context:** Product Catalog BC (owned). Consumed by Fulfillment BC (routing), Listings BC
(marketplace shipping template selection).

**Code Usage:**
- Class: `ProductDimensions` with properties `lengthIn`, `widthIn`, `heightIn`, `weightLbs`
- Event: `ProductDimensionsSet`
- Nullable on `Product` — not all products require dimensions at creation time.

---

#### Tags / Product Tags

**Definition:** An unstructured, flat list of keyword strings associated with a Product for search
and discovery purposes. Tags supplement the structured Category hierarchy — they capture synonyms,
common misspellings, and cross-category discovery terms. Tags are NOT taxonomy nodes and do NOT
establish hierarchy.

**Bounded Context:** Product Catalog BC (owned). Consumed by Customer Experience BC (search index).

**Aliases / Rejected Terms:**
- ~~Keywords~~ — acceptable synonym in SEO contexts; "Tags" is the canonical term in the domain model.
- ~~Labels~~ — rejected; "Label" implies admin classification (like GitHub issue labels). Tags are
  customer-facing discovery aids.

**Code Usage:**
- Property: `tags` (JSON array of strings), `Tags` (C# `IReadOnlyList<string>`)
- Event: `ProductTagsUpdated`

**Example:** A flea collar (`FLEA-COLLAR-012`) might have tags:
`["flea prevention", "tick collar", "parasite control", "8-month protection", "waterproof"]`.
A customer searching "tick collar for dogs" finds it even though the product name says "flea collar."

---

### 1.2 Compliance and Regulatory Terms

---

#### Hazmat Classification

**Definition:** The regulatory classification of a Product as a hazardous material under DOT (Department
of Transportation) shipping rules. A Hazmat Classification indicates what class of hazardous material
the product contains (e.g., flammable liquids, oxidizers, toxic substances) and governs which shipping
methods are permitted. This is the authoritative source checked by Fulfillment BC's routing engine.

**Bounded Context:** Product Catalog BC (authoritative source). Consumed by Fulfillment BC (routing),
Listings BC (marketplace hazmat restrictions apply at submission).

**Aliases / Rejected Terms:**
- ~~Hazardous Flag~~ / ~~IsHazmat~~ — a boolean is insufficient. The DOT class (ORM-D, Class 3, Class 9,
  etc.) determines which carriers and shipping methods are valid. Capture the class, not just a flag.
- ~~Dangerous Goods~~ — IATA terminology for air freight. Use DOT/PHMSA terminology for ground shipping.
  Capture both if the product ships internationally or by air.

**Code Usage:**
- Class: `HazmatClassification` with `dotClass` (string enum), `unNumber` (optional), `packingGroup`
- Property: `hazmatClassification` (nullable — null means not hazmat)
- Event: `HazmatClassificationChanged`

**Example:** Frontline Plus Flea Spray (`FLEA-SPRAY-005`) contains a flammable propellant.
Its `hazmatClassification` is `{ dotClass: "ORM-D", description: "Consumer commodity" }`.
Fulfillment BC's routing engine blocks air shipping for this SKU and flags it for hazmat labeling
at the pack station.

---

#### Age Restriction

**Definition:** The minimum customer age required to purchase a Product, expressed as an integer
(years). An Age Restriction of `18` requires age verification at checkout. Some pet medications,
certain reptile supplies, and regulated pest control products carry age restrictions on specific
marketplaces. A null value means no restriction.

**Bounded Context:** Product Catalog BC (owned). Consumed by Listings BC (marketplace compliance
gate enforcement at listing submission).

**Code Usage:**
- Property: `minimumAge` (nullable integer), e.g., `minimumAge: 18`
- Event field: included in `ComplianceMetadataSet`

---

#### Country Restriction

**Definition:** A list of countries where a Product cannot legally be sold or shipped, due to
regulatory prohibition, ingredient bans, or import restrictions. Country Restrictions inform Listings
BC which marketplace channel/locale combinations are blocked for a given product, and inform
Fulfillment BC that certain international shipping destinations are prohibited.

**Bounded Context:** Product Catalog BC (owned). Consumed by Listings BC, Fulfillment BC.

**Code Usage:**
- Property: `restrictedCountries` (array of ISO 3166-1 alpha-2 codes, e.g., `["CA", "DE"]`)
- Event field: included in `ComplianceMetadataSet`

**Example:** A certain topical flea treatment (`FLEA-TX-007`) is prohibited for sale in California
(state-level restriction, modeled as `"US-CA"` using ISO 3166-2) due to a specific active ingredient.
Listings BC blocks submission to `OWN_WEBSITE` for California-based customers and prevents listing
on `AMAZON_US` without state-level geo-exclusion configured.

---

#### Compliance Metadata

**Definition:** The complete set of regulatory and safety attributes for a Product, including Hazmat
Classification, Age Restriction, Country Restrictions, Proposition 65 warning status, and any
certification claims (organic, non-GMO). Compliance Metadata is a sub-document on the Product record
and is set as a unit via the `SetComplianceMetadata` command. Listings BC uses it as the input to
its Listing Compliance Gate check at submission time.

**Bounded Context:** Product Catalog BC (owned and authoritative).

**Aliases / Rejected Terms:**
- ~~Regulatory Flags~~ — too vague; sounds like a collection of booleans. "Compliance Metadata" names
  the structured concept.
- ~~Safety Data~~ — confused with SDS (Safety Data Sheet), which is a separate document for hazmat
  handling instructions.

**Code Usage:**
- Class: `ComplianceMetadata` (embedded in `Product`)
- Command: `SetComplianceMetadata`
- Event: `ComplianceMetadataSet`
- Property: `complianceMetadata` (nullable — null means no compliance requirements identified yet)

---

### 1.3 Lifecycle Actions

---

#### Add a Product

**Definition:** The action of creating a new Product record in the catalog, establishing its SKU,
initial content, category, and status. This is the birth event for a product stream. The SKU is
assigned at this moment and is immutable thereafter.

**Events:** `ProductAdded`  
**Command:** `AddProduct`  
**UI label:** "Add product", button: "Add Product"

---

#### Update a Product

**Definition:** The action of changing one or more mutable fields of a Product (name, description,
images, tags, dimensions, brand, category). Under event sourcing, each field change produces a
granular domain event rather than a single coarse `ProductUpdated` event. The admin UI may present
a single "Save" action, but the domain records discrete field-level events.

**Events:** `ProductNameChanged`, `ProductDescriptionUpdated`, `ProductCategoryAssigned`,
`ProductImagesUpdated`, `ProductTagsUpdated`, `ProductDimensionsSet`  
**UI label:** "Edit product", "Save changes"

---

#### Discontinue a Product

**Definition:** The action of permanently ending a Product's commercial life. Discontinuation is a
one-way, irreversible transition to `Discontinued` status. It triggers immediate cascade: all active
Listings for this SKU must transition to `Ended`. Inventory BC stops accepting new reservations.
Pricing BC ceases price updates.

**Events:** `ProductDiscontinued`  
**Command:** `DiscontinueProduct`  
**UI label:** "Discontinue product", confirmation prompt: "This cannot be undone."

---

#### Restore a Product

**Definition:** The action of reversing a soft-delete on a Product. Restore is available only for
soft-deleted products (not for `Discontinued` products — discontinuation is irreversible). This is
an admin-only action, rare in practice, and requires explicit justification captured in the event.

**Events:** `ProductRestored`  
**Command:** `RestoreProduct`  
**UI label:** "Restore product"

---

#### Seasonal Deactivation

**Definition:** The action of setting a Product's status to `OutOfSeason` to pause its commercial
activity for a planned period. Unlike Discontinuation, Seasonal Deactivation is reversible — the
product will return. All active Listings for the product are Paused (not Ended) during the period.

**Events:** `ProductSetToOutOfSeason`  
**Command:** `SeasonallyDeactivateProduct`  
**UI label:** "Mark as out of season"

---

#### Recall

**Definition:** The action of initiating a regulatory or safety recall on a Product. A Recall is
a distinct, emergency-class lifecycle action — not a subtype of Discontinuation. It carries regulatory
metadata (recall reason, regulatory body, notification date, affected lot numbers) and triggers a
priority cascade: all Live Listings are immediately Force Downed, recent purchasers are flagged for
notification, and the product status transitions to `Recalled`.

**Events:** `ProductRecallInitiated`  
**Command:** `InitiateProductRecall`  
**UI label:** "Initiate recall", admin-only action, role: `ComplianceOfficer`  
**Integration:** Published to a **dedicated high-priority RabbitMQ exchange** — not the standard
catalog exchange. Listings BC consumes from this exchange on a priority consumer.

**Example:** A batch of PetSupreme Rawhide Chews (`RAWHIDE-024`) is found to be contaminated.
A ComplianceOfficer runs `InitiateProductRecall`. The event carries `{ recallReason: "Salmonella
contamination", regulatoryBody: "FDA", affectedLotNumbers: ["LOT-2025-11-A", "LOT-2025-11-B"] }`.
Within seconds, all Live Amazon, Walmart, and OwnWebsite Listings for `RAWHIDE-024` are Force Downed.

---

## 2. Listings Vocabulary

The Listings BC manages **CritterSupply's commercial presence on specific marketplace channels**.
It answers the question: "Is Product X listed for sale on Channel Y, and in what state?" Listings
does not own product content (that is Product Catalog's job) and does not own marketplace definitions
(that is Marketplaces BC's job). It owns the relationship between them — the Listing itself.

---

### 2.1 Core Entities

---

#### Listing

**Definition:** The representation of a single Product's selling presence on a single Marketplace
Channel. A Listing is the artifact submitted to a marketplace — it carries channel-specific content
(mapped attributes, channel-specific title, compliance attestations) and tracks its own state
through the listing lifecycle. One Product can have multiple Listings (one per channel). One Listing
belongs to exactly one Product and one Marketplace.

**Bounded Context:** Listings BC (owned and authoritative).

**Aliases / Rejected Terms:**
- ~~Channel Listing~~ — rejected as the canonical term (though acceptable as a clarifying adjective
  in cross-context technical documents). "Channel" is implicit — every Listing belongs to a channel
  by definition. "Listing" is what Amazon, Walmart, and eBay themselves call this concept. The term
  vendors already know and use.
- ~~Offer~~ — rejected; "Offer" in marketplace vocabulary refers to a price/availability record
  on an already-existing listing (e.g., Amazon's "New & Used" offers). We are creating the listing
  itself, not adding an offer to someone else's listing.
- ~~Channel Presence~~ — rejected; too abstract and passive. A Listing is an active, stateful record.
- ~~Product Listing~~ — acceptable when distinguishing from "order listing" in a general conversation,
  but within Listings BC, just say "Listing."

**Code Usage:**
- Class: `Listing` (aggregate root)
- Stream ID: UUID v5 from `"listing:{sku}:{channelCode}"` — one stream per product-channel pair
- Events: all Listing events (see §2.2–2.3)
- API: `GET /api/listings/{listingId}`, `GET /api/listings?sku=DOG-BOWL-001`
- UI heading: "Listings", "Manage Listings", "Amazon Listing"

**Example:** CritterSupply creates a Listing for `DOG-BOWL-001` on `AMAZON_US`. That Listing starts
as `Draft`, is enriched with Amazon-specific attributes (color, material, breed size), submitted for
review, and eventually goes `Live`. A separate Listing exists for the same product on `WALMART_US`.
The two are independent — the Walmart Listing may be `Paused` while the Amazon Listing is `Live`.

---

#### Listing State / Listing Status

**Definition:** The current lifecycle stage of a Listing. Listing State is distinct from Product
Status — a Product's `Active` status is a prerequisite for a Listing to be submitted, but the
Listing has its own independent state machine. See §2.2 for the definition of each state.

**Bounded Context:** Listings BC (owned).

**Aliases / Rejected Terms:**
- ~~Listing Status~~ — acceptable synonym; both terms appear in the codebase. Prefer "Listing State"
  in code (enum name: `ListingState`) and "status" in UI labels (e.g., "Status: Live").

**Code Usage:**
- Enum: `ListingState` with values `Draft`, `ReadyForReview`, `Submitted`, `Live`, `Paused`,
  `Ended`, `ForcedDown`
- Property: `state` (JSON), `State` (C# `ListingState`)
- UI label: "Status" (human-facing), filtered views: "Live listings", "Draft listings"

---

#### Marketplace Attributes

**Definition:** The channel-specific attribute values that a Listing carries in addition to the core
product content sourced from Product Catalog. Marketplace Attributes are the answers to the questions
a specific marketplace's Attribute Schema asks — e.g., Amazon's required `breed_recommendation` for
dog bowls, or Walmart's required `primary_material`. These attributes live on the Listing, not on
the Product — the same product may have different attribute values for different channels.

**Bounded Context:** Listings BC (owned). Sourced from both Product Catalog (raw product data) and
merchant input (channel-specific decisions).

**Aliases / Rejected Terms:**
- ~~Channel Attributes~~ — acceptable synonym; use "Marketplace Attributes" as the canonical term
  in the Listings BC, and "Channel Attributes" only when contrasting with product-level attributes
  in a cross-BC discussion.
- ~~Custom Attributes~~ — rejected; implies optional extras. Marketplace Attributes are often
  required by the marketplace, not optional customization.
- ~~Extended Attributes~~ — rejected; implies they extend a base set. They are channel-specific,
  not extensions.

**Code Usage:**
- Type: `Dictionary<string, string>` keyed by the marketplace's attribute name
- Property: `marketplaceAttributes` (JSON object)
- Populated during Listing draft creation; validated against `MarketplaceAttributeSchema` at
  `SubmitListing` time.

---

#### Listing Compliance Gate

**Definition:** A validation checkpoint that a Listing must pass before it can be submitted to a
marketplace. The Compliance Gate checks that the Product's Compliance Metadata satisfies the specific
marketplace's rules — e.g., a hazmat product cannot be listed on a marketplace that prohibits hazmat,
an age-restricted product cannot be listed without the marketplace's age-gate attribute set, a
country-restricted product cannot be listed on a marketplace serving those countries.

**Bounded Context:** Listings BC (owned). Reads from Product Catalog (Compliance Metadata) and
Marketplaces BC (channel rules).

**Code Usage:**
- Class: `ListingComplianceGate`
- Method: `Validate(listing, product.ComplianceMetadata, marketplace.Rules)`
- Returns: `ComplianceGateResult` with `IsPassed`, `Violations[]`
- Events: none (gate result is transient); submission is blocked if gate fails.

---

### 2.2 Listing States

Each state below is a valid value of `ListingState`.

---

#### Draft

**Definition:** A Listing that has been created but is not yet ready for marketplace submission.
The merchant is assembling marketplace-specific attributes, selecting images, writing a channel-
optimized title, and fulfilling the attribute requirements defined by the Marketplace Attribute
Schema. A Draft Listing exists only within CritterSupply's system — no submission to the marketplace
has occurred. A Listing enters `Draft` when it is created.

**Transitions to:** `ReadyForReview` (when all required attributes are filled and the compliance gate
passes internal checks).

---

#### Ready for Review

**Definition:** A Listing that has passed all internal completeness checks and is awaiting final
human review before submission. The Listing has satisfied the Marketplace Attribute Schema's required
fields and the Listing Compliance Gate's internal rules. It has NOT been submitted to the marketplace.
This state exists to support a review workflow — a second pair of eyes before committing to submission.

**Transitions to:** `Submitted` (reviewer approves and triggers submission), `Draft` (reviewer returns
for corrections).

**UI label:** "Ready for review", queue view: "Pending review"

---

#### Submitted

**Definition:** A Listing that has been sent to the marketplace for processing. CritterSupply has
transmitted the listing content to the marketplace's API (e.g., Amazon's SP-API, Walmart's Seller
Center API). The marketplace is processing the submission — validating attributes, checking category
restrictions, assigning identifiers. The merchant is waiting for confirmation. The listing is NOT yet
visible to consumers.

**Transitions to:** `Live` (marketplace confirms acceptance), `Draft` (marketplace rejects — merchant
must correct and resubmit).

**UI label:** "Submitted — awaiting marketplace confirmation"

---

#### Live

**Definition:** A Listing that is active and visible to consumers on the marketplace. The marketplace
has accepted and published the listing. The product is for sale. A Live Listing is the operational
state — CritterSupply is earning revenue from this channel for this product.

**Transitions to:** `Paused` (merchant-initiated temporary suspension), `Ended` (merchant-initiated
permanent removal or channel removal), `ForcedDown` (emergency recall cascade).

**UI label:** "Live" (status badge, green)

---

#### Paused

**Definition:** A Listing that has been temporarily suspended from the marketplace — the product
is no longer visible to consumers, but the listing record and marketplace data are preserved. Pausing
is used for planned periods of unavailability: a seasonal product going out of season, a stock-out
situation, or a content update requiring temporary takedown. A Paused Listing can be resumed.

**Transitions to:** `Live` (merchant resumes), `Ended` (merchant decides not to resume).

**UI label:** "Paused" (status badge, amber)  
**Distinguish from Ended:** A Paused Listing is parked with intent to return. An Ended Listing is
deliberately closed.

---

#### Ended

**Definition:** A Listing that has been permanently removed from marketplace availability. The product
is no longer for sale on this channel. Ending a Listing is not necessarily permanent for the *product*
— a new Listing can be created later if the product re-launches on that channel — but the specific
Listing record is closed and will not be reactivated. Every Ended Listing carries an `EndedReason`
(see §2.4).

**Transitions to:** (terminal state — no further transitions)

**UI label:** "Ended" (status badge, grey)  
**Action label:** "Delist" (the action that results in `Ended` state — see §2.3)

---

#### Forced Down

**Definition:** A Listing that has been taken down through an emergency cascade — specifically, as a
result of a Product Recall. A Forced Down Listing is distinct from Ended because it is the result of
a systemic, emergency action, not a merchant decision. It carries the reason `ForcedDownByRecall` and
the recall event reference. Forced Down listings require explicit acknowledgment before they can be
permanently Ended or (in the case of a false recall) reviewed for reinstatement.

**Transitions to:** `Ended` (after recall is confirmed and merchant acknowledges), `Draft` (if recall
is retracted — rare, admin only).

**UI label:** "Forced down — recall" (status badge, red)  
**Distinction from Ended:** An Ended listing was a deliberate merchant action. Forced Down is a
system-driven emergency response to a regulatory event.

---

### 2.3 Listing Actions

---

#### Create a Listing

**Definition:** The action of initiating a new Listing for a Product on a Marketplace Channel.
Creates the Listing aggregate in `Draft` state. Requires the Product to be `Active` (or `ComingSoon`
for pre-launch listings) and the Marketplace to be configured and active.

**Command:** `CreateListing`  
**Event:** `ListingCreated`  
**UI label:** "Create listing", "List on Amazon"

---

#### Submit a Listing

**Definition:** The action of transmitting a Listing to the marketplace for processing. Submission
triggers the Listing Compliance Gate — if the gate fails, submission is rejected and the Listing
stays in `ReadyForReview`. If the gate passes, the Listing transitions to `Submitted` and the
marketplace API call is made.

**Command:** `SubmitListing`  
**Event:** `ListingSubmitted`  
**UI label:** "Submit to marketplace", "Submit listing"  
**Rejected terms:**
- ~~Publish~~ — rejected; "publish" conflates the act of submitting to the marketplace with the
  marketplace's confirmation that the listing is live. These are two separate moments in time,
  and the gap between them can be hours. "Submit" names what CritterSupply does; "Go Live" names
  what the marketplace does.
- ~~Push~~ — rejected; informal technical jargon, not business vocabulary.
- ~~List~~ (as a verb) — rejected in this context; "list" is overloaded (we say "create a listing,"
  not "list the product"). The verb form causes confusion with the noun "Listing."

---

#### Go Live

**Definition:** The transition from `Submitted` to `Live` — the moment when the marketplace confirms
the Listing is published and visible to consumers. This is NOT a merchant action (the merchant cannot
directly trigger it); it is triggered by the marketplace's response to submission. In the UI, this is
the status the merchant is waiting for. In code, it is handled by the marketplace integration's webhook
or polling response handler.

**Event:** `ListingWentLive`  
**UI label:** "Go live" (as a status update in a timeline), "Your listing is now live on Amazon"  
**Rejected terms:**
- ~~Publish~~ — rejected; same reasons as above. "Publish" obscures who acts. The marketplace
  publishes; CritterSupply submits. The verb belongs to the marketplace, not to us.
- ~~Activate~~ — rejected; "Activate" is used for Product lifecycle (Product Status: `Active`).
  Reusing it for Listing state changes creates cross-BC terminology collision.

---

#### Pause a Listing

**Definition:** The merchant action of temporarily suspending a Live Listing. Sends a takedown
request to the marketplace and transitions the Listing to `Paused`. Used for planned inventory
gaps, seasonal products, or content updates requiring temporary removal.

**Command:** `PauseListing`  
**Event:** `ListingPaused`  
**UI label:** "Pause listing", confirmation: "This listing will be temporarily removed from the marketplace."

---

#### Delist

**Definition:** The merchant action of permanently ending a Listing's presence on a channel. The
canonical term for the action that transitions a Listing to `Ended` state. The merchant is removing
the product from the channel with no current intent to return. Sends a removal request to the
marketplace API.

**Command:** `DelistListing`  
**Event:** `ListingEnded` (with `endedReason: IntentionalEnd`)  
**UI label:** "Delist", "Remove from [channel]", confirmation dialog required  
**Rejected terms:**
- ~~End~~ — rejected as the action verb; "End" is the state result, not the action name. "Delist"
  names the intentional business act of removing from a channel — it is what sellers on every
  marketplace platform call this action.
- ~~Unpublish~~ — rejected; mirrors the "Publish" rejection above.
- ~~Remove~~ — acceptable in UI confirmation copy ("This will remove your listing from Amazon"),
  but "Delist" is the canonical action term.

---

#### Force Down

**Definition:** The system-initiated action of immediately taking down one or more Live Listings as
a result of a Product Recall. This is NOT a merchant action — it is triggered by the recall cascade
from Product Catalog BC. Force Down bypasses normal state transitions and moves a Listing directly
to `ForcedDown` regardless of its current state (even `Submitted` listings in flight should be
force-downed).

**Command:** `ForceDownListing` (system-issued, not merchant-initiated)  
**Event:** `ListingForcedDown`  
**UI label:** "Forced down — recall in progress" (read-only, not a button)

---

### 2.4 Ended Reasons

Every Listing that reaches `Ended` or `ForcedDown` state carries an `EndedReason`. This is
non-optional — understanding WHY a listing ended drives downstream reporting, vendor communications,
and catalog management decisions.

---

#### Intentional End

**Definition:** The Listing was ended by explicit merchant decision via the `Delist` action. The
merchant chose to remove the product from this channel.

**Code value:** `EndedReason.IntentionalEnd`  
**UI label:** "Removed by merchant"

---

#### Channel Removed

**Definition:** The marketplace took the Listing down unilaterally — due to a policy violation,
category restriction change, brand registry conflict, or marketplace-initiated removal. CritterSupply
did not initiate this; the marketplace did. Requires merchant attention (investigate and resolve,
or accept the removal).

**Code value:** `EndedReason.ChannelRemoved`  
**UI label:** "Removed by marketplace — action may be required"

---

#### Expired

**Definition:** The Listing ended due to a time-based rule. Certain marketplaces (notably eBay) have
listing durations after which a listing expires if not renewed. Or CritterSupply may configure a
planned end date for a promotional listing.

**Code value:** `EndedReason.Expired`  
**UI label:** "Expired"

---

#### Forced Down by Recall

**Definition:** The Listing was force-downed as part of a Product Recall cascade. Not a merchant
decision; a compliance-driven system action.

**Code value:** `EndedReason.ForcedDownByRecall`  
**UI label:** "Removed — product recall" (red badge)

---

## 3. Marketplaces Vocabulary

The Marketplaces BC manages **the definitions and rules of the sales channels CritterSupply sells
through**. It answers the questions: "What channels do we sell on?", "What attributes does each
channel require?", and "How does our internal category taxonomy map to this channel's categories?"
Marketplaces BC does not own individual product listings — that is Listings BC's job.

---

### 3.1 Core Entities

---

#### Marketplace

**Definition:** A configured sales channel through which CritterSupply sells products. A Marketplace
is a first-class domain entity with its own rules, Attribute Schema, category mappings, and lifecycle
status. CritterSupply's own website is a Marketplace in this model — it is a channel through which
products are sold, with its own rules (no hazmat restrictions from a carrier perspective, but own
content policies).

**Bounded Context:** Marketplaces BC (owned and authoritative).

**Aliases / Rejected Terms:**
- ~~Channel~~ (as a synonym for Marketplace) — "Channel" and "Marketplace" have a specific
  relationship in this system (see below). Do not use them interchangeably. See §4 for the
  distinction.
- ~~Platform~~ — rejected; "platform" is informal and broader than we need.
- ~~Retailer~~ — rejected; CritterSupply is the retailer. Amazon is the marketplace.

**Code Usage:**
- Class: `Marketplace`
- Document ID: `ChannelCode` (stable string identifier — see below)
- API: `GET /api/marketplaces`, `GET /api/marketplaces/AMAZON_US`
- UI heading: "Marketplaces", "Manage Marketplaces", "Amazon US"

**Example:** `AMAZON_US` is a Marketplace. It has an Attribute Schema (Amazon's required product
attributes for each category), Marketplace Category Mappings (CritterSupply's `Dogs > Food`
maps to Amazon's `Pet Supplies > Dogs > Food`), and rules (hazmat products require FBA hazmat
enrollment). `OWN_WEBSITE` is also a Marketplace — CritterSupply's storefront, with its own
content policies and no carrier-level hazmat restrictions.

---

#### Channel

**Definition:** The abstract concept of a sales pathway through which CritterSupply reaches
customers. A Channel may be a third-party marketplace (Amazon, Walmart), CritterSupply's own
digital storefront, or a future partner channel. In the Marketplaces BC, every configured
`Marketplace` entity IS a Channel. The term "Channel" is used as the broader conceptual umbrella;
"Marketplace" is the implemented, configured entity within the system.

**Bounded Context:** Used as conceptual vocabulary across all three BCs; implemented as `Marketplace`
entity in Marketplaces BC.

**Is our own website a Channel?** **Yes.** `OWN_WEBSITE` is a full Channel/Marketplace in this
model with its own Listings, its own Attribute Schema (minimal), and its own category mappings.
This enables consistent Listings lifecycle management regardless of whether the channel is
third-party or owned.

**Is a physical retail store a Channel?** **No** — not in the current model. A brick-and-mortar
location is an in-store retail context, not a digital marketplace channel. If CritterSupply expands
to wholesale/retail distribution, a dedicated BC would be required.

---

#### Channel Code

**Definition:** The stable, human-readable string identifier for a Marketplace. Channel Codes are
fixed at Marketplace creation and never change — they serve as the foreign key used in Listing
stream IDs, integration messages, and API responses. Channel Codes use `SCREAMING_SNAKE_CASE` by
convention to signal their immutability and identifier nature.

**Current valid values:** `AMAZON_US`, `EBAY_US`, `WALMART_US`, `OWN_WEBSITE`, `TARGET_US`

**Bounded Context:** Marketplaces BC (assigned). Used as a stable reference in Listings BC (stream
IDs), Product Catalog BC (integration messages), and all admin APIs.

**Aliases / Rejected Terms:**
- ~~Marketplace ID~~ — acceptable synonym in technical contexts; `ChannelCode` is the canonical name
  because it communicates the string (not UUID) nature of the identifier.
- ~~Channel Name~~ — rejected; names can change (we might rebrand how we display "Amazon US"). The
  Code is immutable; the display name is not.

**Code Usage:**
- Type: `ChannelCode` (value object wrapping `string`, validated against registered marketplaces)
- Listing stream: `"listing:{sku}:{channelCode}"` — e.g., `"listing:DOG-BOWL-001:AMAZON_US"`
- Property: `channelCode` (JSON), `ChannelCode` (C#)

---

#### Marketplace Attribute Schema

**Definition:** The complete set of attribute definitions that a Marketplace requires or optionally
accepts for a Listing in a given category. The Attribute Schema specifies required attributes,
their data types, valid values (where enumerated), and whether they are required or optional. It is
owned by the Marketplace and may differ by category — the schema for "Dog Food" on Amazon differs
from the schema for "Dog Toys." Listings BC uses the Attribute Schema to validate Listing
completeness at submission time.

**Bounded Context:** Marketplaces BC (owned). Consumed by Listings BC (validation).

**Aliases / Rejected Terms:**
- ~~Template~~ — rejected; "template" implies a document to fill in. The Attribute Schema is a
  validation contract, not a form template. The distinction matters because the schema may be
  programmatically applied and validated, not just filled in manually.
- ~~Attribute Set~~ — rejected; Magento/Adobe Commerce terminology. Carries baggage from a specific
  platform context.
- ~~Spec~~ — acceptable informally in technical discussion; "Marketplace Attribute Schema" is the
  canonical term.
- ~~Channel Schema~~ — acceptable synonym; "Marketplace Attribute Schema" is preferred for clarity.

**Code Usage:**
- Class: `MarketplaceAttributeSchema`
- Property on `Marketplace`: `attributeSchemas` (keyed by category node)
- Used by: `ListingComplianceGate.Validate()`
- Admin API: `GET /api/marketplaces/AMAZON_US/attribute-schema?category=dogs.food`

**Example:** Amazon's Attribute Schema for the category `Pet Supplies > Dogs > Feeding > Bowls`
requires: `material_type` (string, required), `breed_recommendation` (enum, required),
`item_volume` (decimal + unit, optional), `color` (string, required), `item_weight` (decimal + unit,
required). A Listing for `DOG-BOWL-001` on `AMAZON_US` must populate all required attributes
before it can be `Submitted`.

---

#### Marketplace Category Mapping

**Definition:** The translation between a CritterSupply internal Category Taxonomy node and the
equivalent category node in a specific Marketplace's category tree. Because every marketplace has
its own category structure, the same product may be classified differently on each channel.
Marketplace Category Mappings are maintained in the Marketplaces BC and are used by Listings BC
to auto-populate the marketplace category when creating a Draft Listing.

**Bounded Context:** Marketplaces BC (owned). Consumed by Listings BC.

**Code Usage:**
- Class: `MarketplaceCategoryMapping`
- Properties: `channelCode`, `internalCategoryPath`, `marketplaceCategoryId`, `marketplaceCategoryName`
- Admin API: `GET /api/marketplaces/AMAZON_US/category-mappings`

**Example:** CritterSupply internal category `dogs.health.flea-tick.topical` maps to:
- Amazon US: `Pet Supplies > Dogs > Health Supplies > Flea & Tick Remedies > Topical` (node ID: `2975348011`)
- Walmart US: `Pet Supplies > Dogs > Flea & Tick > Spot-On Treatments`
- eBay US: `Pet Supplies > Dog Supplies > Health & Beauty > Flea & Tick`

---

### 3.2 Lifecycle Actions

---

#### Configure a Marketplace

**Definition:** The action of onboarding a new Marketplace Channel into the system — registering
its ChannelCode, display name, integration credentials, Attribute Schema, and category mappings.
A newly configured Marketplace begins in an inactive state until explicitly activated. This is an
admin-only action.

**Command:** `ConfigureMarketplace`  
**Event:** `MarketplaceConfigured`  
**UI label:** "Configure marketplace", "Add marketplace"  
**Rejected terms:** ~~Onboard a Marketplace~~ — acceptable in team conversation, but "Configure"
is the precise domain term (onboarding implies an external party; here CritterSupply is configuring
its own system).

---

#### Suspend a Marketplace

**Definition:** The action of temporarily disabling a Marketplace Channel — preventing new Listing
submissions and pausing active polling/webhook processing. Existing Listings retain their state.
Used when a marketplace integration is broken, an API credential is expired, or a business decision
to temporarily halt selling on a channel is made.

**Command:** `SuspendMarketplace`  
**Event:** `MarketplaceSuspended`  
**UI label:** "Suspend marketplace"

---

#### Offboard a Marketplace

**Definition:** The action of permanently removing a Marketplace Channel from the system. All active
Listings on this channel must be Ended before offboarding can complete. This is an irreversible
action — unlike Suspend, Offboard signals the channel will not be used again. A terminated third-party
marketplace contract (e.g., if CritterSupply stops selling on Target) would trigger this action.

**Command:** `OffboardMarketplace`  
**Event:** `MarketplaceOffboarded`  
**UI label:** "Offboard marketplace", "Remove channel"  
**Rejected terms:** ~~Delete a Marketplace~~ — rejected; records are never destroyed. Offboard
captures the business finality without implying data deletion.

---

## 4. Cross-Cutting Terms

These terms are used across Product Catalog, Listings, and Marketplaces BCs and have consistent
definitions regardless of context.

---

#### Integration Event

**Definition:** A message published by one BC to notify other BCs of a significant domain occurrence.
Integration events are part of the public contract between BCs — they must be versioned and treated as
a stable API. In the three-BC model, key integration events include `ProductDiscontinued` (Catalog →
Listings), `ListingWentLive` (Listings → Customer Experience), and `MarketplaceOffboarded`
(Marketplaces → Listings).

---

#### Stream ID

**Definition:** The deterministic, unique identifier for an event-sourced aggregate stream. All three
BCs use UUID v5 derived from a natural key per ADR 0016. Prefixes:
- Product Catalog: `"catalog:{sku}"`
- Listings: `"listing:{sku}:{channelCode}"`
- Marketplaces: `"marketplace:{channelCode}"`

---

#### Channel Code (cross-cutting)

Used as the stable foreign key linking Listings to Marketplaces. Every BC that references a
channel uses the `ChannelCode` string — never a surrogate UUID. See §3.1 for full definition.

---

#### Compliance Gate

**Definition:** Any validation checkpoint that enforces regulatory or business rules before a state
transition is permitted. Both Listings BC (Listing Compliance Gate at submission) and Product Catalog
BC (recall eligibility checks) use compliance gates. A gate check is synchronous and must pass before
the triggering action proceeds.

---

## 5. Ambiguous Terms — Handle With Care

These terms appear naturally in conversation but mean different things depending on context. Always
qualify them with the bounded context when using them in technical discussion.

---

#### "Product"

- In **Product Catalog BC**: The master catalog record. `Product` class, aggregate root.
- In **Listings BC**: Often used informally to mean "the product this listing is for." Always
  clarify with SKU: "the product (`DOG-BOWL-001`) this listing is for." Never put a `Product`
  object in the Listings BC domain model — reference by SKU only.
- In **Customer Experience / Shopping**: A `ProductListingView` read model composited for the
  storefront. Not the same as the catalog `Product`.
- **Rule:** When in cross-BC discussion, always say "Product Catalog record" or reference by SKU
  to avoid ambiguity.

---

#### "Status"

- **Product Status**: The internal lifecycle state of a Product (`Active`, `Discontinued`, etc.)
- **Listing State / Listing Status**: The channel-specific state of a Listing (`Draft`, `Live`, etc.)
- **Marketplace Status**: Whether a Marketplace is configured, suspended, or offboarded.
- **Rule:** Always prefix with the entity: "Product Status", "Listing status", "Marketplace status."
  Never say just "status" in a cross-BC discussion.

---

#### "Category"

- In **Product Catalog BC**: A node in CritterSupply's internal Category Taxonomy.
- In **Marketplaces BC**: The marketplace's own category tree (Amazon's category, Walmart's category).
- **Rule:** Always say "internal category" (ours) vs. "marketplace category" (theirs). The Marketplace
  Category Mapping is the bridge between them.

---

#### "Publish"

- **Rejected as a domain action term** for Listings. "Publish" conflates two distinct moments:
  (1) CritterSupply submitting to the marketplace (`Submit`), and (2) the marketplace making the
  listing visible (`Go Live`). Using "Publish" erases a meaningful business distinction.
- Acceptable only in the narrow sense of "publish an integration event" (messaging infrastructure).
- **Rule:** Never use "Publish" as a synonym for `Submit` or `Go Live` in Listings contexts.

---

#### "Item"

- In **Cart / Shopping BC**: A `CartItem` — a line item in a customer's cart.
- In **Order BC**: An `OrderLineItem` — a committed line item on a placed order.
- In **Product Catalog BC**: Never say "item" — say "Product" or "SKU."
- **Rule:** "Item" is reserved for cart and order line items. Never use it to refer to the
  catalog master record.

---

#### "End" / "Ended"

- **`Ended`** (capital E): The canonical `ListingState` value indicating a permanently closed Listing.
- **"End" (verb)**: The informal action. Use **"Delist"** as the canonical action verb instead.
- **`EndedReason`**: The reason a Listing reached `Ended` state.
- **Rule:** In code, use `Ended` only as a state value. In commands, use `DelistListing`. In UI
  copy, use "Delist" or "Remove from [channel]."

---

*This glossary supersedes all prior informal term usage in code, documentation, and team communication.
When updating existing code or documentation that uses rejected terms, apply the canonical term on
contact. Questions or proposed amendments should be raised with the Product Owner before changes are
adopted.*

---

**Changelog:**
- 2026-03-10: Initial publication — PO + UX alignment touchpoint. Covers all required terms across
  Product Catalog, Listings, and Marketplaces BCs.

---

## Part B: UX Engineer Addendum

**Author:** UX Engineer
**Date:** 2026-03-10
**Scope:** Admin UI copy, error messages, interaction vocabulary, and domain-vs-UI divergence for
Product Catalog BC, Listings BC, and Marketplaces BC.

This addendum is normative for UI copy. When implementing admin screens, toasts, confirmation
dialogs, error messages, and status badges, use the exact copy specified here. Engineering must not
invent copy for listing state labels, CTA buttons, or error messages — pull from this document.
Deviations require UX sign-off.

---

### B.1 — UX Endorsements and Refinements

---

#### ✅ Endorsed: SKU

"SKU" is standard vocabulary in retail operations, pet specialty retail, and catalog management
tooling worldwide. Every catalog manager, buyer, and vendor-facing team member at a pet supply
company uses this term fluently. It appears in Amazon Seller Central, Walmart Seller Center, and
every PIM system our users will have touched before CritterSupply. No translation needed between
domain and UI. Use "SKU" verbatim in all labels, column headers, and error messages.

---

#### ✅ Endorsed: Submit + Go Live (as two distinct moments)

Separating "Submit" (our action) from "Go Live" (marketplace confirmation) is the right call and
reflects the mental model that experienced marketplace sellers already hold. Anyone who has listed on
Amazon knows there is a gap between clicking "Submit" and the listing appearing. The UX task is to
make both moments legible and to manage the waiting period between them. See §B.2 for the
`Submitted` state copy, which carries the primary burden of communicating that the merchant is now
waiting on an external party. No domain change needed — the UX executes on this split.

---

#### ⚠️ Refine: Product Family

The domain term "Product Family" is well-chosen and should be retained in system headings,
documentation, and navigation. However, the label alone is insufficient for discoverability in a
catalog context. A catalog manager looking at an Orijen Original Dog Food record in 2 lb, 6 lb,
12 lb, and 25 lb sizes will not naturally think "I need to find the Product Family." They will think
"I need to find the sizes" or "I need to see all the variants."

**Guidance:**
- Page heading: "Product Family — Orijen Original Dog Food" ✅
- Navigation / breadcrumb: "Products > Orijen Original Dog Food (4 variants)" ✅
- Empty-state helper text on a variant listing: "This product is part of a family. Manage all sizes
  from the Product Family page." ✅
- Avoid: "Family" as a standalone column header with no context — add "(variants)" parenthetically.

---

#### ⚠️ Refine: Ready for Review (state and CTA)

"Ready for Review" is precise as a domain state name and correct as a queue heading
("Pending Review queue"). However, it fails in two specific surfaces:

1. **As a state badge:** "Ready for review" is six words. Badges must be scannable at a glance.
   Use **"In Review"** as the badge label. It communicates "something is happening, a human is
   involved" without requiring the viewer to parse a sentence.

2. **As a button label:** The glossary does not specify a CTA for this transition. The action a
   catalog manager takes to *put* a listing into this state needs a label. "Mark as ready" is
   vague. Use **"Submit for review"** — it implies intent (you're handing it to someone) without
   using the overloaded word "submit" in the marketplace sense. To avoid collision with "Submit to
   marketplace," always display the destination: "Submit for review" vs. "Submit to Amazon."

---

#### ⚠️ Refine: Delist (action verb)

"Delist" is the correct canonical domain action and should be used as such in command names, event
payloads, audit logs, and developer documentation. It is familiar vocabulary in marketplace
operations and e-commerce platform management.

However, **"Delist" is a poor primary button label** for a catalog manager who may not have
marketplace operations fluency. "Delist this product" as a destructive-action button is opaque to
a junior catalog associate who thinks in terms of channels and products, not marketplace operations
terminology.

**Guidance:**
- Primary button label: **"Remove from [Channel]"** (e.g., "Remove from Amazon US")
- Confirmation dialog header: **"Remove from Amazon US?"**
- Audit log / event display: **"Delisted from Amazon US"** (use the domain term in historical records
  where operations-fluent staff are the audience)
- Developer tools / admin debug views: `DelistListing` command name is correct as-is.

The domain term "Delist" is not wrong — it just belongs one layer deeper than the primary CTA.

---

#### 🚩 Flag: Forced Down (state badge label)

"Forced down — recall" (the current UI label in the glossary) is problematic as a badge:

1. **Length:** Seven characters in the badge itself before the dash, plus the qualifier — too long
   for a compact status badge in a data table.
2. **Vocabulary:** "Forced down" is internal operations language. A catalog manager encountering
   this badge for the first time on a high-stress day (a recall is in progress) needs immediate
   clarity, not a technical action descriptor.
3. **Emotional register:** The word "forced" carries a harsh, accusatory tone that is not
   appropriate when the merchant is already dealing with an emergency.

**Proposed badge label: "Recall Hold"**

"Recall Hold" is short, immediately scannable, communicates urgency without alarm, and matches
the mental model ("something has placed a hold on this listing due to a recall"). It also
naturally suggests there is a next step (holds get lifted).

**This requires PO + domain model discussion** before adoption — "Recall Hold" as a UI label over
a `ForcedDown` domain state is a legitimate divergence that must be documented in §B.4. The domain
term `ForcedDown` / `ListingStatus.ForcedDown` is correct and should not change in code. Only the
display label changes.

**If "Recall Hold" is not adopted**, the minimum fix is to shorten the badge to **"Recall"** (red
badge) with the full explanation available in a tooltip or status detail panel.

---

#### 🚩 Flag: Marketplace Attribute Schema (as a UI label)

"Marketplace Attribute Schema" must never appear verbatim in the admin UI. It is a correct and
precise domain term for code, API routes, and developer documentation. It is not a term any catalog
manager would use or recognize. The UI label for this concept requires a PO decision before
implementation. See §B.4 and §B.6 for proposed alternatives and the open question.

---

### B.2 — UI Copy Guide: Listing States

All badge labels use sentence case. Badge colors are informational guidance only — final palette
is subject to design system token values. "Primary CTA" is the most prominent action available to
a catalog manager when a listing is in this state.

| State | Badge | Description (tooltip / detail panel) | Primary CTA | Blocked message (if action fails) |
|---|---|---|---|---|
| `Draft` | Draft | This listing hasn't been submitted yet. Complete all required fields before sending it to the marketplace. | Submit for review | "This listing can't be submitted — [Field name] is required. Add it before continuing." |
| `ReadyForReview` | In Review | All required fields are complete. This listing is waiting for a final check before it's sent to [Channel]. | Submit to [Channel] | "This listing can't be sent — the compliance check found an issue. Review the errors below." |
| `Submitted` | Submitted | This listing has been sent to [Channel] and is waiting for their confirmation. This usually takes a few minutes to several hours. | (none — awaiting external response) | "No action is available while [Channel] is processing this listing." |
| `Live` | Live | This listing is active on [Channel]. Shoppers can find and purchase this product. | Pause listing | "This listing can't be paused right now — try again in a moment." |
| `Paused` | Paused | This listing has been temporarily removed from [Channel]. It's not visible to shoppers, but your listing data is preserved. | Resume listing | "This listing couldn't be resumed — [Channel] returned an error. Contact support if this persists." |
| `Ended` | Ended | This listing is no longer active on [Channel]. You can create a new listing if you want to sell this product here again. | Create new listing | (no destructive action available — state is terminal) |
| `ForcedDown` | Recall Hold | This listing was automatically taken down because the product has been recalled. Acknowledge the recall notice before taking further action. | Acknowledge recall | "You can't resubmit this listing — the recall must be acknowledged first. Open the recall notice to continue." |

---

### B.3 — Error Message Templates

All error messages follow this structure: **What happened** (plain language) + **What to do next**
(specific, actionable). Never use "Something went wrong." Never blame the user. Never show
exception class names or HTTP status codes to catalog managers.

---

**Error 1 — Listing creation blocked: Product is not Active**

> **Can't create a listing for this product.**
> Frontline Plus Flea & Tick (FLEA-TX-003) has been discontinued and can't be listed on any channel.
> If this is a mistake, ask a catalog admin to review the product status.

*Variant — product is Recalled:*
> **Listing blocked — product recall in effect.**
> PetSupreme Rawhide Chews (RAWHIDE-024) is under an active recall. New listings can't be created
> until the recall is resolved.

---

**Error 2 — Submission blocked: compliance gate failed (missing field)**

> **This listing isn't ready to send to Amazon US.**
> The following required information is missing:
> - Breed recommendation
> - Item weight
>
> Add the missing details and try again.

*Note: Always list specific fields by their UI label, never by their `camelCase` attribute key.
"breedRecommendation" is an attribute key. "Breed recommendation" is a field label.*

---

**Error 3 — Listing paused: marketplace rejected submission**

> **Amazon US didn't accept this listing.**
> Your submission for Orijen Original Dog Food — 6 lb (DOG-ORIJEN-6LB) was reviewed by Amazon and
> returned with errors. Review the details below, correct the listing, and submit again.

*Always show the marketplace's rejection reason(s) below this banner if the API provides them.
Do not hide marketplace-returned error detail — it is the only actionable signal the catalog
manager has.*

---

**Error 4 — Listing forced down: product recall**

> **This listing has been taken down — recall in progress.**
> PetSupreme Rawhide Chews (RAWHIDE-024) on Amazon US was automatically removed because a product
> recall has been initiated. Open the recall notice to see details and acknowledge.

*This message appears as a persistent banner on the listing detail page, not only as a toast.
It must not be dismissible until the recall is acknowledged.*

---

**Error 5 — Resubmission blocked: forced-down listing not yet acknowledged**

> **Acknowledge the recall notice before resubmitting.**
> This listing was taken down due to a product recall. You must acknowledge the recall for
> PetSupreme Rawhide Chews (RAWHIDE-024) before this listing can be updated or resubmitted.
> [View recall notice →]

---

### B.4 — Terminology Divergence: Domain vs. UI

These terms are correct in code and events but require translation before appearing in any
user-facing surface. Engineering must pull display labels from this table — not from the enum name
or class name directly.

| Domain term | Use in code as | Use in UI as | Reason |
|---|---|---|---|
| `ListingStatus.ForcedDown` | `ListingStatus.ForcedDown` (enum) | **"Recall Hold"** (badge); **"Recall hold"** (sentence) | "Forced down" is internal operations language. "Recall Hold" is immediately legible to a catalog manager and implies a resolvable hold rather than a terminal state. |
| `ListingStatus.ReadyForReview` | `ListingStatus.ReadyForReview` (enum) | **"In Review"** (badge); **"Pending review"** (queue heading) | Six words cannot fit in a badge. "In Review" is scannable and conveys the same meaning. |
| `ChannelCode` / `AMAZON_US` | `ChannelCode.AMAZON_US` (constant) | **"Amazon US"** (display name from Marketplace record) | Channel codes are stable internal identifiers. UI must resolve them to human-readable Marketplace names. Never display raw channel codes to catalog managers. |
| `DelistListing` command | `DelistListing` (command class) | **"Remove from [Channel]"** (primary CTA button) | "Delist" is operations vocabulary. "Remove from Amazon US" is immediately clear to any catalog manager. Use "Delist" only in audit logs, event history, and developer-facing surfaces. |
| `MarketplaceAttributeSchema` | `MarketplaceAttributeSchema` (class) | **"[Channel] listing requirements"** (e.g., "Amazon US listing requirements") | "Schema" is engineering vocabulary. Catalog managers understand "requirements" — it matches the mental model of "Amazon needs these fields before they'll accept my listing." |
| `ProductFamily` | `ProductFamily` (class, event) | **"Product Family"** (page heading); **"[Product name] — [N] variants"** (navigation / table cell) | The domain term is correct but insufficient as a standalone table column value. Always supplement with variant count or differentiating attributes in list contexts. |
| `EndedReason.ChannelRemoved` | `EndedReason.ChannelRemoved` (enum) | **"Removed by [Channel]"** (display in listing history) | The marketplace took an action, not the merchant. Passive framing ("Removed by Amazon US") correctly attributes the action. |
| `EndedReason.ForcedDownByRecall` | `EndedReason.ForcedDownByRecall` (enum) | **"Product recall"** (display in ended reason column) | Sufficient and accurate at a glance. Full detail available in the recall notice link. |
| `ProductStatus.OutOfSeason` | `ProductStatus.OutOfSeason` (enum) | **"Out of season"** (badge / filter label) | Direct enum-to-display mapping is acceptable here; the phrase is natural retail language. Ensure the badge is amber (same visual weight as `Paused`) to signal temporary, not terminal. |
| `ProductStatus.ComingSoon` | `ProductStatus.ComingSoon` (enum) | **"Coming soon"** (badge) | Acceptable direct mapping. Use in product table and PDP preview contexts. |

---

### B.5 — Interaction Vocabulary

---

#### Confirmation Dialogs

Destructive or irreversible actions require a confirmation dialog. The button that confirms the
action must use a verb phrase — never "OK" or "Yes." The cancel button always reads "Cancel."

**Discontinue a product**

> **Header:** Discontinue [Product Name]?
>
> **Body:** Discontinuing this product is permanent and cannot be undone. All active listings for
> [Product Name] across all channels will be ended. Stock in Inventory will no longer be
> reservable. This action will be recorded in the audit log.
>
> Type the SKU to confirm: `___________`
>
> **Confirm button:** Discontinue product
> **Cancel button:** Cancel

*Note: Requiring the user to type the SKU before discontinuing is a pattern borrowed from
infrastructure deletion flows (e.g., deleting a Heroku app). It is appropriate here given
irreversibility and cascade scope. PO should confirm whether this friction level is correct for
catalog managers vs. a simpler "I understand" checkbox.*

---

**Remove a listing from a channel (Delist)**

> **Header:** Remove from [Channel]?
>
> **Body:** [Product Name] will no longer be for sale on [Channel]. Your listing data will be
> saved, and you can create a new listing later if needed. This will send a removal request to
> [Channel] immediately.
>
> **Confirm button:** Remove from [Channel]
> **Cancel button:** Keep listing

---

**Pause a listing**

> **Header:** Pause this listing?
>
> **Body:** [Product Name] will be temporarily removed from [Channel]. Shoppers won't be able to
> find or purchase it until you resume the listing.
>
> **Confirm button:** Pause listing
> **Cancel button:** Cancel

*Pausing does not require SKU confirmation — it is reversible.*

---

#### Empty States

Empty states must explain why the list is empty and offer a clear next step. Never show a bare
"No results" message without context.

**Listings list — no listings created yet**

> **Heading:** No listings yet
> **Body:** List your products on Amazon, Walmart, and your other sales channels to start selling.
> Choose a product from your catalog and select a channel to get started.
> **CTA button:** Create your first listing

**Products list — catalog is empty**

> **Heading:** Your catalog is empty
> **Body:** Add your first product to start building your catalog. Once a product is active, you
> can create listings for it on your sales channels.
> **CTA button:** Add a product

**Listings list — filter returns no results**

> **Heading:** No listings match this filter
> **Body:** Try adjusting the status filter or search term. If you're looking for ended listings,
> check the "Ended" filter — they're kept for your records.
> **CTA link:** Clear filters

---

#### Toast Notifications

Toasts surface for asynchronous system events — things that happened while the catalog manager was
on the page but weren't triggered by a direct button click. Maximum 120 characters across title
and description combined. Toasts auto-dismiss after 6 seconds except for error and recall events,
which persist until dismissed.

| Event | Toast title | Toast description | Auto-dismiss? |
|---|---|---|---|
| `ListingWentLive` | Now live on [Channel] | [Product Name] is visible to shoppers on [Channel]. | Yes |
| `ListingForcedDown` | Listing taken down — recall | [Product Name] on [Channel] was removed due to a product recall. | **No** |
| `ListingRejected` by marketplace | [Channel] rejected this listing | [Product Name] wasn't accepted. Review the errors and resubmit. | **No** |
| `ListingPaused` (system-initiated) | Listing paused | [Product Name] on [Channel] has been temporarily paused. | Yes |
| `ListingEnded` (ChannelRemoved) | Removed by [Channel] | [Channel] ended your listing for [Product Name]. | **No** |

---

#### Status Filter Labels

These are the display labels for the Listing Status filter dropdown in the Listings admin table.
The filter must always include an "All statuses" default and the `ForcedDown` state must use the
UI label, not the enum name.

| Filter option display label | Filters for `ListingStatus` |
|---|---|
| All statuses | (no filter) |
| Draft | `Draft` |
| In Review | `ReadyForReview` |
| Submitted | `Submitted` |
| Live | `Live` |
| Paused | `Paused` |
| Ended | `Ended` |
| Recall Hold | `ForcedDown` |

---

### B.6 — Glossary Gaps: Decisions Needed Before UI Design

The following questions were left open by the PO's glossary. Each one will block UI design or
implementation if not resolved. Bring these to the next PO + UX alignment session.

---

**Gap 1 — What is the UI name for "Marketplace Attribute Schema"?**

The domain term is established. The UI label is not. The glossary notes the domain term should not
appear verbatim in the UI but does not ratify an alternative.

*Proposed:* "[Channel] listing requirements" (e.g., "Amazon US listing requirements").
*Needs decision from:* PO + UX. Block: Marketplaces admin screen, Listing edit form field labels.

---

**Gap 2 — Can a catalog manager cancel a Submitted listing before it goes Live?**

The state machine shows `Submitted → Live` and `Submitted → Draft` (on marketplace rejection).
There is no `CancelSubmission` command defined. If a catalog manager spots an error after
submission, what can they do? Is there a "Cancel pending submission" action? If yes, it needs a
command, an event, and a UI affordance. If no, the UI must clearly communicate that no action is
available in `Submitted` state and why.

*Needs decision from:* PO + Engineering. Block: `Submitted` state CTA (currently "(none)" in §B.2).

---

**Gap 3 — Who or what triggers the ReadyForReview transition?**

The glossary defines `ReadyForReview` as a listing that has "passed all internal completeness
checks." It does not specify whether this transition is:

a) **Automatic** — the system evaluates completeness continuously and moves the listing to
   `ReadyForReview` when all required fields are populated, or
b) **Manual** — the catalog manager clicks "Submit for review" when they believe the listing is
   complete, and the system validates on that trigger.

The answer determines whether the primary CTA in `Draft` state is **"Submit for review"**
(manual) or whether the Draft state shows a **completeness checklist** and the CTA appears
automatically when all fields are green (automatic). These are very different UX patterns.

*Needs decision from:* PO + Engineering. Block: Draft state UI design, listing edit form design.

---

**Gap 4 — What is the end-to-end copy for the recall acknowledgment flow?**

The glossary defines `ForcedDown`, the `ForceDownListing` command, and `ForcedDownByRecall` as the
ended reason. It does not define the acknowledgment step — the thing that must happen before a
forced-down listing can proceed to `Ended` or be reviewed for reinstatement.

The UI needs: a recall detail page (or panel), the copy for the recall notice itself, and
confirmation copy for the acknowledgment action. The `InitiateProductRecall` command carries
regulatory metadata — the UI must surface `recallReason`, `regulatoryBody`, and `affectedLotNumbers`
in plain language to the catalog manager acknowledging the notice.

*Needs decision from:* PO + Compliance. Block: Recall acknowledgment screen, forced-down listing
detail page.

---

**Gap 5 — What does the Listing detail page call the Marketplace Attribute fields section?**

When a catalog manager is editing a Draft listing, they will encounter a section of fields drawn
from the `MarketplaceAttributeSchema` for that channel and category. This section needs a heading.
"Marketplace Attributes" is technical. "Amazon Requirements" is opinionated (it implies CritterSupply
didn't choose these requirements, which is true but may read as blame-shifting). "Listing Details"
is too generic and collides with the general listing form.

*Candidates:* "Channel requirements," "[Channel] required fields," "Marketplace details."
*Needs decision from:* PO + UX. Block: Listing edit form section heading, inline help text.

---

*This addendum will be updated as the above decisions are resolved. Amendments follow the same
sign-off process as the main glossary — PO + UX sign-off required before any normative copy
change takes effect.*

---

**Changelog:**
- 2026-03-10: UX Addendum v1 published — covers all six sections for Product Catalog, Listings,
  and Marketplaces BCs. Five open gaps identified for PO + UX resolution.
