Feature: Vendor Portal Analytics Dashboard
  As a vendor user
  I want to see real-time sales performance and inventory data for my products
  So that I can make informed decisions about restocking and pricing

  Background:
    Given the Vendor Portal service is running
    And I am authenticated as vendor "Coastal Pet Supplies Co." with Role "CatalogManager"
    And the following products are associated with my vendor tenant:
      | SKU        | Product Name           |
      | CPE-TOY-01 | Coastal Rope Toy       |
      | CPE-TRT-01 | Salmon Biscuit Treats  |
      | CPE-BED-01 | Premium Dog Bed        |

  # ─────────────────────────────────────────────
  # Dashboard Loading
  # ─────────────────────────────────────────────

  Scenario: Vendor loads analytics dashboard with existing sales data
    Given the following orders have been placed in the last 30 days:
      | SKU        | Quantity | UnitPrice | PlacedAt     |
      | CPE-TOY-01 | 5        | $12.99    | 7 days ago   |
      | CPE-TRT-01 | 12       | $8.49     | 3 days ago   |
      | CPE-TOY-01 | 3        | $12.99    | 1 day ago    |
    When I navigate to the Analytics Dashboard
    Then I see the following summary metrics:
      | Metric               | Value               |
      | Total Revenue (30d)  | $269.23             |
      | Units Sold (30d)     | 20                  |
      | Top Product          | CPE-TRT-01 (12 units) |
    And the data displays a "Last updated" timestamp
    And no error messages are shown

  Scenario: New vendor sees informative empty state (not broken charts)
    Given my vendor tenant was just created and no orders have been placed yet
    And my products have just been associated (VendorProductAssociated received)
    When I navigate to the Analytics Dashboard
    Then I see an empty state message: "Your sales data will appear here as orders are placed"
    And I do NOT see broken charts or error messages
    And the dashboard shows the list of my associated products

  Scenario: Vendor filters analytics by date range
    Given sales data exists across multiple time periods
    When I select "Last 7 Days" from the date range filter
    Then the dashboard shows only metrics for the last 7 days
    When I select "Last Quarter"
    Then the dashboard shows metrics aggregated for the last 90 days

  # ─────────────────────────────────────────────
  # Real-Time Analytics via SignalR
  # ─────────────────────────────────────────────

  Scenario: Dashboard updates in real-time when a new order is placed
    Given I am viewing the Analytics Dashboard with the SignalR connection established
    And my dashboard shows "Total Revenue (30d): $269.23"
    When a customer places an order containing 2 units of "CPE-TOY-01" at $12.99 each
    And the OrderPlaced event is processed by the Vendor Portal
    Then I receive a "SalesMetricUpdated" SignalR notification without refreshing the page
    And the dashboard automatically reflects the updated revenue total

  Scenario: SignalR connection indicator shows live status
    Given I am viewing the Vendor Portal
    When the SignalR connection to the hub is established
    Then I see a "Live" indicator (green) in the portal header
    When the SignalR connection is temporarily lost
    Then the indicator changes to grey with a reconnecting spinner
    When the connection is restored
    Then the indicator returns to green
    And any missed alerts since the last connection are retrieved

  # ─────────────────────────────────────────────
  # Inventory Alerts
  # ─────────────────────────────────────────────

  Scenario: Vendor receives real-time low-stock alert via SignalR
    Given I am viewing the Vendor Portal with an active SignalR connection
    When the Inventory BC publishes a "LowStockDetected" event for:
      | SKU        | CurrentQuantity | Threshold |
      | CPE-TOY-01 | 3               | 10        |
    Then I receive a toast notification: "⚠️ Low stock: CPE-TOY-01 — Only 3 remaining"
    And the alert badge in the portal header increments
    And the Inventory panel on my dashboard updates to show current stock levels

  Scenario: Low-stock alert is deduplicated for the same SKU
    Given an unacknowledged low-stock alert exists for "CPE-TOY-01" with CurrentQuantity=5
    When the Inventory BC publishes a second "LowStockDetected" event for "CPE-TOY-01" with CurrentQuantity=2
    Then NO new alert is created
    And the existing alert's CurrentQuantity is updated to 2
    And only one alert appears in the alert feed (no duplicates)

  Scenario: Vendor acknowledges a low-stock alert
    Given an active low-stock alert exists for "CPE-TOY-01"
    When I click "Acknowledge" on the alert
    And I submit an AcknowledgeLowStockAlert command
    Then the alert Status changes to "Acknowledged"
    And a "LowStockAlertAcknowledged" event is recorded
    And the alert badge count decrements

  Scenario: Offline vendor sees low-stock alert on next login
    Given I am NOT currently logged into the Vendor Portal
    When the Inventory BC publishes a "LowStockDetected" event for "CPE-BED-01"
    Then the alert is persisted in the portal's active alerts feed
    When I log in to the Vendor Portal
    Then I see the low-stock alert for "CPE-BED-01" in the alert feed
    And the alert badge is shown in the portal header

  Scenario: Alert is not raised for a product not associated with my tenant
    Given "CPE-FISH-99" is a product associated with a different vendor tenant
    When the Inventory BC publishes a "LowStockDetected" event for "CPE-FISH-99"
    Then I do NOT receive any low-stock notification
    And no alert is added to my alert feed

  # ─────────────────────────────────────────────
  # Saved Dashboard Views
  # ─────────────────────────────────────────────

  Scenario: Vendor saves a custom dashboard view configuration
    Given I have applied the following filters to my dashboard:
      | Filter      | Value       |
      | Date Range  | Last 7 Days |
      | SKU         | CPE-TOY-01  |
    When I click "Save View" and enter the name "Rope Toy — Weekly"
    Then the view is persisted in my vendor account
    And the view appears in my Saved Views list
    And the view save is NOT published to RabbitMQ (internal to Vendor Portal only)

  Scenario: Vendor loads a saved dashboard view
    Given I have a saved view named "Rope Toy — Weekly"
    When I select "Rope Toy — Weekly" from my saved views
    Then the dashboard applies the saved filter configuration
    And shows data filtered to CPE-TOY-01 for the last 7 days

  Scenario: Cannot save two views with the same name in the same tenant
    Given I already have a saved view named "Top Products"
    When I attempt to save another view with the name "Top Products"
    Then I see an error: "A view with this name already exists"
    And the save is rejected

  Scenario: Vendor deletes a saved view
    Given I have a saved view named "Rope Toy — Weekly"
    When I click "Delete" on the view
    Then the view is removed from my Saved Views list
    And the view is removed from my vendor account
