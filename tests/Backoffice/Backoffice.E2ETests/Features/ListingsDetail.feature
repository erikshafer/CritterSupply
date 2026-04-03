@shard-3
Feature: Listing Detail Page
  As a ProductManager or SystemAdmin
  I want to view listing details from the Backoffice
  So that I can inspect listing state, content, and metadata

  Background:
    Given the Backoffice application is running
    And test listings exist in the Listings service

  Scenario: Admin navigates from listings table to a listing detail page and sees listing info
    Given admin user "Alice" exists with email "alice.detail@crittersupply.com" and role "ProductManager"
    And I am logged in as "alice.detail@crittersupply.com"
    And I am on the listings admin page
    And I can see a listing with a known ID
    When I click on the listing row
    Then I should be on the listing detail page
    And I should see the listing SKU
    And I should see the listing channel
    And I should see the listing status badge
    And I should see the listing product name
    And I should see the listing created at timestamp
    And the approve button should be disabled
    And the pause button should be enabled
    And the end listing button should be enabled

  Scenario: Admin approves a listing from the detail page
    Given admin user "Alice" exists with email "alice.detail@crittersupply.com" and role "ProductManager"
    And I am logged in as "alice.detail@crittersupply.com"
    And a listing exists in "ReadyForReview" status
    When I navigate to the listing detail page
    And I click the "Approve" button
    Then the listing status should change to "Submitted"

  Scenario: Admin pauses a listing from the detail page
    Given admin user "Alice" exists with email "alice.detail@crittersupply.com" and role "ProductManager"
    And I am logged in as "alice.detail@crittersupply.com"
    And a listing exists in "Live" status
    When I navigate to the listing detail page
    And I click the "Pause" button
    And I provide a pause reason
    Then the listing status should change to "Paused"

  Scenario: Admin ends a listing from the detail page
    Given admin user "Alice" exists with email "alice.detail@crittersupply.com" and role "ProductManager"
    And I am logged in as "alice.detail@crittersupply.com"
    And a listing exists in "Live" status
    When I navigate to the listing detail page
    And I click the "End Listing" button
    Then the listing status should change to "Ended"
