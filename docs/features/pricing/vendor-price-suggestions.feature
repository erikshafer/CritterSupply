Feature: Vendor Price Suggestions
  As a Vendor
  I want to submit suggested retail price changes for my products
  So that I can propose passing cost savings to customers and maintain fair pricing

  As a Pricing Manager
  I want to review and approve or reject vendor price suggestions
  So that all price changes are validated against business rules before going live

  Background:
    Given the Pricing BC is running
    And the Vendor Portal BC is running
    And SKU "DOG-FOOD-5LB" has a published price of $21.99
    And "DOG-FOOD-5LB" is associated with Vendor "Acme Pet Supplies" (VendorId: "acme-guid")

  # ─────────────────────────────────────────────────────────
  # Vendor Submits a Price Suggestion
  # ─────────────────────────────────────────────────────────

  Scenario: Vendor Portal publishes a price suggestion and Pricing BC receives it
    When the Vendor Portal publishes a "VendorPriceSuggestionSubmitted" event:
      | SuggestionId  | Sku          | VendorId   | SuggestedPrice | Justification            |
      | suggestion-01 | DOG-FOOD-5LB | acme-guid  | $22.50         | New lower supplier cost  |
    Then a "VendorPriceSuggestionReceived" event should be appended to a new VendorPriceSuggestion stream
    And the PendingPriceSuggestionsView should contain the suggestion with Status "Pending"
    And the suggestion should have an expiry of 7 business days from now

  Scenario: Suggestion flagged when price is below the floor price
    Given "DOG-FOOD-5LB" has a floor price of $18.00
    When the Vendor Portal publishes a price suggestion for "DOG-FOOD-5LB" at $14.00
    Then the suggestion is received with Status "Pending"
    And the PendingPriceSuggestionsView should mark "SuggestedPriceBelowFloor: true"
    And the reviewer sees a prominent floor-price violation flag in the review queue

  # ─────────────────────────────────────────────────────────
  # Pricing Manager Approves a Suggestion
  # ─────────────────────────────────────────────────────────

  Scenario: Pricing Manager approves a vendor price suggestion
    Given a pending suggestion exists for "DOG-FOOD-5LB" with SuggestedPrice $22.50 (SuggestionId: "suggestion-01")
    And I am authenticated as a Pricing Manager
    When I approve suggestion "suggestion-01" with ApprovedPrice $22.00
    Then a "VendorPriceSuggestionApproved" event should be appended to the suggestion stream
    And the suggestion Status should be "Approved"
    And a "PriceChanged" event should be appended to the "DOG-FOOD-5LB" ProductPrice stream
    And the current price for "DOG-FOOD-5LB" should be $22.00
    And a "PriceUpdated" integration event should be published to the Shopping BC and BFF
    And the Vendor Portal should receive notification that the suggestion was approved

  Scenario: Manager can approve at a different price than suggested
    Given a pending suggestion of $22.50 for "DOG-FOOD-5LB" (SuggestionId: "suggestion-01")
    When I approve suggestion "suggestion-01" with ApprovedPrice $21.00 (different from $22.50)
    Then the current price for "DOG-FOOD-5LB" should be $21.00 (not $22.50)
    And the approval event records ApprovedPrice: $21.00 for the audit trail

  # ─────────────────────────────────────────────────────────
  # Pricing Manager Rejects a Suggestion
  # ─────────────────────────────────────────────────────────

  Scenario: Pricing Manager rejects a vendor price suggestion
    Given a pending suggestion exists for "DOG-FOOD-5LB" (SuggestionId: "suggestion-01")
    And I am authenticated as a Pricing Manager
    When I reject suggestion "suggestion-01" with reason "Price increase conflicts with current clearance strategy"
    Then a "VendorPriceSuggestionRejected" event should be appended to the suggestion stream
    And the suggestion Status should be "Rejected"
    And no "PriceChanged" event should be appended to the ProductPrice stream
    And the current price for "DOG-FOOD-5LB" should remain $21.99 (unchanged)
    And the Vendor Portal should receive notification that the suggestion was rejected with the reason

  # ─────────────────────────────────────────────────────────
  # Suggestion Lifecycle
  # ─────────────────────────────────────────────────────────

  Scenario: Approved suggestion cannot be reviewed again (terminal state)
    Given suggestion "suggestion-01" has already been approved
    When I attempt to reject suggestion "suggestion-01"
    Then the request should fail with status code 409
    And the error should indicate "This suggestion has already been resolved"

  Scenario: Suggestion auto-expires after 7 business days with no review
    Given a suggestion "suggestion-01" was submitted more than 7 business days ago with no review action
    # Business day calculation: weekdays only, no holiday calendar for Phase 1 (ISO 8601 week days Mon–Fri)
    When the expiry background job runs
    Then a "VendorPriceSuggestionExpired" event should be appended
    And the suggestion Status should be "Expired"
    And it should be removed from the active review queue
    And the Vendor Portal should be notified of the expiry

  Scenario: Approved price still validated against floor price at review time
    Given "DOG-FOOD-5LB" has a floor price of $18.00
    And a pending suggestion for "DOG-FOOD-5LB" at $22.50 was submitted when no floor existed
    And the floor price was subsequently raised to $23.00
    When I attempt to approve the suggestion at the suggested price of $22.50
    Then the request should fail with status code 422
    And the error should indicate the approved price is below the current floor price of $23.00

  # ─────────────────────────────────────────────────────────
  # Resilience
  # ─────────────────────────────────────────────────────────

  Scenario: VendorPriceSuggestionSubmitted message is handled idempotently (at-least-once delivery)
    When the Vendor Portal publishes "VendorPriceSuggestionSubmitted" for SuggestionId "suggestion-01" twice
    Then only one VendorPriceSuggestion stream should exist for "suggestion-01"
    And the PendingPriceSuggestionsView should contain exactly one entry for "suggestion-01"
