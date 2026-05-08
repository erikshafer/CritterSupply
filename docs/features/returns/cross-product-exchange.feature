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

  @pending
  # Pending: ExchangePartialRefundIssued integration message is defined and
  # routed for publication, but never constructed by any handler — see
  # m45-1-cross-product-exchange-gap-memo.md "What is missing" row #5.
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

  @pending
  # Pending: no Payments subscriber to ExchangeAdditionalPaymentRequired and
  # no emitter of ExchangeAdditionalPaymentCaptured — see
  # m45-1-cross-product-exchange-gap-memo.md "What is missing" rows #3–4.
  Scenario: Cross-product exchange with more expensive replacement — additional payment required
    When the customer requests an exchange for "Pet Carrier (XL Premium)" with SKU "PET-CAR-XLP" priced at $75.00
    And the replacement item is in stock
    And the replacement costs $25.00 more than the original
    Then the exchange is approved with additional payment required
    And the customer is notified that $25.00 additional payment is needed
    When the customer provides payment for the $25.00 difference
    Then the additional payment is captured
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

  @pending
  # Pending: no refund-of-additional-payment compensation path on
  # inspection rejection — see m45-1-cross-product-exchange-gap-memo.md
  # "What is missing" row #7.
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

  @pending
  # Pending: no ExchangeCancelled command/event for the additional-payment
  # capture-failure compensation path — see
  # m45-1-cross-product-exchange-gap-memo.md "What is missing" row #6.
  Scenario: Additional payment capture fails — exchange cancelled
    When the customer requests an exchange for "Pet Carrier (XL Premium)" with SKU "PET-CAR-XLP" priced at $75.00
    And the replacement costs $25.00 more than the original
    And the exchange is approved with additional payment required
    When the payment capture for $25.00 fails
    Then the exchange is cancelled
    And the customer receives a message: "Payment for price difference could not be processed. Exchange cancelled."
