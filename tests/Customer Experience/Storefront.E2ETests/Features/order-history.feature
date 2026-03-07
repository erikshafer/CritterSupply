Feature: Order History
  As a logged-in customer
  I want to view my order history
  So that I can track past purchases and their current status

  # ──────────────────────────────────────────────────
  # These scenarios are deferred to Cycle 21.
  # The Order History page (/orders) is implemented in Storefront.Web
  # and integrated with the Orders BC via IOrdersClient.
  # E2E coverage is blocked on:
  #   - Multi-order test data seeding strategy
  #   - Order detail page (planned for Cycle 21 Product Catalog Phase 2)
  # Tracking: Create a Cycle 21 GitHub Issue to implement these scenarios.
  # ──────────────────────────────────────────────────

  Background:
    Given I am logged in as "alice@example.com"

  @wip @ignore
  Scenario: Customer can view their order history
    Given I have previously placed 3 orders
    When I navigate to the order history page
    Then I should see 3 orders listed
    And each order should display its status and date
    And each order should display its total amount

  @wip @ignore
  Scenario: Customer can see empty state when no orders have been placed
    Given I have no previous orders
    When I navigate to the order history page
    Then I should see an empty order history message
    And I should see a prompt to start shopping

  @wip @ignore
  Scenario: Customer can navigate from order history to order confirmation
    Given I have previously placed an order
    When I navigate to the order history page
    And I click on my most recent order
    Then I should be on the order confirmation page for that order

  @wip @ignore
  Scenario: Order history shows paginated results for customers with many orders
    Given I have previously placed 25 orders
    When I navigate to the order history page
    Then I should see the first 20 orders listed
    And I should see pagination controls
    When I navigate to the next page
    Then I should see the remaining 5 orders
