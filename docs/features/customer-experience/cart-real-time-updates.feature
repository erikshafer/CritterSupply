Feature: Real-Time Cart Updates
  As a customer browsing the storefront
  I want to see my cart update in real-time when I add items
  So that I get immediate feedback without page refreshes

  Background:
    Given I am logged in as customer "alice@example.com" with ID "alice-customer-123"
    And I have an active cart with ID "cart-abc-456"
    And the following products exist in the catalog:
      | SKU         | Name                  | Price  |
      | DOG-BOWL-01 | Ceramic Dog Bowl      | 19.99  |
      | CAT-TOY-05  | Interactive Cat Laser | 29.99  |
      | FISH-TANK   | 20 Gallon Fish Tank   | 89.99  |

  # ========================================
  # User Interaction Scenarios
  # ========================================

  Scenario: Add item from product page reflects in cart page
    Given I have the cart page open in one browser tab
    And I have a product detail page for "DOG-BOWL-01" open in another tab
    And I have subscribed to cart updates via SSE
    When I click "Add to Cart" on the product detail page
    Then the cart page should update within 2 seconds
    And the cart should display 1 line item
    And the line item for "DOG-BOWL-01" should have quantity 1
    And the cart icon badge should show "1"
    And the cart subtotal should be $19.99

  Scenario: Cart updates pushed when item removed
    Given my cart contains:
      | SKU         | Quantity |
      | DOG-BOWL-01 | 2        |
      | CAT-TOY-05  | 1        |
    And I have the cart page open
    And I have subscribed to cart updates via SSE
    When I click "Remove" for "CAT-TOY-05"
    Then the cart page should update within 2 seconds
    And the cart should display 1 line item
    And the cart icon badge should show "2"
    And the cart subtotal should be $39.98

  Scenario: Cart badge updates when quantity changed
    Given my cart contains:
      | SKU         | Quantity |
      | DOG-BOWL-01 | 2        |
    And I have the cart page open
    And I have subscribed to cart updates via SSE
    When I change the quantity of "DOG-BOWL-01" to 5
    Then the cart page should update within 2 seconds
    And the line item for "DOG-BOWL-01" should have quantity 5
    And the cart icon badge should show "5"
    And the cart subtotal should be $99.95

  # ========================================
  # Technical Integration Scenarios
  # ========================================

  Scenario: SSE connection established when cart page loads
    Given I navigate to the cart page
    When the page finishes loading
    Then an SSE connection should be established to "/sse/storefront"
    And the SSE connection should subscribe to cart updates for "cart-abc-456"
    And the SSE connection state should be "open"

  Scenario: Cart updates pushed via SSE when item added
    Given the storefront BFF is listening for "Shopping.ItemAdded" integration messages
    And I have subscribed to cart updates via SSE
    When Shopping BC publishes "Shopping.ItemAdded" for cart "cart-abc-456" with SKU "DOG-BOWL-01"
    Then the BFF should receive the integration message within 500ms
    And the BFF should query Shopping BC for updated cart state
    And the BFF should push "cart-updated" event via SSE to connected clients
    And the SSE event payload should include:
      | Field       | Value        |
      | cartId      | cart-abc-456 |
      | itemCount   | 1            |
      | subtotal    | 19.99        |
    And the cart page should re-render with updated line items

  Scenario: Cart updates pushed via SSE when item removed
    Given my cart contains:
      | SKU         | Quantity |
      | DOG-BOWL-01 | 1        |
    And I have subscribed to cart updates via SSE
    When Shopping BC publishes "Shopping.ItemRemoved" for cart "cart-abc-456" with SKU "DOG-BOWL-01"
    Then the BFF should receive the integration message within 500ms
    And the BFF should query Shopping BC for updated cart state
    And the BFF should push "cart-updated" event via SSE to connected clients
    And the SSE event payload should indicate an empty cart
    And the cart page should display "Your cart is empty"

  Scenario: Cart updates pushed via SSE when quantity changed
    Given my cart contains:
      | SKU         | Quantity |
      | DOG-BOWL-01 | 2        |
    And I have subscribed to cart updates via SSE
    When Shopping BC publishes "Shopping.ItemQuantityChanged" for cart "cart-abc-456" with SKU "DOG-BOWL-01" and new quantity 5
    Then the BFF should receive the integration message within 500ms
    And the BFF should query Shopping BC for updated cart state
    And the BFF should push "cart-updated" event via SSE to connected clients
    And the SSE event payload should show quantity 5 for "DOG-BOWL-01"
    And the cart page should update the line item quantity to 5

  # ========================================
  # Multi-Client Isolation
  # ========================================

  Scenario: Multiple customers receive cart updates independently
    Given customer "alice@example.com" has cart "cart-abc-456" with 1 item
    And customer "bob@example.com" has cart "cart-def-789" with 2 items
    And both customers are viewing their respective cart pages
    And both customers have subscribed to cart updates via SSE
    When alice adds "CAT-TOY-05" to cart "cart-abc-456"
    Then alice's cart page should update to show 2 items
    And alice's cart subtotal should reflect the new total
    But bob's cart page should not change
    And bob's cart should still show 2 items

  Scenario: Cart updates received in multiple browser tabs for same customer
    Given I have the cart page open in browser tab 1
    And I have the cart page open in browser tab 2
    And both tabs have subscribed to cart updates via SSE
    When I add "DOG-BOWL-01" to my cart from tab 1
    Then tab 1 should update to show 1 item within 2 seconds
    And tab 2 should also update to show 1 item within 2 seconds
    And both tabs should display the same cart subtotal

  # ========================================
  # Error Handling & Resilience
  # ========================================

  Scenario: SSE reconnects automatically after temporary disconnection
    Given I have the cart page open
    And I have subscribed to cart updates via SSE
    And the SSE connection is in "open" state
    When the SSE connection is interrupted (network error)
    Then the browser should automatically attempt to reconnect
    And the SSE connection should be re-established within 5 seconds
    And cart updates should resume being received

  Scenario: Cart page gracefully handles SSE connection failure
    Given I have the cart page open
    When the SSE connection fails to establish (server unavailable)
    Then the cart page should display a warning: "Real-time updates unavailable"
    And the cart page should still function with manual refresh
    And I should be able to view cart items
    And I should be able to add/remove items (with page refresh for updates)

  Scenario: Stale cart state refreshed when SSE connection re-established
    Given I have the cart page open
    And the SSE connection was disconnected for 30 seconds
    And during the disconnection, I added 2 items from another device
    When the SSE connection is re-established
    Then the BFF should push the latest cart state to the reconnected client
    And the cart page should update to show 2 new items
    And the cart subtotal should reflect the correct total

  # ========================================
  # Performance & Efficiency
  # ========================================

  Scenario: SSE connection uses single stream for multiple event types
    Given I have the cart page open
    And I have subscribed to cart updates via SSE
    When I inspect the browser Network tab
    Then I should see exactly 1 active SSE connection to "/sse/storefront"
    And the connection should be multiplexing cart, order, and shipment events
    And no additional SSE connections should be opened for different event types

  Scenario: Cart updates debounced for rapid changes
    Given I have the cart page open
    And I have subscribed to cart updates via SSE
    When I rapidly change the quantity of "DOG-BOWL-01" 5 times within 1 second
    Then the BFF should debounce the updates
    And at most 2 SSE "cart-updated" events should be pushed
    And the final SSE event should reflect the correct final quantity

  # ========================================
  # Integration with Shopping BC
  # ========================================

  Scenario: Cart page displays product details from Product Catalog BC
    Given my cart contains:
      | SKU         | Quantity |
      | DOG-BOWL-01 | 2        |
    And I have the cart page open
    When the page finishes loading
    Then the BFF should query Shopping BC for cart state
    And the BFF should query Product Catalog BC for product details for "DOG-BOWL-01"
    And the cart page should display:
      | Field       | Value                |
      | SKU         | DOG-BOWL-01          |
      | Name        | Ceramic Dog Bowl     |
      | Quantity    | 2                    |
      | Unit Price  | $19.99               |
      | Line Total  | $39.98               |
      | Product Image | (image URL present) |
