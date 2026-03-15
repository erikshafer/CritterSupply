Feature: Customer Service Tooling
  As a customer service representative
  I want to look up customers and their orders, cancel orders, and issue store credit
  So that I can resolve customer issues quickly without needing direct database access or engineering support

  Background:
    Given I am logged in to the Backoffice as a "CustomerService" representative
    And a customer "Alice" exists with email "alice@example.com" and customer ID "cust-alice-001"
    And Alice has placed order "order-alice-100" which is currently in "PendingFulfillment" state

  Scenario: Customer service rep looks up a customer by email
    When I navigate to the customer search page
    And I search for "alice@example.com"
    Then I see a customer result showing:
      | Field          | Value              |
      | Display name   | Alice              |
      | Email          | alice@example.com  |
      | Member since   | (registration date)|
      | Recent orders  | (last 5 orders)    |

  Scenario: Customer service rep views a specific order's current state
    Given I have looked up customer "alice@example.com"
    When I click on order "order-alice-100"
    Then I see the order detail view showing:
      | Field              | Description                                 |
      | Order ID           | order-alice-100                             |
      | Current saga state | PendingFulfillment                          |
      | State history      | Timeline of saga transitions with timestamps|
      | Payment status     | Captured / Failed / Refunded                |
      | Fulfilment status  | Awaiting pick / In transit / Delivered      |
      | Items ordered      | SKU, name, quantity, unit price at purchase |

  Scenario: Customer service rep cancels a pre-shipment order at the customer's request
    Given order "order-alice-100" is in "PendingFulfillment" state (not yet shipped)
    When I open the order detail for "order-alice-100"
    And I click "Cancel Order"
    And I select reason "CustomerRequested" and add note "Customer changed their mind"
    And I confirm cancellation
    Then the Backoffice sends a cancel command to the Orders BC attributed to my admin user ID
    And the Orders BC saga transitions the order to "Cancelled" state
    And a "OrderCancelled" event is recorded with my admin user ID and the reason
    And the customer receives an automated cancellation notification (via Notifications BC, when live)
    And the Backoffice shows the updated order state as "Cancelled"

  Scenario: Cannot cancel an order that has already shipped
    Given order "order-alice-200" is in "InTransit" state (already shipped)
    When I open the order detail for "order-alice-200"
    Then the "Cancel Order" button is disabled
    And a message explains "This order has already shipped and cannot be cancelled. Consider initiating a return instead."

  Scenario: Customer service rep issues goodwill store credit
    When I open the customer profile for "alice@example.com"
    And I click "Issue Store Credit"
    And I enter:
      | Field            | Value                                 |
      | Amount           | $15.00                                |
      | Reason           | ShipmentDelay                         |
      | Expiry           | 180 days from today                   |
      | Internal note    | Package was 5 days late — goodwill    |
    And I click Issue
    Then the Store Credit BC records a "StoreCreditIssued" event attributed to my admin user ID
    And the credit appears in Alice's wallet on her next checkout

  Scenario: Customer service rep cannot access pricing or inventory management
    When I navigate to the Backoffice home
    Then I do not see a "Pricing" link in the navigation
    And I do not see an "Inventory" link in the navigation
    And I do not see a "Product Content" link in the navigation

  Scenario: Non-CustomerService role cannot cancel orders
    Given I am logged in to the Backoffice as a "PricingManager"
    When I attempt to cancel order "order-alice-100" via the Backoffice API
    Then the Backoffice API returns 403 Forbidden
    And no cancel command is sent to the Orders BC

  Scenario: Customer service rep views a customer's return history
    Given Alice has a return request "return-alice-001" for order "order-alice-100"
    When I view the order detail for "order-alice-100"
    Then I see a "Returns" section showing the return request status and reason
    And I can see whether a refund has been issued and its current status

  Scenario: Customer lookup result shows PII warning for access logging
    When I search for "alice@example.com"
    Then the system records an access log entry: "CustomerLookup by admin user {adminUserId} for email alice@example.com at {timestamp}"
    And the search results are displayed with a notice that PII access is logged

  @ignore @future
  Scenario: Customer service rep can view and update a customer's address
    Given Alice has addresses on file including "Home (123 Main St)" and "Work (456 Oak Ave)"
    When I view Alice's customer profile
    Then I see her saved addresses listed
    And I can mark an address as "verified" if the customer has confirmed it by phone
