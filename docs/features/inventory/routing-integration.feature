Feature: Routing Integration
  As the Fulfillment routing engine
  I want to query Inventory for per-warehouse stock availability
  So that I can make informed routing decisions about which warehouse fulfills each order

  # Remaster Note (ADR 0060): The routing integration replaces the hardcoded WH-01
  # in OrderPlacedHandler. Fulfillment owns the routing decision. Inventory provides
  # the stock availability data via an HTTP query surface backed by the inline
  # StockAvailabilityView multi-stream projection.
  # Flow: FulfillmentRequested → Fulfillment queries Inventory → FulfillmentCenterAssigned
  # → StockReservationRequested → Inventory reserves at designated warehouse.

  Background:
    Given the inventory system is operational
    And inventory exists across multiple warehouses:
      | SKU            | Warehouse | Available | Reserved | Committed |
      | DOG-FOOD-40LB  | NJ-FC     | 50        | 5        | 3         |
      | DOG-FOOD-40LB  | OH-FC     | 30        | 0        | 2         |
      | DOG-FOOD-40LB  | WA-FC     | 0         | 0        | 0         |
      | DOG-FOOD-40LB  | TX-FC     | 10        | 0        | 0         |
      | CAT-TOY-LASER   | NJ-FC     | 100       | 10       | 5         |
      | CAT-TOY-LASER   | OH-FC     | 0         | 0        | 0         |

  # ============================================================
  # STOCK AVAILABILITY QUERY
  # ============================================================

  Scenario: Fulfillment routing engine queries stock availability for a SKU
    When the routing engine queries GET /api/inventory/availability/DOG-FOOD-40LB
    Then the response includes per-warehouse availability:
      | WarehouseId | AvailableQuantity |
      | NJ-FC       | 50                |
      | OH-FC       | 30                |
      | WA-FC       | 0                 |
      | TX-FC       | 10                |
    And the response includes TotalAvailable = 90
    And warehouses with zero availability are included (routing engine needs the full picture)

  Scenario: Stock availability reflects real-time reservations (inline projection)
    Given the routing engine queries GET /api/inventory/availability/DOG-FOOD-40LB and sees 50 at NJ-FC
    When a reservation of 10 units is made at NJ-FC
    Then a subsequent query immediately shows 40 available at NJ-FC (inline projection)
    And the routing engine sees the updated availability without any delay

  Scenario: Stock availability for unknown SKU returns empty result
    When the routing engine queries GET /api/inventory/availability/NONEXISTENT-SKU
    Then the response returns an empty warehouse list with TotalAvailable = 0
    And the routing engine can use this to determine the SKU is not stocked anywhere

  # ============================================================
  # END-TO-END ROUTING FLOW
  # ============================================================

  Scenario: Complete routing-informed reservation flow
    Given Fulfillment receives FulfillmentRequested for order "ord-route-001" with:
      | SKU            | Quantity |
      | DOG-FOOD-40LB  | 5        |
    When Fulfillment's routing engine queries Inventory availability for "DOG-FOOD-40LB"
    And the response shows NJ-FC has 50 available, OH-FC has 30, TX-FC has 10
    And the routing engine selects NJ-FC (closest to shipping address in Newark)
    Then Fulfillment publishes FulfillmentCenterAssigned:
      | OrderId      | FulfillmentCenterId | Reason                    |
      | ord-route-001 | NJ-FC              | Closest to destination    |
    And Fulfillment sends StockReservationRequested to Inventory:
      | OrderId       | SKU            | WarehouseId | Quantity | ReservationId  |
      | ord-route-001 | DOG-FOOD-40LB  | NJ-FC       | 5        | res-route-001  |
    And Inventory reserves 5 units at NJ-FC
    And Inventory publishes ReservationConfirmed to Orders BC

  Scenario: Routing selects a warehouse but stock is taken before reservation arrives
    Given Fulfillment's routing engine queries and sees 3 available at WA-FC
    And the routing engine selects WA-FC for 3 units of "DOG-FOOD-40LB"
    But between the query and the reservation, another order reserves all 3 units at WA-FC
    When Fulfillment sends StockReservationRequested for 3 units at WA-FC
    Then Inventory returns ReservationFailed with reason "Insufficient stock"
    And Fulfillment must re-query and re-route to an alternative warehouse
    And this is a known race condition handled by Fulfillment's retry/reroute logic

  # ============================================================
  # MULTI-SKU ORDER ROUTING
  # ============================================================

  Scenario: Multi-SKU order — all SKUs available at the same warehouse
    Given Fulfillment receives FulfillmentRequested for order "ord-multi-001" with:
      | SKU            | Quantity |
      | DOG-FOOD-40LB  | 2        |
      | CAT-TOY-LASER   | 3       |
    When the routing engine queries availability for both SKUs
    And NJ-FC has sufficient stock for both (50 DOG-FOOD, 100 CAT-TOY)
    Then the routing engine selects NJ-FC for a single shipment
    And StockReservationRequested is sent for each SKU at NJ-FC

  Scenario: Multi-SKU order — split across warehouses
    Given Fulfillment receives FulfillmentRequested for order "ord-split-001" with:
      | SKU            | Quantity |
      | DOG-FOOD-40LB  | 2        |
      | CAT-TOY-LASER   | 3       |
    And CAT-TOY-LASER only exists at NJ-FC, DOG-FOOD-40LB only at OH-FC
    When the routing engine determines no single warehouse can fulfill both SKUs
    Then Fulfillment publishes OrderSplitIntoShipments (2 shipments)
    And StockReservationRequested is sent per-SKU per-warehouse:
      | SKU            | WarehouseId | Quantity |
      | DOG-FOOD-40LB  | OH-FC       | 2        |
      | CAT-TOY-LASER   | NJ-FC      | 3        |

  # ============================================================
  # MIGRATION: OrderPlacedHandler REMOVAL
  # ============================================================

  Scenario: Migration Phase 1 — both paths active (dual-publish bridge)
    Given the legacy OrderPlacedHandler is still active
    And the new StockReservationRequested handler is also active
    When an OrderPlaced event arrives from Orders BC
    Then the legacy handler creates reservations at WH-01 (existing behavior)
    And new routing-informed reservations can also be triggered by Fulfillment
    And both paths publish ReservationConfirmed/Failed to Orders (same contract)

  Scenario: Migration Phase 2 — legacy handler removed
    Given the Orders saga now sends FulfillmentRequested before reservation
    And Fulfillment's routing engine is active and queries Inventory availability
    When the OrderPlacedHandler is removed from Inventory
    Then Inventory no longer subscribes to OrderPlaced
    And all reservations flow through StockReservationRequested with routing-informed WarehouseId
    And the RabbitMQ queue for OrderPlaced in Inventory can be decommissioned
