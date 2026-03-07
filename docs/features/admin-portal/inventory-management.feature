Feature: Inventory Management
  As a warehouse clerk
  I want to adjust stock levels and receive inbound goods through the Admin Portal
  So that the system reflects physical inventory reality and customers see accurate availability

  Background:
    Given I am logged in to the Admin Portal as a "WarehouseClerk"
    And the system has a warehouse with ID "WH-EAST-01"
    And SKU "CAT-TOY-007" has 5 units available and 2 units reserved at warehouse "WH-EAST-01"
    And the low-stock threshold for SKU "CAT-TOY-007" is 10 units

  Scenario: Warehouse clerk receives inbound stock against a purchase order
    When I navigate to the inventory dashboard for SKU "CAT-TOY-007"
    And I click "Receive Stock"
    And I enter quantity 50, warehouse "WH-EAST-01", and purchase order reference "PO-2026-0042"
    And I click Receive
    Then the Admin Portal shows a success confirmation
    And the Inventory BC records a "StockReplenished" event with quantity 50 and purchase order reference "PO-2026-0042"
    And the available quantity for SKU "CAT-TOY-007" at warehouse "WH-EAST-01" shows 55

  Scenario: Receiving stock clears an active low-stock alert when stock exceeds threshold
    Given there is an active low-stock alert for SKU "CAT-TOY-007"
    When I receive 50 units of SKU "CAT-TOY-007" at warehouse "WH-EAST-01"
    Then the low-stock alert for SKU "CAT-TOY-007" is deactivated
    And connected Admin Portal clients receive a real-time alert dismissal notification

  Scenario: Warehouse clerk adjusts inventory for a cycle count discrepancy
    When I navigate to the inventory dashboard for SKU "CAT-TOY-007"
    And I click "Adjust Inventory"
    And I enter adjustment quantity -2, reason "DamagedGoods", and warehouse "WH-EAST-01"
    And I click Apply Adjustment
    Then the Admin Portal shows a success confirmation with the new available quantity of 3
    And the Inventory BC records an "InventoryAdjusted" event with quantity -2 and reason "DamagedGoods"

  Scenario: Inventory adjustment that drops below low-stock threshold triggers an alert
    When I navigate to the inventory dashboard for SKU "CAT-TOY-007"
    And I apply an adjustment of -3 with reason "DamagedGoods"
    Then the Inventory BC emits a "LowStockDetected" event for SKU "CAT-TOY-007"
    And the Admin Portal shows a low-stock alert notification for SKU "CAT-TOY-007"

  Scenario: Warehouse clerk cannot submit a receive with zero quantity
    When I click "Receive Stock" for SKU "CAT-TOY-007"
    And I enter quantity 0 and purchase order reference "PO-2026-0042"
    And I click Receive
    Then the Admin Portal shows a validation error "Quantity must be greater than zero"
    And no changes are sent to the Inventory BC

  Scenario: Warehouse clerk acknowledges a low-stock alert with a note
    Given there is an active low-stock alert with ID "alert-uuid-001" for SKU "CAT-TOY-007"
    When I navigate to the low-stock alert list
    And I click Acknowledge on alert "alert-uuid-001"
    And I enter the note "Reorder placed, ETA 5 business days"
    And I click Confirm
    Then the Inventory BC records a "LowStockAlertAcknowledged" event with my note
    And the alert is removed from the active alerts list

  Scenario: Warehouse clerk cannot access customer or order data
    When I navigate to the Admin Portal home
    Then I do not see a "Customers" link in the navigation
    And I do not see an "Orders" link in the navigation
    And I do not see a "Pricing" link in the navigation

  Scenario: Operations manager can also receive stock and acknowledge alerts
    Given I am logged in to the Admin Portal as an "OperationsManager"
    When I receive 20 units of SKU "CAT-TOY-007" at warehouse "WH-EAST-01" with PO reference "PO-2026-0099"
    Then the Admin Portal shows a success confirmation
    And the Inventory BC records a "StockReplenished" event attributed to my admin user ID

  @ignore @future
  Scenario: Warehouse clerk scans a barcode to pre-fill the SKU field
    When I click the barcode scan icon on the Receive Stock form
    And I scan the barcode "9781234567897"
    Then the SKU field is pre-filled with the mapped SKU for that barcode
