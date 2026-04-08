Feature: Reservation Lifecycle
  As an inventory management system
  I want to manage soft reservations, commits, releases, and expiry
  So that stock is correctly allocated across concurrent orders without overselling

  # Remaster Note (ADR 0060): Reservations now originate from Fulfillment's routing
  # engine via StockReservationRequested (with WarehouseId from routing decision),
  # not from OrderPlacedHandler with hardcoded WH-01.
  # The lifecycle is: Reserved (soft hold) → Committed (hard allocation after payment)
  # → Picked (physical removal from bin) → Shipped (custody transfer to carrier).
  # During migration, OrderPlacedHandler remains active as a dual-publish bridge (Slice 12).

  Background:
    Given the inventory system is operational
    And inventory exists for SKU "DOG-FOOD-40LB" at warehouse "NJ-FC" with 50 units available
    And inventory exists for SKU "DOG-FOOD-40LB" at warehouse "OH-FC" with 30 units available
    And the StockAvailabilityView for "DOG-FOOD-40LB" shows TotalAvailable = 80

  # ============================================================
  # ROUTING-INFORMED RESERVATION (New Flow)
  # ============================================================

  Scenario: Routing-informed reservation — happy path
    Given Fulfillment's routing engine selected "NJ-FC" for order "ord-001"
    When Fulfillment sends StockReservationRequested for order "ord-001":
      | SKU            | WarehouseId | Quantity | ReservationId              |
      | DOG-FOOD-40LB  | NJ-FC       | 5        | res-aaa-111                |
    Then a "StockReserved" event is appended to the NJ-FC ProductInventory stream with:
      | Field          | Value       |
      | OrderId        | ord-001     |
      | ReservationId  | res-aaa-111 |
      | Quantity       | 5           |
    And the WarehouseSkuDetailView for "DOG-FOOD-40LB" at "NJ-FC" shows:
      | AvailableQuantity | ReservedQuantity | TotalOnHand |
      | 45                | 5                | 50          |
    And a "ReservationConfirmed" integration event is published to Orders BC with:
      | Field       | Value       |
      | OrderId     | ord-001     |
      | Sku         | DOG-FOOD-40LB |
      | WarehouseId | NJ-FC       |
      | Quantity    | 5           |
    And the StockAvailabilityView for "DOG-FOOD-40LB" at "NJ-FC" shows AvailableQuantity = 45

  Scenario: Reservation fails — insufficient stock at designated warehouse
    Given inventory for "CAT-TOY-LASER" at "WA-FC" has 2 units available
    When Fulfillment sends StockReservationRequested for order "ord-002":
      | SKU           | WarehouseId | Quantity | ReservationId |
      | CAT-TOY-LASER | WA-FC       | 5        | res-bbb-222   |
    Then NO "StockReserved" event is appended
    And a "ReservationFailed" integration event is published to Orders BC with:
      | Field             | Value                               |
      | OrderId           | ord-002                             |
      | Sku               | CAT-TOY-LASER                       |
      | RequestedQuantity | 5                                   |
      | AvailableQuantity | 2                                   |
      | Reason            | Insufficient stock                  |

  # ============================================================
  # RESERVATION COMMIT
  # ============================================================

  Scenario: Reservation committed after payment — happy path
    Given a reservation "res-aaa-111" exists for 5 units of "DOG-FOOD-40LB" at "NJ-FC"
    When Orders BC sends ReservationCommitRequested for reservation "res-aaa-111"
    Then a "ReservationCommitted" event is appended with ReservationId = "res-aaa-111"
    And the WarehouseSkuDetailView shows:
      | AvailableQuantity | ReservedQuantity | CommittedQuantity | TotalOnHand |
      | 45                | 0                | 5                 | 50          |
    And a "ReservationCommitted" integration event is published to Orders BC

  Scenario: Reservation commit is idempotent — duplicate delivery
    Given reservation "res-aaa-111" has already been committed
    When Orders BC sends ReservationCommitRequested for reservation "res-aaa-111" again
    Then the handler detects the reservation is not in the Reservations dictionary
    And the handler checks CommittedAllocations — finds it already committed
    And NO duplicate "ReservationCommitted" event is appended
    And the operation is a no-op (idempotent)

  Scenario: Commit requested for unknown reservation
    When Orders BC sends ReservationCommitRequested for reservation "res-nonexistent"
    Then the handler returns a 404 ProblemDetails
    And NO events are appended

  # ============================================================
  # RESERVATION RELEASE
  # ============================================================

  Scenario: Reservation released — payment failed
    Given a reservation "res-ccc-333" exists for 3 units of "DOG-FOOD-40LB" at "NJ-FC"
    And AvailableQuantity at "NJ-FC" is 47
    When Orders BC sends ReservationReleaseRequested for reservation "res-ccc-333" with reason "Payment failed"
    Then a "ReservationReleased" event is appended with:
      | ReservationId | res-ccc-333    |
      | Quantity      | 3              |
      | Reason        | Payment failed |
    And the WarehouseSkuDetailView shows AvailableQuantity = 50 (restored)
    And a "ReservationReleased" integration event is published to Orders BC
    And the StockAvailabilityView shows AvailableQuantity restored

  Scenario: Release requested for already-released reservation — idempotent
    Given reservation "res-ccc-333" was already released
    When Orders BC sends ReservationReleaseRequested for reservation "res-ccc-333" again
    Then the handler detects the reservation is not in the Reservations dictionary
    And the operation is a no-op (idempotent)

  # ============================================================
  # RESERVATION EXPIRY
  # ============================================================

  Scenario: Soft-hold reservation expires after timeout window
    Given a reservation "res-ddd-444" exists for 2 units of "CAT-BOWL-BLUE" at "OH-FC"
    And the reservation was created 30 minutes ago
    And the reservation expiry window is 15 minutes
    When the scheduled "ExpireReservation" command fires for reservation "res-ddd-444"
    Then a "ReservationExpired" event is appended with:
      | ReservationId | res-ddd-444 |
      | Reason        | Reservation expired after timeout |
    And AvailableQuantity is restored (same as release)
    And a "ReservationReleased" integration event is published to Orders BC with reason "Expired"
    And the StockAvailabilityView shows available quantity restored

  Scenario: Expiry scheduled message fires but reservation was already committed
    Given reservation "res-ddd-444" was already committed (payment succeeded)
    When the scheduled "ExpireReservation" command fires for reservation "res-ddd-444"
    Then the handler detects the reservation is NOT in the Reservations dictionary
    And the command is a no-op (idempotent — committed reservations don't expire)

  Scenario: Expiry scheduled message fires but reservation was already released
    Given reservation "res-ddd-444" was already released (order cancelled)
    When the scheduled "ExpireReservation" command fires for reservation "res-ddd-444"
    Then the command is a no-op

  # ============================================================
  # CONCURRENT RESERVATION CONFLICT
  # ============================================================

  Scenario: Two orders reserve last 3 units simultaneously — first wins, second fails
    Given inventory for "CAT-TOY-LASER" at "NJ-FC" has exactly 3 units available
    When order "ord-A" requests reservation for 3 units simultaneously with order "ord-B" requesting 3 units
    Then Marten's optimistic concurrency ensures only one succeeds on the first attempt
    And the winning order receives "ReservationConfirmed"
    And the losing order's handler retries (ConcurrencyException retry policy)
    And on retry, AvailableQuantity is now 0
    And the losing order receives "ReservationFailed" with reason "Insufficient stock"

  Scenario: Two orders reserve from a pool of 5 — both can fit
    Given inventory for "CAT-TOY-LASER" at "NJ-FC" has 5 units available
    When order "ord-A" requests 2 units simultaneously with order "ord-B" requesting 3 units
    Then both reservations eventually succeed (with retry on concurrency conflict)
    And the WarehouseSkuDetailView shows AvailableQuantity = 0, ReservedQuantity = 5

  # ============================================================
  # LEGACY BRIDGE (Migration — Slice 12)
  # ============================================================

  Scenario: OrderPlacedHandler bridge — dual-publish during migration
    Given the OrderPlacedHandler legacy bridge is active (migration Phase 1)
    When Orders BC publishes OrderPlaced for order "ord-legacy":
      | SKU            | Quantity |
      | DOG-FOOD-40LB  | 2        |
      | CAT-TOY-LASER  | 1        |
    Then the legacy handler creates ReserveStock commands with warehouse "WH-01"
    And reservations are created at the hardcoded warehouse (legacy behavior preserved)
    And this flow will be removed in migration Phase 2 when Fulfillment routing is active
