@e2e @vendor-portal @change-request @list
Feature: Vendor Portal Change Request List & Filtering
  As a vendor user
  I want to browse and filter my change requests by status
  So that I can quickly find requests in a specific state

  # ─── ACCEPTANCE CRITERIA ───────────────────────────────────────────────
  # AC-1: List displays all change requests in a sortable table
  # AC-2: Status filter chips filter the table to matching status only
  # AC-3: Empty state shows appropriate message with action link
  # AC-4: Draft rows show delete action; non-draft rows show view only
  # AC-5: Clicking "View" navigates to the detail page
  # AC-6: Delete draft from list removes it immediately
  # ────────────────────────────────────────────────────────────────────────

  # ─── PRIORITY: P1 — List navigation is second-tier after lifecycle ──────

  Background:
    Given I am logged in as "admin@acmepets.test" with password "password"
    And the following change requests exist for "Acme Pet Supplies":
      | SKU          | Type           | Status       | Title                           |
      | DOG-BOWL-01  | Description    | Submitted    | Update bowl description         |
      | CAT-TOY-05   | Image          | Draft        | New laser pointer photos        |
      | DOG-LEASH-03 | Description    | Approved     | Leash description correction    |
      | CAT-FOOD-02  | DataCorrection | NeedsMoreInfo| Cat food weight correction      |
      | DOG-BED-06   | Image          | Rejected     | Dog bed photo update            |
      | DOG-TREAT-04 | Description    | Withdrawn    | Treat description (withdrawn)   |

  @list @p1
  Scenario: Vendor sees all change requests in the list
    When I navigate to the change requests list page
    Then I should see a table with data-testid "change-requests-table"
    And the table should display 6 change requests
    And each row should show SKU, Title, Type chip, Status chip, and Created date

  @list @p1
  Scenario: Vendor filters change requests by Submitted status
    Given I am on the change requests list page
    When I click the "Submitted" status filter chip
    Then the table should display 1 change request
    And I should see the request with SKU "DOG-BOWL-01" and status "Submitted"

  @list @p1
  Scenario: Vendor filters change requests by Draft status
    Given I am on the change requests list page
    When I click the "Draft" status filter chip
    Then the table should display 1 change request
    And I should see the request with SKU "CAT-TOY-05" and status "Draft"
    And I should see a delete action button for the draft request

  @list @p1
  Scenario: Vendor clicks All chip to clear status filter
    Given I am on the change requests list page
    And I have filtered by "Submitted" status
    When I click the "All" status filter chip
    Then the table should display 6 change requests

  @list @p1
  Scenario: Filter with no matching results shows appropriate empty state
    Given I am on the change requests list page
    When I click the "Superseded" status filter chip
    Then I should see an empty state message with data-testid "no-requests-message"
    And the message should indicate no Superseded requests found
    And I should see a "Clear filter" option

  @list @p1
  Scenario: Vendor navigates from list to change request detail
    Given I am on the change requests list page
    When I click the "View" button for the request with SKU "DOG-BOWL-01"
    Then I should be on the change request detail page
    And I should see the title "Update bowl description"

  @list @p2
  Scenario: Vendor deletes a draft change request from the list
    Given I am on the change requests list page
    And I have filtered by "Draft" status
    When I click the delete button for the draft request with SKU "CAT-TOY-05"
    Then the request should be removed from the table
    And I should see a success snackbar confirming the deletion

  @list @p2
  Scenario: Empty state when vendor has no change requests at all
    Given the vendor has no change requests
    When I navigate to the change requests list page
    Then I should see an empty state message with data-testid "no-requests-message"
    And the message should contain a link to submit a new change request
