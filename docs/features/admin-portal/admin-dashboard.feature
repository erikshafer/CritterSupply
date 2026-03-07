Feature: Admin Portal Live Dashboard
  As an operations manager or executive
  I want a live dashboard that reflects the current state of the business in real time
  So that I can spot issues, track performance, and make decisions without refreshing or waiting for reports

  Background:
    Given the Admin Portal is running with SignalR enabled
    And I am connected to the Admin Portal SignalR hub at /hub/admin

  Scenario: Executive sees live revenue counter update when an order is placed
    Given I am logged in to the Admin Portal as an "Executive"
    And the executive dashboard shows today's revenue as $3,412.00
    When a customer places a new order for $87.50
    Then the Orders BC publishes an "OrderPlaced" event
    And the Admin Portal SignalR hub pushes a "LiveMetricUpdated" message to the "role:executive" group
    And my dashboard updates the today's revenue counter to $3,499.50 without a page refresh

  Scenario: Operations manager sees a real-time alert when a payment fails
    Given I am logged in to the Admin Portal as an "OperationsManager"
    When the Payments BC publishes a "PaymentFailed" event for order "order-uuid-001"
    Then the Admin Portal SignalR hub pushes an "AlertRaised" message to the "role:operations" group
    And my dashboard displays a warning alert "Payment failed for order order-uuid-001"
    And the alert includes a link to the order detail view

  Scenario: Warehouse clerk sees a low-stock alert in real time
    Given I am logged in to the Admin Portal as a "WarehouseClerk"
    And SKU "FISH-FLAKES-S" is currently above the low-stock threshold
    When the Inventory BC publishes a "LowStockDetected" event for SKU "FISH-FLAKES-S" with current quantity 3
    Then the Admin Portal SignalR hub pushes a "LowStockAlertRaised" message to the "role:warehouseclerk" group
    And my dashboard displays a low-stock alert "FISH-FLAKES-S is low: 3 remaining (threshold: 10)"
    And the alert includes a link to the inventory management page for SKU "FISH-FLAKES-S"

  Scenario: Operations manager sees the live order pipeline state distribution
    Given I am logged in to the Admin Portal as an "OperationsManager"
    When I load the operations dashboard
    Then I see a pipeline view showing the count of orders currently in each saga state:
      | Saga State           | Description                                |
      | PendingReservation   | Orders awaiting inventory reservation      |
      | PendingPayment       | Orders awaiting payment capture            |
      | PendingFulfillment   | Orders awaiting warehouse processing       |
      | InFulfillment        | Orders actively being picked/packed/shipped|
      | Delivered            | Orders delivered, within return window     |
      | Closed               | Orders past return window                  |

  Scenario: Executive can view top-selling products from Analytics BC
    Given I am logged in to the Admin Portal as an "Executive"
    When I navigate to the executive dashboard
    Then I see a "Top Selling Products" section showing the top 10 SKUs by revenue this month
    And each entry shows: SKU, display name, units sold, total revenue

  Scenario: Executive can export a sales report as CSV
    Given I am logged in to the Admin Portal as an "Executive"
    When I click "Export Sales Report" and select date range 2026-01-01 to 2026-03-31
    Then the Admin Portal downloads a CSV file containing:
      | Column           |
      | Date             |
      | SKU              |
      | Units Sold       |
      | Revenue          |
      | Average Order Value |

  Scenario: Executive dashboard does not show customer PII
    Given I am logged in to the Admin Portal as an "Executive"
    When I view the executive dashboard
    Then I see aggregated order counts and revenue totals
    And I do not see any individual customer names, emails, or addresses
    And I do not see individual order IDs or order details

  Scenario: SignalR alerts are role-scoped — executives do not receive inventory alerts
    Given I am logged in to the Admin Portal as an "Executive"
    When the Inventory BC publishes a "LowStockDetected" event
    Then the Admin Portal SignalR hub does NOT push the "LowStockAlertRaised" message to the "role:executive" group
    And my executive dashboard does not display the inventory alert

  Scenario: Unauthenticated connection to the admin SignalR hub is rejected
    Given I am not logged in
    When I attempt to connect to the Admin Portal SignalR hub at /hub/admin
    Then the WebSocket handshake returns 401 Unauthorized
    And no hub messages are delivered to my client

  @ignore @future
  Scenario: Operations manager receives an alert when a saga has been stuck for over 1 hour
    Given an Order saga for order "order-uuid-stuck" entered the "PendingPayment" state 90 minutes ago
    When the stuck-saga detection job runs
    Then the Admin Portal pushes a "StuckSagaAlertRaised" notification to the "role:operations" group
    And the notification includes the order ID, current state, and time elapsed
