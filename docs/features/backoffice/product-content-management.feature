Feature: Product Content Management
  As a copy writer
  I want to update product descriptions and display names in the Backoffice
  So that customers see accurate, compelling copy without waiting for an engineer to deploy a database change

  Background:
    Given I am logged in to the Backoffice as a "CopyWriter"
    And the product catalog contains a product with SKU "PET-COLLAR-001" and display name "Basic Dog Collar"

  Scenario: Copy writer updates a product description
    Given the current description for SKU "PET-COLLAR-001" is "A collar for dogs"
    When I navigate to the product content editor for SKU "PET-COLLAR-001"
    And I update the description to "Durable nylon collar with quick-release buckle, available in 5 sizes for breeds from chihuahua to great dane"
    And I click Save
    Then the Backoffice shows a success confirmation
    And the Product Catalog BC records a "ProductDescriptionUpdated" event attributed to my admin user ID
    And the updated description is visible when I reload the product content editor

  Scenario: Copy writer updates a product display name
    When I navigate to the product content editor for SKU "PET-COLLAR-001"
    And I update the display name to "Durable Quick-Release Dog Collar"
    And I click Save
    Then the Backoffice shows a success confirmation
    And the display name is updated in the Product Catalog BC

  Scenario: Copy writer cannot submit an empty description
    When I navigate to the product content editor for SKU "PET-COLLAR-001"
    And I clear the description field
    And I click Save
    Then the Backoffice shows a validation error "Description cannot be empty"
    And no changes are sent to the Product Catalog BC

  Scenario: Copy writer cannot submit a description exceeding the maximum length
    When I navigate to the product content editor for SKU "PET-COLLAR-001"
    And I enter a description with 5001 characters
    And I click Save
    Then the Backoffice shows a validation error "Description cannot exceed 5000 characters"
    And no changes are sent to the Product Catalog BC

  Scenario: Copy writer cannot access pricing or inventory data
    When I navigate to the Backoffice home
    Then I do not see a "Pricing" link in the navigation
    And I do not see an "Inventory" link in the navigation
    And I do not see a "Customers" link in the navigation

  Scenario: Non-CopyWriter role cannot update product descriptions
    Given I am logged in to the Backoffice as a "WarehouseClerk"
    When I attempt to update the description for SKU "PET-COLLAR-001" via the API
    Then the Backoffice API returns 403 Forbidden
    And no changes are sent to the Product Catalog BC

  Scenario: Copy writer searches for a product by name
    Given the product catalog contains 100 active products
    When I search for products with the term "collar"
    Then I see a list of products whose names contain "collar"
    And each result shows the product SKU, display name, truncated description, and last-edited-by

  @ignore @future
  Scenario: Copy writer previews how the description will appear on the storefront
    When I update the description for SKU "PET-COLLAR-001"
    Then I can preview the rendered description in a read-only storefront preview panel before saving
