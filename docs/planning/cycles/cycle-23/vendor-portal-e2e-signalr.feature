@e2e @vendor-portal @signalr
Feature: Vendor Portal SignalR Real-Time Updates
  As a vendor user viewing my dashboard
  I want to receive real-time updates without refreshing
  So that I can react to stock alerts, order metrics, and change request decisions immediately

  # ─── ACCEPTANCE CRITERIA ───────────────────────────────────────────────
  # AC-1: SignalR connection indicator shows "Live" (green) when connected
  # AC-2: LowStockAlertRaised → increments KPI card + shows snackbar with SKU
  # AC-3: SalesMetricUpdated → shows "Sales data updated at HH:mm:ss" banner with Refresh button
  # AC-4: ChangeRequestStatusUpdated → refreshes KPI cards (Pending count changes)
  # AC-5: ChangeRequestDecisionPersonal → shows personalized toast (Approved/Rejected/NeedsMoreInfo)
  # AC-6: Disconnected state shows warning banner with Reconnect button
  # ────────────────────────────────────────────────────────────────────────

  # ─── SIGNALR TEST STRATEGY ─────────────────────────────────────────────
  # Messages are injected server-side via IHubContext<VendorPortalHub>,
  # following the same proven pattern as Storefront SignalR tests.
  # The browser has a real WebSocket connection; we inject messages
  # into the correct group ("vendor:{tenantId}") and verify the UI reacts.
  # ────────────────────────────────────────────────────────────────────────

  # ─── PRIORITY: P0 — Browser-only behavior; integration tests can't cover ──

  Background:
    Given I am logged in as "admin@acmepets.test" with password "password"
    And the following change requests exist for "Acme Pet Supplies":
      | SKU          | Type        | Status    | Title                   |
      | DOG-BOWL-01  | Description | Submitted | Update bowl description |
    And there are 0 active low stock alerts
    And the vendor has 10 total SKUs in their product catalog

  @signalr @p0
  Scenario: SignalR connection indicator shows Live status on authenticated dashboard
    When I am on the dashboard page
    Then the SignalR connection indicator should show "Live" with a green icon
    And the indicator should have role="status" for screen reader accessibility

  @signalr @p0
  Scenario: Dashboard receives real-time low stock alert and updates KPI card
    Given I am on the dashboard page
    And the "Low Stock Alerts" KPI card shows "0"
    When the system publishes a LowStockAlertRaised message for SKU "DOG-BONE-07" with current quantity 3 and threshold 10
    Then the "Low Stock Alerts" KPI card should update to "1" within 5 seconds
    And I should see a snackbar notification mentioning "DOG-BONE-07"

  @signalr @p0
  Scenario: Dashboard receives sales metric update and shows refresh banner
    Given I am on the dashboard page
    And I should NOT see the sales metric updated banner
    When the system publishes a SalesMetricUpdated message
    Then I should see a banner with text "Sales data updated" within 5 seconds
    And the banner should have a "Refresh" button
    And the banner should be identified by data-testid "sales-metric-updated-banner"

  @signalr @p1
  Scenario: Dashboard receives change request status update and refreshes KPI
    Given I am on the dashboard page
    And the "Pending Change Requests" KPI card shows "1"
    When the system publishes a ChangeRequestStatusUpdated message indicating the request was approved
    Then the "Pending Change Requests" KPI card should update to "0" within 5 seconds

  @signalr @p1
  Scenario: Dashboard receives personal change request decision notification
    Given I am on the dashboard page
    When the system publishes a ChangeRequestDecisionPersonal message for the current user with decision "Approved" for SKU "DOG-BOWL-01"
    Then I should see a personalized notification indicating approval within 5 seconds
    And the notification should reference "DOG-BOWL-01"

  @signalr @p1
  Scenario: Dashboard receives personal rejection notification with reason
    Given I am on the dashboard page
    When the system publishes a ChangeRequestDecisionPersonal message for the current user with decision "Rejected" for SKU "DOG-BOWL-01" and reason "Images do not meet resolution requirements"
    Then I should see a personalized notification indicating rejection within 5 seconds
    And the notification should include the rejection reason

  @signalr @p2
  Scenario: Disconnected state shows warning banner with reconnect option
    Given I am on the dashboard page
    And the SignalR connection indicator shows "Live"
    When the SignalR connection is interrupted
    Then the SignalR connection indicator should show "Disconnected"
    And I should see a warning banner with data-testid "hub-disconnected-banner"
    And the banner should contain a "Reconnect" button
