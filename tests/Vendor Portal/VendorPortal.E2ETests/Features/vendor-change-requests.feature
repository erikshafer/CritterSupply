@vendor-portal @change-requests
Feature: Vendor Portal Change Requests
  As a catalog manager
  I want to manage product change requests
  So that I can update product information through the approval workflow

  # P0 Scenarios — core business workflow

  @p0
  Scenario: CatalogManager submits a change request end-to-end
    Given I am logged in as "catalog@acmepets.test" with password "password"
    When I navigate to the submit change request page
    And I fill in SKU "DOG-BOWL-01" title "Update bowl description" and details "New premium ceramic finish"
    And I click the submit button
    Then I should be redirected to the change requests list
    And I should see a snackbar containing "submitted"

  @p0
  Scenario: CatalogManager saves a draft change request
    Given I am logged in as "catalog@acmepets.test" with password "password"
    When I navigate to the submit change request page
    And I fill in SKU "DOG-BOWL-01" title "Draft bowl update" and details "Work in progress"
    And I click the save draft button
    Then I should be redirected to the change requests list
    And I should see a snackbar containing "draft"

  # P1a Scenarios — completing the vendor experience

  @p1a
  Scenario: Change requests list shows submitted requests
    Given I am logged in as "catalog@acmepets.test" with password "password"
    And I have submitted a change request for SKU "DOG-BOWL-01" with title "Update description"
    When I navigate to the change requests page
    Then I should see the change requests table
    And the table should contain "DOG-BOWL-01"

  @p1a
  Scenario: ReadOnly user cannot see the submit button
    Given I am logged in as "readonly@acmepets.test" with password "password"
    When I navigate to the change requests page
    Then I should not see the submit change request button on the list page

  @p1a
  Scenario: Logout clears session and redirects to login
    Given I am logged in as "admin@acmepets.test" with password "password"
    When I click the logout button
    Then I should be on the login page
    And I should not see user info in the app bar
