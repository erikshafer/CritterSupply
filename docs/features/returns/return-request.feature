Feature: Return Request Workflow
  As a customer
  I want to request a return for purchased items
  So that I can get a refund for defective or unwanted products

  Background:
    Given the following order exists:
      | OrderId      | CustomerId   | DeliveredAt  | Status    |
      | order-abc-123| customer-001 | 2026-02-01   | Delivered |
    And the order contains the following items:
      | OrderLineItemId | Sku          | ProductName       | Quantity | Price  |
      | line-item-001   | DOG-BOWL-01  | Ceramic Dog Bowl  | 2        | $19.99 |
      | line-item-002   | CAT-TOY-05   | Interactive Laser | 1        | $29.99 |
    And today's date is "2026-02-15" (14 days after delivery)
    And the return window is 30 days from delivery

  Scenario: Customer requests return for defective item - full workflow
    Given I am logged in as customer "customer-001"
    When I navigate to my order history
    And I select order "order-abc-123"
    And I click "Request Return"
    And I select item "DOG-BOWL-01" with reason "Defective"
    And I submit the return request
    Then the return should be approved with prepaid label
    And I should receive refund of $39.98 after inspection
    And inventory should be restocked (2 units of DOG-BOWL-01)

  Scenario: Return with restocking fee (unwanted item)
    When I return item "CAT-TOY-05" with reason "Unwanted"
    Then a 15% restocking fee should be applied
    And I should receive refund of $25.49 ($29.99 - $4.50)
    And I should pay return shipping costs

  Scenario: Return denied (outside 30-day window)
    Given today's date is "2026-03-10" (37 days after delivery)
    When I attempt to request a return
    Then the return should be denied with reason "OutsideReturnWindow"
    And I should see message "Order delivered more than 30 days ago"

  Scenario: Return rejected after inspection (customer damage)
    When inspector finds item damaged by customer
    Then return should be rejected
    And store credit of $15.00 (50% goodwill) should be offered
