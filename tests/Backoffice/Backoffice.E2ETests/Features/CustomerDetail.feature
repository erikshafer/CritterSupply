@shard-3
Feature: Customer Detail Page
  As a Backoffice customer service representative
  I want to view detailed customer information on a dedicated page
  So that I can understand a customer's history, addresses, and orders

  Background:
    Given the Backoffice application is running
    And I am logged in as a customer service admin

  Scenario: Navigate to customer detail from search results
    Given customer "John Doe" exists with email "john.doe@example.com"
    And customer "john.doe@example.com" has 2 orders
    When I navigate to the customer search page
    And I perform a customer search for "john.doe@example.com"
    Then the search results should contain "John Doe"
    When I click view details for customer "john.doe@example.com"
    Then I should be on the customer detail page
    And I should see first name "John"
    And I should see last name "Doe"
    And I should see detail email "john.doe@example.com"

  Scenario: Customer detail shows order history
    Given customer "John Doe" exists with email "john.doe@example.com"
    And customer "john.doe@example.com" has 3 orders
    When I navigate to the customer search page
    And I perform a customer search for "john.doe@example.com"
    And I click view details for customer "john.doe@example.com"
    Then I should see 3 orders in the detail order history

  Scenario: Customer detail with no orders shows empty message
    Given customer "Jane Smith" exists with email "jane.smith@example.com"
    And customer "jane.smith@example.com" has 0 orders
    When I navigate to the customer search page
    And I perform a customer search for "jane.smith@example.com"
    And I click view details for customer "jane.smith@example.com"
    Then I should see the no orders message on the detail page

  Scenario: Customer detail shows addresses
    Given customer "John Doe" exists with email "john.doe@example.com"
    And customer "john.doe@example.com" has address "Home" as default
    And customer "john.doe@example.com" has address "Work"
    When I navigate to the customer search page
    And I perform a customer search for "john.doe@example.com"
    And I click view details for customer "john.doe@example.com"
    Then I should see 2 addresses in the detail addresses table

  Scenario: Navigate back from customer detail to search
    Given customer "John Doe" exists with email "john.doe@example.com"
    When I navigate to the customer search page
    And I perform a customer search for "john.doe@example.com"
    And I click view details for customer "john.doe@example.com"
    And I click back to customer search
    Then I should be on the customer search page

  Scenario: View order from customer detail navigates to order detail
    Given customer "John Doe" exists with email "john.doe@example.com"
    And customer "john.doe@example.com" has 1 orders
    When I navigate to the customer search page
    And I perform a customer search for "john.doe@example.com"
    And I click view details for customer "john.doe@example.com"
    And I click on the first order in the detail order history
    Then I should be on an order detail page

  Scenario: Customer not found for invalid ID
    When I navigate to customer detail for a non-existent customer
    Then I should see the customer not found alert

  Scenario: Search with no results shows empty state
    When I navigate to the customer search page
    And I perform a customer search for "nonexistent@example.com"
    Then I should see no customer search results
