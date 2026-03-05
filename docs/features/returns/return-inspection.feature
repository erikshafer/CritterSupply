Feature: Return Inspection Workflow
  As a warehouse inspector
  I want to record inspection results for returned items
  So that the system can determine disposition and trigger the appropriate downstream actions

  Background:
    Given the following return exists in state "Received":
      | ReturnId      | OrderId       | CustomerId    | Status   |
      | return-001    | order-abc-123 | customer-001  | Received |
    And the return contains the following items:
      | OrderLineItemId | Sku          | ProductName       | Quantity | ReturnReason |
      | line-item-001   | DOG-BOWL-01  | Ceramic Dog Bowl  | 2        | Defective    |
      | line-item-002   | CAT-TOY-05   | Interactive Laser | 1        | Unwanted     |
    And inspector "inspector-w01" is logged in

  Scenario: Inspection passes - all items in acceptable condition and restockable
    Given I am inspecting return "return-001"
    When I start the inspection
    Then the return status should be "Inspecting"

    When I complete the inspection with the following results:
      | Sku          | Condition    | ConditionNotes                        | Restockable | WarehouseLocation |
      | DOG-BOWL-01  | AsExpected   | Matches reported defect, non-sellable | false       | DISPOSE-01        |
      | CAT-TOY-05   | AsExpected   | Original packaging intact             | true        | A-12-3            |
    Then the return status should be "Completed"
    And an "InspectionPassed" event should be recorded in the return stream
    And a "ReturnCompleted" event should be recorded in the return stream
    And a "ReturnCompleted" integration message should be published
    And the integration message should include:
      | Sku          | Quantity | IsRestockable | RestockCondition |
      | DOG-BOWL-01  | 2        | false         | Damaged          |
      | CAT-TOY-05   | 1        | true          | LikeNew          |

  Scenario: Inspection passes - better than expected condition
    When I complete the inspection with the following results:
      | Sku          | Condition          | ConditionNotes                  | Restockable | WarehouseLocation |
      | DOG-BOWL-01  | BetterThanExpected | No visible defect found         | true        | A-15-2            |
      | CAT-TOY-05   | AsExpected         | Normal wear                     | true        | A-12-3            |
    Then the return status should be "Completed"
    And all returned items should be marked restockable
    And the "ReturnCompleted" integration message should have "IsRestockable: true" for all items

  Scenario: Inspection fails - customer-caused damage (Dispose disposition)
    When I complete the inspection with the following results:
      | Sku          | Condition          | ConditionNotes                         | Restockable | Disposition |
      | DOG-BOWL-01  | WorseThanExpected  | Screen cracked, water damage visible   | false       | Dispose     |
      | CAT-TOY-05   | WorseThanExpected  | Chewed and broken, unsellable          | false       | Dispose     |
    Then the return status should be "Rejected"
    And an "InspectionFailed" event should be recorded with disposition "Dispose"
    And a "ReturnRejected" integration message should be published
    And no refund should be triggered

  Scenario: Inspection fails - wrong item returned (ReturnToCustomer disposition)
    When I complete the inspection with the following results:
      | Sku          | Condition         | ConditionNotes                      | Restockable | Disposition      |
      | DOG-BOWL-01  | WorseThanExpected | Wrong SKU received: DOG-LEASH-02    | false       | ReturnToCustomer |
    Then the return status should be "Rejected"
    And an "InspectionFailed" event should be recorded with disposition "ReturnToCustomer"
    And a "ReturnRejected" integration message should be published

  Scenario: Inspection fails - item quarantined for further review
    When I complete the inspection with the following results:
      | Sku          | Condition         | ConditionNotes                   | Restockable | Disposition |
      | DOG-BOWL-01  | WorseThanExpected | Safety concern - unusual odor    | false       | Quarantine  |
    Then the return status should be "Rejected"
    And an "InspectionFailed" event should be recorded with disposition "Quarantine"

  Scenario: Inspection cannot begin before return is received
    Given a return "return-not-received" is in state "Approved"
    When inspector tries to start inspection on return "return-not-received"
    Then the command should be rejected with "Return must be in Received state to begin inspection"

  Scenario: Partial inspection - item not received (customer never included it)
    When I complete the inspection with the following results:
      | Sku          | Condition    | ConditionNotes                          | Restockable | Disposition       |
      | DOG-BOWL-01  | AsExpected   | Received, condition good                | false       | Dispose           |
      | CAT-TOY-05   | NotReceived  | Package did not contain this item       | false       | ReturnToCustomer  |
    Then the return status should be "Completed"
    And the "ReturnCompleted" integration message should only include received items
    And the "FinalRefundAmount" should reflect only the items that were received
