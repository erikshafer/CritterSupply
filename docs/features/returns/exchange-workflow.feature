Feature: Exchange Workflow
  As a customer
  I want to exchange an item for a different size or color
  So that I can get the right product without paying for a new order

  Background:
    Given an order has been delivered 10 days ago
    And the order contains a "Pet Carrier (Medium)" item priced at $50.00
    And the item is eligible for return

  Scenario: Happy path — Exchange same-SKU different size (same price)
    When the customer requests an exchange for "Pet Carrier (Large)" priced at $50.00
    And the replacement item is in stock
    And the replacement price is the same as the original
    Then the exchange is approved
    And a return shipping label is generated
    And the customer is notified to ship by 30 days from now
    When the customer ships the original item
    And the warehouse receives and inspects the item
    And the inspection passes
    Then the replacement item is shipped
    And the customer is notified of replacement shipment with tracking number
    When the customer receives the replacement
    Then the exchange is marked completed
    And no refund or charge is issued

  Scenario: Edge case — Replacement out of stock at approval time
    When the customer requests an exchange for "Pet Carrier (Large)"
    And the replacement item is out of stock
    Then the exchange is denied
    And the customer receives a message: "Replacement item currently unavailable. Please request a refund or try again later."
    And the customer is advised to request a refund instead

  Scenario: Edge case — Replacement is cheaper (price difference refund)
    When the customer requests an exchange for "Pet Carrier (Small)" priced at $40.00
    And the replacement item costs $10.00 less than the original
    And the replacement is in stock
    Then the exchange is approved
    And the customer is notified of expected $10.00 refund
    When the customer ships the original item
    And the warehouse receives and inspects the item
    And the inspection passes
    Then the replacement item is shipped
    And the customer receives a $10.00 refund
    And the exchange is marked completed

  Scenario: Edge case — Replacement is more expensive (denied)
    When the customer requests an exchange for "Pet Carrier (XL)" priced at $65.00
    And the replacement item costs $15.00 more than the original
    Then the exchange is denied
    And the customer receives a message: "Replacement item costs more. Please request a refund for this item and place a new order for the replacement."
    And the customer is advised to use refund + new order workflow

  Scenario: Edge case — Original item fails inspection (downgrade to rejection)
    When the customer requests an exchange for "Pet Carrier (Large)" priced at $50.00
    And the replacement is in stock
    And the exchange is approved
    When the customer ships the original item
    And the warehouse receives the original item
    And the inspection fails due to customer-caused damage
    Then the exchange is rejected
    And the customer is notified: "Item condition does not qualify for exchange. Return rejected."
    And no replacement is shipped
    And no refund is issued

  Scenario: Edge case — Exchange request outside return window
    Given an order was delivered 35 days ago
    When the customer requests an exchange
    Then the exchange is denied
    And the customer receives a message: "Return window has expired (30 days from delivery)."
    And the customer is advised to contact customer service for exceptions

  Scenario: Edge case — Customer never ships original item (exchange expires)
    When the customer requests an exchange for "Pet Carrier (Large)"
    And the replacement is in stock
    And the exchange is approved
    And the customer is notified to ship by {30 days from now}
    When 30 days pass without carrier scan
    Then the exchange expires
    And the customer is notified: "Exchange expired — original item was not shipped within 30 days."
    And the customer must submit a new exchange request if still within original 30-day delivery window

  Scenario: Sequential exchanges — Multiple items exchanged from same order
    Given an order contains 3 items: "Pet Carrier (Medium)", "Dog Leash (6ft)", "Cat Toy"
    And all items are eligible for return
    When the customer requests an exchange for "Pet Carrier (Medium)" → "Pet Carrier (Large)"
    And the exchange is approved and completed
    Then the order still has 2 unreturned items
    When the customer requests a second exchange for "Dog Leash (6ft)" → "Dog Leash (4ft)"
    And the second exchange is approved and completed
    Then both exchanges process successfully
    And the order still has 1 unreturned item ("Cat Toy")
