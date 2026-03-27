Feature: Vendor Team Management
  As a vendor admin
  I want to manage my team members
  So that I can control who has access to our vendor portal and what they can do

  Background:
    Given the Vendor Identity service is running
    And the Vendor Portal service is running
    And vendor tenant "Coastal Pet Supplies Co." exists with tenant ID "vendor-1"
    And I am authenticated as vendor admin "admin@coastalpet.com" with Role "Admin"

  # ─────────────────────────────────────────────
  # Team Roster
  # ─────────────────────────────────────────────

  Scenario: Admin views team roster
    Given the following team members exist for my vendor tenant:
      | Email                  | Name          | Role            | Status   |
      | admin@coastalpet.com   | Chris Admin   | Admin           | Active   |
      | alice@coastalpet.com   | Alice Manager | CatalogManager  | Active   |
      | bob@coastalpet.com     | Bob Reader    | ReadOnly        | Active   |
    When I navigate to "Team Management"
    Then I see a roster of 3 team members
    And each member shows their name, email, role, and status
    And each member shows their last login date

  Scenario: Non-admin user cannot access team management
    Given I am authenticated as a vendor user with Role "CatalogManager"
    When I navigate to "Team Management"
    Then I see a message: "User management is available to Admin users only"
    And no team roster is displayed

  # ─────────────────────────────────────────────
  # Invite Team Member
  # ─────────────────────────────────────────────

  Scenario: Admin invites new team member as CatalogManager
    When I navigate to "Team Management" and click "Invite Member"
    And I enter email "newuser@coastalpet.com"
    And I select role "CatalogManager"
    And I click "Send Invitation"
    Then the invitation is created with status "Pending"
    And the invitation expires in 72 hours
    And the invited user appears in the "Pending Invitations" section
    And an invitation email is sent to "newuser@coastalpet.com"

  Scenario: Cannot invite user with email already in the team
    Given "alice@coastalpet.com" is an active team member
    When I attempt to invite "alice@coastalpet.com"
    Then the invitation is rejected with "A user with this email already exists in your team"

  Scenario: Cannot invite user with invalid email format
    When I attempt to invite "not-an-email"
    Then the invitation is rejected with a validation error for email format

  # ─────────────────────────────────────────────
  # Accept Invitation
  # ─────────────────────────────────────────────

  Scenario: Invited user accepts invitation and joins team
    Given an invitation was sent to "newuser@coastalpet.com" with role "CatalogManager"
    And the invitation has not expired
    When the invited user clicks the invitation link
    And enters their name "New User" and sets a password
    And clicks "Accept Invitation"
    Then the user is activated with role "CatalogManager"
    And they appear in the team roster as "Active"
    And the invitation is removed from the "Pending Invitations" section

  Scenario: Invitation link expired
    Given an invitation was sent to "newuser@coastalpet.com" 4 days ago
    When the invited user clicks the invitation link
    Then they see a message: "This invitation has expired. Please ask your admin to resend."
    And the invitation status is "Expired" in the admin's pending invitations view

  # ─────────────────────────────────────────────
  # Change Role
  # ─────────────────────────────────────────────

  Scenario: Admin changes team member role from CatalogManager to ReadOnly
    Given "alice@coastalpet.com" is an active team member with role "CatalogManager"
    When I change their role to "ReadOnly"
    Then the role change is applied immediately
    And the team roster shows "alice@coastalpet.com" with role "ReadOnly"
    And Alice's next API request uses the updated role permissions

  Scenario: Admin cannot change their own role
    When I attempt to change my own role from "Admin" to "ReadOnly"
    Then the action is rejected with "Cannot change your own role"

  # ─────────────────────────────────────────────
  # Deactivate / Reactivate
  # ─────────────────────────────────────────────

  Scenario: Admin deactivates a team member
    Given "bob@coastalpet.com" is an active team member with role "ReadOnly"
    When I click "Deactivate" on Bob's row and confirm
    Then Bob's status changes to "Deactivated"
    And Bob is immediately logged out via SignalR force-disconnect
    And Bob cannot log in until reactivated

  Scenario: Admin reactivates a previously deactivated team member
    Given "bob@coastalpet.com" has status "Deactivated"
    When I click "Reactivate" on Bob's row
    Then Bob's status changes to "Active"
    And Bob can log in again with their existing credentials

  Scenario: Admin cannot deactivate themselves
    When I attempt to deactivate my own account
    Then the action is rejected with "Cannot deactivate your own account"

  # ─────────────────────────────────────────────
  # Invitation Management
  # ─────────────────────────────────────────────

  Scenario: Admin resends an expired invitation
    Given an invitation to "newuser@coastalpet.com" has status "Expired"
    When I click "Resend Invitation"
    Then a new invitation is created with a fresh 72-hour expiration
    And the resend count is incremented
    And a new invitation email is sent

  Scenario: Admin revokes a pending invitation
    Given an invitation to "newuser@coastalpet.com" has status "Pending"
    When I click "Revoke Invitation" and confirm
    Then the invitation status changes to "Revoked"
    And the invitation link is no longer valid
    And the invitation is removed from the "Pending Invitations" section
