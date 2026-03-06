Feature: Vendor Product Management
  As a vendor user with CatalogManager or Admin role
  I want to manage my product catalog and submit corrections through the vendor portal
  So that my product data on CritterSupply marketplace stays accurate and current

  Background:
    Given I am authenticated as vendor "Acme Pet Supplies" (tenant "acme-pet-supplies")
    And my JWT carries VendorTenantId from the Vendor Identity service
    And the following products are associated with my vendor tenant:
      | SKU         | Product Name           |
      | ACME-TOY-01 | Squeaky Bone Toy       |
      | ACME-TRT-01 | Salmon Training Treats |
    And the Vendor Portal BC is running
    And the Catalog BC is running

  Scenario: Vendor views their product catalog (VendorProductCatalog projection)
    When I navigate to "My Products"
    Then I see the following products associated with my account:
      | SKU         | Product Name           | Status |
      | ACME-TOY-01 | Squeaky Bone Toy       | Active |
      | ACME-TRT-01 | Salmon Training Treats | Active |
    And I do NOT see products from other vendor tenants

  Scenario: Vendor submits a product description change request
    Given I am on the "Request Change" page for SKU "ACME-TOY-01"
    When I select request type "Description Update"
    And I enter the new description: "Durable rubber squeaky bone toy for dogs. BPA-free and non-toxic."
    And I click "Save as Draft"
    Then a ChangeRequest is created with Status "Draft" for ACME-TOY-01
    When I click "Submit for Review"
    Then the ChangeRequest Status changes to "Submitted"
    And a "DescriptionChangeRequested" integration event is published to the Catalog BC
    And the original product description remains unchanged until approval

  Scenario: Catalog admin approves a change request
    Given a ChangeRequest for ACME-TOY-01 with Status "Submitted" exists
    When the Catalog BC publishes a "DescriptionChangeApproved" event for this request
    Then the ChangeRequest Status changes to "Approved"
    And a "ChangeRequestStatusUpdated" SignalR message is sent to the "vendor:{tenantId}" hub group
    And a "ChangeRequestDecisionPersonal" SignalR message is sent to "user:{submittingUserId}"
    And the vendor sees a toast notification: "✅ Your description update for ACME-TOY-01 was approved!"

  Scenario: Vendor submits new images using claim-check pattern (no raw bytes in events)
    Given I am on the "Request Image Change" page for ACME-TOY-01
    When I select 2 product images for upload
    And the images are uploaded directly to object storage via pre-signed URL
    And I click "Submit Image Change Request"
    Then a ChangeRequest is created with ImageStorageKeys (not raw image bytes)
    And the "ImageUploadRequested" integration event carries ImageStorageKeys only

  Scenario: Vendor views sales analytics dashboard
    When I navigate to "Analytics"
    Then I see sales metrics for my products:
      | Metric               | Filter       |
      | Total Revenue (30d)  | All products |
      | Units Sold (30d)     | All products |
      | Top Product by Units | This month   |
    And I see a "Last updated" timestamp on the analytics panels
    And the data is scoped to MY products only (not other vendors' products)

  Scenario: Vendor receives a real-time order sale metric update via SignalR
    Given I am viewing the Analytics Dashboard with SignalR connected
    When a customer places an order containing my product "ACME-TOY-01"
    And the OrderPlaced event is processed by the Vendor Portal fan-out handler
    Then I receive a "SalesMetricUpdated" notification via the "vendor:{tenantId}" hub group
    And my dashboard updates without requiring a page refresh

  Scenario: Vendor cannot access another tenant's data
    When I attempt to query analytics with a different vendor's tenantId in the request
    Then the request is rejected with 403 Forbidden
    And I only ever see data scoped to my VendorTenantId from my JWT claims
