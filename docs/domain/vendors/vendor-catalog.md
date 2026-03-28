# CritterSupply Vendor Catalog

This catalog is the narrative source of truth for CritterSupply's imaginary vendor roster. It gives the Vendor Identity, Vendor Portal, Product Catalog, and Pricing bounded contexts a shared cast of believable companies, users, products, and operational situations for demos, fixtures, and future seed tooling.

The roster intentionally mixes stable default vendors and edge-case vendors. HearthHound Nutrition Co. is the default happy-path tenant; the rest introduce onboarding, suspended access, seasonal assortment changes, discontinued SKUs, sustainability positioning, and legacy catalog behavior.

## HearthHound Nutrition Co.

**Tagline:** Everyday nutrition built for active pets and the people who feed them well.  
**Founded:** 2013 | **HQ:** Denver, Colorado  
**Type:** Privately held mid-size pet nutrition manufacturer  
**Status:** Active  
**Primary specialty:** Food and nutrition  
**Secondary specialty:** Functional treats  
**Fixture role:** Default test vendor / happy-path baseline

### About

HearthHound Nutrition Co. was founded by former regional pet-retail buyers who believed there was room between commodity grocery brands and ultra-premium niche foods. The company built its reputation on dependable replenishment, clear feeding guidance, and approachable formulas that independent retailers could recommend without hesitation. Over the last decade it has expanded into toppers and wellness chews without drifting into catalog sprawl, which makes it feel like a realistic mid-size wholesale partner. In demos, HearthHound is the vendor that makes everything look normal in the best possible way.

### Team

| Name | Title | Vendor Portal Role | User Status | Email |
|------|-------|-------------------|-------------|-------|
| Melissa Kerr | Director of Channel Operations | Admin | Active | mkerr@hearthhound.com |
| Jordan Pike | Senior Catalog Coordinator | CatalogManager | Active | jpike@hearthhound.com |
| Elena Suarez | Finance & Compliance Analyst | ReadOnly | Active | esuarez@hearthhound.com |

### Product Catalog

| SKU | Name | Category | Price | Status |
|-----|------|----------|-------|--------|
| HH-SALMON-22LB | Open Range Salmon Kibble 22 lb | Dry Dog Food | $64.99 | Active |
| HH-TURKEY-TOPPER | Turkey & Pumpkin Topper 12-Pack | Wet Food Toppers | $19.99 | Active |
| HH-JOINT-CHEWS90 | Joint Support Chews 90-Count | Functional Dog Treats | $29.99 | Active |
| HH-PUPPY-STARTER | Puppy Starter Feeding Kit | Puppy Feeding Kits | $34.50 | ComingSoon |

### Notes

- Best all-around baseline vendor for most integration, E2E, and demo scenarios.
- Healthy active account with all three portal roles represented.
- Good default for role-based UI tests, catalog browsing, and standard change request flows.

## TumblePaw Play Labs

**Tagline:** Modern enrichment toys for pets that get bored faster than their humans.  
**Founded:** 2023 | **HQ:** Austin, Texas  
**Type:** VC-backed startup brand  
**Status:** Onboarding  
**Primary specialty:** Toys and enrichment  
**Secondary specialty:** Training accessories  
**Fixture role:** Onboarding and invitation edge case

### About

TumblePaw Play Labs was started by a former children's toy designer and a canine behavior consultant who saw a gap between cheap novelty toys and enrichment products that actually held a pet's attention. The brand gained traction through social-first direct-to-consumer bundles before expanding into selective wholesale. Its assortment is still compact, packaging-forward, and unmistakably startup-shaped: every product feels intentional, but the operational bench is still thin. In CritterSupply, TumblePaw is the vendor that helps onboarding flows feel real instead of hypothetical.

### Team

| Name | Title | Vendor Portal Role | User Status | Email |
|------|-------|-------------------|-------------|-------|
| Asha Bell | Co-Founder & Operations Lead | Admin | Active | asha@tumblepaw.com |
| Connor Reeves | Product Data Specialist | CatalogManager | Invited | connor@tumblepaw.com |
| Mina Albright | Fractional Controller | ReadOnly | Invited | mina@tumblepaw.com |

### Product Catalog

| SKU | Name | Category | Price | Status |
|-----|------|----------|-------|--------|
| TP-ORBIT-FEED | Orbit Puzzle Feeder | Interactive Feeders | $26.00 | Active |
| TP-RIPPLE-MAT | Ripple Silicone Lick Mat | Lick Mats | $14.50 | Active |
| TP-POCKET-TUG | Pocket Tug Mini | Training Toys | $12.00 | Active |
| TP-BURROW-BEETLE | Burrow Beetle Plush Puzzle Set | Plush Puzzle Toys | $24.00 | ComingSoon |

### Notes

- Primary onboarding vendor with pending invitations and an incomplete team roster.
- Best fit for invite resend, activation, and onboarding-dashboard scenarios.
- Portal expectation: the tenant exists, one admin can work, and setup still feels in progress.

## Red Clay Kennel Goods

**Tagline:** Hardworking collars, clean coats, and kennel basics built to last.  
**Founded:** 1992 | **HQ:** Asheville, North Carolina  
**Type:** Family-owned manufacturer and distributor  
**Status:** Active  
**Primary specialty:** Accessories and gear  
**Secondary specialty:** Grooming  
**Fixture role:** Legacy family business with mixed product lifecycle states

### About

Red Clay Kennel Goods began as a family leather shop making leads and collars for hunting dogs across the Southeast. Over time, the McRae family expanded into kennel pads, grooming concentrates, and durable everyday accessories that fit feed stores, grooming shops, and independent pet retailers. The business still has a practical operator-led feel: fewer flashy launches, more dependable replenishment, and just enough catalog drift to produce realistic cleanup work. It is exactly the kind of regional family vendor that would have both loyal buyers and a few long-tail SKUs that need periodic rationalization.

### Team

| Name | Title | Vendor Portal Role | User Status | Email |
|------|-------|-------------------|-------------|-------|
| Thomas McRae | President | Admin | Active | thomas@redclaykennel.com |
| Lila McRae Foster | Merchandising Manager | CatalogManager | Active | lila@redclaykennel.com |
| Denise Waller | Sales Operations Assistant | CatalogManager | Active | denise@redclaykennel.com |
| Owen Patel | Key Accounts Coordinator | ReadOnly | Active | owen.patel@redclaykennel.com |

### Product Catalog

| SKU | Name | Category | Price | Status |
|-----|------|----------|-------|--------|
| RCKG-FIELD-COLLAR | Field Collar 1 in | Dog Collars | $38.00 | Active |
| RCKG-TREAT-POUCH | Waxed Canvas Treat Pouch | Training Gear | $22.00 | Active |
| RCKG-SHED-SHAMPOO | Shed Control Shampoo 16 oz | Dog Grooming | $18.50 | Active |
| RCKG-CEDAR-PAD | Cedar Kennel Pad | Kennel Bedding | $44.00 | OutOfSeason |
| RCKG-BRASS-LEAD | Brass Slip Lead | Slip Leads | $31.00 | Discontinued |

### Notes

- Best family-business example in the roster.
- Useful for testing catalog filters that combine Active, OutOfSeason, and Discontinued products.
- Two CatalogManager users make role-based list and concurrency scenarios more believable.

## Prairie Nest Habitat Works

**Tagline:** Thoughtful habitats and pet furniture built for everyday living spaces.  
**Founded:** 1961 | **HQ:** Madison, Wisconsin  
**Type:** Legacy employee-owned habitat manufacturer  
**Status:** Active  
**Primary specialty:** Habitat and furniture  
**Secondary specialty:** Small animal and bird habitat accessories  
**Fixture role:** Legacy bulky-goods vendor / seasonal assortment example

### About

Prairie Nest Habitat Works started as a Midwestern woodshop producing rabbit hutches and bird stands for local feed stores. Over six decades it evolved into a broad habitat and furniture supplier with products spanning cat towers, small-animal enclosures, and cage accessories sold through specialty retail and regional farm channels. The founder family exited years ago, but the employee-owned structure preserved the company's practical product DNA and slower release cadence. In CritterSupply it represents the vendor whose assortment is operationally heavier: bigger dimensions, more seasonal carryover, and fewer impulse-buy items.

### Team

| Name | Title | Vendor Portal Role | User Status | Email |
|------|-------|-------------------|-------------|-------|
| Barbara Ng | VP of National Accounts | Admin | Active | bng@prairienest.com |
| Michael Sorensen | Catalog Systems Lead | CatalogManager | Active | msorensen@prairienest.com |
| Keisha Darden | Credit Manager | ReadOnly | Active | kdarden@prairienest.com |

### Product Catalog

| SKU | Name | Category | Price | Status |
|-----|------|----------|-------|--------|
| PNHW-ALDER-TOWER | Alder Cat Tower | Cat Furniture | $189.00 | Active |
| PNHW-MEADOW-CONDO | Meadow Rabbit Condo | Small Animal Habitats | $249.00 | Active |
| PNHW-AVIARY-PERCH | Aviary Perch Set | Bird Habitat Accessories | $27.50 | Active |
| PNHW-PORCH-BED | Summer Porch Bed | Pet Furniture | $79.00 | OutOfSeason |
| PNHW-TRAVEL-CRATE | Fold-Flat Travel Crate | Travel Crates | $96.00 | Active |

### Notes

- Best legacy 50+ year vendor in the set.
- Useful for demos involving bulky-goods language, furniture-style categories, and seasonal assortment changes.
- Strong operator lookup example when someone needs “the habitat vendor” rather than a brand-led DTC story.

## Harbor & Fern Pet Apothecary

**Tagline:** Botanical wellness and grooming for pets, packaged with a lighter footprint.  
**Founded:** 2018 | **HQ:** Burlington, Vermont  
**Type:** Certified B Corporation  
**Status:** Active  
**Primary specialty:** Health and wellness  
**Secondary specialty:** Grooming  
**Fixture role:** Sustainability-focused boutique brand / premium content review example

### About

Harbor & Fern Pet Apothecary was founded by an herbal formulator and a former veterinary technician who wanted premium wellness products that looked modern without making irresponsible claims. The brand built an audience through refill pouches, post-consumer packaging, and transparent ingredient sourcing before expanding into selective wholesale. Its assortment is intentionally tight, premium-priced, and copy-sensitive, which makes it useful for tests involving content review, curated merchandising, and brand consistency. This is the vendor that makes sustainability and premium positioning feel specific instead of generic.

### Team

| Name | Title | Vendor Portal Role | User Status | Email |
|------|-------|-------------------|-------------|-------|
| Nora Whitcomb | Founder & CEO | Admin | Active | nora@harborandfern.com |
| Seth Molina | Wholesale Catalog Lead | CatalogManager | Active | seth@harborandfern.com |
| Priyanka Desai | Controller | ReadOnly | Active | priyanka@harborandfern.com |

### Product Catalog

| SKU | Name | Category | Price | Status |
|-----|------|----------|-------|--------|
| HF-CALM-DROPS | Calm Tide Drops | Calming Supplements | $32.00 | Active |
| HF-PAW-BALM | Paw Balm Tin | Paw Care | $16.00 | Active |
| HF-COAT-RINSE | Coat Restore Rinse | Dog Grooming | $24.00 | Active |
| HF-GUT-FLORA | Gut Flora Powder | Digestive Supplements | $36.00 | Active |

### Notes

- Best sustainability-focused vendor in the roster.
- Good fit for premium assortment demos, content-review workflows, and boutique-brand storytelling.
- ReadOnly user is intentionally finance-oriented rather than a synthetic filler role.

## Iron Mesa Animal Health

**Tagline:** Practical animal wellness products scaled for modern retail channels.  
**Founded:** 2004 | **HQ:** Omaha, Nebraska  
**Type:** PE-backed veterinary consumer health platform  
**Status:** Suspended  
**Primary specialty:** Health and wellness  
**Secondary specialty:** Oral care and first aid  
**Fixture role:** Suspended-tenant and compliance-hold edge case

### About

Iron Mesa Animal Health grew through the acquisition of several regional veterinary-adjacent brands, then used private-equity backing to expand quickly into national retail and marketplace distribution. The company has a broad commercial mindset and a catalog built around velocity items like dental, digestive, and joint support products. It is currently suspended because a packaging revision and supporting compliance-document lapse triggered a temporary hold while trading terms and safety substantiation are reviewed. In CritterSupply, Iron Mesa is the vendor that makes access restrictions, operational banners, and support escalation flows feel grounded.

### Team

| Name | Title | Vendor Portal Role | User Status | Email |
|------|-------|-------------------|-------------|-------|
| Garrett Hume | Director of Marketplace Operations | Admin | Active | ghume@ironmesahealth.com |
| Priya Nandakumar | Senior PIM Manager | CatalogManager | Active | priyan@ironmesahealth.com |
| Elaine Broderick | Compliance Counsel | ReadOnly | Active | ebroderick@ironmesahealth.com |

### Product Catalog

| SKU | Name | Category | Price | Status |
|-----|------|----------|-------|--------|
| IMAH-DENTAL-CARE | Dental Care Sticks Medium | Dental Chews | $21.99 | Active |
| IMAH-PROBIO-30 | Probiotic Paste 30 mL | Digestive Health | $18.75 | Active |
| IMAH-HOTSPOT-8OZ | Hot Spot Spray 8 oz | Skin & Coat Care | $17.50 | Active |
| IMAH-SENIOR-MOB | Senior Mobility Tablets | Joint Supplements | $34.99 | Active |
| IMAH-FIRSTAID-KIT | Travel First Aid Kit | Pet First Aid | $28.00 | ComingSoon |

### Notes

- Primary suspended-tenant scenario for authorization, portal messaging, and operator escalation flows.
- Suspension note should be interpreted as “account exists, products exist, write access should be restricted until reinstatement.”
- Best fit for backoffice demos involving compliance holds, support questions, and visible vendor-state banners.
