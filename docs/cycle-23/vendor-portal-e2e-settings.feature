@e2e @vendor-portal @settings
Feature: Vendor Portal Settings & Preferences
  As a vendor user
  I want to manage my notification preferences and saved dashboard views
  So that I can customize my portal experience

  # ─── ACCEPTANCE CRITERIA ───────────────────────────────────────────────
  # AC-1: All 4 notification toggles load with current state (default: all enabled)
  # AC-2: Toggle changes are persisted via Save button and survive page reload
  # AC-3: Saved dashboard views table shows existing views
  # AC-4: Can delete a saved view via the table action button
  # AC-5: Delete confirmation dialog prevents accidental deletion
  # ────────────────────────────────────────────────────────────────────────

  # ─── PRIORITY: P2 — Settings is low-risk, well-covered by integration tests ─

  @settings @p2
  Scenario: Vendor sees all notification preference toggles with default values
    Given I am logged in as "admin@acmepets.test" with password "password"
    And the vendor has default notification preferences (all enabled)
    When I navigate to the settings page
    Then I should see the following notification toggles all enabled:
      | Toggle                    | Data-TestId                    |
      | Low Stock Alerts          | pref-low-stock-alerts          |
      | Change Request Decisions  | pref-change-request-decisions  |
      | Inventory Updates         | pref-inventory-updates         |
      | Sales Metrics             | pref-sales-metrics             |

  @settings @p2
  Scenario: Vendor toggles notification preferences and saves successfully
    Given I am logged in as "admin@acmepets.test" with password "password"
    And I am on the settings page
    When I toggle off "Low Stock Alerts" using data-testid "pref-low-stock-alerts"
    And I toggle off "Sales Metrics" using data-testid "pref-sales-metrics"
    And I click the "Save" button with data-testid "save-preferences-btn"
    Then I should see a success snackbar confirming preferences were saved
    When I reload the settings page
    Then the "Low Stock Alerts" toggle should be off
    And the "Change Request Decisions" toggle should be on
    And the "Inventory Updates" toggle should be on
    And the "Sales Metrics" toggle should be off

  @settings @p2
  Scenario: Vendor sees empty state when no saved dashboard views exist
    Given I am logged in as "admin@acmepets.test" with password "password"
    And the vendor has no saved dashboard views
    When I navigate to the settings page
    Then I should see an empty state message with data-testid "no-saved-views-message"

  @settings @p2
  Scenario: Vendor sees saved dashboard views in the table
    Given I am logged in as "admin@acmepets.test" with password "password"
    And the vendor has the following saved dashboard views:
      | ViewName         | Filter                                |
      | Low Stock Only   | Low stock only, Last 7 days           |
      | Q1 Report        | From: 2025-01-01, To: 2025-03-31     |
    When I navigate to the settings page
    Then I should see a saved views table with data-testid "saved-views-table"
    And the table should display 2 saved views
    And I should see "Low Stock Only" in the view name column
    And I should see "Q1 Report" in the view name column

  @settings @p2
  Scenario: Vendor deletes a saved dashboard view
    Given I am logged in as "admin@acmepets.test" with password "password"
    And the vendor has a saved dashboard view named "Low Stock Only"
    And I am on the settings page
    When I click the delete button for the "Low Stock Only" saved view
    And I confirm the deletion in the dialog
    Then the "Low Stock Only" view should be removed from the table
    And I should see a success snackbar confirming the view was deleted
