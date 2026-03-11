@e2e @vendor-portal @change-request
Feature: Vendor Portal Change Request Lifecycle
  As a vendor catalog manager
  I want to create, submit, view, and act on change requests
  So that I can update my product catalog through the managed approval process

  # ─── ACCEPTANCE CRITERIA ───────────────────────────────────────────────
  # AC-1: Can save a change request as Draft with minimal fields (SKU required)
  # AC-2: Can submit a request (creates draft then immediately submits)
  # AC-3: Submitted request appears in list with "Submitted" status chip
  # AC-4: Detail page shows all request fields, status timeline, and action buttons
  # AC-5: Can withdraw a Submitted request → status changes to "Withdrawn"
  # AC-6: NeedsMoreInfo request shows question + response form
  # AC-7: Providing additional info resubmits the request
  # AC-8: Draft can be deleted from detail page
  # ────────────────────────────────────────────────────────────────────────

  # ─── MUDSELECT STRATEGY ────────────────────────────────────────────────
  # The SubmitChangeRequest form contains a MudSelect for Type.
  # Known risk from Storefront E2E: MudSelect dropdown may not open reliably
  # in Blazor WASM + headless Playwright.
  #
  # MITIGATION:
  # 1. Default Type is "Description" — happy path scenarios don't change it
  # 2. Scenarios requiring Type change are tagged @mudselect-risk
  # 3. If MudSelect fails, mark those scenarios @ignore and note in test report
  # 4. The Type selection IS covered by 143 integration tests — E2E adds UI journey value
  # ────────────────────────────────────────────────────────────────────────

  # ─── TEST DATA SETUP ───────────────────────────────────────────────────
  # Pre-seed via VendorPortal.Api endpoints (authenticated with test JWT):
  #   - Create change requests in various states for list/detail scenarios
  #   - Seed NeedsMoreInfo request with Question field for response scenario
  #   - No pre-seeding needed for create/submit scenarios (fresh form)
  # ────────────────────────────────────────────────────────────────────────

  # ─── PRIORITY: P0 — Core vendor workflow ────────────────────────────────

  @change-request @p0 @smoke
  Scenario: Vendor submits a description change request end-to-end
    Given I am logged in as "admin@acmepets.test" with password "password"
    When I navigate to the submit change request page
    And I enter "DOG-BOWL-01" in the SKU field
    And I enter "Update ceramic bowl description" in the Title field
    And I enter "The description should highlight the non-slip base and dishwasher-safe material" in the Details field
    And I click the "Submit Request" button
    Then I should see a success snackbar "Change request submitted successfully."
    And I should be redirected to the change requests list page
    When I click on the newly created change request
    Then I should see the change request detail page
    And the status should show "Submitted"
    And the SKU should display "DOG-BOWL-01"
    And the title should display "Update ceramic bowl description"

  @change-request @p0
  Scenario: Vendor saves a change request as draft
    Given I am logged in as "admin@acmepets.test" with password "password"
    When I navigate to the submit change request page
    And I enter "CAT-TOY-05" in the SKU field
    And I enter "New laser pointer images" in the Title field
    And I enter "Updated product photography with lifestyle shots" in the Details field
    And I click the "Save as Draft" button
    Then I should see a success snackbar "Change request saved as draft."
    And I should be redirected to the change requests list page

  @change-request @p1
  Scenario: Vendor views change request detail with full information
    Given I am logged in as "admin@acmepets.test" with password "password"
    And a change request exists with:
      | Field   | Value                           |
      | SKU     | DOG-LEASH-03                    |
      | Type    | Description                     |
      | Status  | Submitted                       |
      | Title   | Leash material description fix  |
      | Details | Should say "reinforced nylon"   |
    When I navigate to the change request detail page for that request
    Then I should see the SKU "DOG-LEASH-03"
    And I should see the title "Leash material description fix"
    And I should see the details "Should say \"reinforced nylon\""
    And I should see the status chip showing "Submitted"
    And I should see a "Withdraw Request" button

  @change-request @p1
  Scenario: Vendor withdraws a submitted change request
    Given I am logged in as "admin@acmepets.test" with password "password"
    And a change request exists with:
      | Field  | Value                          |
      | SKU    | DOG-BOWL-01                    |
      | Type   | Description                    |
      | Status | Submitted                      |
      | Title  | Withdraw me                    |
    When I navigate to the change request detail page for that request
    And I click the "Withdraw Request" button identified by data-testid "withdraw-btn"
    Then the status chip should update to "Withdrawn"
    And the "Withdraw Request" button should no longer be visible

  @change-request @p1
  Scenario: Vendor responds to a NeedsMoreInfo change request
    Given I am logged in as "admin@acmepets.test" with password "password"
    And a change request exists with:
      | Field    | Value                                        |
      | SKU      | CAT-FOOD-02                                  |
      | Type     | DataCorrection                               |
      | Status   | NeedsMoreInfo                                |
      | Title    | Weight correction for cat food               |
      | Question | What is the correct weight in grams?         |
    When I navigate to the change request detail page for that request
    Then I should see a warning alert with data-testid "needs-more-info-alert"
    And I should see the question "What is the correct weight in grams?"
    When I enter "The correct weight is 450g per bag" in the additional info response field
    And I click the "Provide Information" button identified by data-testid "provide-info-btn"
    Then the status chip should update to "Submitted"
    And I should see my response in the info responses thread

  @change-request @p2
  Scenario: Vendor deletes a draft change request from detail page
    Given I am logged in as "admin@acmepets.test" with password "password"
    And a change request exists with:
      | Field  | Value                        |
      | SKU    | DOG-TREAT-04                 |
      | Type   | Image                        |
      | Status | Draft                        |
      | Title  | Draft treat photos           |
    When I navigate to the change request detail page for that request
    And I click the "Delete Draft" button identified by data-testid "delete-draft-btn"
    Then I should be redirected to the change requests list page
    And the deleted draft should not appear in the list

  @change-request @p2
  Scenario: Vendor views a rejected change request with rejection reason
    Given I am logged in as "admin@acmepets.test" with password "password"
    And a change request exists with:
      | Field           | Value                                     |
      | SKU             | DOG-BED-06                                |
      | Type            | Image                                     |
      | Status          | Rejected                                  |
      | Title           | New dog bed photos                        |
      | RejectionReason | Images do not meet minimum 1200x1200 resolution |
    When I navigate to the change request detail page for that request
    Then I should see an error alert with data-testid "rejection-reason-alert"
    And the rejection reason should display "Images do not meet minimum 1200x1200 resolution"
    And I should NOT see the "Withdraw Request" button

  @change-request @p2
  Scenario: Vendor views a superseded change request with replacement link
    Given I am logged in as "admin@acmepets.test" with password "password"
    And a change request exists with:
      | Field              | Value                        |
      | SKU                | DOG-BOWL-01                  |
      | Type               | Description                  |
      | Status             | Superseded                   |
      | Title              | Old bowl description         |
      | ReplacedByRequestId| <replacement-request-id>     |
    When I navigate to the change request detail page for that request
    Then I should see an info alert with data-testid "superseded-alert"
    And the alert should contain a link to the replacement request

  @change-request @p2 @mudselect-risk
  Scenario: Vendor submits a DataCorrection type change request
    Given I am logged in as "admin@acmepets.test" with password "password"
    When I navigate to the submit change request page
    And I enter "CAT-FOOD-02" in the SKU field
    And I select "Data Correction" from the change request type dropdown
    And I enter "Correct UPC barcode" in the Title field
    And I enter "Current UPC 012345678901 should be 012345678902" in the Details field
    And I click the "Submit Request" button
    Then I should see a success snackbar "Change request submitted successfully."
    And I should be redirected to the change requests list page
