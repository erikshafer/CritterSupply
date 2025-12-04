# Implementation Plan

- [x] 1. Scaffold Payments bounded context projects






  - [x] 1.1 Create Payments project structure

    - Create `src/Payment Processing/Payments/` project with Wolverine, Marten, FluentValidation dependencies
    - Create `src/Payment Processing/Payments.Api/` project with API configuration
    - Add project references to solution
    - _Requirements: 7.1_

  - [x] 1.2 Create test project structure

    - Create `tests/Payment Processing/Payments.UnitTests/` project
    - Create `tests/Payment Processing/Payments.Api.IntegrationTests/` project
    - Add test dependencies (xUnit, FsCheck, Shouldly, Alba, TestContainers)
    - _Requirements: 7.3_

- [x] 2. Create core data models and value objects





  - [x] 2.1 Create PaymentRequested and RefundRequested command records


    - PaymentRequested: OrderId, CustomerId, Amount, Currency, PaymentMethodToken
    - RefundRequested: PaymentId, OrderId, Amount
    - Place in `Processing/` folder
    - _Requirements: 1.2, 5.2_
  - [x] 2.2 Create domain event records


    - PaymentInitiated, PaymentCapturedEvent, PaymentFailedEvent
    - Include all required fields per design
    - _Requirements: 6.1, 6.2, 6.3_
  - [x] 2.3 Create integration event records


    - PaymentCaptured, PaymentFailed for Orders context
    - RefundCompleted, RefundFailed for Orders context
    - _Requirements: 2.5, 3.4, 5.4, 5.5_
  - [x] 2.4 Create PaymentStatus enum and GatewayResult record


    - PaymentStatus: Pending, Captured, Failed, Refunded
    - GatewayResult: Success, TransactionId, FailureReason, IsRetriable
    - _Requirements: 1.1, 2.2, 3.1_

- [x] 3. Implement Payment gateway abstraction





  - [x] 3.1 Create IPaymentGateway interface


    - CaptureAsync method with amount, currency, token
    - RefundAsync method with transactionId, amount
    - _Requirements: 7.1, 7.2_
  - [x] 3.2 Create StubPaymentGateway implementation


    - Token pattern matching for success/decline/timeout
    - Deterministic behavior for testing
    - _Requirements: 7.3_

- [x] 4. Implement Payment aggregate



  - [x] 4.1 Create Payment record with event sourcing support


    - Properties: Id, OrderId, CustomerId, Amount, Currency, PaymentMethodToken, Status, TransactionId, FailureReason, IsRetriable, InitiatedAt, ProcessedAt
    - PendingEvents collection for uncommitted events
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

  - [x] 4.2 Implement static Create factory method
    - Generate unique ID with Guid.CreateVersion7()
    - Set status to Pending, record timestamp
    - Add PaymentInitiated to pending events
    - _Requirements: 1.1, 1.3, 1.4, 6.1_
  - [x] 4.3 Implement Capture method
    - Update status to Captured, record transaction ID and timestamp
    - Add PaymentCapturedEvent to pending events
    - Return PaymentCaptured integration event
    - _Requirements: 2.2, 2.3, 2.4, 2.5, 6.2_
  - [x] 4.4 Implement Fail method
    - Update status to Failed, record reason and retriable flag
    - Add PaymentFailedEvent to pending events
    - Return PaymentFailed integration event
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 6.3_
  - [x] 4.5 Implement Marten Apply methods for event sourcing
    - Create from PaymentInitiated event
    - Apply PaymentCapturedEvent, PaymentFailedEvent
    - _Requirements: 6.4_
  - [x] 4.6 Write property test for payment creation


    - **Property 1: Payment creation produces valid Payment with Pending status**
    - **Validates: Requirements 1.1, 1.3, 1.4**
  - [x] 4.7 Write property test for data preservation



    - **Property 2: Payment preserves all PaymentRequested data**
    - **Validates: Requirements 1.2**

- [x] 5. Implement validation
  - [x] 5.1 Create PaymentRequestedValidator using FluentValidation
    - Validate Amount > 0
    - Validate OrderId not empty
    - Validate PaymentMethodToken not empty
    - Validate Currency not empty
    - _Requirements: 4.1, 4.2, 4.3, 4.4_
  - [x] 5.2 Write property test for amount validation
    - **Property 5: Validation rejects invalid payment amounts**
    - **Validates: Requirements 4.1**
  - [x] 5.3 Write unit tests for edge case validations
    - Test missing order identifier (Requirement 4.2)
    - Test missing payment method token (Requirement 4.3)
    - Test missing currency (Requirement 4.4)
    - _Requirements: 4.2, 4.3, 4.4_

- [x] 6. Implement message handler





  - [x] 6.1 Create PaymentRequestedHandler


    - Create Payment from command
    - Call gateway CaptureAsync
    - Apply Capture or Fail based on result
    - Persist events to Marten
    - Return integration event for Orders
    - _Requirements: 1.1, 2.1, 2.5, 3.4_
  - [x] 6.2 Write property test for successful capture


    - **Property 3: Successful capture updates Payment and publishes event**
    - **Validates: Requirements 2.2, 2.3, 2.4, 2.5**
  - [x] 6.3 Write property test for failed capture


    - **Property 4: Failed capture updates Payment and publishes event with reason**
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4**

- [ ] 7. Configure Payments.Api
  - [ ] 7.1 Configure Program.cs with Marten and Wolverine
    - Register Payment aggregate for event sourcing
    - Register IPaymentGateway with StubPaymentGateway (dev) or real gateway (prod)
    - Configure FluentValidation
    - _Requirements: 6.1, 7.1_
  - [ ] 7.2 Write property test for event sourcing reconstruction
    - **Property 8: Event sourcing state reconstruction**
    - **Validates: Requirements 6.4**

- [ ] 8. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 9. Implement query endpoint
  - [ ] 9.1 Create PaymentResponse DTO record
    - Map from Payment aggregate to response
    - Include PaymentId, OrderId, Amount, Currency, Status, TransactionId, FailureReason, Timestamps
    - _Requirements: 8.1, 8.3_
  - [ ] 9.2 Create GetPaymentEndpoint using Wolverine HTTP
    - Implement GET /api/payments/{paymentId} endpoint
    - Use Marten AggregateStreamAsync to rebuild Payment from events
    - Return 404 if not found, 200 with PaymentResponse if found
    - _Requirements: 8.1, 8.2, 8.3_
  - [ ] 9.3 Write property test for payment query
    - **Property 9: Payment query returns existing payments**
    - **Validates: Requirements 8.1**
  - [ ] 9.4 Write unit test for not-found response
    - Test querying non-existent payment returns 404
    - _Requirements: 8.2_

- [ ] 10. Implement refund handling
  - [ ] 10.1 Create RefundRequestedValidator
    - Validate PaymentId not empty
    - Validate Amount > 0
    - _Requirements: 5.1, 5.3_
  - [ ] 10.2 Create RefundRequestedHandler
    - Load original payment, validate captured status
    - Validate refund amount <= captured amount
    - Call gateway RefundAsync
    - Publish RefundCompleted or RefundFailed
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_
  - [ ] 10.3 Write property test for refund validation
    - **Property 6: Refund validation rejects invalid requests**
    - **Validates: Requirements 5.1, 5.3**
  - [ ] 10.4 Write property test for successful refund
    - **Property 7: Successful refund publishes RefundCompleted event**
    - **Validates: Requirements 5.4**

- [ ] 11. Set up integration test infrastructure
  - [ ] 11.1 Create TestFixture with TestContainers for PostgreSQL
    - Configure Alba host with Marten connection to test container
    - Register StubPaymentGateway for testing
    - Disable external Wolverine transports
    - _Requirements: 6.1, 8.1_
  - [ ] 11.2 Write integration test for successful payment flow
    - Send PaymentRequested with success token, verify captured
    - _Requirements: 1.1, 2.2, 2.5_
  - [ ] 11.3 Write integration test for failed payment flow
    - Send PaymentRequested with decline token, verify failed
    - _Requirements: 3.1, 3.4_

- [ ] 12. Final Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.
