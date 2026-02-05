Feature: Add Product to Catalog
  As a catalog administrator
  I want to add new products to the catalog
  So that customers can browse and purchase them

  Background:
    Given the product catalog is empty

  Scenario: Add a valid product
    Given I have a product with SKU "DOG-BOWL-01"
    And the product name is "Ceramic Dog Bowl"
    And the product category is "Dogs"
    And the product description is "A durable ceramic bowl for dogs"
    When I add the product to the catalog
    Then the product should be successfully created
    And the product should be retrievable by SKU "DOG-BOWL-01"
    And the product status should be "Active"

  Scenario: Add product with images
    Given I have a product with SKU "CAT-TOY-05"
    And the product name is "Interactive Cat Laser"
    And the product category is "Cats"
    And the product has the following images:
      | Url                                    | AltText           | DisplayOrder |
      | https://example.com/cat-laser-01.jpg   | Cat laser pointer | 0            |
      | https://example.com/cat-laser-02.jpg   | Laser in use      | 1            |
    When I add the product to the catalog
    Then the product should be successfully created
    And the product should have 2 images

  Scenario: Cannot add product with duplicate SKU
    Given a product with SKU "DOG-BOWL-01" already exists
    When I attempt to add another product with SKU "DOG-BOWL-01"
    Then the request should fail with status code 409
    And the error message should indicate "Product with SKU already exists"

  Scenario: Cannot add product with invalid SKU format
    Given I have a product with SKU "invalid sku!"
    And the product name is "Test Product"
    And the product category is "Dogs"
    When I add the product to the catalog
    Then the request should fail with status code 400
    And the error message should contain "SKU"
