Feature: Product Catalog Event Sourcing Migration
  As a catalog administrator
  I want the product catalog to use event sourcing
  So that we have a full audit trail and can support granular change events

  Background:
    Given the product catalog service is running

  # ─────────────────────────────────────────────
  # Migration from Document Store
  # ─────────────────────────────────────────────

  Scenario: Migrate existing product document to event stream
    Given a product exists in the document store with:
      | SKU       | DOG-BOWL-01          |
      | Name      | Ceramic Dog Bowl     |
      | Category  | Dogs                 |
      | Status    | Active               |
    When the migration process runs for SKU "DOG-BOWL-01"
    Then a ProductMigrated event is appended to the event stream
    And the ProductMigrated event contains a full snapshot of the product data
    And the ProductCatalogView projection produces the same data as the original document
    And the product is retrievable by SKU "DOG-BOWL-01" with identical data

  Scenario: Migration is idempotent — running twice does not duplicate events
    Given product "DOG-BOWL-01" has already been migrated
    When the migration process runs for SKU "DOG-BOWL-01" again
    Then no new events are appended
    And the product data remains unchanged

  # ─────────────────────────────────────────────
  # Event-Sourced Product Creation
  # ─────────────────────────────────────────────

  Scenario: Create new product via event sourcing
    When I create a product with:
      | SKU         | CAT-PERCH-01               |
      | Name        | Deluxe Cat Window Perch     |
      | Category    | Cats                        |
      | Description | Suction-cup mounted perch   |
      | Status      | Active                      |
    Then a ProductCreated event is appended to the stream for "CAT-PERCH-01"
    And the ProductCatalogView shows the new product
    And a ProductAdded integration event is published

  Scenario: Cannot create product with duplicate SKU
    Given product "CAT-PERCH-01" already exists
    When I attempt to create another product with SKU "CAT-PERCH-01"
    Then the request is rejected with "Product with SKU already exists"
    And no events are appended

  # ─────────────────────────────────────────────
  # Granular Update Events
  # ─────────────────────────────────────────────

  Scenario: Change product name emits granular event
    Given product "DOG-BOWL-01" exists with name "Ceramic Dog Bowl"
    When I change the product name to "Premium Ceramic Dog Bowl"
    Then a ProductNameChanged event is appended with:
      | PreviousName | Ceramic Dog Bowl         |
      | NewName      | Premium Ceramic Dog Bowl |
    And the ProductCatalogView shows name "Premium Ceramic Dog Bowl"
    And a ProductContentUpdated integration event is published

  Scenario: Change product description emits granular event
    Given product "DOG-BOWL-01" exists with description "A durable ceramic bowl"
    When I change the product description to "A premium, dishwasher-safe ceramic bowl for dogs of all sizes"
    Then a ProductDescriptionChanged event is appended
    And the ProductCatalogView shows the updated description

  Scenario: Change product category emits granular event
    Given product "DOG-BOWL-01" exists in category "Dogs"
    When I change the product category to "Dog Feeding Supplies"
    Then a ProductCategoryChanged event is appended with:
      | PreviousCategory | Dogs                 |
      | NewCategory      | Dog Feeding Supplies |
    And the ProductCatalogView shows category "Dog Feeding Supplies"
    And a ProductCategoryChanged integration event is published

  # ─────────────────────────────────────────────
  # Status Changes
  # ─────────────────────────────────────────────

  Scenario: Discontinue a product
    Given product "DOG-BOWL-01" exists with status "Active"
    When I change the product status to "Discontinued" with reason "Supplier discontinued"
    Then a ProductStatusChanged event is appended with:
      | PreviousStatus | Active                 |
      | NewStatus      | Discontinued           |
      | Reason         | Supplier discontinued  |
    And the ProductCatalogView shows status "Discontinued"
    And a ProductDiscontinued integration event is published

  Scenario: Reactivate a discontinued product
    Given product "DOG-BOWL-01" has status "Discontinued"
    When I change the product status to "Active"
    Then a ProductStatusChanged event is appended with:
      | PreviousStatus | Discontinued |
      | NewStatus      | Active       |
    And the ProductCatalogView shows status "Active"

  # ─────────────────────────────────────────────
  # Soft Delete and Restore
  # ─────────────────────────────────────────────

  Scenario: Soft delete a product
    Given product "DOG-BOWL-01" exists with status "Active"
    When I soft delete product "DOG-BOWL-01"
    Then a ProductSoftDeleted event is appended
    And the product no longer appears in active product listings
    And the product event stream is preserved for audit

  Scenario: Restore a soft-deleted product
    Given product "DOG-BOWL-01" has been soft deleted
    When I restore product "DOG-BOWL-01"
    Then a ProductRestored event is appended
    And the product reappears in active product listings with its previous data

  # ─────────────────────────────────────────────
  # Projection Consistency
  # ─────────────────────────────────────────────

  Scenario: Multiple changes produce correct final state
    Given I create a product with SKU "FISH-TANK-01" and name "Basic Fish Tank"
    When I change the product name to "Premium Fish Tank"
    And I change the product category to "Aquarium Supplies"
    And I update the product description to "Professional-grade aquarium"
    Then the ProductCatalogView shows:
      | SKU         | FISH-TANK-01               |
      | Name        | Premium Fish Tank          |
      | Category    | Aquarium Supplies          |
      | Description | Professional-grade aquarium |
    And the event stream contains 4 events in chronological order
