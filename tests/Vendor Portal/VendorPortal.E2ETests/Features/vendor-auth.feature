@vendor-portal @auth
Feature: Vendor Portal Authentication
  As a vendor user
  I want to log in with my credentials
  So that I can access the Vendor Portal dashboard

  # P0 Scenarios — validated by Principal Architect, QA Engineer, and Product Owner

  @p0
  Scenario: Admin logs in with valid credentials and sees the dashboard
    When I navigate to the login page
    And I enter "mkerr@hearthhound.com" as email and "Dev@123!" as password
    And I click the sign in button
    Then I should be redirected to the dashboard
    And I should see the user info "Alice" in the app bar

  @p0
  Scenario: Invalid credentials show inline error message
    When I navigate to the login page
    And I enter "mkerr@hearthhound.com" as email and "wrong-password" as password
    And I click the sign in button
    Then I should see the login error "Invalid email or password. Please try again."
    And I should still be on the login page

  @p0
  Scenario: Unauthenticated user is redirected to login
    When I navigate to the dashboard without logging in
    Then I should be on the login page
