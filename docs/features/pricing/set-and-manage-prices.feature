Feature: Set and Manage Product Prices
  As a Pricing Manager
  I want to set, change, and schedule prices for products
  So that the storefront displays accurate prices and customers pay the right amount

  Background:
    Given the Pricing BC is running
    And the Product Catalog BC has published ProductAdded for "DOG-FOOD-5LB"
    And the Pricing BC has registered SKU "DOG-FOOD-5LB" (Status: Unpriced)

  # ─────────────────────────────────────────────────────────
  # Setting an Initial Price
  # ─────────────────────────────────────────────────────────

  Scenario: Pricing Manager sets an initial price for a newly registered SKU
    Given I am authenticated as a Pricing Manager
    When I set the price for SKU "DOG-FOOD-5LB" to $24.99
    Then the SKU "DOG-FOOD-5LB" should have status "Published"
    And the current price should be $24.99
    And a "PricePublished" integration event should be published for SKU "DOG-FOOD-5LB"

  Scenario: Cannot set a price of zero
    Given I am authenticated as a Pricing Manager
    When I attempt to set the price for "DOG-FOOD-5LB" to $0.00
    Then the request should fail with status code 422
    And the error should indicate "Price must be greater than zero"
    And no events should be appended to the ProductPrice stream

  Scenario: Cannot set a price for an unregistered SKU
    When I attempt to set the price for unknown SKU "UNKNOWN-SKU-99" to $9.99
    Then the request should fail with status code 404
    And the error should indicate "SKU has not been registered in Pricing yet"

  Scenario: Cannot set a price for a discontinued SKU
    Given SKU "DOG-FOOD-5LB" has been discontinued
    When I attempt to set the price for "DOG-FOOD-5LB" to $19.99
    Then the request should fail with status code 400
    And the error should indicate "Cannot price a discontinued product"

  # ─────────────────────────────────────────────────────────
  # Changing an Existing Price
  # ─────────────────────────────────────────────────────────

  Scenario: Pricing Manager changes a price (within normal range)
    Given SKU "DOG-FOOD-5LB" has a published price of $24.99
    When I change the price for "DOG-FOOD-5LB" to $21.99 with reason "Competitive response"
    Then the current price for "DOG-FOOD-5LB" should be $21.99
    And the previous price should be $24.99
    And a "PriceUpdated" integration event should be published
    And the price change percentage was 12% (below the 30% anomaly threshold)

  Scenario: Large price change requires explicit confirmation
    Given SKU "DOG-FOOD-5LB" has a published price of $24.99
    When I attempt to change the price for "DOG-FOOD-5LB" to $14.99
    Then the response should have status code 202
    And the response should indicate "requiresConfirmation: true" with changePercent greater than 30%
    And no PriceChanged event should have been appended yet
    When I resubmit the same change with "confirmAnomaly: true"
    Then the price for "DOG-FOOD-5LB" should be $14.99
    And a "PriceUpdated" integration event should be published

  Scenario: Price change is blocked below floor price
    Given SKU "DOG-FOOD-5LB" has a published price of $24.99 and a floor price of $18.00
    When I attempt to change the price for "DOG-FOOD-5LB" to $15.00
    Then the request should fail with status code 422
    And the error should indicate the price is below the floor price of $18.00

  Scenario: Price change is blocked above ceiling price
    Given SKU "DOG-FOOD-5LB" has a published price of $24.99 and a ceiling price of $35.00
    When I attempt to change the price for "DOG-FOOD-5LB" to $39.99
    Then the request should fail with status code 422
    And the error should indicate the price exceeds the ceiling price of $35.00

  # ─────────────────────────────────────────────────────────
  # Price History (Audit Trail)
  # ─────────────────────────────────────────────────────────

  Scenario: Price history is available for audit
    Given SKU "DOG-FOOD-5LB" has had the following price changes:
      | Price  | Reason               | ChangedBy       |
      | $24.99 | Initial price        | manager-alice   |
      | $21.99 | Competitive response | manager-alice   |
      | $19.99 | Clearance            | manager-bob     |
    When I request the price history for SKU "DOG-FOOD-5LB"
    Then the history should contain 3 entries in chronological order
    And each entry should include the price, reason, actor, and timestamp

  # ─────────────────────────────────────────────────────────
  # Retroactive Price Correction
  # ─────────────────────────────────────────────────────────

  Scenario: Retroactive correction updates the current price and storefront immediately
    Given SKU "DOG-FOOD-5LB" has a published price of $49.99 (entered in error)
    When I submit a price correction for "DOG-FOOD-5LB" to $24.99 with reason "Data entry error"
    Then the current price should be $24.99 immediately (inline projection)
    And a "PriceUpdated" integration event should be published (storefront must update)
    And the price history should contain both the erroneous price and the correction event
    And no marketing notification should be triggered (correction is not a promotional event)
