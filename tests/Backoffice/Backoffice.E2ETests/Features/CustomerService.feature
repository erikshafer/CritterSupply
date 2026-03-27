@shard-3
Feature: Customer Service Workflows
  As a Backoffice customer service representative
  I want to search for customers and manage their orders and returns
  So that I can resolve customer issues efficiently

  Background:
    Given the Backoffice application is running
    And I am logged in as a customer service admin

  Scenario: Search for customer by email shows results in table
    Given customer "John Doe" exists with email "john.doe@example.com"
    When I navigate to the customer search page
    And I perform a customer search for "john.doe@example.com"
    Then the search results should contain "John Doe"

  Scenario: Search for customer and navigate to detail page with order history
    Given customer "John Doe" exists with email "john.doe@example.com"
    And customer "john.doe@example.com" has 2 orders
    When I navigate to the customer search page
    And I perform a customer search for "john.doe@example.com"
    And I click view details for customer "john.doe@example.com"
    Then I should be on the customer detail page
    And I should see first name "John"
    And I should see last name "Doe"
    And I should see 2 orders in the detail order history

  Scenario: Search for customer with no orders shows empty order message
    Given customer "Jane Smith" exists with email "jane.smith@example.com"
    And customer "jane.smith@example.com" has 0 orders
    When I navigate to the customer search page
    And I perform a customer search for "jane.smith@example.com"
    And I click view details for customer "jane.smith@example.com"
    Then I should see the no orders message on the detail page

  Scenario: Search for non-existent customer shows no results
    When I navigate to the customer search page
    And I perform a customer search for "nonexistent@example.com"
    Then I should see no customer search results

  Scenario: View order details from customer detail page
    Given customer "John Doe" exists with email "john.doe@example.com"
    And customer "john.doe@example.com" has 1 orders
    When I navigate to the customer search page
    And I perform a customer search for "john.doe@example.com"
    And I click view details for customer "john.doe@example.com"
    And I click on the first order in the detail order history
    Then I should be on an order detail page

  Scenario: Case-insensitive email search
    Given customer "John Doe" exists with email "john.doe@example.com"
    When I navigate to the customer search page
    And I perform a customer search for "JOHN.DOE@EXAMPLE.COM"
    Then the search results should contain "John Doe"
