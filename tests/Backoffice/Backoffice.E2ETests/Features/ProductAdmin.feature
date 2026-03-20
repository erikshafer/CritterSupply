Feature: Product Administration
  As a ProductManager or CopyWriter
  I want to manage product details via the Backoffice
  So that I can keep the product catalog accurate

  Background:
    Given the Backoffice application is running
    And test products exist in the catalog

  Scenario: ProductManager can browse product list
    Given admin user "Alice" exists with email "alice.product@crittersupply.com" and role "ProductManager"
    And I am logged in as "alice.product@crittersupply.com"
    When I navigate to the products list
    Then I should see the product table
    And I should see product "DEMO-001" in the list

  Scenario: ProductManager can edit product display name and description
    Given admin user "Alice" exists with email "alice.product@crittersupply.com" and role "ProductManager"
    And I am logged in as "alice.product@crittersupply.com"
    And I am on the product list page
    When I search for product "DEMO-001"
    And I click Edit for product "DEMO-001"
    Then I should be on the product edit page for "DEMO-001"
    And the display name field should be enabled
    And the description field should be enabled
    When I change the display name to "Updated Product Name"
    And I change the description to "This is an updated product description for testing"
    And I click the Save button
    Then I should see a success message
    And the product changes should be saved

  Scenario: CopyWriter can only edit product description
    Given admin user "Bob" exists with email "bob.copywriter@crittersupply.com" and role "CopyWriter"
    And I am logged in as "bob.copywriter@crittersupply.com"
    And I am on the product list page
    When I click Edit for product "DEMO-001"
    Then I should be on the product edit page for "DEMO-001"
    And the display name field should be disabled
    And the description field should be enabled
    When I change the description to "Updated description by copywriter"
    And I click the Save button
    Then I should see a success message

  Scenario: ProductManager can discontinue a product with two-click workflow
    Given admin user "Alice" exists with email "alice.product@crittersupply.com" and role "ProductManager"
    And I am logged in as "alice.product@crittersupply.com"
    And I am on product "DEMO-001" edit page
    And the product status is "Active"
    When I click the Discontinue Product button
    Then I should see a warning message
    When I click the Discontinue Product button again
    Then the product should be discontinued
    And I should see a success message

  Scenario: CopyWriter cannot see discontinue button
    Given admin user "Bob" exists with email "bob.copywriter@crittersupply.com" and role "CopyWriter"
    And I am logged in as "bob.copywriter@crittersupply.com"
    And I am on product "DEMO-001" edit page
    Then the Discontinue Product button should not be visible

  Scenario: Product list search filters by SKU
    Given admin user "Alice" exists with email "alice.product@crittersupply.com" and role "ProductManager"
    And I am logged in as "alice.product@crittersupply.com"
    And I am on the product list page
    When I search for "DEMO-001"
    Then I should see only products matching "DEMO-001"
    And I should see product "DEMO-001" in the filtered results

  Scenario: Product list search filters by name
    Given admin user "Alice" exists with email "alice.product@crittersupply.com" and role "ProductManager"
    And I am logged in as "alice.product@crittersupply.com"
    And I am on the product list page
    When I search for "Premium"
    Then I should see only products matching "Premium"

  Scenario: Session expired during product edit
    Given admin user "Alice" exists with email "alice.product@crittersupply.com" and role "ProductManager"
    And I am logged in as "alice.product@crittersupply.com"
    And I am on product "DEMO-001" edit page
    When my session expires
    And I try to save product changes
    Then I should be redirected to the login page
    And the return URL should be captured for post-auth redirect
