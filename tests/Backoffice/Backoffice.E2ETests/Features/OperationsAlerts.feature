Feature: Operations Alert Feed with Real-Time Updates
  As a Backoffice operations manager
  I want to monitor low-stock alerts and acknowledge them
  So that I can proactively manage inventory and prevent stockouts

  Background:
    Given the Backoffice application is running
    And I am logged in as an operations admin
    And I am on the operations alerts page

  Scenario: View all unacknowledged low-stock alerts
    Given there are 3 unacknowledged low-stock alerts
    When I navigate to the operations alerts page
    Then I should see 3 alerts in the feed
    And each alert should display severity, SKU, and current stock level
    And the real-time indicator should show "Connected"

  Scenario: Acknowledge a low-stock alert
    Given there is an unacknowledged low-stock alert for SKU "TREAT-001"
    When I navigate to the operations alerts page
    And I click on the alert for SKU "TREAT-001"
    And I acknowledge the alert
    Then the alert status should change to "Acknowledged"
    And the alert should no longer appear in the unacknowledged filter

  Scenario: Filter alerts by severity - Critical only
    Given there are 2 critical low-stock alerts
    And there are 3 warning low-stock alerts
    When I navigate to the operations alerts page
    And I filter alerts by severity "Critical"
    Then I should see 2 alerts in the feed
    And all alerts should have severity "Critical"

  Scenario: Filter alerts by severity - Warning only
    Given there are 2 critical low-stock alerts
    And there are 3 warning low-stock alerts
    When I navigate to the operations alerts page
    And I filter alerts by severity "Warning"
    Then I should see 3 alerts in the feed
    And all alerts should have severity "Warning"

  Scenario: Filter alerts by status - Unacknowledged only
    Given there are 3 unacknowledged low-stock alerts
    And there are 2 acknowledged low-stock alerts
    When I navigate to the operations alerts page
    And I filter alerts by status "Unacknowledged"
    Then I should see 3 alerts in the feed
    And all alerts should have status "Unacknowledged"

  Scenario: Filter alerts by status - Acknowledged only
    Given there are 3 unacknowledged low-stock alerts
    And there are 2 acknowledged low-stock alerts
    When I navigate to the operations alerts page
    And I filter alerts by status "Acknowledged"
    Then I should see 2 alerts in the feed
    And all alerts should have status "Acknowledged"

  Scenario: Real-time alert push - New low-stock alert appears
    Given I am on the operations alerts page
    And there are 2 unacknowledged low-stock alerts
    When a new low-stock alert is triggered for SKU "FOOD-005"
    Then I should see 3 alerts in the feed
    And the new alert for SKU "FOOD-005" should appear at the top
    And the alert should have severity "Critical" or "Warning"

  Scenario: Real-time alert push - Alert status update via SignalR
    Given there is an unacknowledged low-stock alert with ID "{AlertId1}"
    And I am on the operations alerts page
    When another admin acknowledges alert "{AlertId1}" from a different session
    Then the alert status should update to "Acknowledged" in real-time
    And I should see the status change without refreshing the page

  Scenario: View alert details modal
    Given there is an unacknowledged low-stock alert for SKU "TREAT-001"
    When I navigate to the operations alerts page
    And I click on the alert for SKU "TREAT-001"
    Then I should see the alert details modal
    And the modal should display SKU, product name, current stock level, reorder threshold, and severity
    And I should see an "Acknowledge" button

  Scenario: Close alert details modal without acknowledging
    Given there is an unacknowledged low-stock alert for SKU "TREAT-001"
    When I navigate to the operations alerts page
    And I click on the alert for SKU "TREAT-001"
    And I close the alert details modal
    Then the modal should be hidden
    And the alert should still be unacknowledged

  Scenario: No alerts message when feed is empty
    Given there are 0 low-stock alerts
    When I navigate to the operations alerts page
    Then I should see a "no alerts" message
    And I should not see any alert rows

  Scenario: Off-the-beaten-path - SignalR reconnection after disconnect
    Given I am on the operations alerts page
    And the real-time indicator shows "Connected"
    When the SignalR connection is temporarily lost
    Then the real-time indicator should show "Disconnected"
    When the SignalR connection is re-established
    Then the real-time indicator should show "Connected"
    And any missed alert updates should sync

  Scenario: Off-the-beaten-path - Multiple alerts for same SKU
    Given there are 2 unacknowledged low-stock alerts for SKU "TREAT-001"
    When I navigate to the operations alerts page
    Then I should see 2 alerts in the feed
    And both alerts should be for SKU "TREAT-001"
    And each alert should have a unique alert ID

  Scenario: Off-the-beaten-path - Alert feed with 50+ alerts (performance)
    Given there are 50 unacknowledged low-stock alerts
    When I navigate to the operations alerts page
    Then I should see 50 alerts in the feed
    And the page should load within 5 seconds
    And scrolling should be smooth
    And the real-time indicator should show "Connected"

  Scenario: Off-the-beaten-path - Acknowledge all alerts in batch
    Given there are 5 unacknowledged low-stock alerts
    When I navigate to the operations alerts page
    And I acknowledge all 5 alerts one by one
    Then all alert statuses should change to "Acknowledged"
    And I should see a "no unacknowledged alerts" message when filtering by status "Unacknowledged"

  Scenario: P0-2 - Acknowledge alert with 409 Conflict (already acknowledged by someone else)
    Given there is an unacknowledged low-stock alert with ID "{AlertId1}" for SKU "TREAT-001"
    And I am on the operations alerts page
    When another admin acknowledges alert "{AlertId1}" from a different session
    And I click on the alert for SKU "TREAT-001"
    And I acknowledge the alert
    Then I should see an info message "Alert was already acknowledged"
    And the alert should be removed from my feed
    And the acknowledge button should return to normal state

  Scenario: P0-2 - Acknowledge alert with optimistic UI update
    Given there is an unacknowledged low-stock alert for SKU "FOOD-005"
    And I am on the operations alerts page
    When I click on the alert for SKU "FOOD-005"
    And I click the acknowledge button
    Then the button should show "Acknowledging..." immediately
    And the button should be disabled during the request
    When the acknowledgment succeeds
    Then the alert should be removed from the feed immediately
    And I should see a success message "Alert acknowledged"

  Scenario: P0-2 - Acknowledge alert with 401 Unauthorized (session expired)
    Given there is an unacknowledged low-stock alert for SKU "TOY-010"
    And I am on the operations alerts page
    When my session expires
    And I click on the alert for SKU "TOY-010"
    And I acknowledge the alert
    Then the button should show "Acknowledging..." briefly
    And the session expired modal should appear
    And the alert should still be visible in the feed
    And the acknowledge button should return to normal state

  Scenario: P0-2 - Multiple rapid acknowledgments with race condition handling
    Given there are 3 unacknowledged low-stock alerts
    And I am on the operations alerts page
    When I rapidly acknowledge all 3 alerts within 2 seconds
    Then each button should show "Acknowledging..." in sequence
    And all 3 alerts should be removed from the feed
    And I should see 3 success messages

  Scenario: P1-2 - Data freshness indicator shows "Last updated" timestamp
    Given there are 3 unacknowledged low-stock alerts
    When I navigate to the operations alerts page
    Then I should see "Last updated: just now" below the alert count
    And the timestamp should be visible

  Scenario: P1-2 - Data freshness indicator updates after manual refresh
    Given there are 3 unacknowledged low-stock alerts
    And I am on the operations alerts page
    And I wait 2 minutes
    When I click the refresh button
    Then the "Last updated" timestamp should change to "just now"
    And the alert count should be refreshed

  Scenario: P1-2 - New alerts banner appears with refresh option
    Given I am on the operations alerts page
    And there are 2 unacknowledged low-stock alerts
    When 3 new low-stock alerts are triggered via SignalR
    Then I should see a banner "3 new alert(s) received"
    And the banner should have a "Refresh" button
    When I click the "Refresh" button in the banner
    Then the alert feed should show 5 total alerts
    And the new alerts banner should disappear
    And the "Last updated" timestamp should change to "just now"
