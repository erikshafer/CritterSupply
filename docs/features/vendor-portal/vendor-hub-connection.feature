Feature: Vendor Portal Hub Connection Lifecycle
  As a vendor user
  I need the real-time hub connection to be resilient and transparent
  So that I always know whether my dashboard is receiving live data
  And I can trust that security events (deactivation, suspension) reach me immediately

  # These scenarios apply to all VP personas:
  #   Warehouse/Ops Manager  — 8–12 hour sessions (ambient awareness on second monitor)
  #   CatalogManager         — 3–6 hour sessions (batch change requests, waits for decisions)
  #   Admin                  — 2–4 hour recurring sessions (may forget to close tabs)
  #   ReadOnly (exec)        — 20–60 min active, tab may remain open for days

  Background:
    Given the VendorPortal.Api service is running with the VendorPortalHub configured
    And the VendorPortal.Web Blazor WASM application is loaded

  # ─────────────────────────────────────────────
  # Connection Establishment
  # ─────────────────────────────────────────────

  Scenario: Live indicator shows connected state on authenticated load
    Given I am authenticated as a vendor user with role "CatalogManager"
    And my vendor tenant "Coastal Pet Supplies Co." has status "Active"
    When I navigate to the Vendor Portal dashboard
    Then a WebSocket connection is established to "/hub/vendor-portal"
    And the JWT access token is passed via the AccessTokenProvider factory
    And I am enrolled in the "vendor:{tenantId}" SignalR group
    And I am enrolled in the "user:{userId}" SignalR group
    And the "Live" indicator in the header shows green (🟢)

  Scenario: Suspended tenant is rejected at hub connection time
    Given I am authenticated as a vendor user with role "Admin"
    But my vendor tenant "Coastal Pet Supplies Co." has status "Suspended"
    When the WASM client attempts to establish a hub connection
    Then the hub aborts the connection immediately
    And I am shown the suspension notice page with the suspension reason
    And the suspension notice includes a vendor support contact

  Scenario: Terminated tenant is rejected at hub connection time
    Given I am authenticated as a vendor user with role "CatalogManager"
    But my vendor tenant has status "Terminated"
    When the WASM client attempts to establish a hub connection
    Then the hub aborts the connection immediately
    And I am redirected to the login page with a termination notice

  # ─────────────────────────────────────────────
  # Connection Resilience (PO Non-Negotiable)
  # ─────────────────────────────────────────────

  Scenario: Live indicator shows reconnecting state on network blip
    Given I am viewing the Vendor Portal dashboard with an active hub connection
    And the "Live" indicator shows green (🟢)
    When the WebSocket connection is temporarily interrupted
    Then the "Live" indicator changes to grey with a reconnecting spinner
    And no user interaction is required
    When the connection is restored within the automatic retry window
    Then the "Live" indicator returns to green (🟢)
    And any low-stock alerts missed during the disconnection are retrieved via catch-up query

  Scenario: Reconnect-and-catch-up retrieves missed alerts since last seen
    Given I have been connected to the hub with a "lastSeenAt" timestamp of "T-0"
    And the connection drops at time "T+5min"
    And 3 low-stock alerts were raised between "T+5min" and "T+7min"
    When the connection is restored at "T+7min"
    Then the WASM client queries "GET /api/vendor-portal/alerts?since=T+5min"
    And all 3 missed low-stock alerts are displayed in the alerts panel
    And the "lastSeenAt" timestamp is updated to "T+7min"

  Scenario: Server deployment does not permanently disconnect the WASM client
    Given I am a Warehouse Manager with the portal open for 6 hours
    And the hub connection has been active for 6 hours
    When the VendorPortal.Api server is redeployed (rolling restart)
    Then the WASM client detects the disconnection
    And the automatic reconnect fires within 30 seconds
    And the hub connection is re-established with the current access token
    And the "Live" indicator returns to green (🟢)
    # Note: WASM rendering is unaffected by the server restart — the UI state is preserved
    # in WASM memory. This is a key advantage over Blazor Server (which would lose circuit state).

  # ─────────────────────────────────────────────
  # Security Events (PO Priority 🔴 — Must Hit All Tabs)
  # ─────────────────────────────────────────────

  Scenario: Force-logout on user deactivation reaches all active tabs simultaneously
    Given I have the Vendor Portal open in two browser tabs
    And Tab 1 has an active hub connection enrolled in "user:{userId}" group
    And Tab 2 has an active hub connection enrolled in "user:{userId}" group
    When an administrator deactivates my user account
    Then Wolverine publishes a "UserDeactivated" message to the "user:{userId}" hub group
    And both Tab 1 and Tab 2 receive the "UserDeactivated" message via the hub
    And both tabs navigate to the login page within 2 seconds
    And the message shown is "Your account has been deactivated. Please contact your administrator."
    And the WASM access token is cleared from memory in each tab

  Scenario: Tenant suspension closes all active sessions for all tenant users
    Given vendor "Coastal Pet Supplies Co." has 3 active users with open hub connections
    And all 3 connections are enrolled in the "vendor:{tenantId}" group
    When an administrator suspends the "Coastal Pet Supplies Co." tenant
    Then Wolverine publishes a "TenantSuspended" message to the "vendor:{tenantId}" hub group
    And all 3 active sessions receive the "TenantSuspended" message
    And all 3 users are shown the suspension notice with reason and support contact
    And no active user can submit new change requests or acknowledge alerts

  Scenario: Tenant reinstatement restores access for all tenant users
    Given vendor "Coastal Pet Supplies Co." is suspended
    When an administrator reinstates the tenant
    Then Wolverine publishes a "TenantReinstated" message to the "vendor:{tenantId}" group
    And active sessions that received the suspension notice show a reinstatement banner
    And the "Submit Change Request" and "Acknowledge Alert" actions become available again

  # ─────────────────────────────────────────────
  # JWT Token Refresh During Long Sessions (PO Non-Negotiable)
  # ─────────────────────────────────────────────

  Scenario: Access token is proactively refreshed before expiry
    Given I am authenticated as a vendor with a 15-minute access token issued at "T+0"
    When 13 minutes have elapsed (at "T+13min")
    Then the WASM background token refresh timer fires
    And the WASM client calls "POST /api/vendor-identity/refresh"
    And the browser sends the HttpOnly refresh token cookie automatically
    And the new 15-minute access token is stored in WASM memory
    And the session continues without any interruption or re-login prompt

  Scenario: Hub reconnect uses the most recently refreshed token
    Given I am in an active hub session with a recently refreshed access token
    When the hub connection drops and automatic reconnect fires
    Then the AccessTokenProvider factory returns the current (refreshed) access token
    And the hub accepts the reconnection with the new token
    # The factory-per-reconnect design (ADR 0015) ensures stale tokens are never reused

  Scenario: Refresh token expiry after 7 days prompts graceful re-login
    Given I am authenticated and my 7-day refresh token has expired
    When the WASM background timer fires and calls "POST /api/vendor-identity/refresh"
    Then the refresh endpoint returns 401
    And the WASM client clears the access token from memory
    And I am navigated to the login page with message "Your session has expired. Please sign in again."

  # ─────────────────────────────────────────────
  # Multi-Tab Behavior
  # ─────────────────────────────────────────────

  Scenario: Single toast per real-world event across multiple tabs (message deduplication)
    Given I have the Vendor Portal open in two browser tabs
    And both tabs have active hub connections
    When a "LowStockAlertRaised" message is delivered to the "vendor:{tenantId}" group
    Then both tabs receive the message (each tab has its own hub connection)
    But only one toast notification is shown per tab
    And the message ID from the CloudEvents envelope is used for deduplication within each tab
    # Cross-tab deduplication via BroadcastChannel API is a Phase 4 enhancement

  Scenario: lastSeenAt persists across tab refresh for catch-up continuity
    Given I have seen a low-stock alert at "T+0" with the lastSeenAt stored in localStorage
    When I refresh the browser tab at "T+5min"
    And the WASM app reloads and re-establishes the hub connection
    And 2 alerts were raised between "T+0" and "T+5min"
    Then the catch-up query uses the "T+0" lastSeenAt from localStorage
    And both missed alerts are retrieved and displayed
