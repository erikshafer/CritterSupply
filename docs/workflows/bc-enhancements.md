# Bounded Context Enhancements

**Purpose:** Document future enhancements for existing, implemented bounded contexts  
**Implementation Status:** 📋 Planned (Future Cycles)  
**Priority:** Low-Medium (Post-Core Implementation)  

---

## Overview

This document captures enhancement workflows for bounded contexts that are already implemented but could benefit from additional features. These enhancements are "nice-to-have" improvements that add value but aren't required for the core e-commerce reference architecture.

---

## Table of Contents

1. [Shopping BC Enhancements](#shopping-bc-enhancements)
2. [Product Catalog BC Enhancements](#product-catalog-bc-enhancements)
3. [Customer Identity BC Enhancements](#customer-identity-bc-enhancements)
4. [Orders BC Enhancements](#orders-bc-enhancements)
5. [Inventory BC Enhancements](#inventory-bc-enhancements)
6. [Fulfillment BC Enhancements](#fulfillment-bc-enhancements)

---

## Shopping BC Enhancements

### Current State (Implemented)

- ✅ Cart initialization
- ✅ Add/remove items
- ✅ Change item quantity
- ✅ Clear cart
- ✅ Checkout initiation (handoff to Orders BC)

### Enhancement 1: Wishlist Management

**Business Value:** Allows customers to save items for future purchase (increases conversion, enables gift registries)

**New Aggregate: Wishlist**

**Lifecycle Events:**
- `WishlistCreated` — Customer creates wishlist
- `ItemAddedToWishlist` — Customer saves item for later
- `ItemRemovedFromWishlist` — Customer removes item
- `ItemMovedToCart` — Customer moves wishlist item to cart
- `WishlistShared` — Customer shares wishlist via link (gift registry)

**Workflows:**

1. **Add Item to Wishlist:**
   ```
   Customer browsing → Click "❤️ Add to Wishlist"
   → ItemAddedToWishlist
   → Display toast: "Added to wishlist"
   ```

2. **Move Wishlist Item to Cart:**
   ```
   Customer views wishlist → Click "Move to Cart" button
   → ItemRemovedFromWishlist
   → Cart.ItemAdded
   → Display toast: "Moved to cart"
   ```

3. **Share Wishlist (Gift Registry):**
   ```
   Customer → Click "Share Wishlist"
   → Generate public link: /wishlist/shared/abc123
   → WishlistShared
   → Friends can view (read-only) + purchase items as gifts
   ```

**Estimated Effort:** 2-3 sessions

---

### Enhancement 2: Product Search

**Business Value:** Faster product discovery (reduces bounce rate, increases conversions)

**Implementation Options:**

**Option A: Simple (Marten Full-Text Search)**
- Query Product Catalog BC with LIKE queries
- Fast for small catalogs (<10K products)

**Option B: Advanced (Dedicated Search BC)**
- Elasticsearch/OpenSearch index
- Faceted search (filter by category, price range, brand)
- Autocomplete/typeahead
- Synonym support ("dog food" = "canine nutrition")

**Workflows:**

1. **Search Products:**
   ```
   Customer types "dog bowl" in search box
   → Query Product Catalog BC (or Search BC)
   → Return results ranked by relevance
   → Display: Product name, image, price, rating
   ```

2. **Faceted Filtering:**
   ```
   Customer searches "dog toys"
   → Apply filters:
      - Category: Toys
      - Price: $10-$20
      - Brand: Acme Pet Supplies
   → Results update dynamically (AJAX)
   ```

**Estimated Effort:** 3-5 sessions (Option A), 5-8 sessions (Option B)

---

### Enhancement 3: Abandoned Cart Recovery

**Business Value:** Recover 10-15% of abandoned carts via email campaigns (industry standard)

**New Events:**
- `CartAbandoned` — Cart idle for 24 hours (anonymous carts only, authenticated carts never abandon)

**Workflows:**

1. **Detect Abandoned Cart (Background Job):**
   ```
   System runs daily job
   → Query carts with LastModifiedAt > 24 hours ago
   → For each cart:
      - If anonymous: Emit CartAbandoned (for analytics only)
      - If authenticated: Send email reminder
   ```

2. **Email Reminder:**
   ```
   Customer receives email:
   - Subject: "You left items in your cart!"
   - Body: "Complete your purchase and save 10% (promo code COMEBACK10)"
   - CTA button: "Return to Cart" → Link to /cart
   ```

3. **Promo Code Application:**
   ```
   Customer clicks link → Returns to cart
   → Auto-apply promo code COMEBACK10
   → Display: "10% discount applied!"
   ```

**Estimated Effort:** 2-3 sessions

---

### Enhancement 4: Price Drift Handling

**Business Value:** Handle price changes between cart addition and checkout (transparency + trust)

**New Events:**
- `ItemPriceChanged` — Product price updated since item added to cart

**Workflows:**

1. **Detect Price Drift:**
   ```
   Customer adds item to cart:
   - ItemAdded: Sku "DOG-BOWL-01", UnitPrice $19.99, AddedAt 2026-02-01

   Catalog team updates price:
   - ProductUpdated: Sku "DOG-BOWL-01", NewPrice $22.99, UpdatedAt 2026-02-05

   Customer proceeds to checkout (2026-02-05):
   - System compares: Cart price ($19.99) vs Catalog price ($22.99)
   - Price drift detected: +$3.00 (15% increase)
   ```

2. **Notify Customer:**
   ```
   Display warning at checkout:
   - "⚠️ Price Update: Dog Bowl is now $22.99 (was $19.99 when added to cart)"
   - "Your cart total has been updated to reflect current prices"
   - Options:
      [1] "Proceed with updated price" → Continue checkout
      [2] "Remove item" → Remove from cart
   ```

3. **Emit Event:**
   ```
   Customer proceeds → ItemPriceChanged
   - OldPrice: $19.99
   - NewPrice: $22.99
   - CustomerAccepted: true
   ```

**Estimated Effort:** 2 sessions

---

## Product Catalog BC Enhancements

### Current State (Implemented)

- ✅ Product CRUD (Create, Read, Update, Delete)
- ✅ SKU and ProductName value objects
- ✅ Category as primitive string (queryable)
- ✅ Product status (Draft, Active, Discontinued)

### Enhancement 1: Hierarchical Category Management

**Business Value:** Organize products in nested categories (improves navigation, SEO)

**New Entity: Category**

**Structure:**
```
Categories (Tree):
  - Dogs
    - Food
      - Dry Food
      - Wet Food
      - Treats
    - Toys
    - Accessories
  - Cats
    - Food
    - Toys
  - Birds
    - Food
    - Cages
```

**Fields:**
- CategoryId (Guid)
- Name (string)
- Slug (string) — URL-friendly ("dog-food", "cat-toys")
- ParentCategoryId (Guid, nullable) — For nested categories
- Description (string)
- DisplayOrder (int) — For sorting in navigation
- Status (enum: Active, Hidden)

**Workflows:**

1. **Create Category Hierarchy:**
   ```
   Admin → Create root category "Dogs"
   Admin → Create child category "Food" (parent: Dogs)
   Admin → Create child category "Dry Food" (parent: Food)
   ```

2. **Assign Product to Category:**
   ```
   Vendor → Select category "Dogs > Food > Dry Food"
   → Product.CategoryId = [Dry Food category ID]
   → Breadcrumb navigation: Home > Dogs > Food > Dry Food > [Product Name]
   ```

3. **Category Navigation:**
   ```
   Customer → Click "Dogs" in navigation
   → Display all products where CategoryId IN (Dogs subtree)
   → Include: Dogs, Dogs/Food, Dogs/Toys, Dogs/Food/Dry Food, etc.
   ```

**Estimated Effort:** 3-4 sessions

---

### Enhancement 2: Product Recommendations

**Business Value:** "Customers also bought" suggestions (increases average order value by 10-20%)

**Implementation Options:**

**Option A: Simple (Co-Purchase Analysis)**
- Query Orders BC: "Which products were purchased together with this product?"
- Example: "Customers who bought Dog Bowl also bought Dog Food (60% of orders)"

**Option B: Advanced (Machine Learning)**
- Collaborative filtering (Amazon-style recommendations)
- Requires ML model training, separate Recommendations BC

**Workflows (Option A):**

1. **Generate Recommendations (Background Job):**
   ```
   System runs daily job:
   → Query Orders BC: Get orders containing "DOG-BOWL-01"
   → Analyze line items in those orders
   → Calculate co-purchase frequency:
      - DOG-FOOD-01: 120 co-purchases (60% of DOG-BOWL-01 orders)
      - DOG-TOY-05: 80 co-purchases (40%)
   → Store recommendations in Product Catalog projection
   ```

2. **Display Recommendations:**
   ```
   Customer views product "DOG-BOWL-01"
   → Below product details, display:
      "Customers Also Bought:"
      - [Image] Premium Dog Food ($49.99) — 60% of customers
      - [Image] Squeaky Bone Toy ($12.99) — 40% of customers
   → Click recommendation → Navigate to product page
   ```

**Estimated Effort:** 2-3 sessions (Option A), 8-12 sessions (Option B)

---

### Enhancement 3: Bulk Import/Export

**Business Value:** Vendors can upload/download thousands of products via CSV (efficiency)

**Workflows:**

1. **Export Products to CSV:**
   ```
   Admin → Click "Export Products"
   → System generates CSV:
      SKU,Name,Category,Price,Status,Description
      DOG-BOWL-01,Ceramic Dog Bowl,Dogs,19.99,Active,"Durable ceramic..."
      CAT-TOY-05,Interactive Cat Laser,Cats,29.99,Active,"Laser pointer..."
   → Download: products-2026-02-18.csv
   ```

2. **Import Products from CSV:**
   ```
   Admin → Upload CSV: products-import.csv
   → System validates:
      - SKU format (alphanumeric + hyphens)
      - Category exists
      - Price is positive decimal
   → Preview changes (insert 50, update 20, skip 5 errors)
   → Admin confirms → Batch create/update products
   ```

**Estimated Effort:** 2 sessions

---

## Customer Identity BC Enhancements

### Current State (Implemented)

- ✅ Customer CRUD
- ✅ Address management (add, edit, delete)
- ✅ EF Core with foreign key relationships

### Enhancement 1: Customer Profile Management

**Business Value:** Customers update personal info, preferences (improves personalization)

**New Fields:**
- Phone (string, nullable)
- DateOfBirth (DateOnly, nullable) — For birthday discounts
- PreferredContactMethod (enum: Email, SMS, Phone)
- MarketingOptIn (bool) — For promotional emails

**Workflows:**

1. **Update Profile:**
   ```
   Customer → Navigate to "My Account" → "Profile"
   → Edit form:
      - First Name: Alice
      - Last Name: Johnson
      - Email: alice@example.com (read-only, requires verification to change)
      - Phone: +1-555-0123
      - Date of Birth: 1990-05-15
      - Preferred Contact: Email
      - Marketing Opt-In: [x] Yes, send me promotions
   → Click "Save"
   → Command: UpdateCustomerProfile
   → Event: CustomerProfileUpdated
   ```

2. **Birthday Discount Campaign:**
   ```
   System runs daily job → Query customers with birthday = today
   → Send email: "Happy Birthday! Enjoy 20% off your next order (code BDAY20)"
   ```

**Estimated Effort:** 1-2 sessions

---

### Enhancement 2: Payment Method Storage (Tokenized)

**Business Value:** Save credit cards securely for faster checkout (PCI DSS compliant via tokenization)

**New Entity: PaymentMethod**

**Fields:**
- PaymentMethodId (Guid)
- CustomerId (Guid) — Foreign key
- Type (enum: CreditCard, DebitCard, PayPal, BankAccount)
- Token (string) — Payment gateway token (NOT raw card number)
- Last4Digits (string) — "4242" (for display only)
- ExpiryMonth (int)
- ExpiryYear (int)
- BillingAddress (Address)
- IsDefault (bool) — Pre-selected at checkout

**Workflows:**

1. **Add Payment Method:**
   ```
   Customer → Navigate to "My Account" → "Payment Methods"
   → Click "Add Credit Card"
   → Enter card details:
      - Card Number: 4242 4242 4242 4242
      - Expiry: 12/26
      - CVV: 123
   → Payment gateway tokenizes → Returns token: "tok_visa_abc123"
   → Store: PaymentMethod
      - Token: "tok_visa_abc123"
      - Last4Digits: "4242"
      - Type: CreditCard
   → Display: "Visa ending in 4242"
   ```

2. **Use Saved Payment Method at Checkout:**
   ```
   Customer → Checkout Step 3 (Payment)
   → Display saved payment methods:
      [•] Visa ending in 4242 (default)
      [ ] Mastercard ending in 5555
      [ ] Add new card
   → Select saved card → Skip card entry form
   → Click "Continue to Review"
   ```

**Security Note:** NEVER store raw card numbers. Always use payment gateway tokenization (Stripe, PayPal, etc.)

**Estimated Effort:** 2-3 sessions

---

### Enhancement 3: Multi-Address Management Improvements

**Business Value:** Enhanced address book features (nicknames, default shipping/billing)

**New Fields (CustomerAddress):**
- IsDefaultShipping (bool) — Pre-selected for shipping
- IsDefaultBilling (bool) — Pre-selected for payment billing address
- AddressType (enum: Residential, Business, PO Box) — For shipping validation

**Workflows:**

1. **Set Default Addresses:**
   ```
   Customer → Address book shows:
      [⭐ Default Shipping] Home — 123 Main St, Seattle, WA
      [⭐ Default Billing]  Work — 456 Office Blvd, Seattle, WA
      [ ] Vacation Home — 789 Beach Rd, Malibu, CA
   
   Customer → Click "Set as Default Shipping" on Vacation Home
   → Update: IsDefaultShipping = true (only one default at a time)
   → Old default (Home) → IsDefaultShipping = false
   ```

2. **Address Validation (USPS API):**
   ```
   Customer → Enter address: "123 main st seattle wa"
   → Call USPS Address Validation API
   → Return standardized: "123 Main St, Seattle, WA 98101-1234"
   → Display: "Did you mean: 123 Main St, Seattle, WA 98101-1234?"
   → Customer confirms → Save standardized address
   ```

**Estimated Effort:** 1-2 sessions

---

## Orders BC Enhancements

### Current State (Implemented)

- ✅ Checkout aggregate (4-step wizard)
- ✅ Order saga (orchestrates Payments, Inventory, Fulfillment)
- ✅ 11+ state transitions (Placed → Fulfilling → Shipped → Delivered → Cancelled)

### Enhancement 1: Order Modification (Before Fulfillment)

**Business Value:** Customers can add/remove items or change quantity before shipment

**New Events:**
- `OrderModificationRequested` — Customer requests changes
- `OrderModificationApproved` — System validates + applies changes
- `OrderModificationRejected` — Too late to modify (already shipped)

**Workflows:**

1. **Request Order Modification:**
   ```
   Customer → Order status: "Payment Confirmed" (not yet shipped)
   → Click "Modify Order"
   → Display current line items:
      [x] DOG-BOWL-01 (Qty: 2) — Keep
      [x] CAT-TOY-05 (Qty: 1) — Keep
      [+] Add new item: DOG-FOOD-01 (Qty: 1)
   → New total: $79.97 (was $69.97)
   → Additional payment: $10.00
   ```

2. **System Validates Modification:**
   ```
   Check order status:
   - If status = Placed, PaymentAuthorized → OK to modify
   - If status = Fulfilling, Shipped → TOO LATE, reject
   
   If OK:
   → Command: AuthorizeAdditionalPayment (Payments BC)
   → Command: ReserveAdditionalInventory (Inventory BC)
   → If both succeed: Apply modification
   → Event: OrderModificationApproved
   ```

**Estimated Effort:** 3-4 sessions

---

### Enhancement 2: Partial Cancellation

**Business Value:** Customer cancels some items (not entire order) before shipment

**New Events:**
- `OrderPartiallyCancelled` — Some line items cancelled, others proceed to fulfillment

**Workflows:**

1. **Request Partial Cancellation:**
   ```
   Customer → Order status: "Payment Confirmed"
   → Click "Cancel Items"
   → Select items to cancel:
      [ ] DOG-BOWL-01 (Qty: 2) — Keep
      [x] CAT-TOY-05 (Qty: 1) — Cancel
   → Submit
   ```

2. **System Processes Partial Cancellation:**
   ```
   → Command: ReleaseReservation (Inventory BC, Sku: CAT-TOY-05)
   → Command: RefundPartialAmount (Payments BC, $29.99)
   → Event: OrderPartiallyCancelled
   → Updated order total: $39.98 (was $69.97)
   → Remaining items proceed to fulfillment
   ```

**Estimated Effort:** 2-3 sessions

---

### Enhancement 3: Split Shipment Handling

**Business Value:** Support multi-warehouse fulfillment (faster delivery, reduced shipping costs)

**Current Limitation:** Order assumes single shipment from one warehouse

**Enhanced Flow:**
```
Order placed with 3 items:
  - DOG-BOWL-01 (Qty: 2) — Available at Warehouse A
  - CAT-TOY-05 (Qty: 1) — Available at Warehouse B
  - DOG-FOOD-01 (Qty: 1) — Available at Warehouse A

Fulfillment BC creates 2 shipments:
  - Shipment 1 (Warehouse A): DOG-BOWL-01, DOG-FOOD-01 → Ships first
  - Shipment 2 (Warehouse B): CAT-TOY-05 → Ships separately

Customer receives 2 tracking numbers:
  - Tracking 1: 1Z999AA10123456784 (Delivered Feb 20)
  - Tracking 2: 1Z888BB20234567895 (Delivered Feb 22)

Order status: "Partially Shipped" → "Delivered" (when all shipments delivered)
```

**New Events:**
- `ShipmentPartiallyFulfilled` — Some items shipped, others pending
- `OrderFullyFulfilled` — All shipments delivered

**Estimated Effort:** 3-4 sessions

---

### Enhancement 4: Reorder Functionality

**Business Value:** One-click reorder from order history (convenience, repeat purchases)

**Workflows:**

1. **Reorder from Order History:**
   ```
   Customer → Navigate to "Order History"
   → View past order (ID: order-abc-123):
      - DOG-BOWL-01 (Qty: 2) — $39.98
      - CAT-TOY-05 (Qty: 1) — $29.99
      - Total: $69.97
   → Click "Reorder" button
   ```

2. **System Creates New Cart:**
   ```
   → Command: InitializeCart (customerId)
   → For each line item in past order:
      - Command: AddItemToCart (Sku, Quantity)
   → Check product availability:
      - DOG-BOWL-01: In stock ✅
      - CAT-TOY-05: Discontinued ❌
   → Display: "2 items added to cart. 1 item (CAT-TOY-05) is no longer available."
   → Redirect to cart
   ```

**Estimated Effort:** 1-2 sessions

---

## Inventory BC Enhancements

### Enhancement 1: Backorder Support

**Business Value:** Accept orders for out-of-stock items (capture sales, fulfill when restocked)

**New Events:**
- `BackorderCreated` — Customer orders out-of-stock item
- `BackorderFulfilled` — Inventory restocked, backorder shipped

**Workflows:**

1. **Customer Orders Out-of-Stock Item:**
   ```
   Customer → Add DOG-FOOD-01 to cart
   → Proceed to checkout
   → System checks inventory: AvailableQuantity = 0
   
   Option A: Block checkout (current behavior)
   → Display: "Item out of stock. Cannot proceed."
   
   Option B: Allow backorder (enhanced)
   → Display: "Item currently out of stock. Estimated restock date: Feb 25. Proceed with backorder?"
   → Customer accepts → Order placed with BackorderCreated event
   → Email: "Your order will ship when item is back in stock (estimated Feb 25)"
   ```

2. **Restock & Fulfill Backorders:**
   ```
   Warehouse receives shipment → Inventory restocked
   → Event: Inventory.StockReceived (Sku: DOG-FOOD-01, Qty: 100)
   → System queries backorders for DOG-FOOD-01
   → For each backorder (oldest first):
      - Allocate inventory
      - Fulfill order
      - Event: BackorderFulfilled
   ```

**Estimated Effort:** 3-4 sessions

---

### Enhancement 2: Low Stock Alerts (Automated)

**Business Value:** Prevent stockouts by alerting when inventory below reorder point

**New Events:**
- `LowStockDetected` — Available quantity < reorder point

**Workflows:**

1. **Detect Low Stock (Background Job):**
   ```
   System runs hourly job:
   → Query products where AvailableQuantity < ReorderPoint
   → For each product:
      - Event: LowStockDetected
      - Integration: Email purchasing team
   ```

2. **Email Alert:**
   ```
   Subject: "Low Stock Alert: Dog Food (SKU: DOG-FOOD-01)"
   Body: "Current stock: 15 units. Reorder point: 50 units. Please reorder."
   CTA: [Reorder Now] button → Navigate to supplier portal
   ```

**Estimated Effort:** 1-2 sessions

---

## Fulfillment BC Enhancements

### Enhancement 1: Carrier Integration (Real-Time Tracking)

**Business Value:** Accurate delivery estimates, proactive customer notifications

**Current State:** Manual tracking number entry

**Enhanced Flow:**
```
1. Vendor marks as shipped → Tracking number entered
2. System calls carrier API (UPS, FedEx, USPS) → Register webhook
3. Carrier sends updates:
   - Package scanned at origin
   - In transit
   - Out for delivery
   - Delivered
4. For each update:
   → Event: ShipmentStatusUpdated
   → Integration: Customer Experience BC → SSE notification
   → Customer sees real-time updates in UI
```

**APIs:**
- UPS Tracking API
- FedEx Track & Trace API
- USPS Tracking API
- EasyPost (multi-carrier aggregator)

**Estimated Effort:** 4-5 sessions

---

### Enhancement 2: Delivery Failure Handling

**Business Value:** Automate redelivery, return to warehouse, customer notifications

**New Events:**
- `DeliveryAttemptFailed` — Customer unavailable, wrong address
- `RedeliveryScheduled` — Carrier attempts redelivery
- `ReturnedToWarehouse` — Package undeliverable after 3 attempts

**Workflows:**

1. **First Delivery Attempt Failed:**
   ```
   Carrier webhook → DeliveryAttemptFailed
   → Reason: "Customer not home"
   → System: Schedule automatic redelivery (next business day)
   → Event: RedeliveryScheduled
   → Email customer: "Delivery attempt failed. We'll try again tomorrow."
   ```

2. **Package Returned to Warehouse:**
   ```
   After 3 failed attempts:
   → Carrier returns package to warehouse
   → Event: ReturnedToWarehouse
   → Email customer: "Unable to deliver. Please contact us to reschedule or arrange pickup."
   → Options:
      [1] Schedule redelivery (customer updates address)
      [2] Refund order
      [3] Pickup at warehouse
   ```

**Estimated Effort:** 2-3 sessions

---

## Summary Table

| BC | Enhancement | Business Value | Effort | Priority |
|---|---|---|---|---|
| **Shopping** | Wishlist | Gift registries, save for later | 2-3 sessions | Medium |
| **Shopping** | Product Search | Faster discovery | 3-8 sessions | High |
| **Shopping** | Abandoned Cart Recovery | 10-15% revenue recovery | 2-3 sessions | High |
| **Shopping** | Price Drift Handling | Transparency, trust | 2 sessions | Medium |
| **Catalog** | Hierarchical Categories | Better navigation, SEO | 3-4 sessions | High |
| **Catalog** | Product Recommendations | +10-20% AOV | 2-12 sessions | High |
| **Catalog** | Bulk Import/Export | Efficiency for vendors | 2 sessions | Medium |
| **Customer Identity** | Profile Management | Personalization | 1-2 sessions | Low |
| **Customer Identity** | Payment Method Storage | Faster checkout | 2-3 sessions | High |
| **Customer Identity** | Enhanced Address Management | Better UX | 1-2 sessions | Low |
| **Orders** | Order Modification | Customer flexibility | 3-4 sessions | Medium |
| **Orders** | Partial Cancellation | Customer flexibility | 2-3 sessions | Medium |
| **Orders** | Split Shipment Handling | Faster delivery | 3-4 sessions | Medium |
| **Orders** | Reorder Functionality | Convenience, repeat sales | 1-2 sessions | High |
| **Inventory** | Backorder Support | Capture lost sales | 3-4 sessions | High |
| **Inventory** | Low Stock Alerts | Prevent stockouts | 1-2 sessions | High |
| **Fulfillment** | Carrier Integration | Real-time tracking | 4-5 sessions | High |
| **Fulfillment** | Delivery Failure Handling | Automation | 2-3 sessions | Medium |

**Total Estimated Effort:** 45-75 sessions (90-150 hours)

---

## Implementation Prioritization

### Phase 1: High ROI, Low Effort (Cycle 20-22)
1. Product Search (Option A: Simple)
2. Abandoned Cart Recovery
3. Reorder Functionality
4. Low Stock Alerts
5. Payment Method Storage

### Phase 2: High Value, Medium Effort (Cycle 23-25)
1. Hierarchical Categories
2. Product Recommendations (Option A: Co-Purchase)
3. Backorder Support
4. Carrier Integration

### Phase 3: Nice-to-Have (Cycle 26+)
1. Wishlist
2. Order Modification
3. Split Shipment Handling
4. Delivery Failure Handling
5. Bulk Import/Export

---

**Document Owner:** Product Owner (Erik Shafer)  
**Last Updated:** 2026-02-18  
**Status:** 🟢 Ready for Prioritization
