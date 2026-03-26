@shard-1
Feature: Role-Based Access Control
  As a Backoffice system administrator
  I want different admin roles to have appropriate access to features
  So that users can only perform actions within their authorization scope

  Background:
    Given the Backoffice application is running

  Scenario: System Admin has access to all pages
    Given admin user "SystemAdmin" exists with email "sysadmin@crittersupply.com" and role "system-admin"
    And I am logged in as "sysadmin@crittersupply.com"
    When I navigate to the dashboard
    Then I should see the executive dashboard KPI cards
    When I navigate to the operations alerts page
    Then I should see the operations alerts feed
    When I navigate to the customer search page
    Then I should see the customer search form

  Scenario: Operations Manager has access to dashboard and operations alerts
    Given admin user "OpsManager" exists with email "opsmgr@crittersupply.com" and role "operations-manager"
    And I am logged in as "opsmgr@crittersupply.com"
    When I navigate to the dashboard
    Then I should see the executive dashboard KPI cards
    When I navigate to the operations alerts page
    Then I should see the operations alerts feed

  Scenario: Warehouse Clerk has access to operations alerts only
    Given admin user "WarehouseClerk" exists with email "warehouse@crittersupply.com" and role "warehouse-clerk"
    And I am logged in as "warehouse@crittersupply.com"
    When I navigate to the operations alerts page
    Then I should see the operations alerts feed
    And I should be able to acknowledge alerts

  Scenario: Customer Service has access to customer search
    Given admin user "CSRep" exists with email "support@crittersupply.com" and role "customer-service"
    And I am logged in as "support@crittersupply.com"
    When I navigate to the customer search page
    Then I should see the customer search form
    And I should be able to search for customers

  Scenario: Warehouse Clerk cannot access Dashboard (403 Forbidden or redirect)
    Given admin user "WarehouseClerk" exists with email "warehouse@crittersupply.com" and role "warehouse-clerk"
    And I am logged in as "warehouse@crittersupply.com"
    When I attempt to navigate directly to "/dashboard"
    Then I should see an "Access Denied" message or be redirected to a default page

  Scenario: Customer Service cannot access Operations Alerts (403 Forbidden or redirect)
    Given admin user "CSRep" exists with email "support@crittersupply.com" and role "customer-service"
    And I am logged in as "support@crittersupply.com"
    When I attempt to navigate directly to "/alerts"
    Then I should see an "Access Denied" message or be redirected to a default page

  Scenario: Warehouse Clerk acknowledges alert successfully (P0-1 regression test)
    Given admin user "WarehouseClerk" exists with email "warehouse@crittersupply.com" and role "warehouse-clerk"
    And I am logged in as "warehouse@crittersupply.com"
    And there is an unacknowledged low-stock alert for SKU "TREAT-001"
    When I navigate to the operations alerts page
    And I click on the alert for SKU "TREAT-001"
    And I acknowledge the alert
    Then the alert status should change to "Acknowledged"
    And the alert should no longer appear in the unacknowledged filter

  Scenario: Operations Manager acknowledges alert successfully
    Given admin user "OpsManager" exists with email "opsmgr@crittersupply.com" and role "operations-manager"
    And I am logged in as "opsmgr@crittersupply.com"
    And there is an unacknowledged low-stock alert for SKU "FOOD-005"
    When I navigate to the operations alerts page
    And I click on the alert for SKU "FOOD-005"
    And I acknowledge the alert
    Then the alert status should change to "Acknowledged"

  Scenario: System Admin acknowledges alert successfully
    Given admin user "SystemAdmin" exists with email "sysadmin@crittersupply.com" and role "system-admin"
    And I am logged in as "sysadmin@crittersupply.com"
    And there is an unacknowledged low-stock alert for SKU "TOY-010"
    When I navigate to the operations alerts page
    And I click on the alert for SKU "TOY-010"
    And I acknowledge the alert
    Then the alert status should change to "Acknowledged"

  Scenario: Executive has access to Dashboard with KPI cards
    Given admin user "Executive" exists with email "exec@crittersupply.com" and role "executive"
    And I am logged in as "exec@crittersupply.com"
    When I navigate to the dashboard
    Then I should see the executive dashboard KPI cards
    And I should see the "Total Customers" KPI
    And I should see the "Active Orders" KPI
    And I should see the "Pending Returns" KPI

  Scenario: Finance Clerk has limited access (no operations alerts)
    Given admin user "FinanceClerk" exists with email "finance@crittersupply.com" and role "finance-clerk"
    And I am logged in as "finance@crittersupply.com"
    When I attempt to navigate directly to "/alerts"
    Then I should see an "Access Denied" message or be redirected to a default page

  Scenario: Copy Writer has limited access (no customer search)
    Given admin user "CopyWriter" exists with email "copywriter@crittersupply.com" and role "copy-writer"
    And I am logged in as "copywriter@crittersupply.com"
    When I attempt to navigate directly to "/customer-search"
    Then I should see an "Access Denied" message or be redirected to a default page

  Scenario: Off-the-beaten-path — User with multiple roles has access to all authorized pages
    Given admin user "SuperUser" exists with email "superuser@crittersupply.com" and roles "system-admin,operations-manager,warehouse-clerk"
    And I am logged in as "superuser@crittersupply.com"
    When I navigate to the dashboard
    Then I should see the executive dashboard KPI cards
    When I navigate to the operations alerts page
    Then I should see the operations alerts feed
    When I navigate to the customer search page
    Then I should see the customer search form

  Scenario: Off-the-beaten-path — Navigation menu shows only authorized links
    Given admin user "WarehouseClerk" exists with email "warehouse@crittersupply.com" and role "warehouse-clerk"
    And I am logged in as "warehouse@crittersupply.com"
    When I view the navigation menu
    Then I should see a link to "Operations Alerts"
    And I should not see a link to "Dashboard"
    And I should not see a link to "Customer Search"

  Scenario: Off-the-beaten-path — JWT token contains correct role claims
    Given admin user "OpsManager" exists with email "opsmgr@crittersupply.com" and role "operations-manager"
    When I log in with email "opsmgr@crittersupply.com" and password "Password123!"
    Then the JWT access token should contain role claim "operations-manager"
    And the JWT should not contain role claim "system-admin"
