# Marketplace API Discovery: Amazon, Walmart, eBay

**Date:** 2026-03-30  
**Authors:** @PO (discovery lead), @PSA (technical analysis), @UXE (UX implications)  
**Status:** Discovery complete  
**Purpose:** Inform stub adapter interface design and Phase 3 readiness

## Executive Summary

- **All three platforms are asynchronous in practice, even when submission is synchronous on paper.** Amazon can return `ACCEPTED` while processing continues in the background, Walmart returns a `feedId` that must be tracked, and eBay requires an explicit `publishOffer` step after staging inventory and offer data. The current `SubmitListingAsync` shape needs a correlated status model, not just a boolean success/failure.
- **Category and attribute rigidity is much higher than Shopify.** Amazon requires marketplace/product-type JSON schemas from the Product Type Definitions API, Walmart requires taxonomy + spec lookups per product type, and eBay requires category IDs plus business policy IDs before an offer can go live. A generic listing payload without channel-specific extensions will be too weak for real adapters.
- **Pet-supply compliance is a first-class requirement.** Amazon surfaces hazmat, safety data sheet, California Proposition 65, and pesticide-marking fields in product schemas; Walmart explicitly flags hazardous and pesticide items and can place WFS items on compliance hold; eBay is more policy-driven, but live animals, pesticides, and hazardous materials remain restricted. CritterSupply cannot treat compliance as an afterthought.
- **Walmart is gated most by business onboarding; Amazon is gated most by schema and async complexity; eBay is gated most by account setup and policy configuration.** That makes eBay the easiest technical model to understand, but Amazon remains the best stress-test for CritterSupply's adapter abstraction.
- **The current stub contract is directionally right but incomplete.** `SubmitListingAsync` and `DeactivateListingAsync` are still valid core verbs, but real adapters will also need a submission correlation ID, a status-check path, and a channel-specific extension payload.

## Amazon (AMAZON_US)

### Product Listing Data Shape

Amazon's current path is the [Selling Partner API (SP-API)](https://developer-docs.amazon.com/sp-api), not legacy MWS. For listings, Amazon's own guides point to a workflow built from the [Catalog Items API](https://developer-docs.amazon.com/sp-api/reference/catalog-items-v2022-04-01), [Product Type Definitions API](https://developer-docs.amazon.com/sp-api/reference/product-type-definitions-v2020-09-01), [Listings Items API](https://developer-docs.amazon.com/sp-api/reference/listings-items-v2021-08-01), and optionally the [Feeds API](https://developer-docs.amazon.com/sp-api/reference/feeds-v2021-06-30). ([Manage Product Listings](https://developer-docs.amazon.com/sp-api/docs/manage-product-listings-guide), [Building listings workflows](https://developer-docs.amazon.com/sp-api/docs/building-listings-management-workflows-guide))

The key implication is that Amazon does **not** have a loose, Shopify-style `product_type` string. Instead, CritterSupply must first discover the Amazon product type and then fetch the JSON schema for that product type in the target marketplace. Amazon's own example schema groups listing attributes into sections such as `offer`, `images`, `shipping`, `variations`, `safety_and_compliance`, `product_identity`, and `product_details`. ([Manage Product Listings](https://developer-docs.amazon.com/sp-api/docs/manage-product-listings-guide))

Important structural observations for CritterSupply:

- Amazon listing submissions are **SKU-centric**, but category and validation are **product-type-centric**.
- Parent/child variation structure is explicit through attributes such as `parentage_level`, `child_parent_sku_relationship`, and `variation_theme`. ([Manage Product Listings](https://developer-docs.amazon.com/sp-api/docs/manage-product-listings-guide))
- Amazon supports both single-listing submission through [`putListingsItem`](https://developer-docs.amazon.com/sp-api/reference/putlistingsitem) and bulk submission via `JSON_LISTINGS_FEED`. ([Manage Product Listings](https://developer-docs.amazon.com/sp-api/docs/manage-product-listings-guide))
- Amazon distinguishes between listing with full product facts plus sales terms (`requirements=LISTING`) and listing with product facts only (`requirements=LISTING_PRODUCT_ONLY`). ([Building listings workflows](https://developer-docs.amazon.com/sp-api/docs/building-listings-management-workflows-guide))

For CritterSupply's product model, Amazon is the clearest case that `ListingSubmission` cannot be just title/description/price plus a category code. It needs a way to carry marketplace-specific attribute payloads keyed to the selected product type.

### Submission → Activation Flow

Amazon's own listing workflow is explicitly multi-step:

1. Search catalog to see whether the item already exists.
2. Search product types.
3. Fetch the product-type JSON schema.
4. Validate the payload against the schema.
5. Submit through `putListingsItem`.
6. Inspect the immediate response.
7. Wait for asynchronous status and issue notifications. ([Building listings workflows](https://developer-docs.amazon.com/sp-api/docs/building-listings-management-workflows-guide))

The critical behavior for CritterSupply is that `putListingsItem` can return a submission status of `ACCEPTED` without meaning that the listing is live. Amazon's guide explicitly says that once a submission is accepted, the data is still being processed by Amazon and downstream issues may surface later through [`LISTINGS_ITEM_STATUS_CHANGE`](https://developer-docs.amazon.com/sp-api/docs/notifications-api-v1-use-case-guide#listings_item_status_change) and [`LISTINGS_ITEM_ISSUES_CHANGE`](https://developer-docs.amazon.com/sp-api/docs/notifications-api-v1-use-case-guide#listings_item_issues_change) notifications. ([Building listings workflows](https://developer-docs.amazon.com/sp-api/docs/building-listings-management-workflows-guide), [Notifications API use case guide](https://developer-docs.amazon.com/sp-api/docs/notifications-api-v1-use-case-guide))

Amazon recommends notifications instead of pure polling, but also explicitly recommends a backup retrieval mechanism in case notification delivery is delayed or unavailable. ([Notifications API use case guide](https://developer-docs.amazon.com/sp-api/docs/notifications-api-v1-use-case-guide))

This maps cleanly to CritterSupply's `ListingApproved` → `MarketplaceListingActivated` flow only if the adapter can express:

- accepted but still pending,
- accepted but later rejected with issues,
- and activated/live as a later confirmation.

### Pet Supply Constraints

Amazon's public product-type example already shows the kind of compliance surface area CritterSupply should expect for regulated pet products. The `safety_and_compliance` section includes fields such as `hazmat`, `safety_data_sheet_url`, `supplier_declared_dg_hz_regulation`, `california_proposition_65`, and `pesticide_marking`. ([Manage Product Listings](https://developer-docs.amazon.com/sp-api/docs/manage-product-listings-guide))

Additional implications:

- Amazon provides a [Listings Restrictions API](https://developer-docs.amazon.com/sp-api/reference/listings-restrictions-v2021-08-01) that can identify whether an ASIN or product type is restricted and can return approval next steps. ([Listings Restrictions use case guide](https://developer-docs.amazon.com/sp-api/docs/listings-restrictions-api-v2021-08-01-use-case-guide))
- For CritterSupply pet supply categories, likely high-risk areas are flea/tick treatments, pest-control products, aerosols, supplements, and anything that can trigger hazmat or pesticide classification.
- Amazon's more detailed restricted-products and dangerous-goods guidance appears to live behind Seller Central access, so the exact requirements for specific pet product types remain partially opaque from public docs.

**Discovery conclusion:** Amazon's public docs are sufficient to prove that pet-supply compliance fields are real and category-dependent, but not sufficient to finalize field requirements for real CritterSupply SKUs without seller-account verification.

### Authentication Model

Amazon SP-API uses OAuth through Login with Amazon (LWA). In the public appstore workflow, the seller authorizes the app, Amazon returns an authorization code, and the integrator exchanges that code for a refresh token. At runtime, the refresh token is exchanged for an access token used to call SP-API. ([Selling Partner Appstore authorization workflow](https://developer-docs.amazon.com/sp-api/docs/selling-partner-appstore-authorization-workflow))

For CritterSupply this means:

- Vault must store at least a long-lived refresh token plus app credentials.
- Access tokens are runtime artifacts, not static configuration.
- Seller authorization and role assignment are part of adapter onboarding, not just deployment.

### Rate Limit Posture

Amazon publishes operation-specific rate limits and also a shared token-bucket model. ([Usage plans and rate limits](https://developer-docs.amazon.com/sp-api/docs/usage-plans-and-rate-limits))

Relevant public defaults:

- Listings Items API operations such as `getListingsItem`, `putListingsItem`, and `searchListingsItems` default to **5 requests/second per account-application pair** with burst values of 5. ([Listings Items API rate limits](https://developer-docs.amazon.com/sp-api/docs/listings-items-api-rate-limits))
- Product Type Definitions API defaults to **5 requests/second per account-application pair** with burst 5. ([Product Type Definitions API rate limits](https://developer-docs.amazon.com/sp-api/docs/product-type-definitions-api-rate-limits))
- Amazon explicitly recommends batching and notifications to reduce throttling pressure. ([Usage plans and rate limits](https://developer-docs.amazon.com/sp-api/docs/usage-plans-and-rate-limits))

This is enough to say that a naive per-SKU synchronous loop will not scale well for large catalog pushes. CritterSupply's eventual Amazon adapter should leave room for feeds/bulk workflows.

### Implications for IMarketplaceAdapter

Amazon is the strongest evidence that the current two-method contract is incomplete.

What the real adapter needs beyond today's shape:

- `SubmitListingAsync(...)` should return a **correlation identifier** (submission ID, SKU correlation, or similar), not just success/failure.
- The adapter contract likely needs `CheckSubmissionStatusAsync(...)` or an equivalent status-check path, because synchronous acceptance is not activation.
- `ListingSubmission` needs a **channel-specific extension payload** for Amazon product-type attributes and compliance fields.
- Bulk submission is not required to change the interface today, but the Phase 3 implementation should avoid painting itself into a single-SKU-only corner.

## Walmart (WALMART_US)

### Product Listing Data Shape

Walmart's listing model is built around a stricter taxonomy than Shopify but a simpler public workflow than Amazon. Its taxonomy is explicitly **Category → Product Type Group → Product Type**. Sellers are expected to use the [Taxonomy API](https://developer.walmart.com/us-marketplace/docs/understanding-the-requirements-for-listing-an-item) to identify the correct product type and the Get Spec API to retrieve the required attributes for that type. ([Understanding the requirements for listing an item](https://developer.walmart.com/us-marketplace/docs/understanding-the-requirements-for-listing-an-item), [Item Management overview](https://developer.walmart.com/us-marketplace/docs/item-management-api-overview-1))

Walmart also has two distinct listing paths:

- **Offer Setup by Match** when the item already exists in the Walmart catalog.
- **Full Item Setup** when the item does not yet exist and the seller must provide the full attribute set. ([Create items on Walmart.com](https://developer.walmart.com/us-marketplace/docs/create-items-on-walmartcom), [Item Management overview](https://developer.walmart.com/us-marketplace/docs/item-management-api-overview-1))

This matters for CritterSupply because a single `ListingSubmission` concept may still work, but the Walmart adapter must decide whether it is creating a new item or matching an existing one.

Other important data-shape observations:

- Walmart item specs are versioned and change over time. The developer portal's "What's New" feed shows frequent item spec version updates. ([What's New](https://developer.walmart.com/us-marketplace/page/whats-new))
- Item setup includes detailed product content, pricing, inventory, and category-specific attributes. ([Item Management overview](https://developer.walmart.com/us-marketplace/docs/item-management-api-overview-1))
- Walmart is less variation-centric in public docs than Amazon/eBay, but product-type specs still drive which attributes must be present.

### Submission → Activation Flow

Walmart's public item workflow is feed-oriented and asynchronous.

- Item setup or maintenance submissions return a **feed ID**.
- Sellers then use feed status and item status APIs to determine whether the feed processed successfully and whether the item ingested successfully. ([Monitor my item](https://developer.walmart.com/us-marketplace/docs/monitor-my-item))
- The feed status uses values such as `PROCESSED` and `ERROR`, and item ingestion status also surfaces whether the item itself was submitted successfully. ([Monitor my item](https://developer.walmart.com/us-marketplace/docs/monitor-my-item))
- The [Notifications overview](https://developer.walmart.com/us-marketplace/docs/notifications-overview) confirms that Walmart supports push notifications/webhooks for events such as item unpublished, order created, and inventory out of stock.

So Walmart gives CritterSupply two practical status mechanisms:

1. polling via feed/item status APIs, and
2. event-driven notifications.

That is a better fit for CritterSupply's eventual activation flow than a pure synchronous model, but it still means `SubmitListingAsync` is only the start of the workflow.

Walmart also adds a second status axis for WFS compliance. When creating or converting WFS items, Walmart can place items on hold for compliance review, with states such as **In Review**, **Action Needed**, and **Prohibited**. ([Monitor my item](https://developer.walmart.com/us-marketplace/docs/monitor-my-item), [WFS hazmat items on hold](https://developer.walmart.com/us-marketplace/docs/wfs-hazmat-items-on-hold))

### Pet Supply Constraints

Walmart's public policies are explicit that sellers must comply with federal, state, and local regulations, and that Walmart may prohibit products it deems unsafe or hazardous. ([Hazardous and regulated products](https://marketplacelearn.walmart.com/guides/prohibited-products-policy-hazardous-items?locale=en-US))

Important findings for pet supply:

- Pesticides and pesticide devices must comply with FIFRA, including registration and labeling requirements; non-compliant pesticide products are prohibited. This matters directly to flea/tick and pest-control categories. ([Hazardous and regulated products](https://marketplacelearn.walmart.com/guides/prohibited-products-policy-hazardous-items?locale=en-US))
- Animal-related restrictions exist for products derived from prohibited species or violating wildlife laws. ([Animals policy](https://marketplacelearn.walmart.com/guides/Prohibited-Products-Policy:-Animals))
- For WFS, some products that may be allowed in the general marketplace can still be restricted or prohibited in WFS. WFS policy explicitly calls out pesticides and fully regulated hazardous items as restricted categories. ([WFS prohibited products policy](https://marketplacelearn.walmart.com/guides/wfs-prohibited-products-policy))
- Walmart's hazmat guidance shows that SDS documents or label images may be required and that inaccurate compliance declarations can keep items on hold or mark them prohibited. ([WFS hazmat items on hold](https://developer.walmart.com/us-marketplace/docs/wfs-hazmat-items-on-hold))

**Discovery conclusion:** Walmart pet-supply listing feasibility is not just about whether the item can be sold; it is also about whether the item can be sold through the specific fulfillment path CritterSupply wants to use.

### Authentication Model

Walmart Marketplace APIs use OAuth 2.0. The public docs say sellers must generate a Client ID and Client Secret in the Walmart Developer Portal, obtain an access token before calling APIs, and handle expiration/refresh behavior. ([Introduction to Marketplace APIs](https://developer.walmart.com/us-marketplace/docs/introduction-to-marketplace-apis), [Token API](https://developer.walmart.com/us-marketplace/reference/tokenapi))

Published token lifetimes:

- **Access token:** 15 minutes
- **Refresh token:** 1 year ([Token API](https://developer.walmart.com/us-marketplace/reference/tokenapi))

Walmart also has a real business gate before auth matters: seller onboarding. The public onboarding guide requires business verification, marketplace/eCommerce history, GTIN/UPC GS1 numbers, and a U.S. warehouse with returns capability (or WFS). ([Before you start selling on Walmart Marketplace](https://marketplacelearn.walmart.com/guides/Getting%20started/Onboarding/Before-you-start-selling-on-Walmart-Marketplace))

### Rate Limit Posture

Walmart publishes endpoint-level rate limits rather than a single generalized statement. ([Rate limiting](https://developer.walmart.com/us-marketplace/docs/rate-limiting))

A useful signal for CritterSupply is that the feed-status endpoints are generous:

- **All feed statuses:** 5000/minute (shared with feed item status)
- **Feed item status:** 5000/minute (shared) ([Rate limiting](https://developer.walmart.com/us-marketplace/docs/rate-limiting))

The broader lesson is that Walmart is comfortable with polling status APIs, but it also provides notifications to reduce that polling. ([Notifications overview](https://developer.walmart.com/us-marketplace/docs/notifications-overview))

### Implications for IMarketplaceAdapter

For Walmart, the core issue is less "can the interface submit a listing?" and more "can the interface represent async feed processing and spec-driven payloads?"

What the real adapter needs:

- `SubmitListingAsync(...)` should surface a **feed ID** or equivalent external submission ID.
- CritterSupply likely needs a `CheckSubmissionStatusAsync(...)` path to query feed/item status when a webhook has not yet arrived.
- `ListingSubmission` needs a **Walmart extension payload** that can carry product-type/spec-derived fields, and probably a hint for "match existing catalog item" vs. "full item setup".
- The interface itself does **not** need a separate Walmart-specific submit verb today; the distinction can stay inside the adapter.

## eBay (EBAY_US)

### Product Listing Data Shape

eBay's modern path is the Sell API family, especially the [Inventory API](https://developer.ebay.com/api-docs/sell/inventory/overview.html), with support from the [Account API](https://developer.ebay.com/api-docs/sell/account/resources/methods), [Taxonomy API](https://developer.ebay.com/api-docs/commerce/taxonomy/resources/methods), and optionally the Notification API.

The public eBay workflow is explicit and stateful:

1. Create an inventory location.
2. Create an inventory item (SKU).
3. Optionally create an inventory item group for variations.
4. Create an offer.
5. Publish the offer. ([Inventory API overview](https://developer.ebay.com/api-docs/sell/inventory/overview.html))

Key shape requirements:

- The inventory item holds SKU, product details, condition, quantity, aspects, images, identifiers, and shipping-relevant dimensions. ([createOrReplaceInventoryItem](https://developer.ebay.com/api-docs/sell/inventory/resources/inventory_item/methods/createOrReplaceInventoryItem))
- Variation listings use an inventory item group with `variantSKUs`, shared aspects, and `variesBy` specifications. ([createOrReplaceInventoryItemGroup](https://developer.ebay.com/api-docs/sell/inventory/resources/inventory_item_group/methods/createOrReplaceInventoryItemGroup), [Publishing offers](https://developer.ebay.com/api-docs/sell/static/inventory/publishing-offers.html))
- An offer is the thing that becomes live, and it must eventually include category ID, marketplace ID, price, quantity, merchant location, and business policy IDs. ([createOffer](https://developer.ebay.com/api-docs/sell/inventory/resources/offer/methods/createOffer), [Publishing offers](https://developer.ebay.com/api-docs/sell/static/inventory/publishing-offers.html))

Compared to Amazon/Walmart, eBay's public docs are the clearest about the step-by-step listing graph.

### Submission → Activation Flow

eBay separates **creating** an offer from **publishing** an offer.

- `createOffer` stages the offer and returns an `offerId`. ([createOffer](https://developer.ebay.com/api-docs/sell/inventory/resources/offer/methods/createOffer))
- `publishOffer` is the step that turns the offer into an active listing. ([Publishing offers](https://developer.ebay.com/api-docs/sell/static/inventory/publishing-offers.html))

That means eBay's activation model is more explicit than Amazon's or Walmart's:

- the listing is not live at `createOffer`,
- and it becomes live only after all required publish-time fields are present and `publishOffer` succeeds.

eBay does have a [Notification API](https://developer.ebay.com/api-docs/commerce/notifications/overview.html), but in practice the design still benefits from polling the listing/offer state because the publish step itself is already a synchronous control point.

### Pet Supply Constraints

eBay feels less schema-driven and more policy-driven than Amazon or Walmart.

Useful public signals:

- Live animals are heavily restricted, with only narrow exceptions. ([Live animals policy](https://www.ebay.com/help/policies/prohibited-restricted-items/zoo-animals-wildlife-products-policy?id=4327))
- eBay maintains policy pages for prohibited/restricted items, including hazardous materials and pesticides. ([Prohibited and restricted items overview](https://www.ebay.com/help/policies/prohibited-restricted-items/prohibited-restricted-items?id=4207))
- For CritterSupply, the practical risk areas are pesticides/flea-tick products, medicated or regulated products, and anything that crosses into hazardous-material shipping rules.

Public eBay docs are good enough to show that these restrictions exist, but not as good as Amazon's/Walmart's public docs for turning them into field-level engineering requirements. That means eBay may be easier to integrate technically, but it still requires policy validation during real adapter work.

### Authentication Model

eBay REST APIs use OAuth 2.0. The official docs distinguish between application tokens and user tokens, and the Sell Inventory APIs require access tokens created with the **authorization code grant** flow. ([OAuth access tokens](https://developer.ebay.com/api-docs/static/oauth-tokens.html), [createOffer](https://developer.ebay.com/api-docs/sell/inventory/resources/offer/methods/createOffer))

Additional account prerequisites matter:

- the seller must have an active eBay Developer Program account,
- and the seller account must be opted into business policies before Inventory API publishing is possible. ([Inventory API overview](https://developer.ebay.com/api-docs/sell/inventory/overview.html), [Publishing offers](https://developer.ebay.com/api-docs/sell/static/inventory/publishing-offers.html))

### Rate Limit Posture

eBay publishes daily call limits by API family. For the ones most relevant here:

- **Inventory API:** 2,000,000 API calls/day
- **Notification API:** 10,000 API calls/day
- **Taxonomy API:** 5,000 API calls/day ([API Call Limits](https://developer.ebay.com/develop/get-started/api-call-limits))

Operationally relevant limits also include:

- an Inventory API listing can be revised up to **250 times in one calendar day**, and
- Inventory API listings are managed through the API rather than later edited in Seller Hub. ([createOrReplaceInventoryItem](https://developer.ebay.com/api-docs/sell/inventory/resources/inventory_item/methods/createOrReplaceInventoryItem), [Inventory API overview](https://developer.ebay.com/api-docs/sell/inventory/overview.html))

For CritterSupply, eBay is the least likely channel to force an early bulk-ingestion architecture.

### Implications for IMarketplaceAdapter

eBay proves that `SubmitListingAsync(...)` can still be the right top-level verb, but it cannot mean "one HTTP call." Internally, the eBay adapter must perform multiple idempotent steps and probably return the resulting `offerId`.

What the real adapter needs:

- `SubmitListingAsync(...)` should be allowed to perform **stage + publish** under the hood.
- The result should carry an **offer ID** or channel listing identifier.
- `ListingSubmission` needs channel-specific fields or extension payloads for **business policy IDs**, category ID, and optional variation grouping metadata.
- `CheckSubmissionStatusAsync(...)` is still useful for interface consistency, even if eBay's activation path is clearer than Amazon/Walmart.

## Cross-Platform Comparison

| Platform | Taxonomy complexity | Submission/activation feedback | Data richness required | Pet-supply openness | CritterSupply fit | Operator burden |
|---|---|---|---|---|---|---|
| **Amazon SP-API** | **High** — dynamic product-type schemas | **Async/opaque** — `ACCEPTED` can still fail later | **Very high** — variations, compliance, restrictions | **Moderate** — many pet categories work, but regulated SKUs add friction | **Strong strategic fit** | **Highest** |
| **Walmart Marketplace** | **Medium-high** — taxonomy + spec model | **Async/feed-based** — `feedId`, item status, WFS holds | **High** — spec fields plus match/full-item branching | **Moderate** — open overall, but WFS and pesticides add edge cases | **Good after onboarding** | **High** |
| **eBay** | **Low-medium** — explicit item/offer/publish graph | **Clearer** — staged create, explicit publish | **Medium** — category + business policies | **Moderate-high** — fewer schema gates, more policy checks | **Strong early fit** | **Lowest** |

## IMarketplaceAdapter Interface Implications

CritterSupply's current reference contract is still the right starting point:

- `SubmitListingAsync(ListingSubmission)`
- `DeactivateListingAsync(string channelProductId)`

But discovery across all three platforms points to three missing capabilities that should be addressed **before** Phase 3 real integrations begin.

### 1. Submission correlation ID

All three platforms produce an external identifier that matters after the first submission:

- Amazon: submission/processing correlation,
- Walmart: `feedId`,
- eBay: `offerId`.

**Recommendation:** make the submission result carry an `ExternalSubmissionId` (or similarly named field) now.

### 2. Explicit status check path

Amazon and Walmart, especially, do not fit a "submit and immediately know if we're live" model.

**Recommendation:** add `CheckSubmissionStatusAsync(...)` now, or plan a neighboring abstraction that clearly owns status reconciliation. Waiting until Phase 3 would force either interface churn or platform-specific workarounds.

### 3. Channel-specific extension payload

A fully generic `ListingSubmission` will either become a huge kitchen-sink model or fail to carry required channel-specific attributes.

**Recommendation:** keep the shared payload small, but add a channel-specific extension section now — likely a typed dictionary/JSON bag or a schema-driven extension model.

What does **not** need to change immediately:

- inventory sync methods,
- fulfillment reporting methods,
- or bulk submission methods.

Those are real future concerns (the Shopify example already previews some of them), but this spike's strongest signal is that **submission correlation, status reconciliation, and extension data** are the three interface pressures that will definitely recur across Amazon, Walmart, and eBay.

## Phase 3 Readiness Assessment

### Amazon

**What we know well enough now:** Amazon's public docs clearly define the API family, auth model, schema-discovery pattern, async status notifications, and public rate limits. That is enough to conclude that CritterSupply's current stub shape is under-modelled for real Amazon work.

**What must exist before real adapter work starts:**

- a real seller/developer app registration,
- confirmed seller roles and auth flow,
- verified product-type schemas for representative CritterSupply pet categories,
- and a decision on whether Phase 3 starts with Listings Items API only, or includes feed-based bulk paths.

**Key uncertainty:** exact category-level requirements for regulated pet products still need live seller-account validation.

### Walmart

**What we know well enough now:** Walmart's taxonomy/spec model, OAuth token behavior, async feed-based listing flow, notification capability, and onboarding requirements are all clear from public docs.

**What must exist before real adapter work starts:**

- approved Marketplace seller account access,
- API credentials,
- a decision on whether CritterSupply is targeting seller-fulfilled only or WFS as well,
- and sample item specs for the first pet categories to support.

**Key uncertainty:** business approval timing and the real-world impact of WFS compliance holds on CritterSupply's likely pet-supply catalog.

### eBay

**What we know well enough now:** eBay's public docs clearly describe the Inventory API object model, the explicit publish step, auth model, business policy dependency, and rate limits.

**What must exist before real adapter work starts:**

- seller account with business policies configured,
- policy IDs retrievable for payment/return/fulfillment,
- category mapping strategy for CritterSupply's initial categories,
- and a decision on whether to support variations in the first adapter slice.

**Key uncertainty:** exact policy enforcement for flea/tick, supplements, and other regulated pet products should be confirmed through live test submissions or seller help-center validation.

## References

### Amazon

- [Selling Partner API portal](https://developer-docs.amazon.com/sp-api)
- [Manage Product Listings with the Selling Partner API](https://developer-docs.amazon.com/sp-api/docs/manage-product-listings-guide)
- [Building listings management workflows](https://developer-docs.amazon.com/sp-api/docs/building-listings-management-workflows-guide)
- [Listings Items API reference](https://developer-docs.amazon.com/sp-api/reference/listings-items-v2021-08-01)
- [Listings Items API rate limits](https://developer-docs.amazon.com/sp-api/docs/listings-items-api-rate-limits)
- [Product Type Definitions API reference](https://developer-docs.amazon.com/sp-api/reference/product-type-definitions-v2020-09-01)
- [Product Type Definitions API rate limits](https://developer-docs.amazon.com/sp-api/docs/product-type-definitions-api-rate-limits)
- [Listings Restrictions API use case guide](https://developer-docs.amazon.com/sp-api/docs/listings-restrictions-api-v2021-08-01-use-case-guide)
- [Notifications API use case guide](https://developer-docs.amazon.com/sp-api/docs/notifications-api-v1-use-case-guide)
- [Selling Partner Appstore authorization workflow](https://developer-docs.amazon.com/sp-api/docs/selling-partner-appstore-authorization-workflow)
- [Usage plans and rate limits](https://developer-docs.amazon.com/sp-api/docs/usage-plans-and-rate-limits)
- Seller Central restricted-products and dangerous-goods pages appear to require seller login; they should be consulted during Phase 3 account setup.

### Walmart

- [Introduction to Walmart Marketplace APIs](https://developer.walmart.com/us-marketplace/docs/introduction-to-marketplace-apis)
- [Item Management API overview](https://developer.walmart.com/us-marketplace/docs/item-management-api-overview-1)
- [Understanding the requirements for listing an item](https://developer.walmart.com/us-marketplace/docs/understanding-the-requirements-for-listing-an-item)
- [Create items on Walmart.com](https://developer.walmart.com/us-marketplace/docs/create-items-on-walmartcom)
- [Monitor my item](https://developer.walmart.com/us-marketplace/docs/monitor-my-item)
- [Notifications overview](https://developer.walmart.com/us-marketplace/docs/notifications-overview)
- [Token API](https://developer.walmart.com/us-marketplace/reference/tokenapi)
- [Rate limiting](https://developer.walmart.com/us-marketplace/docs/rate-limiting)
- [Before you start selling on Walmart Marketplace](https://marketplacelearn.walmart.com/guides/Getting%20started/Onboarding/Before-you-start-selling-on-Walmart-Marketplace)
- [Hazardous and regulated products](https://marketplacelearn.walmart.com/guides/prohibited-products-policy-hazardous-items?locale=en-US)
- [Animals policy](https://marketplacelearn.walmart.com/guides/Prohibited-Products-Policy:-Animals)
- [WFS prohibited products policy](https://marketplacelearn.walmart.com/guides/wfs-prohibited-products-policy)
- [WFS hazmat items on hold](https://developer.walmart.com/us-marketplace/docs/wfs-hazmat-items-on-hold)
- [What's New](https://developer.walmart.com/us-marketplace/page/whats-new)

### eBay

- [Sell APIs overview](https://developer.ebay.com/develop/selling-apps)
- [Inventory API overview](https://developer.ebay.com/api-docs/sell/inventory/overview.html)
- [createOrReplaceInventoryItem](https://developer.ebay.com/api-docs/sell/inventory/resources/inventory_item/methods/createOrReplaceInventoryItem)
- [createOrReplaceInventoryItemGroup](https://developer.ebay.com/api-docs/sell/inventory/resources/inventory_item_group/methods/createOrReplaceInventoryItemGroup)
- [createOffer](https://developer.ebay.com/api-docs/sell/inventory/resources/offer/methods/createOffer)
- [Required fields for publishing an offer](https://developer.ebay.com/api-docs/sell/static/inventory/publishing-offers.html)
- [OAuth access tokens](https://developer.ebay.com/api-docs/static/oauth-tokens.html)
- [API Call Limits](https://developer.ebay.com/develop/get-started/api-call-limits)
- [Notification API overview](https://developer.ebay.com/api-docs/commerce/notifications/overview.html)
- [Prohibited and restricted items overview](https://www.ebay.com/help/policies/prohibited-restricted-items/prohibited-restricted-items?id=4207)
- [Live animals policy](https://www.ebay.com/help/policies/prohibited-restricted-items/zoo-animals-wildlife-products-policy?id=4327)
