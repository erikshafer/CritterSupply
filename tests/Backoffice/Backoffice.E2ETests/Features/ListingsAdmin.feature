@shard-3
Feature: Listings Administration
  As a ProductManager or SystemAdmin
  I want to manage listings via the Backoffice admin page
  So that I can oversee product listings across all channels

  Background:
    Given the Backoffice application is running
    And test listings exist in the Listings service

  Scenario: Admin navigates to listings page and sees the listings table
    Given admin user "Alice" exists with email "alice.listings@crittersupply.com" and role "ProductManager"
    And I am logged in as "alice.listings@crittersupply.com"
    When I navigate to the listings admin page
    Then I should see the listings table
    And I should see at least one listing row

  Scenario: Admin filters listings by status Live
    Given admin user "Alice" exists with email "alice.listings@crittersupply.com" and role "ProductManager"
    And I am logged in as "alice.listings@crittersupply.com"
    And I am on the listings admin page
    When I filter listings by status "Live"
    Then I should see only listings with status "Live"

  Scenario: Admin clicks a listing row and navigates to the detail page
    Given admin user "Alice" exists with email "alice.listings@crittersupply.com" and role "ProductManager"
    And I am logged in as "alice.listings@crittersupply.com"
    And I am on the listings admin page
    And I can see a listing with a known ID
    When I click on the listing row
    Then I should be on the listing detail page

  @wip
  Scenario: Admin creates a new listing from the admin page
    # Blocked: listing create form not yet implemented in Backoffice.Web
    Given admin user "Alice" exists with email "alice.listings@crittersupply.com" and role "ProductManager"
    And I am logged in as "alice.listings@crittersupply.com"
    And I am on the listings admin page
    When I click the "Create Listing" button
    And I fill in the listing details
    And I submit the new listing form
    Then I should see the new listing in the table

  @wip
  Scenario: Admin ends a listing from the admin page
    # Blocked: listing action buttons not yet wired in admin table — actions are on detail page only (disabled stubs)
    Given admin user "Alice" exists with email "alice.listings@crittersupply.com" and role "ProductManager"
    And I am logged in as "alice.listings@crittersupply.com"
    And I am on the listings admin page
    And a live listing exists
    When I click the "End" action on the listing
    Then the listing status should change to "Ended"
