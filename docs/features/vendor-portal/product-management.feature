Feature: Vendor Product Management
  As a vendor
  I want to manage my products through the vendor portal
  So that I can sell items on the CritterSupply marketplace

  Background:
    Given I am logged in as vendor "Acme Pet Supplies" (tenant ID "acme-tenant-123")
    And I have "Owner" role permissions
    And the Vendor Portal BC is running
    And the Product Catalog BC is running

  Scenario: Vendor adds new product (no approval required for Draft)
    When I navigate to "Products" → "Add New Product"
    And I fill in the product form:
      | Field       | Value                                    |
      | SKU         | ACME-TOY-01                              |
      | Name        | Squeaky Bone Toy                         |
      | Category    | Dogs > Toys                              |
      | Price       | $12.99                                   |
      | Description | Durable rubber bone toy for dogs...     |
      | Status      | Draft                                    |
    And I upload 3 product images
    And I submit the product form
    Then the product should be created in Product Catalog BC
    And the product should appear in my vendor product list
    And the product status should be "Draft" (not visible to customers)

  Scenario: Vendor publishes product (makes visible to customers)
    Given I have a product "Squeaky Bone Toy" with status "Draft"
    When I click "Publish Product"
    Then the product status should change to "Active"
    And the product should become visible in customer storefront
    And customers should be able to add it to cart

  Scenario: Vendor updates published product (requires approval)
    Given I have a product "Squeaky Bone Toy" with status "Active"
    When I click "Edit Description"
    And I change the description to include safety information
    And I provide reason: "Added BPA-free information per customer requests"
    And I submit the change request
    Then a change request should be created with status "Pending"
    And platform admins should be notified of pending review
    And the original product description should remain unchanged until approved

  Scenario: Platform admin approves change request
    Given a change request "change-req-123" exists with status "Pending"
    When the platform admin reviews the change request
    And the admin clicks "Approve"
    Then the change request status should change to "Approved"
    And the changes should be applied to the product in Product Catalog BC
    And the vendor should receive notification: "Your change request has been approved"

  Scenario: Vendor updates inventory via CSV bulk import
    Given I have 500 products in my catalog
    When I navigate to "Inventory" → "Bulk Update"
    And I click "Download Current Inventory CSV"
    Then I should receive a CSV file with all 500 products and their stock levels
    
    When I update quantities in the CSV
    And I upload the modified CSV
    Then the system should validate all SKUs and quantities
    And I should see a preview of changes (50 increased, 40 decreased)
    When I confirm the changes
    Then all 500 products should be updated in Inventory BC
    And I should see confirmation: "Inventory updated successfully"

  Scenario: Vendor receives order notification
    Given a customer places an order containing my product "Squeaky Bone Toy"
    When the Orders BC publishes "OrderPlaced" integration message
    Then I should see a new order in my vendor dashboard
    And the order should show "Ready to Ship" status
    And I should receive email notification of new order

  Scenario: Vendor marks order as shipped
    Given I have an order "order-abc-123" with status "Ready to Ship"
    When I click "Mark as Shipped"
    And I enter tracking number "1Z999AA10123456784"
    And I select carrier "UPS"
    Then the Fulfillment BC should be notified
    And "ShipmentDispatched" integration message should be published
    And the customer should receive tracking number via email
    And my dashboard should show order status: "Shipped"

  Scenario: Vendor views sales analytics
    When I navigate to "Analytics" dashboard
    Then I should see sales metrics for last 30 days:
      | Metric               | Value     |
      | Total Orders         | 142       |
      | Total Revenue        | $4,256.78 |
      | Average Order Value  | $29.98    |
      | Top Product          | Squeaky Bone Toy (32 units) |
    And I should see low stock alerts for 3 products
    And I should see a sales trend chart (daily revenue graph)
