@shard-4
Feature: Order Detail
  As a Backoffice admin
  I want to view order details
  So that I can review order information and assist customers

  Background:
    Given the Backoffice application is running
    And I am logged in as a customer service admin

  Scenario: View order detail from search results
    Given an order exists with ID "44444444-4444-4444-4444-444444444444"
    When I navigate to the order search page
    And I search for order "44444444-4444-4444-4444-444444444444"
    And I click view details for order "44444444-4444-4444-4444-444444444444"
    Then I should be on the order detail page
    And I should see the order ID displayed
    And I should see the order status

  Scenario: Navigate back from order detail to search
    Given an order exists with ID "44444444-4444-4444-4444-444444444444"
    When I navigate to the order search page
    And I search for order "44444444-4444-4444-4444-444444444444"
    And I click view details for order "44444444-4444-4444-4444-444444444444"
    And I click the back button
    Then I should be on the order search page
