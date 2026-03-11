@e2e @vendor-portal @rbac
Feature: Vendor Portal Role-Based Access Control
  As a vendor system
  I want to enforce role-based visibility and permissions in the UI
  So that ReadOnly users cannot perform catalog management actions

  # ─── ACCEPTANCE CRITERIA ───────────────────────────────────────────────
  # AC-1: ReadOnly user cannot see "Submit Change Request" button on list page
  # AC-2: ReadOnly user navigating to /change-requests/submit gets denied or redirected
  # AC-3: ReadOnly user CAN view change request details (read-only, no action buttons)
  # AC-4: CatalogManager CAN submit, withdraw, provide info — same as Admin for change requests
  # AC-5: ReadOnly user CAN access Settings page (preferences are personal, not restricted)
  # ────────────────────────────────────────────────────────────────────────

  # ─── WHY E2E FOR RBAC? ────────────────────────────────────────────────
  # Integration tests verify the API returns 403 Forbid.
  # E2E tests verify the UI hides/shows elements based on role,
  # AND that the UI gracefully handles 403 responses if somehow reached.
  # ────────────────────────────────────────────────────────────────────────

  # ─── PRIORITY: P1 — Role enforcement is security-relevant ──────────────

  Background:
    Given the following change requests exist for "Acme Pet Supplies":
      | SKU          | Type        | Status    | Title                   |
      | DOG-BOWL-01  | Description | Submitted | Update bowl description |
      | CAT-TOY-05   | Image       | Draft     | New laser pointer photos|

  @rbac @p1
  Scenario: ReadOnly user cannot see submit button on change requests list
    Given I am logged in as "readonly@acmepets.test" with password "password"
    When I navigate to the change requests list page
    Then I should NOT see a "Submit Change Request" button with data-testid "submit-change-request-btn"
    But I should see the change requests table with existing requests

  @rbac @p1
  Scenario: ReadOnly user can view change request detail but sees no action buttons
    Given I am logged in as "readonly@acmepets.test" with password "password"
    And I am on the change requests list page
    When I click the "View" button for the request with SKU "DOG-BOWL-01"
    Then I should see the change request detail page
    And I should see the status "Submitted"
    And I should NOT see the "Withdraw Request" button
    And I should NOT see the "Submit Request" button
    And I should NOT see the "Delete Draft" button

  @rbac @p1
  Scenario: ReadOnly user cannot see delete button for draft requests in list
    Given I am logged in as "readonly@acmepets.test" with password "password"
    When I navigate to the change requests list page
    And I click the "Draft" status filter chip
    Then I should see the draft request with SKU "CAT-TOY-05"
    But I should NOT see a delete button for that draft

  @rbac @p1
  Scenario: CatalogManager can submit change requests like an Admin
    Given I am logged in as "catalog@acmepets.test" with password "password"
    When I navigate to the submit change request page
    And I enter "DOG-BONE-07" in the SKU field
    And I enter "Bone description update" in the Title field
    And I enter "Update the chewing hardness rating" in the Details field
    And I click the "Submit Request" button
    Then I should see a success snackbar "Change request submitted successfully."
    And I should be redirected to the change requests list page

  @rbac @p2
  Scenario: ReadOnly user can access settings and manage personal preferences
    Given I am logged in as "readonly@acmepets.test" with password "password"
    When I navigate to the settings page
    Then I should see the notification preference toggles
    And I should be able to toggle "Low Stock Alerts" off
    And I should be able to save preferences successfully

  @rbac @p2
  Scenario: Each role sees their correct role badge in the app bar
    Given I am logged in as "catalog@acmepets.test" with password "password"
    Then I should see "Catalog Manager" as the role badge
    When I log out and log in as "readonly@acmepets.test" with password "password"
    Then I should see "Read Only" as the role badge
