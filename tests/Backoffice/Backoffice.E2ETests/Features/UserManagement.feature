@shard-3
Feature: User Management (SystemAdmin)
  As a SystemAdmin
  I want to manage backoffice users
  So that I can control access and permissions

  Background:
    Given the Backoffice application is running
    And stub catalog has product "DEMO-001" with name "Demo Product"
    And I am logged in as "system-admin@critter.test" with name "System Admin" and role "SystemAdmin"

  Scenario: Browse user list
    Given 3 users exist in the system:
      | Email                      | FirstName | LastName | Role            | Status |
      | copy-writer@critter.test   | Jane      | Writer   | CopyWriter      | Active |
      | warehouse-clerk@critter.test | Bob      | Clerk    | WarehouseClerk  | Active |
      | pricing-mgr@critter.test   | Alice     | Manager  | PricingManager  | Active |
    When I navigate to "/users"
    Then I should see 3 users in the table
    And I should see the "Create User" button

  Scenario: Search users by email
    Given user "copy-writer@critter.test" exists with name "Jane Writer"
    And user "warehouse-clerk@critter.test" exists with name "Bob Clerk"
    When I navigate to "/users"
    And I search for "copy-writer"
    Then I should see 1 user in the table
    And I should see "copy-writer@critter.test"

  Scenario: Create new user (happy path)
    When I navigate to "/users/create"
    And I fill in "email-input" with "new-user@critter.test"
    And I fill in "password-input" with "SecureP@ss123"
    And I fill in "first-name-input" with "John"
    And I fill in "last-name-input" with "Doe"
    And I select "Customer Service" from role dropdown
    And I click "submit-button"
    Then I should see "User created successfully"
    And I should be redirected to "/users" within 2 seconds

  Scenario: Create user with duplicate email
    Given user "existing@critter.test" exists
    When I navigate to "/users/create"
    And I fill in "email-input" with "existing@critter.test"
    And I fill in "password-input" with "SecureP@ss123"
    And I fill in "first-name-input" with "John"
    And I fill in "last-name-input" with "Doe"
    And I select "Customer Service" from role dropdown
    And I click "submit-button"
    Then I should see "A user with this email already exists"
    And I should still be on "/users/create"

  Scenario: Validation - Password too short
    When I navigate to "/users/create"
    And I fill in "email-input" with "test@critter.test"
    And I fill in "password-input" with "Short1"
    And I fill in "first-name-input" with "John"
    And I fill in "last-name-input" with "Doe"
    And I select "Customer Service" from role dropdown
    Then "submit-button" should be disabled

  Scenario: Change user role
    Given user "user@critter.test" exists with role "CopyWriter"
    When I navigate to "/users/{userId}/edit"
    And I select "Pricing Manager" from role dropdown
    And I click "change-role-button"
    Then I should see "Role changed successfully"
    And the user's role should be "PricingManager"

  Scenario: Reset user password (two-click pattern)
    Given user "user@critter.test" exists
    When I navigate to "/users/{userId}/edit"
    And I fill in "new-password-input" with "NewSecureP@ss123"
    And I fill in "confirm-password-input" with "NewSecureP@ss123"
    And I click "reset-password-button"
    Then I should see "confirm-reset-password-button"
    When I click "confirm-reset-password-button"
    Then I should see "Password reset successfully"

  Scenario: Password mismatch validation
    Given user "user@critter.test" exists
    When I navigate to "/users/{userId}/edit"
    And I fill in "new-password-input" with "Password123"
    And I fill in "confirm-password-input" with "DifferentPassword123"
    Then "reset-password-button" should be disabled

  Scenario: Deactivate user (two-click pattern)
    Given user "user@critter.test" exists with status "Active"
    When I navigate to "/users/{userId}/edit"
    And I fill in "deactivation-reason-input" with "User requested account closure"
    And I click "deactivate-button"
    Then I should see "confirm-deactivate-button"
    When I click "confirm-deactivate-button"
    Then I should see "User deactivated successfully"
    And the user's status should be "Deactivated"

  Scenario: Session expired during user creation
    Given the session will expire
    When I navigate to "/users/create"
    And I fill in "email-input" with "test@critter.test"
    And I fill in "password-input" with "SecureP@ss123"
    And I fill in "first-name-input" with "John"
    And I fill in "last-name-input" with "Doe"
    And I select "Customer Service" from role dropdown
    And I click "submit-button"
    Then I should be redirected to "/login"

  Scenario: Non-SystemAdmin blocked from user management
    Given I am logged in as "copy-writer@critter.test" with name "Jane Writer" and role "CopyWriter"
    When I navigate to "/users"
    Then I should be redirected to "/"

  Scenario: Deactivate section hidden for already-deactivated users
    Given user "deactivated@critter.test" exists with status "Deactivated"
    When I navigate to "/users/{userId}/edit"
    Then I should not see "deactivate-section"
