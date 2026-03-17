Feature: Backoffice Authentication
  As a Backoffice administrator
  I want to log in with my credentials
  So that I can access the Backoffice dashboard and perform administrative tasks

  Background:
    Given the Backoffice application is running
    And I am on the login page

  Scenario: Successful login with valid credentials
    Given admin user "Alice" exists with email "alice.admin@crittersupply.com"
    When I log in with email "alice.admin@crittersupply.com" and password "Password123!"
    Then I should be redirected to the dashboard
    And I should see the executive dashboard KPI cards
    And the real-time indicator should show "Connected"

  Scenario: Failed login with invalid password
    Given admin user "Alice" exists with email "alice.admin@crittersupply.com"
    When I log in with email "alice.admin@crittersupply.com" and password "WrongPassword"
    Then I should remain on the login page
    And I should see an error message "Invalid email or password"

  Scenario: Failed login with non-existent user
    When I log in with email "nonexistent@crittersupply.com" and password "Password123!"
    Then I should remain on the login page
    And I should see an error message "Invalid email or password"

  Scenario: Logout and re-login
    Given admin user "Alice" exists with email "alice.admin@crittersupply.com"
    And I am logged in as "alice.admin@crittersupply.com"
    When I log out
    Then I should be redirected to the login page
    When I log in with email "alice.admin@crittersupply.com" and password "Password123!"
    Then I should be redirected to the dashboard

  Scenario: Session persists across page refresh
    Given admin user "Alice" exists with email "alice.admin@crittersupply.com"
    And I am logged in as "alice.admin@crittersupply.com"
    When I refresh the page
    Then I should still be on the dashboard
    And the real-time indicator should show "Connected"
