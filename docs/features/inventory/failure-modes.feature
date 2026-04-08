Feature: Failure Modes
  As an inventory management system
  I want to correctly handle short picks, stock discrepancies, backorders, and physical operations
  So that the system remains accurate and recoverable under real-world warehouse conditions

  # Remaster Note (ADR 0060): Failure modes are P1 slices (13-24). They depend on
  # the P0 foundation being in place. Physical pick/ship tracking requires Inventory
  # to subscribe to ItemPicked and ShipmentHandedToCarrier from Fulfillment BC.
  # The ProductInventory aggregate gains a PickedAllocations bucket for tracking
  # items between physical pick and carrier handoff.

  Background:
    Given the inventory system is operational
    And inventory exists for SKU "DOG-FOOD-40LB" at warehouse "NJ-FC":
      | AvailableQuantity | ReservedQuantity | CommittedQuantity | TotalOnHand |
      | 40                | 5                | 5                 | 50          |
    And reservation "res-001" for 5 units is in Committed state for order "ord-001"

  # ============================================================
  # PHYSICAL PICK TRACKING (Slices 13-14)
  # ============================================================

  Scenario: Physical pick — Fulfillment reports item picked successfully
    Given reservation "res-001" is committed for 5 units at "NJ-FC"
    When Fulfillment publishes ItemPicked for order "ord-001":
      | SKU            | WarehouseId | Quantity | OrderId  |
      | DOG-FOOD-40LB  | NJ-FC       | 5        | ord-001  |
    Then Inventory appends "StockPicked" to the NJ-FC ProductInventory stream:
      | ReservationId | Quantity | PickedAt    |
      | res-001       | 5        | (timestamp) |
    And the WarehouseSkuDetailView shows:
      | AvailableQuantity | ReservedQuantity | CommittedQuantity | PickedQuantity | TotalOnHand |
      | 40                | 5                | 0                 | 5              | 50          |
    And TotalOnHand remains 50 (item is still physically in the warehouse)

  Scenario: Physical ship — carrier takes custody of picked items
    Given reservation "res-001" is in Picked state for 5 units
    When Fulfillment publishes ShipmentHandedToCarrier for order "ord-001":
      | ShipmentId | OrderId  | Carrier | TrackingNumber |
      | shp-001    | ord-001  | FedEx   | 1Z999AA10123   |
    Then Inventory appends "StockShipped" to the NJ-FC ProductInventory stream:
      | ReservationId | Quantity | ShipmentId | ShippedAt   |
      | res-001       | 5        | shp-001    | (timestamp) |
    And the WarehouseSkuDetailView shows:
      | AvailableQuantity | ReservedQuantity | CommittedQuantity | PickedQuantity | TotalOnHand |
      | 40                | 5                | 0                 | 0              | 45          |
    And TotalOnHand decreased by 5 (stock has physically left the building)

  Scenario: ItemPicked arrives for reservation already released — no-op
    Given reservation "res-expired" was released 10 minutes ago (order cancelled)
    When Fulfillment publishes ItemPicked referencing the released reservation
    Then the handler detects no matching committed allocation
    And the event is logged as a warning (stale message from Fulfillment)
    And NO StockPicked event is appended (idempotent protection)

  Scenario: ShipmentHandedToCarrier arrives before ItemPicked — out-of-order delivery
    Given reservation "res-002" is in Committed state (not yet picked)
    When Fulfillment publishes ShipmentHandedToCarrier for the reservation
    Then the handler detects no matching PickedAllocation
    And the handler treats this as a combined pick-and-ship (common for small packages)
    And both "StockPicked" and "StockShipped" events are appended atomically
    And TotalOnHand is correctly decremented

  # ============================================================
  # SHORT PICK DETECTION (Slice 15)
  # ============================================================

  Scenario: Short pick — picker finds fewer items than committed
    Given reservation "res-001" is committed for 5 units at "NJ-FC"
    When Fulfillment publishes ItemPicked with Quantity = 3 (short pick: expected 5, found 3)
    Then Inventory appends "StockPicked" with Quantity = 3
    And Inventory appends "StockDiscrepancyFound" with:
      | ExpectedQuantity | ActualQuantity | DiscrepancyType | Description                                |
      | 5                | 3              | ShortPick       | Short pick detected during order fulfillment |
    And an alert is added to the AlertFeedView for the operations manager
    And the 2-unit discrepancy requires investigation (cycle count recommended)

  Scenario: Short pick — zero items found at bin (complete miss)
    Given reservation "res-003" is committed for 2 units of "CAT-TOY-LASER" at "NJ-FC"
    When Fulfillment publishes ItemPicked with Quantity = 0 for "CAT-TOY-LASER"
    Then Inventory appends "StockDiscrepancyFound" with DiscrepancyType = "ZeroPick"
    And the discrepancy is flagged as high severity (complete bin discrepancy)
    And NO StockPicked event is appended (nothing was physically removed)

  # ============================================================
  # CYCLE COUNT AND DISCREPANCY (Slices 20-22)
  # ============================================================

  Scenario: Cycle count — no discrepancy found
    Given inventory for "DOG-FOOD-40LB" at "NJ-FC" shows system quantity:
      | AvailableQuantity | ReservedQuantity | CommittedQuantity | TotalOnHand |
      | 40                | 5                | 5                 | 50          |
    When warehouse clerk initiates a cycle count for "DOG-FOOD-40LB" at "NJ-FC"
    Then a "CycleCountInitiated" event is appended
    When the clerk reports physical count = 50
    Then the system calculates: physical (50) - reserved (5) - committed (5) = 40 (matches available)
    And a "CycleCountCompleted" event is appended with no discrepancy
    And NO InventoryAdjusted event is needed

  Scenario: Cycle count — physical count reveals shortage (theft or unrecorded damage)
    Given system shows TotalOnHand = 50 for "DOG-FOOD-40LB" at "NJ-FC"
    When the clerk reports physical count = 47
    Then the system calculates discrepancy: physical (47) - reserved (5) - committed (5) = 37 available (vs 40 system)
    And a "CycleCountCompleted" event is appended
    And a "StockDiscrepancyFound" event is appended with:
      | ExpectedQuantity | ActualQuantity | DiscrepancyType  |
      | 50               | 47             | CycleCount       |
    And an "InventoryAdjusted" event is appended with AdjustmentQuantity = -3
    And the WarehouseSkuDetailView shows AvailableQuantity = 37
    And the AlertFeedView shows the discrepancy for operations manager review

  Scenario: Cycle count — physical count exceeds system count (found extra stock)
    Given system shows TotalOnHand = 50 for "DOG-FOOD-40LB" at "NJ-FC"
    When the clerk reports physical count = 53
    Then a "CycleCountCompleted" event is appended
    And an "InventoryAdjusted" event is appended with AdjustmentQuantity = +3
    And the WarehouseSkuDetailView shows AvailableQuantity = 43 (40 + 3)

  Scenario: Discrepancy found with in-flight reservations — no disruption
    Given 5 units are reserved for order "ord-001" at "NJ-FC"
    And a cycle count reveals 3 fewer available units than expected
    When the correction adjustment of -3 is applied
    Then the reserved 5 units are NOT affected (reservations are a separate bucket)
    And only AvailableQuantity is reduced by 3
    And if AvailableQuantity would go below 0, the adjustment requires operations manager approval

  # ============================================================
  # DAMAGE AND WRITE-OFF (Slices 23-24)
  # ============================================================

  Scenario: Damage discovered during storage — stock written off
    Given inventory for "CAT-LITTER-20LB" at "OH-FC" has 30 units available
    When warehouse clerk records damage for 2 units:
      | Reason         | Water damage during storage   |
      | RecordedBy     | M. Johnson                    |
    Then a "DamageRecorded" event is appended with:
      | Quantity | DamageReason                  |
      | 2        | Water damage during storage   |
    And an "InventoryAdjusted" event is appended with AdjustmentQuantity = -2
    And the WarehouseSkuDetailView shows AvailableQuantity = 28

  Scenario: Stock write-off — regulatory recall of pet food
    Given inventory for "DOG-FOOD-BRAND-X" at "NJ-FC" has 100 units available
    When operations manager writes off 100 units with reason "Regulatory recall - FDA advisory"
    Then a "StockWrittenOff" event is appended with:
      | Quantity | Reason                           | WrittenOffBy   |
      | 100      | Regulatory recall - FDA advisory | Operations Mgr |
    And an "InventoryAdjusted" event is appended with AdjustmentQuantity = -100
    And the WarehouseSkuDetailView shows AvailableQuantity = 0
    And a "LowStockThresholdBreached" event may fire (if applicable)

  # ============================================================
  # BACKORDER TRACKING (Slices 18-19)
  # ============================================================

  Scenario: Backorder registered — all warehouses have zero stock for a SKU
    Given inventory for "RARE-PET-VITAMIN" shows 0 available at ALL warehouses
    When Fulfillment publishes BackorderCreated for order "ord-backorder-001":
      | OrderId            | ShipmentId  | Items                               |
      | ord-backorder-001  | shp-bo-001  | [ { Sku: RARE-PET-VITAMIN, Qty: 3 } ] |
    Then Inventory appends "BackorderRegistered" on the RARE-PET-VITAMIN ProductInventory stream:
      | OrderId            | ShipmentId  | Quantity |
      | ord-backorder-001  | shp-bo-001  | 3        |
    And the HasPendingBackorders flag is set to true on the aggregate
    And the BackorderImpactView is updated with the affected order details

  Scenario: Stock arrives for backordered SKU — automatic notification to Fulfillment
    Given "RARE-PET-VITAMIN" has pending backorders (HasPendingBackorders = true)
    And the BackorderImpactView shows 2 orders waiting for a total of 8 units
    When a warehouse clerk receives 20 units of "RARE-PET-VITAMIN" from supplier
    Then a "StockReceived" event is appended (AvailableQuantity increases to 20)
    And a "BackorderCleared" event is appended
    And a "BackorderStockAvailable" integration event is published to Fulfillment:
      | Sku                | WarehouseId | AvailableQuantity |
      | RARE-PET-VITAMIN   | NJ-FC       | 20                |
    And Fulfillment can re-attempt routing for the backordered shipments
    And HasPendingBackorders flag is cleared

  Scenario: Customer cancels backordered order — backorder cleared
    Given "RARE-PET-VITAMIN" has a pending backorder for order "ord-backorder-001"
    When the Orders BC cancels the order and sends ReservationReleaseRequested
    Then the backorder registration is cleared for that order
    And if no other backorders remain, HasPendingBackorders is set to false

  Scenario: Multiple backorders for same SKU — all notified when stock arrives
    Given "RARE-PET-VITAMIN" has pending backorders:
      | OrderId            | Quantity |
      | ord-backorder-001  | 3        |
      | ord-backorder-002  | 5        |
    When 20 units are received from supplier
    Then "BackorderStockAvailable" is published once (not per-order)
    And Fulfillment determines priority/FIFO for re-routing the backordered shipments
    And Inventory does NOT decide which order gets stock first (that's Fulfillment's job)

  # ============================================================
  # QUARANTINE (Slices 33-35, P2 — included for scenario completeness)
  # ============================================================

  Scenario: Stock quarantined — suspected quality issue
    Given inventory for "DOG-TREAT-BEEF" at "NJ-FC" has 25 units available
    When a quality inspector quarantines 5 units:
      | Reason          | Suspected contamination — lot #2026-03-15 |
      | QuarantinedBy   | QA Inspector K. Lee                       |
    Then a "StockQuarantined" event is appended
    And an "InventoryAdjusted" event reduces AvailableQuantity by 5
    And the WarehouseSkuDetailView shows AvailableQuantity = 20
    And quarantined stock is NOT available for reservation

  Scenario: Quarantine released — inspection clears the lot
    Given 5 units of "DOG-TREAT-BEEF" at "NJ-FC" are quarantined
    When the quality inspector releases the quarantine (lot passes inspection)
    Then a "QuarantineReleased" event is appended
    And an "InventoryAdjusted" event increases AvailableQuantity by 5
    And the WarehouseSkuDetailView shows AvailableQuantity = 25 (restored)

  Scenario: Quarantine disposed — contamination confirmed
    Given 5 units of "DOG-TREAT-BEEF" at "NJ-FC" are quarantined
    When the quality inspector confirms contamination and orders disposal
    Then a "QuarantineDisposed" event is appended
    And a "StockWrittenOff" event is appended (permanent removal)
    And NO adjustment to AvailableQuantity (already removed during quarantine)
