Feature: Stock Lifecycle
  As an inventory management system
  I want to correctly track stock receipt, restocking, adjustments, and threshold alerts
  So that warehouse quantities remain accurate and operational visibility is maintained

  # Remaster Note (ADR 0060): ProductInventory aggregate uses UUID v5 stream IDs
  # derived from "inventory:{SKU}:{WarehouseId}". MD5-based CombinedGuid is retired.
  # StockReceived, StockRestocked, and TransferReceived are three distinct events
  # with different payloads and audit trails (confirmed by Product Owner).

  Background:
    Given the inventory system is operational
    And the following warehouses are active:
      | Warehouse ID | Name              | Location     |
      | NJ-FC        | NJ Fulfillment    | Newark, NJ   |
      | OH-FC        | OH Fulfillment    | Columbus, OH |
      | WA-FC        | WA Fulfillment    | Kent, WA     |
      | TX-FC        | TX 3PL Partner    | Dallas, TX   |

  # ============================================================
  # INITIALIZATION
  # ============================================================

  Scenario: Initialize inventory for a SKU at a warehouse with UUID v5 stream ID
    When an admin initializes inventory for SKU "DOG-FOOD-40LB" at warehouse "NJ-FC" with quantity 100
    Then an "InventoryInitialized" event is appended to the stream
    And the stream ID is a deterministic UUID v5 derived from "inventory:DOG-FOOD-40LB:NJ-FC"
    And the WarehouseSkuDetailView shows:
      | Field             | Value         |
      | Sku               | DOG-FOOD-40LB |
      | WarehouseId       | NJ-FC         |
      | AvailableQuantity | 100           |
      | TotalOnHand       | 100           |

  Scenario: Initializing the same SKU at a different warehouse creates a separate stream
    Given inventory exists for SKU "DOG-FOOD-40LB" at warehouse "NJ-FC" with 100 units
    When an admin initializes inventory for SKU "DOG-FOOD-40LB" at warehouse "OH-FC" with quantity 50
    Then a separate "InventoryInitialized" event is appended to a different stream
    And the StockAvailabilityView for "DOG-FOOD-40LB" shows:
      | WarehouseId | AvailableQuantity |
      | NJ-FC       | 100               |
      | OH-FC       | 50                |
      | TotalAvailable | 150            |

  Scenario: Duplicate initialization for the same SKU and warehouse is rejected
    Given inventory exists for SKU "DOG-FOOD-40LB" at warehouse "NJ-FC" with 100 units
    When an admin attempts to initialize inventory for SKU "DOG-FOOD-40LB" at warehouse "NJ-FC"
    Then the command is rejected with "Inventory already initialized for this SKU at this warehouse"

  # ============================================================
  # STOCK RECEIPT (Supplier PO)
  # ============================================================

  Scenario: Receive stock from supplier PO — happy path
    Given inventory exists for SKU "CAT-TOY-LASER" at warehouse "NJ-FC" with 20 units available
    When a warehouse clerk receives 80 units of "CAT-TOY-LASER" from supplier "PetCo Wholesale" with PO "PO-2026-0042"
    Then a "StockReceived" event is appended with:
      | Field           | Value            |
      | SupplierId      | PetCo Wholesale  |
      | Quantity         | 80               |
      | PurchaseOrderId  | PO-2026-0042    |
    And the WarehouseSkuDetailView shows AvailableQuantity = 100
    And the StockAvailabilityView for "CAT-TOY-LASER" is updated
    And a "StockReplenished" integration event is published

  Scenario: Receive stock from supplier — short delivery (98 of 100 ordered)
    Given inventory exists for SKU "DOG-LEASH-RED" at warehouse "OH-FC" with 10 units available
    And a PO "PO-2026-0055" expects 100 units of "DOG-LEASH-RED"
    When a warehouse clerk receives 98 units of "DOG-LEASH-RED" from supplier with PO "PO-2026-0055"
    Then a "StockReceived" event is appended with Quantity = 98
    And the WarehouseSkuDetailView shows AvailableQuantity = 108
    And the PO variance (2 units short) is recorded for supplier compliance tracking

  # ============================================================
  # STOCK RESTOCKING (Return Inspection)
  # ============================================================

  Scenario: Restock from return inspection — item is resalable
    Given inventory exists for SKU "PET-BED-LARGE" at warehouse "NJ-FC" with 15 units available
    When the Returns BC publishes that return "ret-abc-123" passed inspection for 2 units of "PET-BED-LARGE" at "NJ-FC"
    Then a "StockRestocked" event is appended with:
      | Field    | Value        |
      | ReturnId | ret-abc-123  |
      | Quantity | 2            |
    And the WarehouseSkuDetailView shows AvailableQuantity = 17
    And the event carries ReturnId for return-rate analytics traceability

  Scenario: Return item NOT restocked — damaged or unsalable
    Given inventory exists for SKU "PET-BED-LARGE" at warehouse "NJ-FC" with 15 units available
    When the Returns BC publishes that return "ret-def-456" failed inspection for 1 unit of "PET-BED-LARGE"
    Then NO "StockRestocked" event is appended
    And the WarehouseSkuDetailView shows AvailableQuantity = 15 (unchanged)
    And the item may be quarantined or written off separately

  # ============================================================
  # MANUAL ADJUSTMENTS
  # ============================================================

  Scenario: Positive manual adjustment — found extra stock during counting
    Given inventory exists for SKU "CAT-BOWL-BLUE" at warehouse "WA-FC" with 25 units available
    When warehouse clerk "J. Martinez" adjusts inventory by +3 with reason "Found on shelf during reorganization"
    Then an "InventoryAdjusted" event is appended with:
      | Field              | Value                                     |
      | AdjustmentQuantity | 3                                         |
      | Reason             | Found on shelf during reorganization      |
      | AdjustedBy         | J. Martinez                               |
    And the WarehouseSkuDetailView shows AvailableQuantity = 28

  Scenario: Negative manual adjustment — damage write-off
    Given inventory exists for SKU "DOG-FOOD-40LB" at warehouse "NJ-FC" with 50 units available
    When warehouse clerk "R. Chen" adjusts inventory by -2 with reason "Water damage during storage"
    Then an "InventoryAdjusted" event is appended with AdjustmentQuantity = -2
    And the WarehouseSkuDetailView shows AvailableQuantity = 48

  Scenario: Negative adjustment rejected when it would make available quantity negative
    Given inventory exists for SKU "CAT-LITTER-20LB" at warehouse "OH-FC" with 5 units available
    And 3 units are reserved for pending orders
    When an admin attempts to adjust by -4
    Then the command is rejected with "Cannot adjust by -4. Available quantity is 5"

  # ============================================================
  # LOW-STOCK THRESHOLD
  # ============================================================

  Scenario: Low-stock threshold breached after adjustment
    Given inventory exists for SKU "DOG-TREAT-BEEF" at warehouse "NJ-FC" with 12 units available
    And the low-stock threshold is 10
    When warehouse clerk adjusts inventory by -5
    Then an "InventoryAdjusted" event is appended
    And a "LowStockThresholdBreached" event is appended (12 → 7, crossed below 10)
    And a "LowStockDetected" integration event is published to Backoffice and Vendor Portal
    And the LowStockAlertView includes "DOG-TREAT-BEEF" at "NJ-FC"

  Scenario: Low-stock threshold NOT breached when already below
    Given inventory exists for SKU "DOG-TREAT-BEEF" at warehouse "NJ-FC" with 8 units available
    And the low-stock threshold is 10
    When warehouse clerk adjusts inventory by -2
    Then an "InventoryAdjusted" event is appended
    But NO "LowStockThresholdBreached" event is appended (was already below threshold)

  Scenario: Stock replenishment clears low-stock alert
    Given inventory exists for SKU "DOG-TREAT-BEEF" at warehouse "NJ-FC" with 5 units available
    And the LowStockAlertView includes "DOG-TREAT-BEEF" at "NJ-FC"
    When 20 units are received from supplier
    Then a "StockReceived" event is appended
    And the WarehouseSkuDetailView shows AvailableQuantity = 25
    And the LowStockAlertView no longer includes "DOG-TREAT-BEEF" at "NJ-FC"
