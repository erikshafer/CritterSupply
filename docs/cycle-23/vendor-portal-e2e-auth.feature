@e2e @vendor-portal @auth
Feature: Vendor Portal Authentication & Protected Routes
  As a vendor user
  I want to log in to the Vendor Portal and be redirected appropriately
  So that I can access my dashboard and tools securely

  # ─── ACCEPTANCE CRITERIA ───────────────────────────────────────────────
  # AC-1: Valid credentials → redirect to /dashboard (or returnUrl)
  # AC-2: Invalid credentials → inline error (NOT snackbar — a11y requirement)
  # AC-3: Unauthenticated access → redirect to /login?returnUrl=...
  # AC-4: Already-authenticated visit to /login → redirect to /dashboard
  # AC-5: Logout → SignalR disconnect, cookie cleared, redirect to /login
  # AC-6: returnUrl is preserved through login and honored on success
  # ────────────────────────────────────────────────────────────────────────

  # ─── TEST DATA ─────────────────────────────────────────────────────────
  # Uses VendorIdentity seed data (auto-created on startup):
  #   admin@acmepets.test / password  (Admin role)
  #   catalog@acmepets.test / password  (CatalogManager role)
  #   readonly@acmepets.test / password  (ReadOnly role)
  # No pre-seeding required — VendorIdentitySeedData runs on app startup.
  # ────────────────────────────────────────────────────────────────────────

  # ─── PRIORITY: P0 — Gate for all other scenarios ───────────────────────

  @auth @p0 @smoke
  Scenario: Vendor admin logs in with valid credentials and is redirected to dashboard
    Given I am on the Vendor Portal login page
    And I can see the demo account instructions
    When I enter "admin@acmepets.test" as the email
    And I enter "password" as the password
    And I click the Sign In button
    Then I should be redirected to the dashboard page
    And I should see "Alice" in the app bar user display
    And I should see "Acme Pet Supplies" as the tenant name
    And I should see "Admin" as the role badge

  @auth @p0
  Scenario: Login fails with invalid credentials and shows inline error
    Given I am on the Vendor Portal login page
    When I enter "admin@acmepets.test" as the email
    And I enter "wrongpassword" as the password
    And I click the Sign In button
    Then I should see an inline error message "Invalid email or password. Please try again."
    And I should still be on the login page
    And the error message should have aria-live="assertive" for screen reader announcement

  @auth @p0
  Scenario: Unauthenticated user accessing dashboard is redirected to login with returnUrl
    Given I am not logged in
    When I navigate directly to "/dashboard"
    Then I should be redirected to the login page
    And the URL should contain "returnUrl" with the original path

  @auth @p1
  Scenario: Login preserves returnUrl and redirects to original destination
    Given I am not logged in
    When I navigate directly to "/change-requests"
    Then I should be redirected to the login page
    When I enter "admin@acmepets.test" as the email
    And I enter "password" as the password
    And I click the Sign In button
    Then I should be redirected to the change requests page

  @auth @p1
  Scenario: Unauthenticated user accessing settings is redirected to login
    Given I am not logged in
    When I navigate directly to "/settings"
    Then I should be redirected to the login page
    And the URL should contain "returnUrl"

  @auth @p1
  Scenario: Already-authenticated user visiting login page is redirected to dashboard
    Given I am logged in as "admin@acmepets.test" with password "password"
    When I navigate directly to "/login"
    Then I should be redirected to the dashboard page

  @auth @p2
  Scenario: Vendor logs out and is redirected to login page
    Given I am logged in as "admin@acmepets.test" with password "password"
    And the SignalR connection indicator shows "Live"
    When I click the Sign Out button in the app bar
    Then I should be redirected to the login page
    And I should not see any user information in the app bar

  @auth @p2
  Scenario: After logout, accessing protected route redirects to login
    Given I am logged in as "admin@acmepets.test" with password "password"
    When I click the Sign Out button in the app bar
    And I navigate directly to "/dashboard"
    Then I should be redirected to the login page
