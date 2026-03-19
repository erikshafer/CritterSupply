Feature: Session Expiry and Recovery
  As a Backoffice administrator
  I want to be notified when my session expires
  So that I can re-authenticate and continue my work without losing context

  Background:
    Given the Backoffice application is running
    And admin user "Alice" exists with email "alice.admin@crittersupply.com"
    And I am logged in as "alice.admin@crittersupply.com"

  Scenario: Session expires while viewing Dashboard — modal appears
    Given I am on the dashboard
    When my session expires
    And I trigger a data refresh
    Then I should see the session expired modal
    And the modal should display "Your session has expired"
    And the modal should have a "Log In Again" button
    And the modal should block interaction with the page

  Scenario: Session expires while viewing Operations Alerts — modal appears
    Given I am on the operations alerts page
    When my session expires
    And I try to acknowledge an alert
    Then I should see the session expired modal
    And the modal should display "Your session has expired"
    And I should not be able to interact with the alerts feed

  Scenario: Session expires while searching customers — modal appears
    Given I am on the customer search page
    When my session expires
    And I trigger a search
    Then I should see the session expired modal
    And the modal should display "Your session has expired"

  Scenario: Re-login after session expiry — redirects to login with returnUrl
    Given I am on the dashboard
    When my session expires
    And I trigger a data refresh
    And the session expired modal appears
    When I click "Log In Again"
    Then I should be redirected to the login page
    And the returnUrl query parameter should be "/dashboard"

  Scenario: Re-login from alerts page — returnUrl preserves context
    Given I am on the operations alerts page
    When my session expires
    And I try to acknowledge an alert
    And the session expired modal appears
    When I click "Log In Again"
    Then I should be redirected to the login page
    And the returnUrl query parameter should be "/operations/alerts"

  Scenario: Complete re-authentication flow — returns to original page
    Given I am on the dashboard
    When my session expires
    And I trigger a data refresh
    And the session expired modal appears
    When I click "Log In Again"
    And I log in with email "alice.admin@crittersupply.com" and password "Password123!"
    Then I should be redirected back to the dashboard
    And I should see the executive dashboard KPI cards
    And the real-time indicator should show "Connected"

  Scenario: Complete re-authentication from alerts page — returns to alerts
    Given I am on the operations alerts page
    When my session expires
    And I try to acknowledge an alert
    And the session expired modal appears
    When I click "Log In Again"
    And I log in with email "alice.admin@crittersupply.com" and password "Password123!"
    Then I should be redirected back to the operations alerts page
    And I should see the operations alerts feed

  Scenario: Session expiry during alert acknowledgment — optimistic UI rollback
    Given there is an unacknowledged low-stock alert for SKU "TREAT-001"
    And I am on the operations alerts page
    When my session expires
    And I click on the alert for SKU "TREAT-001"
    And I click the acknowledge button
    Then the button should show "Acknowledging..." briefly
    And the session expired modal should appear
    And the alert should still be visible in the feed
    And the acknowledge button should be enabled again

  Scenario: Session expiry on multiple pages — modal only appears once
    Given I am on the dashboard
    When my session expires
    And I trigger a data refresh
    And the session expired modal appears
    And I navigate to the operations alerts page
    Then the session expired modal should still be visible
    And I should not see a duplicate modal

  Scenario: Close session expired modal without re-login — stays on current page
    Given I am on the dashboard
    When my session expires
    And I trigger a data refresh
    And the session expired modal appears
    When I close the modal
    Then the modal should be hidden
    And I should still be on the dashboard
    And subsequent API calls should still trigger the modal

  Scenario: Off-the-beaten-path — Session expiry with invalid credentials on re-login
    Given I am on the dashboard
    When my session expires
    And I trigger a data refresh
    And the session expired modal appears
    When I click "Log In Again"
    And I log in with email "alice.admin@crittersupply.com" and password "WrongPassword"
    Then I should remain on the login page
    And I should see an error message "Invalid email or password"
    And the returnUrl parameter should still be preserved

  Scenario: Off-the-beaten-path — Multiple 401 responses before modal is dismissed
    Given I am on the operations alerts page
    And there are 3 unacknowledged low-stock alerts
    When my session expires
    And I try to acknowledge alert 1
    And I try to acknowledge alert 2
    And I try to acknowledge alert 3
    Then I should see the session expired modal
    And I should see exactly 1 session expired modal
    And all 3 alerts should still be unacknowledged
