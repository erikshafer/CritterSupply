Feature: Return Eligibility Window
  As a customer
  I want to know whether my order is eligible for a return
  So that I can submit return requests within the allowed timeframe

  Background:
    Given the return policy window is 30 days from delivery date
    And the following products exist in the catalog:
      | Sku             | ProductName              | IsReturnable | Category     |
      | DOG-BOWL-01     | Ceramic Dog Bowl         | true         | Accessories  |
      | CAT-TOY-05      | Interactive Laser        | true         | Toys         |
      | DOG-FOOD-CUSTOM | Custom Engraved Dog Bowl | false        | Personalized |
      | CAT-FOOD-OPEN   | Opened Cat Food (5lb)    | false        | Consumables  |
      | SALE-ITEM-01    | Final Sale Cat Bed       | false        | Final Sale   |

  Scenario: Return window opens when shipment is delivered
    Given order "order-abc-123" was placed by customer "customer-001"
    And the order contains the following items:
      | OrderLineItemId | Sku          | ProductName      | Quantity |
      | line-item-001   | DOG-BOWL-01  | Ceramic Dog Bowl | 1        |
    When Fulfillment BC publishes "ShipmentDelivered" for order "order-abc-123" on "2026-03-01"
    Then a "ReturnEligibilityEstablished" event should be recorded
    And the return eligibility window should expire on "2026-03-31" (30 days after delivery)
    And a scheduled "ExpireReturnWindow" message should be created for "2026-03-31"

  Scenario: Customer can request return within eligibility window
    Given order "order-abc-123" was delivered on "2026-03-01"
    And today's date is "2026-03-15" (14 days after delivery)
    When customer "customer-001" submits a return request for item "DOG-BOWL-01"
    Then the return request should be accepted
    And a "ReturnRequested" event should be recorded

  Scenario: Return denied when outside the 30-day window
    Given order "order-abc-123" was delivered on "2026-02-01"
    And today's date is "2026-03-10" (37 days after delivery)
    When customer "customer-001" attempts to submit a return request
    Then the return request should be denied
    And the denial reason should be "OutsideReturnWindow"
    And the system should respond with an appropriate error message
    And a "ReturnDenied" integration event should be published

  Scenario: Return denied for personalized item (non-returnable)
    Given order "order-cust-789" was delivered on "2026-03-01"
    And the order contains item "DOG-FOOD-CUSTOM" (non-returnable: personalized)
    And today's date is "2026-03-10" (within return window)
    When customer "customer-001" attempts to return item "DOG-FOOD-CUSTOM"
    Then the return request should be denied
    And the denial reason should be "NonReturnableItem"
    And the system message should explain the item is non-returnable

  Scenario: Return denied for opened consumable (non-returnable)
    Given order "order-food-456" was delivered on "2026-03-01"
    And the order contains item "CAT-FOOD-OPEN" (non-returnable: consumable)
    When customer "customer-001" attempts to return item "CAT-FOOD-OPEN"
    Then the return request should be denied
    And the denial reason should be "NonReturnableItem"

  Scenario: Return denied for final sale item (non-returnable)
    Given order "order-sale-321" was delivered on "2026-03-01"
    And the order contains item "SALE-ITEM-01" (non-returnable: final sale)
    When customer "customer-001" attempts to return item "SALE-ITEM-01"
    Then the return request should be denied
    And the denial reason should be "NonReturnableItem"

  Scenario: Partial return - eligible and non-eligible items in the same order
    Given order "order-mixed-111" was delivered on "2026-03-01"
    And the order contains:
      | OrderLineItemId | Sku             | IsReturnable |
      | line-001        | DOG-BOWL-01     | true         |
      | line-002        | DOG-FOOD-CUSTOM | false        |
    When customer "customer-001" submits a return for only item "DOG-BOWL-01"
    Then the return request should be accepted for "DOG-BOWL-01"
    And a "ReturnRequested" event should be recorded including only the eligible item

  Scenario: Return request rejected when no delivery event exists
    Given order "order-not-delivered" has been placed but never delivered
    When customer "customer-001" attempts to return an item from order "order-not-delivered"
    Then the return request should be denied
    And the denial reason should indicate the order has not been delivered

  Scenario: Return eligibility window on the exact last day
    Given order "order-last-day" was delivered on "2026-03-01"
    And today's date is "2026-03-31" (exactly 30 days after delivery — last eligible day)
    When customer "customer-001" submits a return request
    Then the return request should be accepted (on boundary is within window)

  Scenario: Duplicate return request for already-returned item is rejected
    Given order "order-abc-123" was delivered on "2026-03-01"
    And item "DOG-BOWL-01" from order "order-abc-123" has already been returned and completed
    When customer "customer-001" attempts to submit another return for "DOG-BOWL-01"
    Then the return request should be denied
    And the system should indicate the item has already been returned
