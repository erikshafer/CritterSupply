Feature: Scheduled Price Changes
  As a Pricing Manager
  I want to schedule future price changes
  So that promotional and campaign prices go live automatically without manual intervention

  Background:
    Given the Pricing BC is running
    And SKU "DOG-FOOD-5LB" has a published price of $24.99
    And I am authenticated as a Pricing Manager

  # ─────────────────────────────────────────────────────────
  # Scheduling a Future Price Change
  # ─────────────────────────────────────────────────────────

  Scenario: Pricing Manager schedules a future price change
    When I schedule a price change for "DOG-FOOD-5LB" to $19.99 effective in 7 days
    Then a "PriceChangeScheduled" event should be appended to the ProductPrice stream
    And the current price should still be $24.99 (schedule is pending, not active)
    And the CurrentPriceView should show "HasPendingSchedule: true" with the scheduled date
    And the ScheduledChangesView should contain a new entry with Status "Pending"
    And a Wolverine durable scheduled message should be queued for activation

  Scenario: Scheduled price activates automatically at the scheduled time
    Given I have scheduled a price change for "DOG-FOOD-5LB" to $19.99 for 2 seconds from now
    When the scheduled time arrives
    Then a "ScheduledPriceActivated" event should be appended to the ProductPrice stream
    And the current price for "DOG-FOOD-5LB" should be $19.99
    And the previous price should be $24.99
    And a "PriceUpdated" integration event should be published
    And the ScheduledChangesView entry should have Status "Activated"

  Scenario: Scheduled price survives a process restart and activates on recovery
    Given I have scheduled a price change for "DOG-FOOD-5LB" to $19.99 for 2 seconds from now
    And the Pricing.Api service restarts before the scheduled time
    When the scheduled time arrives after restart
    Then the price change should still activate correctly
    And a "ScheduledPriceActivated" event should be in the ProductPrice stream

  # ─────────────────────────────────────────────────────────
  # Cancelling a Scheduled Change
  # ─────────────────────────────────────────────────────────

  Scenario: Pricing Manager cancels a pending scheduled change
    Given I have scheduled a price change for "DOG-FOOD-5LB" to $19.99 in 7 days (ScheduleId: "schedule-guid-A")
    When I cancel the scheduled change "schedule-guid-A" for "DOG-FOOD-5LB"
    Then a "PriceChangeScheduleCancelled" event should be appended
    And the CurrentPriceView should show "HasPendingSchedule: false"
    And the ScheduledChangesView entry should have Status "Cancelled"
    And the current price should remain $24.99

  Scenario: Cancelled schedule is silently discarded when Wolverine fires it
    Given I scheduled and then cancelled a price change (ScheduleId: "schedule-guid-A")
    When the Wolverine scheduled message fires (activation attempt for "schedule-guid-A")
    Then no "ScheduledPriceActivated" event should be appended
    And the current price should remain unchanged at $24.99
    And no "PriceUpdated" integration event should be published

  Scenario: Scheduled activation is idempotent under at-least-once delivery
    Given I have scheduled a price change for "DOG-FOOD-5LB" to $19.99
    And the scheduled change has already been activated (ScheduledPriceActivated appended)
    When the Wolverine scheduled message is delivered a second time (at-least-once retry)
    Then no additional "ScheduledPriceActivated" event should be appended
    And only one "PriceUpdated" integration event should have been published

  # ─────────────────────────────────────────────────────────
  # Invariants and Sad Paths
  # ─────────────────────────────────────────────────────────

  Scenario: Cannot schedule a price change in the past
    When I attempt to schedule a price change for "DOG-FOOD-5LB" to $19.99 effective yesterday
    Then the request should fail with status code 422
    And the error should indicate "Scheduled time must be in the future"

  Scenario: Cannot schedule a price below the floor price
    Given "DOG-FOOD-5LB" has a floor price of $18.00
    When I attempt to schedule a price change for "DOG-FOOD-5LB" to $15.00 effective in 7 days
    Then the request should fail with status code 422
    And the error should indicate the price is below the floor price

  Scenario: Cannot create two concurrent scheduled changes for the same SKU
    Given I have already scheduled a price change for "DOG-FOOD-5LB" to $19.99 in 7 days
    When I attempt to schedule another price change for "DOG-FOOD-5LB" to $21.99 in 14 days
    Then the request should fail with status code 409
    And the error should indicate "An existing pending schedule already exists for this SKU"

  Scenario: Scheduled change for a discontinued SKU is silently discarded
    Given I scheduled a price change for "DOG-FOOD-5LB" to $19.99
    And "DOG-FOOD-5LB" was discontinued after the schedule was created
    When the scheduled activation fires
    Then no "ScheduledPriceActivated" event should be appended
    And the SKU remains in "Discontinued" status
