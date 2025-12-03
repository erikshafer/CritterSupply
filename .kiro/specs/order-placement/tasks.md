# Implementation Plan

- [x] 1. Create core data models and value objects





  - [x] 1.1 Create ShippingAddress, CheckoutLineItem, and AppliedDiscount records in Orders project


    - Define immutable records with proper nullability
    - Place in `Placement/` folder following vertical slice structure
    - _Requirements: 1.2, 1.3_
  - [x] 1.2 Create CheckoutCompleted integration event record


    - Include all fields from Shopping context: CartId, CustomerId, LineItems, ShippingAddress, ShippingMethod, PaymentMethodToken, AppliedDiscounts, CompletedAt
    - _Requirements: 1.1, 1.2, 1.3_
  - [x] 1.3 Create OrderLineItem and OrderPlaced domain event records


    - OrderLineItem includes Sku, Quantity, UnitPrice, LineTotal
    - OrderPlaced includes OrderId, CustomerId, LineItems, ShippingAddress, ShippingMethod, PaymentMethodToken, TotalAmount, PlacedAt
    - _Requirements: 2.1, 2.2, 2.3_
  - [x] 1.4 Create OrderStatus enum with all saga states


    - Include: Placed, PendingPayment, PaymentConfirmed, PaymentFailed, OnHold, Fulfilling, Shipped, Delivered, Cancelled, ReturnRequested, Closed
    - _Requirements: 1.4_

- [x] 2. Implement Order Wolverine Saga





  - [x] 2.1 Create Order saga class extending Wolverine's Saga base


    - Define Id property as saga correlation identifier
    - Add properties for CustomerId, LineItems, ShippingAddress, ShippingMethod, PaymentMethodToken, TotalAmount, Status, PlacedAt
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_
  - [x] 2.2 Implement static Start method for saga creation from CheckoutCompleted

    - Create new saga with unique Guid.CreateVersion7() identifier
    - Map CheckoutLineItems to OrderLineItems with calculated LineTotal
    - Calculate TotalAmount from line items
    - Set Status to Placed and record PlacedAt timestamp
    - Return tuple of (Order saga, OrderPlaced event)
    - _Requirements: 1.1, 1.4, 1.5, 2.1_
  - [ ]* 2.3 Write property test for saga creation
    - **Property 1: Saga creation produces valid Order with Placed status**
    - **Validates: Requirements 1.1, 1.4, 1.5**
  - [ ]* 2.4 Write property test for data preservation
    - **Property 2: Order saga preserves all CheckoutCompleted data**
    - **Validates: Requirements 1.2, 1.3**
  - [ ]* 2.5 Write property test for OrderPlaced event completeness
    - **Property 3: OrderPlaced event contains complete order data**
    - **Validates: Requirements 2.1, 2.2, 2.3**

- [x] 3. Implement validation





  - [x] 3.1 Create CheckoutCompletedValidator using FluentValidation


    - Validate CustomerId not empty
    - Validate LineItems not empty
    - Validate each LineItem has Quantity > 0 and PriceAtPurchase > 0
    - Validate ShippingAddress not null
    - Validate PaymentMethodToken not empty
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6_
  - [x] 3.2 Write property test for line item validation






    - **Property 4: Validation rejects invalid line items**
    - **Validates: Requirements 3.2, 3.3**
  - [x] 3.3 Write unit tests for edge case validations






    - Test empty line items list (Requirement 3.1)
    - Test missing customer identifier (Requirement 3.4)
    - Test missing shipping address (Requirement 3.5)
    - Test missing payment method token (Requirement 3.6)
    - _Requirements: 3.1, 3.4, 3.5, 3.6_

- [x] 4. Configure Wolverine and Marten for saga persistence





  - [x] 4.1 Register Order saga with Marten in Program.cs


    - Configure Marten to store Order saga documents
    - Ensure proper serialization settings for saga state
    - _Requirements: 4.1, 4.2_
  - [x] 4.2 Write property test for saga persistence






    - **Property 5: Saga is persisted and retrievable**
    - **Validates: Requirements 4.1, 4.2**

- [x] 5. Implement serialization round-trip testing





  - [x]* 5.1 Write property test for event serialization round-trip


    - **Property 6: Event serialization round-trip**
    - **Validates: Requirements 5.2, 5.3**

- [x] 6. Checkpoint - Ensure all tests pass





  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Implement query endpoint





  - [x] 7.1 Create OrderResponse DTO record


    - Map from Order saga to response, avoiding saga internals exposure
    - Include OrderId, CustomerId, LineItems, ShippingAddress, ShippingMethod, TotalAmount, Status, PlacedAt
    - _Requirements: 6.1, 6.3_

  - [x] 7.2 Create GetOrderEndpoint using Wolverine HTTP

    - Implement GET /api/orders/{orderId} endpoint
    - Use Marten LoadAsync to retrieve Order saga by ID
    - Return 404 if order not found, 200 with OrderResponse if found
    - _Requirements: 6.1, 6.2, 6.3_
  - [x] 7.3 Write property test for order query






    - **Property 7: Order query returns existing orders**
    - **Validates: Requirements 6.1**
  - [x] 7.4 Write unit test for not-found response






    - Test querying non-existent order returns 404
    - _Requirements: 6.2_

- [x] 8. Set up integration test infrastructure






  - [x] 8.1 Create TestFixture with TestContainers for PostgreSQL

    - Configure Alba host with Marten connection to test container
    - Disable external Wolverine transports
    - Implement IAsyncLifetime for proper setup/teardown
    - _Requirements: 4.1, 6.1_
  - [x] 8.2 Write integration test for full order placement flow






    - Send CheckoutCompleted message, verify saga created and queryable
    - _Requirements: 1.1, 2.1, 4.1, 6.1_

- [x] 9. Final Checkpoint - Ensure all tests pass





  - Ensure all tests pass, ask the user if questions arise.
