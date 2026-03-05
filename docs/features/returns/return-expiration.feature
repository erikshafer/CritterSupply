Feature: Return Expiration
  As a system operator
  I want approved returns that are never shipped to be automatically expired
  So that we don't have open return authorizations that never complete

  Background:
    Given the return approval expiry window is 30 days
    And the following return exists in state "Approved":
      | ReturnId      | OrderId       | CustomerId   | ApprovedAt  | ShipByDate  |
      | return-exp-01 | order-abc-123 | customer-001 | 2026-03-01  | 2026-03-31  |
    And the return contains:
      | Sku          | Quantity | ReturnReason |
      | DOG-BOWL-01  | 1        | Defective    |

  Scenario: Scheduled expiry fires when customer never ships return
    Given the current date is "2026-04-01" (31 days after approval)
    And no "ReturnLabelGenerated" or "ReturnShipmentInTransit" events have been recorded
    When the scheduled "ExpireReturn" command fires for return "return-exp-01"
    Then the return status should be "Expired"
    And a "ReturnExpired" event should be recorded in the return stream
    And a "ReturnExpired" integration event should be published
    And no refund should be triggered

  Scenario: Scheduled expiry is cancelled when customer ships within window
    Given the current date is "2026-03-20" (19 days after approval, within window)
    And the customer ships the return with tracking number "1Z999AA10123456784"
    When a "ReturnShipmentInTransit" event is recorded for return "return-exp-01"
    Then the return status should be "InTransit"
    And when the scheduled "ExpireReturn" command fires on "2026-04-01"
    Then the return should NOT transition to "Expired" (already in InTransit state)
    And the expiry command should be a no-op

  Scenario: Expired return cannot be re-approved
    Given return "return-exp-01" is in state "Expired"
    When a customer service agent attempts to approve return "return-exp-01"
    Then the command should be rejected with "Expired returns cannot be reactivated"
    And the return status should remain "Expired"

  Scenario: Customer can submit a new return after expiry (if still within delivery window)
    Given return "return-exp-01" is in state "Expired"
    And today's date is "2026-03-25" (still within the 30-day delivery window)
    When customer "customer-001" submits a new return request for item "DOG-BOWL-01" from order "order-abc-123"
    Then the new return request should be accepted (delivery window still open)
    And a new return stream should be created with a different "ReturnId"

  Scenario: Customer cannot submit new return if delivery window has also closed
    Given return "return-exp-01" is in state "Expired"
    And the order was delivered on "2026-02-01"
    And today's date is "2026-04-15" (more than 30 days after delivery)
    When customer "customer-001" attempts to submit a new return request for order "order-abc-123"
    Then the return request should be denied with reason "OutsideReturnWindow"

  Scenario: Expiry notification is sent to customer
    Given the scheduled "ExpireReturn" command fires for return "return-exp-01"
    When the return expires
    Then a "ReturnExpired" integration event should be published
    And the Notifications BC should receive the event and send an expiry email to the customer

  Scenario: Multiple approved returns — only the unshipped one expires
    Given the following returns exist in state "Approved":
      | ReturnId      | OrderId       | ApprovedAt | ShipByDate |
      | return-exp-01 | order-abc-123 | 2026-03-01 | 2026-03-31 |
      | return-exp-02 | order-xyz-456 | 2026-03-15 | 2026-04-14 |
    And return "return-exp-01" has NOT been shipped
    And return "return-exp-02" HAS been shipped (status: InTransit)
    When the scheduled "ExpireReturn" command fires for "return-exp-01" on "2026-04-01"
    Then return "return-exp-01" status should be "Expired"
    And return "return-exp-02" status should remain "InTransit"
