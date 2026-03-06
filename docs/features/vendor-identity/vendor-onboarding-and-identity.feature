Feature: Vendor Onboarding and Identity Management
  As a CritterSupply administrator
  I want to onboard vendor organizations and manage their users
  So that vendors can securely access the Vendor Portal with appropriate permissions

  Background:
    Given the Vendor Identity service is running
    And the Vendor Portal service is running

  # ─────────────────────────────────────────────
  # Vendor Tenant Lifecycle
  # ─────────────────────────────────────────────

  Scenario: Administrator creates a new vendor tenant
    Given I am authenticated as a CritterSupply administrator
    When I submit a CreateVendorTenant command with:
      | Field            | Value                          |
      | OrganizationName | Coastal Pet Supplies Co.       |
      | ContactEmail     | onboarding@coastalpet.example  |
    Then a new VendorTenant is created with Status "Onboarding"
    And a "VendorTenantCreated" integration event is published to RabbitMQ
    And the Vendor Portal initializes empty tenant-scoped projections

  Scenario: Cannot create two vendor tenants with the same organization name
    Given a VendorTenant "Coastal Pet Supplies Co." already exists
    When I submit a CreateVendorTenant command with OrganizationName "Coastal Pet Supplies Co."
    Then the command is rejected with "Organization name already exists"
    And no "VendorTenantCreated" event is published

  # ─────────────────────────────────────────────
  # User Invitation Flow
  # ─────────────────────────────────────────────

  Scenario: Administrator invites a vendor user with Admin role
    Given a VendorTenant "Coastal Pet Supplies Co." exists with Status "Active"
    When I submit an InviteVendorUser command with:
      | Field  | Value                          |
      | Email  | admin@coastalpet.example       |
      | Role   | Admin                          |
    Then a VendorUser is created with Status "Invited"
    And a "VendorUserInvited" integration event is published carrying:
      | Field     | Value                    |
      | Email     | admin@coastalpet.example |
      | Role      | Admin                    |
      | ExpiresAt | (InvitedAt + 72 hours)   |
    And the invitation token is stored as a cryptographic hash (never plaintext)
    And an invitation email is sent to "admin@coastalpet.example"

  Scenario: Vendor user completes registration using invitation link
    Given a VendorUser invitation exists for "admin@coastalpet.example" with Status "Pending"
    And the invitation has not expired
    When I submit a CompleteVendorUserRegistration command with:
      | Field    | Value             |
      | Token    | (valid token)     |
      | Password | SecureP@ssw0rd!1  |
    Then the VendorUser Status changes to "Active"
    And the password is stored as an Argon2id hash (never plaintext)
    And a "VendorUserActivated" integration event is published
    And the invitation Status changes to "Accepted"

  Scenario: Registration rejected when invitation has expired
    Given a VendorUser invitation exists for "expired@coastalpet.example" with Status "Expired"
    When I attempt to complete registration using the expired token
    Then the command is rejected with "Invitation link has expired"
    And no "VendorUserActivated" event is published
    And the VendorUser remains in "Invited" status

  Scenario: Invitation expires automatically after 72 hours
    Given a VendorUser invitation was created 73 hours ago with Status "Pending"
    When the invitation expiry background job runs
    Then the invitation Status changes to "Expired"
    And a "VendorUserInvitationExpired" integration event is published
    And the admin dashboard shows this invitation in the "Expired" queue

  Scenario: Administrator resends an expired invitation
    Given a VendorUser invitation is in "Expired" status for "vendor@coastalpet.example"
    When I submit a ResendVendorUserInvitation command
    Then a new invitation token is generated (old token invalidated)
    And the invitation ExpiresAt is reset to (now + 72 hours)
    And the invitation ResendCount increments by 1
    And a "VendorUserInvitationResent" integration event is published
    And a new invitation email is sent to "vendor@coastalpet.example"

  Scenario: Administrator revokes a pending invitation before acceptance
    Given a VendorUser invitation is in "Pending" status for "leaving@coastalpet.example"
    When I submit a RevokeVendorUserInvitation command with Reason "Employee left company before onboarding"
    Then the invitation Status changes to "Revoked"
    And a "VendorUserInvitationRevoked" integration event is published
    And attempting to use the revoked token is rejected

  # ─────────────────────────────────────────────
  # Authentication and JWT Issuance
  # ─────────────────────────────────────────────

  Scenario: Active vendor user logs in and receives JWT
    Given a VendorUser "admin@coastalpet.example" exists with Status "Active" and Role "Admin"
    And the associated VendorTenant has Status "Active"
    When I submit an AuthenticateVendorUser command with correct credentials
    Then a JWT access token is issued containing:
      | Claim              | Value                          |
      | VendorUserId       | (user's Guid)                  |
      | VendorTenantId     | (tenant's Guid)                |
      | VendorTenantStatus | Active                         |
      | Email              | admin@coastalpet.example       |
      | Role               | Admin                          |
      | exp                | (now + 15 minutes)             |
    And a refresh token is issued in an HttpOnly cookie (7-day lifetime)

  Scenario: Login rejected for deactivated vendor user
    Given a VendorUser "deactivated@coastalpet.example" exists with Status "Deactivated"
    When I attempt to authenticate with correct credentials
    Then the command is rejected with "Account has been deactivated"
    And no JWT is issued

  Scenario: Login rejected when vendor tenant is suspended
    Given a VendorUser "admin@coastalpet.example" exists with Status "Active"
    And the associated VendorTenant has Status "Suspended"
    When I attempt to authenticate
    Then the command is rejected with "Vendor account is suspended"
    And no JWT is issued
    And the response includes the suspension reason and support contact

  # ─────────────────────────────────────────────
  # User Management (Admin operations)
  # ─────────────────────────────────────────────

  Scenario: Admin deactivates a vendor user
    Given two Active VendorUsers exist in tenant "Coastal Pet Supplies Co."
    And user "manager@coastalpet.example" has Role "Admin"
    When I submit a DeactivateVendorUser command for "employee@coastalpet.example"
    Then the VendorUser Status changes to "Deactivated"
    And a "VendorUserDeactivated" integration event is published
    And the Vendor Portal force-logs out the deactivated user's active sessions

  Scenario: Cannot deactivate the last Admin in a tenant
    Given only one VendorUser with Role "Admin" exists in the tenant
    When I attempt to deactivate that Admin user
    Then the command is rejected with "Cannot deactivate the last admin in a tenant"
    And the VendorUser Status remains "Active"

  Scenario: Admin reactivates a previously deactivated vendor user
    Given a VendorUser "returning@coastalpet.example" has Status "Deactivated"
    When I submit a ReactivateVendorUser command
    Then the VendorUser Status changes to "Active"
    And a "VendorUserReactivated" integration event is published
    And the user can log in again

  Scenario: Admin changes a vendor user's role
    Given a VendorUser "analyst@coastalpet.example" has Role "ReadOnly"
    When I submit a ChangeVendorUserRole command with NewRole "CatalogManager"
    Then the VendorUser Role changes to "CatalogManager"
    And a "VendorUserRoleChanged" integration event is published
    And the user's next JWT reflects the new role

  # ─────────────────────────────────────────────
  # Tenant Lifecycle: Suspension and Termination
  # ─────────────────────────────────────────────

  Scenario: Administrator suspends a vendor tenant
    Given a VendorTenant "Coastal Pet Supplies Co." has Status "Active"
    When I submit a SuspendVendorTenant command with Reason "Quality investigation pending"
    Then the VendorTenant Status changes to "Suspended"
    And a "VendorTenantSuspended" integration event is published with the Reason
    And all active sessions for users of this tenant are terminated via SignalR
    And all users see "Account suspended: Quality investigation pending" with support contact
    And in-flight change requests remain in their current state (frozen, not rejected)

  Scenario: Administrator reinstates a suspended vendor tenant
    Given a VendorTenant "Coastal Pet Supplies Co." has Status "Suspended"
    When I submit a ReinstateVendorTenant command
    Then the VendorTenant Status changes to "Active"
    And a "VendorTenantReinstated" integration event is published
    And vendors can log in and access the portal again
    And previously frozen change requests resume from their frozen state

  Scenario: Administrator terminates a vendor tenant (permanent)
    Given a VendorTenant "Coastal Pet Supplies Co." has Status "Active"
    And 3 ChangeRequests are in "Submitted" status for this tenant
    When I submit a TerminateVendorTenant command
    Then the VendorTenant Status changes to "Terminated"
    And a "VendorTenantTerminated" integration event is published
    And all 3 in-flight change requests are auto-rejected with Reason "Vendor contract ended"
    And "ChangeRequestRejected" events are published for each
    And all vendor users can no longer log in
