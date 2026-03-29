@shard-4
Feature: Order Search
  As a Backoffice admin
  I want to search for orders by order ID
  So that I can quickly find and review customer orders

  Background:
    Given the Backoffice application is running
    And I am logged in as a customer service admin

  Scenario: Search for an existing order shows results
    Given an order exists with ID "44444444-4444-4444-4444-444444444444"
    When I navigate to the order search page
    And I search for order "44444444-4444-4444-4444-444444444444"
    Then I should see 1 order in the search results

  Scenario: Search for a non-existent order shows no results
    When I navigate to the order search page
    And I search for order "00000000-0000-0000-0000-000000000000"
    Then I should see a no results message
