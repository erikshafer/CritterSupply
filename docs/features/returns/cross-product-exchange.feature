Feature: Cross-Product Exchange
  As a customer
  I want to exchange an item for a different product
  So that I can get a more suitable product without a separate return and purchase

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

  Scenario: Additional payment capture fails — exchange cancelled
    When the customer requests an exchange for "Pet Carrier (XL Premium)" with SKU "PET-CAR-XLP" priced at $75.00
    And the replacement costs $25.00 more than the original
    And the exchange is approved with additional payment required
    When the payment capture for $25.00 fails
    Then the exchange is cancelled
    And the customer receives a message: "Payment for price difference could not be processed. Exchange cancelled."
