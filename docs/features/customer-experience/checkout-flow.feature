Feature: Checkout Flow
  As a customer who has items in my cart
  I want to complete the checkout process step-by-step
  So that I can place an order with my shipping and payment information

  Background:
    Given I am logged in as customer "alice@example.com" with ID "alice-customer-123"
    And I have an active cart with ID "cart-abc-456"
    And my cart contains:
      | SKU         | Name                  | Quantity | Price  |
      | DOG-BOWL-01 | Ceramic Dog Bowl      | 2        | 19.99  |
      | CAT-TOY-05  | Interactive Cat Laser | 1        | 29.99  |
    And the following addresses are saved for customer "alice-customer-123":
      | ID          | Nickname | Address Line 1 | City    | State | Zip   | Country |
      | addr-home   | Home     | 123 Main St    | Seattle | WA    | 98101 | USA     |
      | addr-work   | Work     | 456 Office Blvd| Seattle | WA    | 98102 | USA     |

  # ========================================
  # Happy Path: Complete Checkout
  # ========================================

  Scenario: Complete checkout successfully with saved address
    Given I navigate to the cart page
    When I click "Proceed to Checkout"
    Then I should be redirected to the checkout page
    And the checkout wizard should display "Step 1 of 4: Shipping Address"

    # Step 1: Select Shipping Address
    When I select the saved address "Home" (addr-home)
    And I click "Continue to Shipping Method"
    Then the checkout wizard should advance to "Step 2 of 4: Shipping Method"
    And the selected address should be "123 Main St, Seattle, WA 98101"

    # Step 2: Select Shipping Method
    When I select "Standard Ground" shipping ($5.99)
    And I click "Continue to Payment"
    Then the checkout wizard should advance to "Step 3 of 4: Payment"
    And the order summary should display:
      | Field          | Value  |
      | Subtotal       | $69.97 |
      | Shipping       | $5.99  |
      | Tax            | $0.00  |
      | Total          | $75.96 |

    # Step 3: Provide Payment Method
    When I enter credit card token "tok_visa_test_12345"
    And I click "Continue to Review"
    Then the checkout wizard should advance to "Step 4 of 4: Review & Submit"

    # Step 4: Review & Submit
    When I review the order details
    And I click "Place Order"
    Then the checkout should complete successfully
    And I should be redirected to the order confirmation page
    And the order confirmation should display an order ID
    And the order status should be "Placed"

  # ========================================
  # Step-by-Step Navigation
  # ========================================

  Scenario: Navigate back to previous checkout step
    Given I am on the checkout page at "Step 2 of 4: Shipping Method"
    And I have selected address "Home" (addr-home)
    When I click "Back to Shipping Address"
    Then the checkout wizard should return to "Step 1 of 4: Shipping Address"
    And the previously selected address "Home" should still be selected

  Scenario: Cannot skip checkout steps
    Given I am on the checkout page at "Step 1 of 4: Shipping Address"
    When I attempt to navigate directly to "Step 3 of 4: Payment" via URL
    Then I should be redirected back to "Step 1 of 4: Shipping Address"
    And I should see a validation message "Please complete all previous steps"

  Scenario: Checkout progress persisted across page refreshes
    Given I am on the checkout page at "Step 2 of 4: Shipping Method"
    And I have selected address "Home" (addr-home)
    When I refresh the browser page
    Then the checkout wizard should still be on "Step 2 of 4: Shipping Method"
    And the selected address "Home" should still be displayed
    And I should be able to continue to "Step 3 of 4: Payment"

  # ========================================
  # Address Selection
  # ========================================

  Scenario: Select saved shipping address
    Given I am on the checkout page at "Step 1 of 4: Shipping Address"
    When I view the saved addresses
    Then I should see 2 saved addresses:
      | Nickname | Address Line 1  | City    |
      | Home     | 123 Main St     | Seattle |
      | Work     | 456 Office Blvd | Seattle |
    When I select "Work" (addr-work)
    And I click "Continue to Shipping Method"
    Then the checkout should advance to "Step 2 of 4: Shipping Method"
    And the selected address should be "456 Office Blvd, Seattle, WA 98102"

  Scenario: Add new shipping address during checkout
    Given I am on the checkout page at "Step 1 of 4: Shipping Address"
    When I click "Add New Address"
    Then I should see a form to enter a new address
    When I enter the following address:
      | Field          | Value           |
      | Nickname       | Vacation Home   |
      | Address Line 1 | 789 Beach Rd    |
      | City           | Malibu          |
      | State          | CA              |
      | Zip            | 90265           |
      | Country        | USA             |
    And I click "Save and Use This Address"
    Then the new address should be saved to Customer Identity BC
    And the checkout should advance to "Step 2 of 4: Shipping Method"
    And the selected address should be "789 Beach Rd, Malibu, CA 90265"

  # ========================================
  # Shipping Method Selection
  # ========================================

  Scenario: Select shipping method with different costs
    Given I am on the checkout page at "Step 2 of 4: Shipping Method"
    And I have selected address "Home" (addr-home)
    When I view the available shipping methods
    Then I should see the following options:
      | Method           | Delivery Time | Cost   |
      | Standard Ground  | 5-7 days      | $5.99  |
      | Express Shipping | 2-3 days      | $12.99 |
      | Next Day Air     | 1 day         | $24.99 |
    When I select "Express Shipping" ($12.99)
    Then the order summary should update to:
      | Field          | Value  |
      | Subtotal       | $69.97 |
      | Shipping       | $12.99 |
      | Tax            | $0.00  |
      | Total          | $82.96 |

  # ========================================
  # Payment Method
  # ========================================

  Scenario: Provide credit card payment token
    Given I am on the checkout page at "Step 3 of 4: Payment"
    When I enter credit card details:
      | Field       | Value               |
      | Card Number | 4242 4242 4242 4242 |
      | Expiry      | 12/26               |
      | CVV         | 123                 |
    Then the payment gateway should tokenize the card
    And I should receive a payment token "tok_visa_test_12345"
    When I click "Continue to Review"
    Then the checkout should advance to "Step 4 of 4: Review & Submit"
    And the payment method should display "Visa ending in 4242"

  Scenario: Use saved payment method (future enhancement)
    Given I am on the checkout page at "Step 3 of 4: Payment"
    And I have a saved payment method "Visa ending in 1234"
    When I select the saved payment method
    And I click "Continue to Review"
    Then the checkout should advance to "Step 4 of 4: Review & Submit"
    And the payment method should display "Visa ending in 1234"

  # ========================================
  # Order Review & Submission
  # ========================================

  Scenario: Review order details before submission
    Given I am on the checkout page at "Step 4 of 4: Review & Submit"
    And I have completed all previous steps
    When I review the order summary
    Then I should see the following details:
      | Section         | Details                                  |
      | Shipping Address| 123 Main St, Seattle, WA 98101           |
      | Shipping Method | Standard Ground (5-7 days) - $5.99       |
      | Payment Method  | Visa ending in 4242                      |
      | Line Items      | Ceramic Dog Bowl (2) - $39.98            |
      |                 | Interactive Cat Laser (1) - $29.99       |
      | Subtotal        | $69.97                                   |
      | Shipping        | $5.99                                    |
      | Tax             | $0.00                                    |
      | Total           | $75.96                                   |

  Scenario: Place order triggers Orders BC saga
    Given I am on the checkout page at "Step 4 of 4: Review & Submit"
    And I have completed all previous steps
    When I click "Place Order"
    Then the BFF should send "CompleteCheckout" command to Orders BC
    And Orders BC should publish "Shopping.CheckoutCompleted" integration message
    And Orders BC should start the Order saga
    And the Order saga should publish "Orders.OrderPlaced" to downstream BCs (Payments, Inventory)
    And I should be redirected to the order confirmation page

  # ========================================
  # Real-Time Order Status Updates
  # ========================================

  Scenario: Order confirmation page shows real-time status updates via SSE
    Given I have successfully placed an order
    And I am on the order confirmation page
    And I have subscribed to order status updates via SSE
    When Payments BC publishes "Payments.PaymentCaptured" for my order
    Then the order confirmation page should update within 2 seconds
    And the order status should change from "Placed" to "Payment Confirmed"
    And I should see a notification "Payment successful!"

  Scenario: Order status updates pushed via SSE during fulfillment
    Given I am on the order confirmation page for order "order-xyz-789"
    And I have subscribed to order status updates via SSE
    When Inventory BC publishes "Inventory.ReservationCommitted" for my order
    Then the order status should update to "Preparing for Shipment"
    When Fulfillment BC publishes "Fulfillment.ShipmentDispatched" for my order
    Then the order status should update to "Shipped"
    And I should see the tracking number "1Z999AA10123456784"

  # ========================================
  # Validation & Error Handling
  # ========================================

  Scenario: Cannot proceed to checkout with empty cart
    Given I have an empty cart
    When I navigate to the cart page
    Then the "Proceed to Checkout" button should be disabled
    And I should see a message "Your cart is empty"

  Scenario: Checkout fails if cart is cleared during checkout
    Given I am on the checkout page at "Step 2 of 4: Shipping Method"
    When another browser session clears my cart
    And I click "Continue to Payment"
    Then I should see an error message "Your cart is empty. Please add items to your cart before checking out."
    And I should be redirected to the cart page

  Scenario: Checkout fails if payment token is invalid
    Given I am on the checkout page at "Step 3 of 4: Payment"
    When I enter an invalid credit card token "tok_invalid"
    And I click "Continue to Review"
    Then I should see an error message "Invalid payment method. Please try again."
    And the checkout wizard should remain on "Step 3 of 4: Payment"

  Scenario: Order submission fails if payment declined
    Given I am on the checkout page at "Step 4 of 4: Review & Submit"
    And I have completed all previous steps with a test card that will be declined
    When I click "Place Order"
    Then the Orders saga should start
    And Payments BC should publish "Payments.PaymentFailed" with reason "Insufficient funds"
    And I should see an error message "Payment declined: Insufficient funds. Please try a different payment method."
    And I should be redirected back to "Step 3 of 4: Payment"

  # ========================================
  # BFF Composition
  # ========================================

  Scenario: Checkout page composes data from multiple BCs
    Given I navigate to the checkout page
    When the page finishes loading
    Then the BFF should query Orders BC for checkout state
    And the BFF should query Customer Identity BC for saved addresses
    And the BFF should query Shopping BC for cart line items
    And the BFF should query Product Catalog BC for product details
    And the checkout page should display the composed view:
      | Data Source        | Information Displayed           |
      | Orders BC          | Checkout wizard state (step 1)  |
      | Customer Identity  | Saved addresses (Home, Work)    |
      | Shopping BC        | Cart line items (2 items)       |
      | Product Catalog    | Product names and images        |

  # ========================================
  # Mobile Responsiveness (Future)
  # ========================================

  @mobile
  Scenario: Checkout wizard adapts to mobile screen size
    Given I am on the checkout page
    And I am viewing the page on a mobile device (width < 768px)
    Then the checkout wizard should display in single-column layout
    And the order summary should collapse into an expandable section
    And all buttons should be large enough for touch input (min 44px height)

  # ========================================
  # Accessibility (Future)
  # ========================================

  @accessibility
  Scenario: Checkout wizard is keyboard navigable
    Given I am on the checkout page at "Step 1 of 4: Shipping Address"
    When I navigate using only the keyboard (Tab, Enter keys)
    Then I should be able to select a saved address using Tab + Enter
    And I should be able to click "Continue to Shipping Method" using Enter
    And the checkout wizard should advance to "Step 2 of 4: Shipping Method"
    And focus should be set on the first shipping method option

  @accessibility
  Scenario: Checkout wizard has proper ARIA labels for screen readers
    Given I am on the checkout page
    When I inspect the HTML with a screen reader
    Then each checkout step should have an ARIA label (e.g., "Step 1: Shipping Address")
    And the "Continue" buttons should announce the next step (e.g., "Continue to Shipping Method")
    And form validation errors should be announced via ARIA live regions
