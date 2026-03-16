@vendor-portal @dashboard @ignore
Feature: Vendor Portal Dashboard
  As a vendor user
  I want to see my dashboard with KPI cards and live updates
  So that I can monitor my business at a glance

  # P0 Scenarios — core vendor value chain

  @p0
  Scenario: Dashboard shows accurate KPI cards after login
    Given I am logged in as "admin@acmepets.test" with password "password"
    Then I should see the dashboard KPI cards
    And the low stock alerts count should be "0"
    And the pending change requests count should be "0"
    And the total SKUs count should be "0"

  @p0
  Scenario: SignalR connection indicator shows Live
    Given I am logged in as "admin@acmepets.test" with password "password"
    Then the hub status indicator should show "Live"

  @p0
  Scenario: Low stock alert via SignalR updates the KPI card count
    Given I am logged in as "admin@acmepets.test" with password "password"
    And I am on the dashboard
    When a LowStockAlertRaised hub message is sent to the tenant group
    Then the low stock alerts count should be "1"

  @p0
  Scenario: Change request decision toast appears via SignalR
    Given I am logged in as "admin@acmepets.test" with password "password"
    And I am on the dashboard
    When a ChangeRequestDecisionPersonal hub message with decision "Approved" is sent
    Then I should see a snackbar containing "approved"
