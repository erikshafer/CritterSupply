Feature: Order Modifications
  As a customer (or customer-service agent acting on the customer's behalf)
  I want to change limited details of an order after it is placed
  So that I can correct mistakes (wrong address) without having to cancel and re-place

  # ─────────────────────────────────────────────
  # Background
  # ─────────────────────────────────────────────
  # The Orders BC permits a narrow set of modifications between order placement and
  # the warehouse hand-off (InventoryCommitted). Modifications after hand-off require
  # a recall / re-pick flow that is out of scope for this slice — see
  # docs/planning/milestones/m45-1-order-post-placement-modifications-gap-memo.md.
  #
  # This feature covers the M45.1 / S4 "address change" vertical slice. Subsequent
  # modification slices (line cancel, quantity change) are tracked separately in the
  # Orders remaster charter.

  Background:
    Given an order has been placed with shipping address "123 Old Street, Springfield, IL"

  # ─────────────────────────────────────────────
  # Happy path — pre-handoff window
  # ─────────────────────────────────────────────

  Scenario: Change shipping address while order is in Placed status
    Given the order is in status "Placed"
    When the customer requests to change the shipping address to "456 New Street, Springfield, IL"
    And the customer provides reason "Typo in original address"
    Then the request is accepted with status 202
    And the order's shipping address is updated to "456 New Street, Springfield, IL"
    And a "ShippingAddressChanged" integration event is published

  Scenario Outline: Change shipping address is allowed in pre-handoff statuses
    Given the order is in status "<status>"
    When the customer requests to change the shipping address to "789 Elm St, Springfield, IL"
    And the customer provides reason "Customer moved"
    Then the request is accepted with status 202
    And a "ShippingAddressChanged" integration event is published

    Examples:
      | status              |
      | Placed              |
      | PendingPayment      |
      | PaymentConfirmed    |
      | InventoryReserved   |
      | OnHold              |

  # ─────────────────────────────────────────────
  # Denial — post-handoff statuses
  # ─────────────────────────────────────────────

  Scenario Outline: Change shipping address is rejected after warehouse hand-off
    Given the order is in status "<status>"
    When the customer requests to change the shipping address to "789 Elm St, Springfield, IL"
    And the customer provides reason "Customer moved"
    Then the request is rejected with status 409
    And the customer receives a message indicating the warehouse has begun picking
    And the order's shipping address is not modified
    And no "ShippingAddressChanged" integration event is published

    Examples:
      | status              |
      | InventoryCommitted  |
      | Fulfilling          |
      | Shipped             |
      | Delivered           |
      | DeliveryFailed      |
      | Reshipping          |
      | Backordered         |

  Scenario: Change shipping address is rejected for cancelled order
    Given the order is in status "Cancelled"
    When the customer requests to change the shipping address to "789 Elm St, Springfield, IL"
    And the customer provides reason "Misclick"
    Then the request is rejected with status 409
    And the customer receives a message indicating the order is cancelled

  # ─────────────────────────────────────────────
  # Validation
  # ─────────────────────────────────────────────

  Scenario: Change shipping address requires a reason
    Given the order is in status "Placed"
    When the customer requests to change the shipping address to "789 Elm St, Springfield, IL"
    And the customer omits the reason
    Then the request is rejected with status 400
    And the response indicates "A reason for the address change is required"

  Scenario: Change shipping address requires a complete address
    Given the order is in status "Placed"
    When the customer requests to change the shipping address with no street value
    And the customer provides reason "Customer moved"
    Then the request is rejected with status 400

  Scenario: Change shipping address rejects unknown order
    Given no order exists with the requested identifier
    When the customer requests to change the shipping address to "789 Elm St, Springfield, IL"
    And the customer provides reason "Customer moved"
    Then the request is rejected with status 404

  # ─────────────────────────────────────────────
  # Idempotency under at-least-once delivery
  # ─────────────────────────────────────────────

  Scenario: Late-arriving address-change message after warehouse hand-off is silently ignored
    # Wolverine guarantees at-least-once delivery. If a ChangeShippingAddress command is
    # delivered after the order has already moved to InventoryCommitted (e.g., a retry), the
    # saga must re-validate eligibility and silently ignore the change rather than mutating
    # the address mid-pick.
    Given the saga has progressed from "InventoryReserved" to "InventoryCommitted"
    And a ChangeShippingAddress command is delivered as a late retry
    Then the saga does not mutate the shipping address
    And no "ShippingAddressChanged" integration event is emitted by the saga
