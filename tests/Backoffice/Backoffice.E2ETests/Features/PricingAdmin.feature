Feature: Pricing Admin
  As a Pricing Manager
  I want to manage product prices
  So that I can maintain competitive pricing

  Background:
    Given the Backoffice system is running
    And stub catalog client has product "DEMO-001" with name "Cat Food Premium"
    And stub pricing client has product "DEMO-001" with current price "$19.99"

  Scenario: Pricing Manager can set base price
    Given admin user exists with email "pricing@example.com" and role "PricingManager"
    When I log in with email "pricing@example.com" and password "password123"
    And I navigate to the price edit page for SKU "DEMO-001"
    Then I should see the current price "$19.99"
    When I set the price to "$24.99"
    And I submit the price change
    Then I should see the success message "Price updated successfully"
    And the current price should be "$24.99"

  Scenario: Price must be greater than zero
    Given admin user exists with email "pricing@example.com" and role "PricingManager"
    When I log in with email "pricing@example.com" and password "password123"
    And I navigate to the price edit page for SKU "DEMO-001"
    And I set the price to "$0.00"
    Then the submit button should be disabled

  Scenario: Floor price constraint is enforced
    Given admin user exists with email "pricing@example.com" and role "PricingManager"
    And stub pricing client has floor price "$15.00" for SKU "DEMO-001"
    When I log in with email "pricing@example.com" and password "password123"
    And I navigate to the price edit page for SKU "DEMO-001"
    And I set the price to "$10.00"
    And I submit the price change
    Then I should see the error message "Price cannot be below floor price of $15.00"

  Scenario: Ceiling price constraint is enforced
    Given admin user exists with email "pricing@example.com" and role "PricingManager"
    And stub pricing client has ceiling price "$30.00" for SKU "DEMO-001"
    When I log in with email "pricing@example.com" and password "password123"
    And I navigate to the price edit page for SKU "DEMO-001"
    And I set the price to "$35.00"
    And I submit the price change
    Then I should see the error message "Price cannot exceed ceiling price of $30.00"

  Scenario: Session expired redirects to login
    Given admin user exists with email "pricing@example.com" and role "PricingManager"
    When I log in with email "pricing@example.com" and password "password123"
    And I navigate to the price edit page for SKU "DEMO-001"
    And my session expires
    And I set the price to "$24.99"
    And I submit the price change
    Then I should be redirected to the login page

  Scenario: SystemAdmin can set prices
    Given admin user exists with email "admin@example.com" and role "SystemAdmin"
    When I log in with email "admin@example.com" and password "password123"
    And I navigate to the price edit page for SKU "DEMO-001"
    Then I should see the price edit form
