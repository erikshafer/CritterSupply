Feature: Marketplace Administration
  As a ProductManager or SystemAdmin
  I want to view marketplace channels and category mappings
  So that I can oversee marketplace integration configuration

  Background:
    Given the Backoffice application is running
    And test marketplace data exists in the Marketplaces service

  # ─── Marketplace List Page ─────────────────────────────────────────────────

  Scenario: Admin navigates to marketplaces page and sees the 3 seeded channels
    Given admin user "Bob" exists with email "bob.markets@crittersupply.com" and role "ProductManager"
    And I am logged in as "bob.markets@crittersupply.com"
    When I navigate to the marketplaces list page
    Then I should see the marketplaces table
    And I should see 3 marketplace rows
    And I should see marketplace row "AMAZON_US" with display name "Amazon US"
    And I should see marketplace row "WALMART_US" with display name "Walmart US"
    And I should see marketplace row "EBAY_US" with display name "eBay US"

  Scenario: Each marketplace row shows correct status chip
    Given admin user "Bob" exists with email "bob.markets@crittersupply.com" and role "ProductManager"
    And I am logged in as "bob.markets@crittersupply.com"
    When I navigate to the marketplaces list page
    Then marketplace "AMAZON_US" should show status "Active"
    And marketplace "WALMART_US" should show status "Active"
    And marketplace "EBAY_US" should show status "Active"

  Scenario: Unauthenticated request to marketplaces page redirects to login
    When I navigate directly to "/marketplaces"
    Then I should be on the login page

  # ─── Category Mapping List Page ────────────────────────────────────────────

  Scenario: Admin navigates to category mappings page and sees all 18 seeded mappings
    Given admin user "Bob" exists with email "bob.markets@crittersupply.com" and role "ProductManager"
    And I am logged in as "bob.markets@crittersupply.com"
    When I navigate to the category mappings page
    Then I should see the category mappings table
    And I should see 18 category mapping rows

  Scenario: Admin filters category mappings by AMAZON_US
    Given admin user "Bob" exists with email "bob.markets@crittersupply.com" and role "ProductManager"
    And I am logged in as "bob.markets@crittersupply.com"
    And I am on the category mappings page
    When I filter category mappings by channel "Amazon US"
    Then I should see exactly 6 category mapping rows

  Scenario: Category mappings page shows correct breadcrumb trail
    Given admin user "Bob" exists with email "bob.markets@crittersupply.com" and role "ProductManager"
    And I am logged in as "bob.markets@crittersupply.com"
    When I navigate to the category mappings page
    Then I should see the breadcrumb trail
    And the breadcrumb trail should contain "Home"
    And the breadcrumb trail should contain "Marketplaces"
    And the breadcrumb trail should contain "Category Mappings"
