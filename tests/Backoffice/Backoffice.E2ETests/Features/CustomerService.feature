Feature: Customer Service Workflows
  As a Backoffice customer service representative
  I want to search for customers and manage their orders and returns
  So that I can resolve customer issues efficiently

  Background:
    Given the Backoffice application is running
    And I am logged in as a customer service admin
    And I am on the customer service page

  Scenario: Search for customer by email and view order history
    Given customer "John Doe" exists with email "john.doe@example.com"
    And customer "john.doe@example.com" has 2 orders
    When I search for customer by email "john.doe@example.com"
    Then I should see customer details for "John Doe"
    And I should see 2 orders in the order history table
    And the order history should include order IDs

  Scenario: Search for customer with no orders
    Given customer "Jane Smith" exists with email "jane.smith@example.com"
    And customer "jane.smith@example.com" has 0 orders
    When I search for customer by email "jane.smith@example.com"
    Then I should see customer details for "Jane Smith"
    And I should see an empty order history message

  Scenario: Search for non-existent customer
    When I search for customer by email "nonexistent@example.com"
    Then I should see a "no results" message
    And I should not see customer details

  Scenario: View order details from customer history
    Given customer "John Doe" exists with email "john.doe@example.com"
    And customer "john.doe@example.com" has 1 order with ID "{OrderId1}"
    When I search for customer by email "john.doe@example.com"
    And I click on order "{OrderId1}"
    Then I should see order details for order "{OrderId1}"
    And the order details should include line items, status, and total

  Scenario: Approve a pending return request
    Given customer "John Doe" exists with email "john.doe@example.com"
    And customer has order "{OrderId1}" with status "Delivered"
    And customer has return request "{ReturnId1}" for order "{OrderId1}" with status "Pending"
    When I search for customer by email "john.doe@example.com"
    And I view return request "{ReturnId1}"
    And I approve the return request
    Then the return request status should change to "Approved"
    And I should see a confirmation message

  Scenario: Deny a return request with reason
    Given customer "John Doe" exists with email "john.doe@example.com"
    And customer has order "{OrderId1}" with status "Delivered"
    And customer has return request "{ReturnId1}" for order "{OrderId1}" with status "Pending"
    When I search for customer by email "john.doe@example.com"
    And I view return request "{ReturnId1}"
    And I deny the return request with reason "Product was damaged by customer"
    Then the return request status should change to "Denied"
    And the denial reason should be recorded
    And I should see a confirmation message

  Scenario: Search for customer with multiple return requests
    Given customer "John Doe" exists with email "john.doe@example.com"
    And customer has 3 return requests
    When I search for customer by email "john.doe@example.com"
    Then I should see 3 return requests in the customer details
    And each return request should show its status

  Scenario: Case-insensitive email search
    Given customer "John Doe" exists with email "john.doe@example.com"
    When I search for customer by email "JOHN.DOE@EXAMPLE.COM"
    Then I should see customer details for "John Doe"
    And I should see the customer's email displayed as "john.doe@example.com"

  Scenario: Off-the-beaten-path - Customer with cancelled order
    Given customer "John Doe" exists with email "john.doe@example.com"
    And customer has order "{OrderId1}" with status "Cancelled"
    When I search for customer by email "john.doe@example.com"
    Then I should see 1 order in the order history table
    And the order status should be "Cancelled"
    And I should not see any return requests for the cancelled order

  Scenario: Off-the-beaten-path - Customer with very long order history
    Given customer "Jane Power User" exists with email "jane.poweruser@example.com"
    And customer "jane.poweruser@example.com" has 50 orders
    When I search for customer by email "jane.poweruser@example.com"
    Then I should see customer details for "Jane Power User"
    And I should see 50 orders in the order history table
    And the order history table should support pagination or scrolling
