@shard-2
Feature: Return Management
  As a Backoffice customer service or operations manager
  I want to view and filter the active return queue
  So that I can process return requests efficiently

  Background:
    Given the Backoffice application is running
    And I am logged in as a customer service admin
    And I am on the dashboard page

  Scenario: Navigate to Return Management page from dashboard
    When I click on the "Return Management" navigation link
    Then I should be on the "/returns" page
    And I should see the page heading "Return Management"
    And I should see the status filter dropdown

  Scenario: Load returns list with default "Requested" filter
    Given 3 returns exist with status "Requested"
    And 2 returns exist with status "Approved"
    When I navigate to the "/returns" page
    Then I should see the status filter set to "Requested"
    And I should see 3 returns in the table
    And the return count badge should show "Requested"

  Scenario: Filter returns by status - Approved
    Given 3 returns exist with status "Requested"
    And 2 returns exist with status "Approved"
    And 1 return exists with status "Denied"
    When I navigate to the "/returns" page
    And I select "Approved" from the status filter
    And I click the "Refresh" button
    Then I should see 2 returns in the table
    And the return count badge should show "Approved"
    And all displayed returns should have status "Approved"

  Scenario: Filter returns by status - All
    Given 3 returns exist with status "Requested"
    And 2 returns exist with status "Approved"
    And 1 return exists with status "Denied"
    When I navigate to the "/returns" page
    And I select "All" from the status filter
    And I click the "Refresh" button
    Then I should see 6 returns in the table
    And the return count badge should not be visible

  Scenario: Empty state when no returns match filter
    Given 3 returns exist with status "Requested"
    When I navigate to the "/returns" page
    And I select "Denied" from the status filter
    And I click the "Refresh" button
    Then I should see the "no returns" alert
    And the return count should show 0
    And I should not see the returns table

  Scenario: Pending Returns count matches dashboard KPI
    Given 5 returns exist with status "Requested"
    And 2 returns exist with status "Approved"
    When I navigate to the dashboard page
    Then the "Pending Returns" KPI should show "5"
    When I navigate to the "/returns" page
    Then I should see 5 returns in the table
    And the return count badge should show "Requested"

  Scenario: Refresh returns list updates count
    Given 3 returns exist with status "Requested"
    When I navigate to the "/returns" page
    And I see 3 returns in the table
    And 2 more returns are created with status "Requested"
    And I click the "Refresh" button
    Then I should see 5 returns in the table

  Scenario: Authorization - Customer Service role can access Returns
    Given I am logged out
    And I log in with customer-service role
    When I navigate to the "/returns" page
    Then I should see the Return Management page
    And I should not see an authorization error

  Scenario: Authorization - Operations Manager role can access Returns
    Given I am logged out
    And I log in with operations-manager role
    When I navigate to the "/returns" page
    Then I should see the Return Management page
    And I should not see an authorization error

  Scenario: Authorization - System Admin role can access Returns
    Given I am logged out
    And I log in with system-admin role
    When I navigate to the "/returns" page
    Then I should see the Return Management page
    And I should not see an authorization error

  Scenario: Session expiry during return management
    Given 3 returns exist with status "Requested"
    And I navigate to the "/returns" page
    When my session expires
    And I click the "Refresh" button
    Then I should see the session expired modal
    And I should not see updated return data
