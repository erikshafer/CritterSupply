Feature: Cross-Product Exchange
  As a customer
  I want to exchange an item for a different product
  So that I can get a more suitable product without a separate return and purchase

  # ─────────────────────────────────────────────
  # Honesty pass (M46.0/A) — scenarios marked @pending below are
  # specified by the PO but NOT yet end-to-end implementable. The
  # Returns BC owns its own state machine for them, but the cross-BC
  # choreography (Inventory replacement reservation, Payments delta
  # capture/refund, Orders saga consumption of cross-product exchange
  # events) does not exist yet. See:
  #   docs/planning/milestones/m45-1-cross-product-exchange-gap-memo.md
  # Removing @pending without first landing the cross-BC work in that
  # memo will turn these scenarios into vapourware tests — green on
  # the Returns side while customers in production see the failure
  # modes the memo enumerates ("never charged for upcharge", "never
  # refunded the difference", "no replacement reservation").
  # ─────────────────────────────────────────────

  Background:
    Given an order has been delivered 10 days ago
    And the order contains a "Pet Carrier (Medium)" item with SKU "PET-CAR-M" priced at $50.00
    And the item is eligible for return

  # ─────────────────────────────────────────────
  # Happy Path — Cross-Product Exchange
  # ─────────────────────────────────────────────

  Scenario: Cross-product exchange with same-price replacement
    When the customer requests an exchange for "Pet Bed (Large)" with SKU "PET-BED-L" priced at $50.00
    And the replacement item is in stock
    And the replacement price equals the original price
    Then the exchange is approved
    And a return shipping label is generated
    And the customer is notified to ship by 30 days from now
    When the customer ships the original item
    And the warehouse receives and inspects the item
    And the inspection passes
    Then the replacement "Pet Bed (Large)" is shipped
    And the exchange is marked completed
    And no refund or additional charge is issued

  # Closed in M47.0 / Slice 2 — Returns ↔ Payments choreography lands the
  # partial refund. See ADR 0062 and docs/planning/milestones/m47-0-plan.md.
  Scenario: Cross-product exchange with cheaper replacement — partial refund issued
    When the customer requests an exchange for "Pet Mat (Small)" with SKU "PET-MAT-S" priced at $30.00
    And the replacement item is in stock
    And the replacement costs $20.00 less than the original
    Then the exchange is approved
    And the customer is notified of expected $20.00 refund upon completion
    When the customer ships the original item
    And the warehouse receives and inspects the item
    And the inspection passes
    Then the replacement "Pet Mat (Small)" is shipped
    And a $20.00 partial refund is issued to the original payment method
    And the exchange is marked completed

  # Closed in M47.0 / Slice 2 — Returns ↔ Payments choreography lands the
  # delta capture. See ADR 0062 and docs/planning/milestones/m47-0-plan.md.
  #
  # Deviation from the original M35.0 / S4 spec: there is no in-flow
  # "customer provides payment" interstitial — the customer's existing
  # payment method (the one used for the original order) is reused for
  # the upcharge capture. PO sign-off is pending; see ADR 0062 §Deviation
  # for the rationale.
  Scenario: Cross-product exchange with more expensive replacement — additional payment captured
    When the customer requests an exchange for "Pet Carrier (XL Premium)" with SKU "PET-CAR-XLP" priced at $75.00
    And the replacement item is in stock
    And the replacement costs $25.00 more than the original
    Then the exchange is approved with additional payment required
    And the $25.00 upcharge is captured against the original payment method
    And the customer is notified that the $25.00 upcharge has been billed
    And the customer is notified to ship the original item by 30 days from now
    When the customer ships the original item
    And the warehouse receives and inspects the item
    And the inspection passes
    Then the replacement "Pet Carrier (XL Premium)" is shipped
    And the exchange is marked completed

  # ─────────────────────────────────────────────
  # Denial Scenarios
  # ─────────────────────────────────────────────

  # Closed in M47.0 / Slice 1 — Inventory replacement reservation
  # choreography is now end-to-end implementable. See ADR 0061 and
  # docs/planning/milestones/m47-0-plan.md. The Returns BC subscribes
  # to Inventory's ReplacementReservationFailed reply and transitions
  # the exchange to Denied with the customer-facing wording below.
  Scenario: Cross-product exchange denied — replacement out of stock
    When the customer requests an exchange for "Pet Bed (Large)" with SKU "PET-BED-L"
    And the replacement item is out of stock
    Then the exchange is denied
    And the customer receives a message: "Replacement item currently unavailable. Please request a refund or try again later."

  Scenario: Cross-product exchange denied — outside return window
    Given an order was delivered 35 days ago
    When the customer requests a cross-product exchange
    Then the exchange is denied
    And the customer receives a message: "Return window has expired (30 days from delivery)."

  # ─────────────────────────────────────────────
  # Inspection and Failure Scenarios
  # ─────────────────────────────────────────────

  Scenario: Cross-product exchange rejected — original item fails inspection
    When the customer requests an exchange for "Pet Bed (Large)" with SKU "PET-BED-L" priced at $50.00
    And the replacement is in stock
    And the exchange is approved
    When the customer ships the original item
    And the warehouse receives the original item
    And the inspection fails due to customer-caused damage
    Then the exchange is rejected
    And the customer is notified: "Item condition does not qualify for exchange. Return rejected."
    And no replacement is shipped
    And no refund is issued

  # Closed in M47.0 / Slice 4 — Returns ↔ Payments compensation path
  # refunds the captured additional-payment delta when inspection rejects
  # a cross-product exchange. The Returns BC's SubmitInspection handler
  # publishes Payments.RefundExchangeDeltaRequested when
  # IsCrossProductExchange && AdditionalPaymentCaptured; the Payments BC's
  # RefundExchangeDeltaHandler refunds against the deterministic
  # delta-Payment stream and replies via the existing
  # ExchangePartialRefundIssued contract (Storefront already renders it).
  Scenario: Cross-product exchange with additional payment rejected — refund payment difference
    When the customer requests an exchange for "Pet Carrier (XL Premium)" with SKU "PET-CAR-XLP" priced at $75.00
    And the replacement costs $25.00 more than the original
    And the exchange is approved with additional payment required
    And the customer provides payment for the $25.00 difference
    When the customer ships the original item
    And the inspection fails due to customer-caused damage
    Then the exchange is rejected
    And the $25.00 additional payment is refunded to the customer
    And no replacement is shipped

  # ─────────────────────────────────────────────
  # Edge Cases
  # ─────────────────────────────────────────────

  Scenario: Cross-product exchange expires — customer never ships original
    When the customer requests an exchange for "Pet Bed (Large)" with SKU "PET-BED-L"
    And the replacement is in stock
    And the exchange is approved
    And the customer is notified to ship by 30 days from now
    When 30 days pass without carrier scan
    Then the exchange expires
    And the customer is notified: "Exchange expired — original item was not shipped within 30 days."

  # Closed in M47.0 / Slice 4 — Returns ↔ Payments compensation path
  # transitions the Return to the new terminal Cancelled status when the
  # additional-payment capture fails. The Returns BC's
  # ExchangeDeltaCaptureFailedHandler appends ExchangeCancelled, publishes
  # Returns.ExchangeCancelled (Storefront notification), and releases the
  # held replacement reservation in Inventory.
  Scenario: Additional payment capture fails — exchange cancelled
    When the customer requests an exchange for "Pet Carrier (XL Premium)" with SKU "PET-CAR-XLP" priced at $75.00
    And the replacement costs $25.00 more than the original
    And the exchange is approved with additional payment required
    When the payment capture for $25.00 fails
    Then the exchange is cancelled
    And the customer receives a message: "Payment for price difference could not be processed. Exchange cancelled."
