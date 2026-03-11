@e2e @vendor-portal @dashboard
Feature: Vendor Portal Dashboard & KPI Display
  As a vendor user
  I want to see my business KPIs at a glance on the dashboard
  So that I can quickly assess my product health and pending work

  # ─── ACCEPTANCE CRITERIA ───────────────────────────────────────────────
  # AC-1: Dashboard displays 3 KPI cards (Low Stock Alerts, Pending Requests, Total SKUs)
  # AC-2: KPI values reflect seeded data accurately
  # AC-3: Quick action buttons are role-appropriate (Admin/CatalogManager see Submit, ReadOnly doesn't)
  # AC-4: Dashboard loads without error from authenticated state
  # ────────────────────────────────────────────────────────────────────────

  # ─── TEST DATA SETUP (via API before browser steps) ────────────────────
  # 1. Login as admin@acmepets.test to get JWT
  # 2. Seed via VendorPortal.Api:
  #    - POST change requests in Draft/Submitted states → PendingChangeRequests count
  #    - VendorProductCatalog entries → TotalSkus count
  #    - Low stock alerts → ActiveLowStockAlerts count
  # 3. Navigate to dashboard and verify rendered values
  # ────────────────────────────────────────────────────────────────────────

  # ─── PRIORITY: P0 — Core user landing page ─────────────────────────────

  Background:
    Given the following change requests exist for "Acme Pet Supplies":
      | SKU          | Type        | Status    | Title                        |
      | DOG-BOWL-01  | Description | Submitted | Update bowl description      |
      | CAT-TOY-05   | Image       | Draft     | New laser pointer photos     |
      | DOG-LEASH-03 | Description | Approved  | Leash description correction |
    And the vendor has 12 total SKUs in their product catalog
    And there are 2 active low stock alerts

  @dashboard @p0 @smoke
  Scenario: Admin sees accurate KPI cards on dashboard after login
    Given I am logged in as "admin@acmepets.test" with password "password"
    When I am on the dashboard page
    Then I should see the "Low Stock Alerts" KPI card with value "2"
    And I should see the "Pending Change Requests" KPI card with value "1"
    And I should see the "Total SKUs" KPI card with value "12"

  @dashboard @p1
  Scenario: Admin sees role-appropriate quick action buttons
    Given I am logged in as "admin@acmepets.test" with password "password"
    When I am on the dashboard page
    Then I should see the "Submit Change Request" quick action button
    And I should see the "View Change Requests" quick action button
    And I should see the "Settings" quick action button

  @dashboard @p1
  Scenario: ReadOnly user sees limited quick actions without submit capability
    Given I am logged in as "readonly@acmepets.test" with password "password"
    When I am on the dashboard page
    Then I should NOT see the "Submit Change Request" quick action button
    And I should NOT see the "View Change Requests" quick action button
    And I should see the "Settings" quick action button
    And I should see a message explaining what ReadOnly users can do

  @dashboard @p1
  Scenario: Quick action navigates to Submit Change Request page
    Given I am logged in as "admin@acmepets.test" with password "password"
    And I am on the dashboard page
    When I click the "Submit Change Request" quick action button
    Then I should be on the submit change request page at "/change-requests/submit"

  @dashboard @p2
  Scenario: Quick action navigates to Settings page
    Given I am logged in as "admin@acmepets.test" with password "password"
    And I am on the dashboard page
    When I click the "Settings" quick action button
    Then I should be on the settings page at "/settings"
