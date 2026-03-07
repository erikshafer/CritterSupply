Feature: Storefront Protected Routes
  As a visitor or customer
  I want protected pages to redirect me to login when I am not authenticated
  So that my account information and purchase flow remain secure

  # ──────────────────────────────────────────────────
  # These scenarios start WITHOUT a Background login step.
  # Each scenario gets a fresh browser context with no session cookie —
  # the unauthenticated state is the natural starting condition.
  #
  # The Checkout, Cart, and Account pages all have @attribute [Authorize].
  # ASP.NET Core's cookie auth middleware (LoginPath = "/login") handles
  # the challenge redirect at the HTTP pipeline level.
  # ──────────────────────────────────────────────────

  @auth
  Scenario: Unauthenticated user is redirected to login when accessing checkout
    Given I am not logged in
    When I navigate directly to "/checkout"
    Then I should be redirected to "/login"

  @auth
  Scenario: Unauthenticated user is redirected to login when accessing cart
    Given I am not logged in
    When I navigate directly to "/cart"
    Then I should be redirected to "/login"

  @auth
  Scenario: Authenticated user accessing checkout without an active cart is redirected to cart
    Given I am logged in as "alice@example.com"
    And I have no active cart in localStorage
    When I navigate directly to "/checkout"
    Then I should be redirected to "/cart"
