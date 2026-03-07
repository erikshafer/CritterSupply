Feature: Storefront Protected Routes
  As a visitor or customer
  I want protected pages to redirect me to login when I am not authenticated
  So that my account information and purchase flow remain secure

  # ASP.NET Core cookie auth middleware (LoginPath = "/login") handles challenge redirects
  # at the HTTP pipeline level. Pages with @attribute [Authorize]:
  #   - /checkout → protected
  #   - /cart     → protected
  #   - /account  → protected

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
