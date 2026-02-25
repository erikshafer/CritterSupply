Feature: Customer Authentication
  As a customer
  I want to log in to CritterSupply
  So that I can access my cart and checkout

  Background:
    Given the Customer Identity API is running
    And the following test users exist:
      | Email                  | Password | FirstName | LastName |
      | alice@critter.test     | password | Alice     | Anderson |
      | bob@critter.test       | password | Bob       | Builder  |
      | charlie@critter.test   | password | Charlie   | Chen     |

  Scenario: Successful login with valid credentials
    When I login with email "alice@critter.test" and password "password"
    Then the login should succeed
    And I should receive a session cookie
    And the response should contain my customer information:
      | Field     | Value                                |
      | Email     | alice@critter.test                   |
      | FirstName | Alice                                |
      | LastName  | Anderson                             |
      | CustomerId| aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa |

  Scenario: Login with invalid email
    When I login with email "nonexistent@critter.test" and password "password"
    Then the login should fail with status 401

  Scenario: Login in dev mode accepts any password
    When I login with email "bob@critter.test" and password "wrongpassword"
    Then the login should succeed
    And the response should contain "bob@critter.test" in the email field

  Scenario: Access protected endpoint without authentication
    When I request my current user information without authentication
    Then I should receive a 401 Unauthorized response

  Scenario: Access protected endpoint with valid session
    Given I am logged in as "charlie@critter.test"
    When I request my current user information
    Then the response should contain my customer information:
      | Field     | Value                                |
      | Email     | charlie@critter.test                 |
      | FirstName | Charlie                              |
      | CustomerId| cccccccc-cccc-cccc-cccc-cccccccccccc |

  Scenario: Logout clears authentication session
    Given I am logged in as "alice@critter.test"
    When I logout
    Then the logout should succeed
    And my session cookie should be cleared
    And I should no longer be able to access protected endpoints

  Scenario: Complete authentication flow
    When I login with email "bob@critter.test" and password "password"
    Then the login should succeed
    When I request my current user information
    Then the response should contain "bob@critter.test" in the email field
    When I logout
    Then the logout should succeed
    When I request my current user information without authentication
    Then I should receive a 401 Unauthorized response
