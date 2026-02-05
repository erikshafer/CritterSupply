Feature: Product Browsing
  As a customer visiting the storefront
  I want to browse and search for pet products
  So that I can find items I want to purchase

  Background:
    Given the following products exist in the catalog:
      | SKU           | Name                        | Category | Price  | Status |
      | DOG-BOWL-01   | Ceramic Dog Bowl (Large)    | Dogs     | 19.99  | Active |
      | DOG-BOWL-02   | Stainless Steel Dog Bowl    | Dogs     | 24.99  | Active |
      | DOG-TOY-01    | Rope Tug Toy                | Dogs     | 12.99  | Active |
      | CAT-TOY-05    | Interactive Cat Laser       | Cats     | 29.99  | Active |
      | CAT-COLLAR-01 | Reflective Cat Collar       | Cats     | 8.99   | Active |
      | FISH-TANK-20  | 20 Gallon Fish Tank         | Fish     | 89.99  | Active |
      | BIRD-CAGE-01  | Large Bird Cage             | Birds    | 149.99 | Active |
      | DOG-BOWL-03   | Ceramic Dog Bowl (Seasonal) | Dogs     | 19.99  | OutOfSeason |

  # ========================================
  # Product Listing Page
  # ========================================

  Scenario: View all products on homepage
    Given I navigate to the homepage
    When the page finishes loading
    Then I should see a product listing
    And the listing should display 7 active products
    And each product card should show:
      | Field       | Present |
      | Product Image | Yes   |
      | Product Name  | Yes   |
      | Price         | Yes   |
      | Add to Cart   | Yes   |
    And products with status "OutOfSeason" should not be displayed

  Scenario: Product listing paginates results
    Given the catalog contains 50 active products
    And I navigate to the homepage
    When the page finishes loading
    Then I should see 20 products (page 1)
    And I should see pagination controls with 3 pages
    When I click "Next Page"
    Then I should see products 21-40 (page 2)
    When I click "Page 3"
    Then I should see products 41-50 (page 3)

  # ========================================
  # Category Filtering
  # ========================================

  Scenario: Filter products by category
    Given I navigate to the homepage
    When I click on the "Dogs" category filter
    Then the product listing should display only Dog products
    And I should see 3 products:
      | Name                        | Price  |
      | Ceramic Dog Bowl (Large)    | $19.99 |
      | Stainless Steel Dog Bowl    | $24.99 |
      | Rope Tug Toy                | $12.99 |
    And the URL should include "?category=Dogs"

  Scenario: Clear category filter
    Given I am viewing the "Dogs" category
    And I see 3 Dog products
    When I click "Clear Filters" or "All Products"
    Then I should see all 7 active products
    And the category filter should be reset
    And the URL should be the homepage URL (no query parameters)

  Scenario: Multiple category selections (future enhancement)
    Given I navigate to the homepage
    When I select both "Dogs" and "Cats" categories
    Then I should see 5 products (3 Dogs + 2 Cats)
    And the URL should include "?category=Dogs,Cats"

  # ========================================
  # Product Search
  # ========================================

  Scenario: Search products by name
    Given I navigate to the homepage
    When I enter "bowl" in the search box
    And I click "Search"
    Then I should see 2 products:
      | Name                        | Category |
      | Ceramic Dog Bowl (Large)    | Dogs     |
      | Stainless Steel Dog Bowl    | Dogs     |
    And the URL should include "?q=bowl"

  Scenario: Search products by SKU
    Given I navigate to the homepage
    When I enter "CAT-TOY-05" in the search box
    And I click "Search"
    Then I should see 1 product:
      | Name                  | Category |
      | Interactive Cat Laser | Cats     |

  Scenario: Search with no results
    Given I navigate to the homepage
    When I enter "hamster wheel" in the search box
    And I click "Search"
    Then I should see a message "No products found matching 'hamster wheel'"
    And I should see a link "Clear search and browse all products"

  Scenario: Search combined with category filter
    Given I am viewing the "Dogs" category
    When I enter "bowl" in the search box
    And I click "Search"
    Then I should see 2 products:
      | Name                        | Price  |
      | Ceramic Dog Bowl (Large)    | $19.99 |
      | Stainless Steel Dog Bowl    | $24.99 |
    And the URL should include "?category=Dogs&q=bowl"

  # ========================================
  # Product Detail Page
  # ========================================

  Scenario: View product details
    Given I navigate to the homepage
    When I click on the product card for "Ceramic Dog Bowl (Large)"
    Then I should be redirected to the product detail page
    And the page URL should include "/products/DOG-BOWL-01"
    And I should see the following details:
      | Field             | Value                          |
      | SKU               | DOG-BOWL-01                    |
      | Name              | Ceramic Dog Bowl (Large)       |
      | Price             | $19.99                         |
      | Category          | Dogs                           |
      | Description       | (product description text)     |
      | Long Description  | (full product details)         |
      | Product Images    | (primary image + gallery)      |
      | Dimensions        | (size/weight info)             |
      | Add to Cart       | (button present)               |

  Scenario: View product image gallery
    Given I am on the product detail page for "Ceramic Dog Bowl (Large)"
    And the product has 3 images
    When I view the image gallery
    Then the primary image should be displayed prominently
    And I should see thumbnail images for all 3 images
    When I click on thumbnail 2
    Then the main image should switch to image 2
    When I click "Next Image"
    Then the main image should switch to image 3

  # ========================================
  # Add to Cart from Product Pages
  # ========================================

  Scenario: Add product to cart from product listing page
    Given I am logged in as customer "alice@example.com"
    And I have an active cart with ID "cart-abc-456"
    And I am viewing the product listing page
    When I click "Add to Cart" on the "Ceramic Dog Bowl (Large)" product card
    Then the product should be added to my cart
    And I should see a notification "Added to cart"
    And the cart icon badge should update to show "1"

  Scenario: Add product to cart from product detail page
    Given I am logged in as customer "alice@example.com"
    And I have an active cart with ID "cart-abc-456"
    And I am on the product detail page for "Ceramic Dog Bowl (Large)"
    When I set the quantity to 2
    And I click "Add to Cart"
    Then 2 units of "DOG-BOWL-01" should be added to my cart
    And I should see a notification "Added 2 items to cart"
    And the cart icon badge should update to show "2"

  Scenario: Add to cart creates new cart if customer has no active cart
    Given I am logged in as customer "bob@example.com"
    And I do not have an active cart
    And I am viewing the product listing page
    When I click "Add to Cart" on the "Rope Tug Toy" product card
    Then a new cart should be created for customer "bob@example.com"
    And the product should be added to the new cart
    And the cart icon badge should update to show "1"

  # ========================================
  # Stock Availability (Integration with Inventory BC)
  # ========================================

  @future
  Scenario: Display stock availability on product listing
    Given the following inventory levels exist:
      | SKU         | Available | Warehouse  |
      | DOG-BOWL-01 | 50        | Seattle-01 |
      | DOG-BOWL-02 | 2         | Seattle-01 |
      | DOG-TOY-01  | 0         | Seattle-01 |
    And I navigate to the homepage
    When the page finishes loading
    Then the BFF should query Inventory BC for stock levels
    And I should see the following availability indicators:
      | Product                     | Indicator        |
      | Ceramic Dog Bowl (Large)    | In Stock         |
      | Stainless Steel Dog Bowl    | Only 2 left!     |
      | Rope Tug Toy                | Out of Stock     |

  @future
  Scenario: Cannot add out-of-stock product to cart
    Given "Rope Tug Toy" (DOG-TOY-01) has 0 available inventory
    And I am on the product detail page for "Rope Tug Toy"
    Then the "Add to Cart" button should be disabled
    And I should see a message "Out of Stock"
    And I should see a link "Notify me when available"

  # ========================================
  # BFF Composition
  # ========================================

  Scenario: Product listing page composes data from multiple BCs
    Given I navigate to the homepage
    When the page finishes loading
    Then the BFF should query Product Catalog BC for product listing
    And the BFF should compose a ProductListingView with:
      | Data Source    | Information Displayed                |
      | Catalog BC     | Product SKU, Name, Price, Images     |
      | Inventory BC   | Stock availability (future)          |
      | Pricing BC     | Promotional pricing (future)         |
    And the composed view should be returned to the Blazor frontend

  Scenario: Product detail page composes data from multiple BCs
    Given I navigate to the product detail page for "Ceramic Dog Bowl (Large)"
    When the page finishes loading
    Then the BFF should query Product Catalog BC for product details
    And the BFF should query Inventory BC for stock availability (future)
    And the BFF should compose a ProductDetailView with:
      | Data Source    | Information Displayed                          |
      | Catalog BC     | SKU, Name, Price, Description, Images, Dimensions |
      | Inventory BC   | Available quantity, warehouse location (future) |
      | Reviews BC     | Customer ratings, reviews (future)             |

  # ========================================
  # Performance & Caching
  # ========================================

  @performance
  Scenario: Product listing page loads within 2 seconds
    Given I navigate to the homepage
    When I measure the page load time
    Then the product listing should load within 2 seconds
    And the BFF should cache the product listing for 5 minutes
    When I refresh the page within 5 minutes
    Then the cached product listing should be returned
    And the page load time should be under 500ms (cache hit)

  @performance
  Scenario: Product images served from CDN
    Given I am viewing the product listing page
    When I inspect the product image URLs
    Then all images should be served from the CDN domain "https://cdn.crittersupply.com"
    And image URLs should include cache-busting parameters
    And images should be optimized for web (WebP format preferred)

  # ========================================
  # Mobile Responsiveness (Future)
  # ========================================

  @mobile
  Scenario: Product listing adapts to mobile screen size
    Given I navigate to the homepage
    And I am viewing the page on a mobile device (width < 768px)
    Then the product grid should display 2 columns
    And product cards should stack vertically
    And the category filter should collapse into a dropdown menu

  @mobile
  Scenario: Product detail page adapts to mobile screen size
    Given I am on the product detail page for "Ceramic Dog Bowl (Large)"
    And I am viewing the page on a mobile device (width < 768px)
    Then the product images should display in a swipeable carousel
    And the "Add to Cart" button should be fixed at the bottom of the screen
    And product details should display in a single-column layout

  # ========================================
  # Accessibility (Future)
  # ========================================

  @accessibility
  Scenario: Product listing has proper semantic HTML
    Given I navigate to the homepage
    When I inspect the HTML with a screen reader
    Then each product card should be a semantic <article> element
    And product images should have descriptive alt text
    And "Add to Cart" buttons should announce the product name (e.g., "Add Ceramic Dog Bowl to cart")

  @accessibility
  Scenario: Product listing is keyboard navigable
    Given I navigate to the homepage
    When I navigate using only the keyboard (Tab, Enter keys)
    Then I should be able to tab through all product cards
    And I should be able to activate "Add to Cart" buttons using Enter
    And focus indicators should be clearly visible on all interactive elements
