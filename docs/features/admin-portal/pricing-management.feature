Feature: Pricing Management
  As a pricing manager
  I want to set and schedule product prices through the Admin Portal
  So that price changes are applied accurately, on time, and with full audit history — without requiring a code deployment

  Background:
    Given I am logged in to the Admin Portal as a "PricingManager"
    And the Pricing BC is active and holds price data for all products
    And SKU "DOG-FOOD-LG" has a current base price of $24.99

  Scenario: Pricing manager sets a new base price immediately
    When I navigate to the pricing dashboard for SKU "DOG-FOOD-LG"
    And I click "Set Base Price"
    And I enter the new price $21.99 with reason "Supplier cost reduction — Q2 2026"
    And I click Apply
    Then the Admin Portal shows a success confirmation
    And the Pricing BC records a "BasePriceSet" event attributed to my admin user ID
    And the pricing dashboard shows the current base price as $21.99
    And new add-to-cart operations for SKU "DOG-FOOD-LG" use the price $21.99

  Scenario: Pricing manager schedules a future sale price
    When I navigate to the pricing dashboard for SKU "DOG-FOOD-LG"
    And I click "Schedule Price Change"
    And I enter:
      | Field        | Value                          |
      | New price    | $19.99                         |
      | Effective at | 2026-11-28 00:00:00 UTC        |
      | Expires at   | 2026-12-02 23:59:59 UTC        |
      | Reason       | Black Friday 2026 sale         |
    And I click Schedule
    Then the Admin Portal shows a success confirmation
    And the Pricing BC records a "PriceChangeScheduled" event with the effective and expiry timestamps
    And the pricing dashboard shows an upcoming scheduled change for SKU "DOG-FOOD-LG"

  Scenario: Pricing manager cancels a scheduled price change before it takes effect
    Given there is a scheduled price change for SKU "DOG-FOOD-LG" with ID "schedule-uuid-001"
    And the scheduled effective date is in the future
    When I click Cancel on the scheduled price change "schedule-uuid-001"
    And I confirm cancellation
    Then the Pricing BC records a "PriceChangeCancelled" event
    And the scheduled change is no longer shown in the pricing dashboard

  Scenario: Pricing manager cannot set a price of zero or negative
    When I attempt to set the base price for SKU "DOG-FOOD-LG" to $0.00
    Then the Admin Portal shows a validation error "Price must be greater than zero"
    And no changes are sent to the Pricing BC

  Scenario: Pricing manager cannot schedule a change where expires-at is before effective-at
    When I schedule a price change for SKU "DOG-FOOD-LG" with:
      | Field        | Value                    |
      | Effective at | 2026-12-01 00:00:00 UTC  |
      | Expires at   | 2026-11-30 23:59:59 UTC  |
    Then the Admin Portal shows a validation error "Expiry must be after effective date"
    And no changes are sent to the Pricing BC

  Scenario: Pricing manager views price history for a product
    When I navigate to the pricing dashboard for SKU "DOG-FOOD-LG"
    And I click "View Price History"
    Then I see a chronological list of price changes with:
      | Column       | Description                            |
      | Price        | The price that was set                 |
      | Effective at | When the price took effect             |
      | Set by       | The admin user who made the change     |
      | Reason       | The reason recorded at time of change  |

  Scenario: Price history cannot be modified — it is immutable audit data
    Given there is a price history entry from yesterday
    When I view the price history for SKU "DOG-FOOD-LG"
    Then I do not see any "Edit" or "Delete" buttons on historical price entries

  Scenario: Pricing manager cannot access product descriptions or inventory
    When I navigate to the Admin Portal home
    Then I do not see a "Product Content" link in the navigation
    And I do not see an "Inventory" link in the navigation
    And I do not see a "Customers" link in the navigation

  Scenario: Non-PricingManager role cannot set prices
    Given I am logged in to the Admin Portal as a "CopyWriter"
    When I attempt to set the base price for SKU "DOG-FOOD-LG" via the API
    Then the Admin Portal API returns 403 Forbidden
    And no changes are sent to the Pricing BC

  @ignore @future
  Scenario: Pricing manager receives a real-time notification when a scheduled price change activates
    Given I have a scheduled price change for SKU "DOG-FOOD-LG" effective in 1 minute
    When the scheduled time arrives and the Pricing BC activates the change
    Then I receive a real-time SignalR notification "Price change activated for DOG-FOOD-LG: $19.99"
