Feature: Vendor Product Change Requests
  As a vendor user with CatalogManager or Admin role
  I want to submit and track product change requests
  So that my product data on CritterSupply stays accurate and current

  Background:
    Given the Vendor Portal service is running
    And the Catalog BC is running
    And I am authenticated as vendor "Coastal Pet Supplies Co." with Role "CatalogManager"
    And the following products are associated with my vendor tenant:
      | SKU        | Product Name          |
      | CPE-TOY-01 | Coastal Rope Toy      |
      | CPE-TRT-01 | Salmon Biscuit Treats |

  # ─────────────────────────────────────────────
  # Drafting a Change Request
  # ─────────────────────────────────────────────

  Scenario: Vendor starts a description change request as a draft
    When I navigate to "Products" → "CPE-TOY-01" → "Request Change"
    And I select request type "Description Update"
    And I enter the new description:
      """
      Premium braided rope toy for dogs. Made from 100% natural cotton fibers.
      BPA-free and non-toxic. Suitable for medium to large breeds.
      """
    And I click "Save as Draft"
    Then a ChangeRequest is created with:
      | Field   | Value              |
      | Sku     | CPE-TOY-01         |
      | Type    | DescriptionUpdate  |
      | Status  | Draft              |
    And I can see the draft in my "Drafts" list
    And the original product description is NOT changed yet

  Scenario: Vendor cannot draft a change for a product not in their catalog
    When I attempt to submit a change request for SKU "COMPETITOR-SKU-99"
    Then the request is rejected with "Product not associated with your vendor account"
    And no ChangeRequest is created

  # ─────────────────────────────────────────────
  # Submitting a Change Request
  # ─────────────────────────────────────────────

  Scenario: Vendor submits a draft description change request
    Given I have a Draft ChangeRequest for CPE-TOY-01 (DescriptionUpdate)
    When I click "Submit for Review"
    Then the ChangeRequest Status changes to "Submitted"
    And a "DescriptionChangeRequested" integration event is published to the Catalog BC via Wolverine outbox
    And the request appears in my "Open Requests" list with status "Pending Review"

  Scenario: Catalog BC unavailable when change request submitted — request still succeeds
    Given the Catalog BC is temporarily unavailable
    When I submit a DescriptionUpdate change request for CPE-TOY-01
    Then the ChangeRequest is persisted locally with Status "Submitted"
    And I see status "Pending Review" (not an error)
    And the "DescriptionChangeRequested" integration message is queued in Wolverine outbox
    When the Catalog BC comes back online
    Then the message is delivered from the outbox
    And the Catalog BC receives the change request

  Scenario: Only one active change request per product per request type
    Given I have a Submitted ChangeRequest for CPE-TOY-01 (DescriptionUpdate)
    When I try to start a new DescriptionUpdate for CPE-TOY-01
    Then I see a warning: "You already have an active Description Update request for CPE-TOY-01"
    And I am given the options:
      | Option                                    |
      | Withdraw the existing request and proceed |
      | Cancel this new request                   |
    When I select "Withdraw the existing request and proceed"
    Then the existing ChangeRequest Status changes to "Withdrawn"
    And a "ChangeRequestWithdrawn" event is recorded with Reason "Superseded by new submission"
    And the new Draft ChangeRequest is created

  # ─────────────────────────────────────────────
  # Image Upload (Claim-Check Pattern)
  # ─────────────────────────────────────────────

  Scenario: Vendor submits product images via claim-check pattern
    Given I am on the "Request Image Change" page for CPE-TOY-01
    When I select 2 product images for upload
    Then the portal requests a pre-signed upload URL from object storage
    And I see a progress indicator: "Uploading images..."
    When the images are uploaded to object storage
    Then I see "Images uploaded successfully"
    And I click "Submit Image Change Request"
    Then the ChangeRequest is created with:
      | Field            | Value                               |
      | Sku              | CPE-TOY-01                          |
      | Type             | ImageUpload                         |
      | Status           | Submitted                           |
      | ImageStorageKeys | (list of 2 object storage keys)     |
    And the "ImageUploadRequested" integration event carries ImageStorageKeys (NOT raw image bytes)
    And I see BOTH confirmations: "Images uploaded" AND "Change request submitted"

  Scenario: Image upload to object storage fails before request creation
    Given I have selected images for upload
    When the upload to object storage fails
    Then I see an error: "Image upload failed. Please try again."
    And NO ChangeRequest is created
    And NO integration event is published

  # ─────────────────────────────────────────────
  # Withdrawing a Change Request
  # ─────────────────────────────────────────────

  Scenario: Vendor withdraws a submitted change request
    Given I have a ChangeRequest for CPE-TOY-01 with Status "Submitted"
    When I click "Withdraw Request" and confirm
    Then the ChangeRequest Status changes to "Withdrawn"
    And a "ChangeRequestWithdrawn" event is recorded
    And the request moves from "Open Requests" to "Request History"

  Scenario: Cannot withdraw an approved or rejected change request
    Given I have a ChangeRequest for CPE-TOY-01 with Status "Approved"
    When I attempt to click "Withdraw Request"
    Then the button is not available (request is in a terminal state)
    And attempting the action via API is rejected: "Cannot withdraw a request in Approved status"

  # ─────────────────────────────────────────────
  # Receiving Change Request Decisions via SignalR
  # ─────────────────────────────────────────────

  Scenario: Vendor receives real-time approval notification via SignalR
    Given I have a ChangeRequest (requestId: "req-abc-123") for CPE-TOY-01 with Status "Submitted"
    And I am viewing the Vendor Portal with an active SignalR connection
    When the Catalog BC publishes a "DescriptionChangeApproved" event for "req-abc-123"
    Then I receive a "ChangeRequestStatusUpdated" SignalR notification to the tenant group
    And I receive a "ChangeRequestDecisionPersonal" notification to my user group
    And I see a toast notification: "✅ Your description update for CPE-TOY-01 was approved!"
    And the ChangeRequest Status in my Open Requests list changes to "Approved"
    And the request moves to "Request History"

  Scenario: Vendor receives real-time rejection notification with reason
    Given I have a ChangeRequest for CPE-TOY-01 with Status "Submitted"
    And I am viewing the Vendor Portal with an active SignalR connection
    When the Catalog BC publishes a "DescriptionChangeRejected" event with:
      | Reason | Description must be at least 150 characters |
    Then I see a toast notification: "❌ Change request rejected: 'Description must be at least 150 characters'"
    And I see a call-to-action: "Submit new request"
    And the ChangeRequest Status changes to "Rejected"

  Scenario: Offline vendor sees change request decision on next login
    Given I am NOT logged into the Vendor Portal
    And I have a ChangeRequest for CPE-TOY-01 with Status "Submitted"
    When the Catalog BC approves the change request
    Then the ChangeRequestStatus projection is updated
    And a notification is persisted in my notification feed
    When I log in to the Vendor Portal
    Then I see the approval notification in my feed
    And the change request shows Status "Approved" in my history

  # ─────────────────────────────────────────────
  # NeedsMoreInfo Round-Trip
  # ─────────────────────────────────────────────

  Scenario: Catalog team requests more information before deciding
    Given I have a ChangeRequest for CPE-TRT-01 with Status "Submitted"
    And I am viewing the Vendor Portal with an active SignalR connection
    When the Catalog BC publishes a "MoreInfoRequestedForChangeRequest" event with:
      | Question | Please provide the source of the "100% natural" claim with supplier documentation |
    Then I receive a toast notification: "📋 Catalog team has a question about your request"
    And I see a call-to-action: "Respond to request"
    And the ChangeRequest Status changes to "NeedsMoreInfo"
    And the Catalog team's question is displayed on the request detail page

  Scenario: Vendor responds to a NeedsMoreInfo request
    Given I have a ChangeRequest for CPE-TRT-01 with Status "NeedsMoreInfo"
    And the question is "Please provide documentation for the '100% natural' claim"
    When I submit a ProvideAdditionalInfo command with:
      | Response | Please find attached our supplier cert from Pacific Fisheries Inc. (doc ref: PFI-2026-NAT-003) |
    Then the ChangeRequest Status changes back to "Submitted"
    And the response is recorded on the aggregate
    And the Catalog BC is notified that additional information has been provided

  Scenario: Vendor withdraws a NeedsMoreInfo request instead of responding
    Given I have a ChangeRequest for CPE-TOY-01 with Status "NeedsMoreInfo"
    When I click "Withdraw Request" and confirm
    Then the ChangeRequest Status changes to "Withdrawn"
    And a "ChangeRequestWithdrawn" event is recorded

  # ─────────────────────────────────────────────
  # Change Request History
  # ─────────────────────────────────────────────

  Scenario: Vendor views their complete change request history
    Given my tenant has the following completed change requests:
      | RequestId  | Sku        | Type              | Status    |
      | req-001    | CPE-TOY-01 | DescriptionUpdate | Approved  |
      | req-002    | CPE-TRT-01 | ImageUpload       | Rejected  |
      | req-003    | CPE-TOY-01 | DescriptionUpdate | Withdrawn |
    When I navigate to "Request History"
    Then I see all 3 requests with their status and timestamps
    And I can filter by Status (All / Approved / Rejected / Withdrawn / Replaced)
    And I can filter by SKU

  Scenario: Replaced request shows linkage to the newer request
    Given a ChangeRequest "req-001" for CPE-TOY-01 (DescriptionUpdate) was approved in March
    And a newer ChangeRequest "req-002" for CPE-TOY-01 (DescriptionUpdate) was approved in April
    When I view "req-001" in my request history
    Then the Status shows "Replaced"
    And I see a note: "Replaced by a newer request" with a link to "req-002"
    And the original approval timestamp is preserved

  # ─────────────────────────────────────────────
  # Role-Based Access Control
  # ─────────────────────────────────────────────

  Scenario: ReadOnly user cannot submit change requests
    Given I am authenticated as a vendor user with Role "ReadOnly"
    When I navigate to "Products" → "CPE-TOY-01"
    Then the "Request Change" button is not visible
    And attempting to POST a change request via API returns 403 Forbidden

  Scenario: CatalogManager can submit but cannot manage users
    Given I am authenticated as a vendor user with Role "CatalogManager"
    When I submit a change request for CPE-TOY-01
    Then the request is accepted and processed
    When I navigate to "Team Members"
    Then I see a message: "User management is available to Admin users only"
