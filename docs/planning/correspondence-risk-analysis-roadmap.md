# Correspondence BC — Risk Analysis & Implementation Roadmap

**Date:** 2026-03-13
**Status:** ✅ Approved for implementation
**Cycle:** 28 (Correspondence BC Phase 1)
**Companion Document:** [`correspondence-event-model.md`](./correspondence-event-model.md)

---

## Part 1: Risk and Collision Analysis

### 1. Integration Risks

#### Risk 1.1: Schema Changes in Upstream BCs

**Scenario:** Orders BC changes the `OrderPlaced` event schema (e.g., renames `CustomerId` → `BuyerId`).

**Impact:** Correspondence BC's `OrderPlacedHandler` breaks at runtime. No compile-time safety for integration events (they are serialized as JSON over RabbitMQ).

**Mitigation:**
- **Contract tests:** Add integration tests in `Messages.Contracts.Tests` that validate event schemas across BCs
- **Versioning strategy:** Use event version headers (`EventVersion: v1`) in RabbitMQ messages; Correspondence handlers can route to v1 vs. v2 handlers
- **Monitoring:** Set up alerts for deserialization failures in Wolverine message processing (catch `JsonException` in error handler)
- **Communication:** Any BC changing an integration event schema must announce the change in a GitHub Issue tagged with all consuming BCs

**Likelihood:** Medium — schema changes will happen as BCs evolve
**Severity:** High — breaks customer communication (silently failing)
**Priority:** P0 — Address in Phase 1 implementation

---

#### Risk 1.2: Customer Identity BC Availability

**Scenario:** Customer Identity BC is down or slow (>1 second response time). Correspondence handlers query `/api/customers/{customerId}` before sending messages.

**Impact:** Message delivery is blocked. Customer misses order confirmation email.

**Mitigation:**
- **Timeout and circuit breaker:** Use Polly library for HTTP client resilience (5-second timeout, circuit breaker after 5 consecutive failures)
- **Graceful degradation:** If Customer Identity BC is unavailable, **proceed with sending email to the email address in the integration event** (e.g., `OrderPlaced.CustomerEmail`). Risk: customer may have updated preferences, but missing email is worse than ignoring opt-out temporarily.
- **Retry policy:** If Customer Identity call fails, schedule the entire `SendMessage` command for retry (same exponential backoff as provider failures)
- **Monitoring:** Alert on circuit breaker open events

**Likelihood:** Low — Customer Identity BC is EF Core-backed, not event-sourced (simpler failure modes)
**Severity:** High — blocks all message delivery
**Priority:** P1 — Address in Phase 1 implementation (Polly integration)

---

#### Risk 1.3: RabbitMQ Queue Backlog / Lag

**Scenario:** Correspondence BC is offline for 1 hour. Returns BC publishes 500 `ReturnApproved` events during that hour. When Correspondence BC restarts, it must process 500 messages.

**Impact:** Message delivery lag. Customers receive "return approved" emails 2 hours after approval.

**Mitigation:**
- **Horizontal scaling:** Run 2-3 instances of Correspondence.Api (Wolverine distributes messages across instances via RabbitMQ consumer groups)
- **Queue monitoring:** Set up alerts for queue depth >100 messages
- **Batch processing:** No special handling needed — Wolverine processes messages as fast as possible
- **Priority queues (future):** Separate high-priority events (OrderPlaced) from low-priority (ReturnExpired) into different queues

**Likelihood:** Medium — will happen during deployments or outages
**Severity:** Medium — delayed messages are acceptable (not real-time)
**Priority:** P2 — Monitor in Phase 1; optimize in Phase 2 if needed

---

### 2. Duplicate Handling and Idempotency

#### Risk 2.1: Duplicate Integration Events

**Scenario:** RabbitMQ delivers the same `OrderPlaced` event twice (at-least-once delivery guarantee). Correspondence BC receives the event twice and sends two order confirmation emails to the customer.

**Impact:** Poor customer experience (duplicate emails), wasted SendGrid quota.

**Mitigation:**
- **Idempotency key:** Use `MessageId` (Guid in RabbitMQ message headers) as the event stream ID for the Message aggregate. Wolverine sets `MessageId` automatically.
- **Before creating Message aggregate:** Query Marten event store: `session.Events.AggregateStreamAsync<Message>(messageId)`. If stream already exists, skip message creation.
- **Pattern:**
  ```csharp
  var existingMessage = await session.Events.AggregateStreamAsync<Message>(messageId);
  if (existingMessage != null)
      return OutgoingMessages.Empty; // Already processed
  ```

**Likelihood:** High — at-least-once delivery is the default for Wolverine + RabbitMQ
**Severity:** Medium — annoys customers but not a data corruption issue
**Priority:** P0 — Must implement in Phase 1

---

#### Risk 2.2: Duplicate Provider Calls (SendGrid)

**Scenario:** `SendMessageHandler` calls SendGrid API, receives 200 OK, but the handler crashes before persisting `MessageDelivered` event. Wolverine retries the handler, and SendGrid is called twice.

**Impact:** Customer receives two identical emails.

**Mitigation:**
- **SendGrid idempotency:** SendGrid does not provide built-in idempotency keys. We must handle this in Correspondence BC.
- **Solution:** Store `MessageId` in SendGrid's `CustomArgs` field (metadata attached to email). If SendGrid receives a second request with the same `CustomArgs.MessageId`, it **will not deduplicate** (this is a limitation of SendGrid).
- **Accept the risk for Phase 1:** Duplicate sends are rare (requires handler crash after HTTP call but before transaction commit). Cost is low (<$0.0001 per email). Customer impact is minimal.
- **Phase 2 improvement:** Implement a `SentMessageCache` (Redis or in-memory) with 5-minute TTL. Before calling SendGrid, check cache: `if (cache.Contains(messageId)) return;`

**Likelihood:** Low — requires specific failure timing (crash after HTTP call, before Marten commit)
**Severity:** Low — duplicate emails are annoying but not harmful
**Priority:** P2 — Accept risk in Phase 1; add cache in Phase 2 if needed

---

### 3. Naming Collisions

#### Collision 3.1: "Notifications" Folder in Customer Experience BC

**Status:** ✅ Resolved by ADR 0030 (rename to Correspondence BC)

**Context:** The Customer Experience BC (Storefront) has a `Storefront/Notifications/` folder containing 16 integration message handlers for real-time UI updates via SignalR. These are **not** the same as the Correspondence BC.

**Mitigation:** The BC rename (Notifications → Correspondence) eliminates the collision. The folder name `Storefront/Notifications/` remains unchanged (it's not a BC name).

---

#### Collision 3.2: "Message" as Aggregate Name vs. RabbitMQ Messages

**Risk:** Confusion when discussing "messages" — do we mean the Correspondence BC's `Message` aggregate or RabbitMQ messages?

**Mitigation:**
- **Naming convention:** Always qualify:
  - "Correspondence Message" or "Message aggregate" → Correspondence BC domain entity
  - "Integration message" or "RabbitMQ message" → messaging infrastructure
- **Code namespaces:**
  - `Correspondence.Message` → aggregate
  - `Wolverine.Runtime.Envelope` → RabbitMQ message envelope

**Likelihood:** High — developers will confuse these terms
**Severity:** Low — communication issue, not a runtime issue
**Priority:** P1 — Document in CLAUDE.md skill file (Phase 1)

---

#### Collision 3.3: SendGrid vs. Wolverine "MessageId"

**Risk:** SendGrid uses `MessageId` to identify sent emails. Wolverine uses `MessageId` to identify RabbitMQ messages. Correspondence BC uses `MessageId` as the aggregate stream ID.

**Mitigation:**
- **Naming convention:**
  - Correspondence BC: `MessageId` (Guid) — aggregate ID
  - SendGrid: `SendGridMessageId` (string) — provider response field
  - Wolverine: `Envelope.Id` (Guid) — RabbitMQ message ID (same as Correspondence BC `MessageId`)
- **Code pattern:**
  ```csharp
  var messageId = envelope.Id; // Wolverine MessageId
  var (message, queued) = MessageFactory.Create(...);
  session.Events.StartStream<Message>(messageId, queued); // Use Wolverine MessageId as stream ID
  ```

**Likelihood:** Medium — developers will encounter this when debugging
**Severity:** Low — naming confusion only
**Priority:** P2 — Document in code comments

---

### 4. Scope Creep Risks

#### Scope Creep 4.1: Marketing Email Campaigns

**Risk:** Product Owner requests: "Can Correspondence BC send promotional emails for Black Friday sale?"

**Impact:** Blurs BC boundaries. Correspondence BC is for **transactional** emails triggered by business events. Marketing emails are **promotional** and belong in the **Promotions BC** (Cycle 29).

**Mitigation:**
- **Boundary enforcement:** Correspondence BC handlers are triggered by integration events (OrderPlaced, ShipmentDispatched). Marketing campaigns are triggered by cron jobs or admin commands.
- **Policy:** Correspondence BC never exposes `POST /api/correspondence/messages` endpoint. All messages are event-driven.
- **Communication:** Reference ADR 0030 and CONTEXTS.md when rejecting scope creep requests.

**Likelihood:** High — stakeholders will request this
**Severity:** Medium — accepting scope creep degrades architecture
**Priority:** P0 — Enforce boundary in Phase 1 design

---

#### Scope Creep 4.2: Customer Preference Management

**Risk:** Product Owner requests: "Can Correspondence BC let customers unsubscribe from emails?"

**Impact:** Correspondence BC does not own customer preferences — Customer Identity BC does. Adding preference management duplicates responsibility and creates data consistency issues.

**Mitigation:**
- **Policy:** Correspondence BC always queries Customer Identity BC for preferences. It never stores or updates preferences.
- **Unsubscribe link:** Emails include an unsubscribe link that routes to Customer Identity BC: `https://crittersupply.com/account/preferences?unsubscribe=email&token={jwt}`
- **Customer Identity BC responsibility:** Handle unsubscribe requests, update preferences, return updated preferences via API

**Likelihood:** High — stakeholders will request this
**Severity:** Medium — accepting scope creep creates data ownership conflicts
**Priority:** P0 — Enforce boundary in Phase 1 design

---

#### Scope Creep 4.3: In-App Notifications

**Risk:** Product Owner requests: "Can Correspondence BC send in-app notifications to the Storefront?"

**Impact:** Blurs boundary with Customer Experience BC. Real-time in-app updates are already handled by `Storefront/Notifications/` folder (SignalR handlers).

**Mitigation:**
- **Policy:** Correspondence BC handles **asynchronous, out-of-band communication** (email, SMS). Customer Experience BC handles **real-time, in-app communication** (SignalR).
- **Separation of concerns:**
  - Correspondence BC: "Your order has shipped" → email with tracking link
  - Customer Experience BC: "Your order has shipped" → SignalR push to active browser tab + toast notification
- **Both BCs can subscribe to the same integration event** (e.g., `ShipmentDispatched`) but they serve different channels.

**Likelihood:** Medium — stakeholders may not understand the distinction
**Severity:** Medium — accepting scope creep creates redundant message handling
**Priority:** P0 — Clarify in ADR 0030 and CONTEXTS.md

---

#### Scope Creep 4.4: SMS Without Customer Opt-In

**Risk:** Product Owner requests: "Send SMS for all 'shipment dispatched' events, even if customer hasn't opted in."

**Impact:** Legal risk (TCPA violations in the US, GDPR violations in the EU). SMS requires explicit opt-in.

**Mitigation:**
- **Policy:** Correspondence BC always checks `customer.SmsOptIn` before sending SMS. If `false`, skip SMS and only send email.
- **Phase 1:** Email only (no SMS implementation)
- **Phase 2:** SMS opt-in UI in Customer Identity BC + Storefront preferences page

**Likelihood:** Medium — stakeholders may not understand legal requirements
**Severity:** High — legal liability
**Priority:** P0 — Enforce opt-in check in Phase 2 (when SMS is implemented)

---

## Part 2: Implementation Roadmap

### Phase 1: Transactional Email Foundation (Cycle 28)

**Goal:** Implement core Correspondence BC with email-only support for 4 high-value integration events.

**Priority:** 🔴 P0 — Customer-facing gap (no post-checkout communication)

**Deliverables:**

1. **Message Aggregate (Event-Sourced)**
   - `Message` record with `Create()` factory method
   - 4 domain events: `MessageQueued`, `MessageDelivered`, `DeliveryFailed`, `MessageSkipped`
   - `Apply()` methods for event sourcing
   - `MessageStatus` enum (Queued, Delivered, Failed, Skipped)

2. **Marten Configuration**
   - Schema: `correspondence` (via `DatabaseSchemaName`)
   - Event store: `message-{Guid}` stream naming
   - Inline projection: `MessageListView` (for customer message history queries)

3. **Integration Event Handlers (4 Initial Events)**
   - `OrderPlacedHandler` → Order confirmation email
   - `ShipmentDispatchedHandler` → Tracking number email
   - `ReturnApprovedHandler` → Return label email
   - `ReturnCompletedHandler` → Return received + refund email

4. **RabbitMQ Queue Configuration**
   - Subscribe to: `correspondence-orders-events`, `correspondence-fulfillment-events`, `correspondence-returns-events`
   - Publish to: `correspondence-events` exchange (3 outbound events)

5. **SendGrid Integration**
   - `ICorrespondenceChannel` interface (email, SMS, push)
   - `SendGridEmailChannel` implementation
   - Configuration: API key in `appsettings.json` (Azure Key Vault in production)

6. **HTTP Client for Customer Identity BC**
   - `ICustomerIdentityClient` interface
   - `CustomerIdentityClient` implementation (queries `/api/customers/{customerId}`)
   - Polly resilience: 5-second timeout, circuit breaker

7. **Message Templates (Razor)**
   - `OrderConfirmationEmail.cshtml` — Order summary, items, shipping address
   - `ShipmentDispatchedEmail.cshtml` — Tracking number, carrier link, estimated delivery
   - `ReturnApprovedEmail.cshtml` — Return label, ship-by deadline, instructions
   - `ReturnCompletedEmail.cshtml` — Return received, refund amount, refund timeline

8. **Idempotency Logic**
   - Before creating Message aggregate, check if stream already exists
   - Use Wolverine `Envelope.Id` as Message aggregate stream ID

9. **Retry Logic**
   - 3 retry attempts with exponential backoff (5 min, 30 min, 2 hours)
   - Wolverine `DelayedFor()` for durable scheduling
   - Permanent failure after 3 attempts → publish `CorrespondenceFailed`

10. **HTTP Endpoints (Read-Only Queries)**
    - `GET /api/correspondence/messages/{customerId}` — list messages sent to customer
    - `GET /api/correspondence/messages/{messageId}` — get message details

11. **Integration Tests (Alba + TestContainers)**
    - Test fixture: Correspondence.Api + RabbitMQ + Postgres + Customer Identity stub
    - Scenarios:
      - Order confirmation email sent after `OrderPlaced`
      - Tracking email sent after `ShipmentDispatched`
      - Idempotency: duplicate `OrderPlaced` event → single email sent
      - Opted out customer: `OrderPlaced` event → message skipped
      - SendGrid failure → retry → eventual delivery
      - Customer Identity BC down → circuit breaker trips → graceful fallback

12. **Unit Tests**
    - Message aggregate: `Create()`, `Apply()` methods
    - Message template rendering
    - Retry backoff calculation

13. **Documentation**
    - ADR 0030: Notifications → Correspondence rename (already created)
    - Event Model: `correspondence-event-model.md` (already created)
    - Skill file: `docs/skills/correspondence-patterns.md` (new)
    - CONTEXTS.md: already updated

14. **Port Allocation**
    - Reserve port `5248` for Correspondence.Api (next available after Backoffice reservations)
    - Update CLAUDE.md port allocation table

**Dependencies:**
- Customer Identity BC must have `GET /api/customers/{customerId}` endpoint (already exists)
- Returns BC must publish `ReturnApproved`, `ReturnCompleted` events (already implemented in Cycle 27)
- SendGrid account created (stub provider for local development)

**Estimated Effort:**
- **PSA:** 3-4 sessions
  - Session 1: Project scaffold, Marten config, Message aggregate
  - Session 2: Integration handlers (4 events), RabbitMQ wiring
  - Session 3: SendGrid integration, templates, HTTP endpoints
  - Session 4: Integration tests, idempotency validation
- **QA:** 1 session (test plan, manual testing)
- **UXE:** 0.5 sessions (review email templates, suggest copy improvements)
- **PO:** 0.5 sessions (review + sign-off)

**Success Criteria:**
- ✅ Customer receives order confirmation email within 1 minute of checkout
- ✅ Customer receives tracking email within 1 minute of shipment dispatch
- ✅ Customer receives return approval email within 1 minute of approval
- ✅ Customer receives return completed email within 1 minute of return completion
- ✅ Duplicate integration events do not result in duplicate emails
- ✅ Opted-out customers do not receive emails
- ✅ All integration tests pass (100% pass rate)

---

### Phase 2: Returns Events + SMS Support (Cycle 29)

**Goal:** Add remaining Returns events (6 additional handlers) + SMS channel support.

**Priority:** 🟡 P1 — Completes Returns BC integration, adds SMS for high-priority events

**Deliverables:**

1. **Additional Integration Event Handlers (6 Events)**
   - `ReturnDeniedHandler` → Denial reason email
   - `ReturnExpiredHandler` → Expiration reminder email
   - `ReturnReceivedHandler` → Warehouse receipt acknowledgment email
   - `ReturnRejectedHandler` → Inspection rejection email
   - `ShipmentDeliveredHandler` → Delivery confirmation email + review request
   - `RefundCompletedHandler` → Refund confirmation email

2. **SMS Channel Support**
   - `TwilioSmsChannel` implementation (via `ICorrespondenceChannel`)
   - SMS templates (plain text, 160-character limit)
   - Opt-in check: `customer.SmsOptIn == true`
   - SMS-enabled events: `ShipmentDispatched`, `ReturnApproved`

3. **Message Templates (6 Additional Emails)**
   - `ReturnDeniedEmail.cshtml`
   - `ReturnExpiredEmail.cshtml`
   - `ReturnReceivedEmail.cshtml`
   - `ReturnRejectedEmail.cshtml`
   - `ShipmentDeliveredEmail.cshtml` (includes review request CTA)
   - `RefundCompletedEmail.cshtml`

4. **RabbitMQ Queue Expansion**
   - Subscribe to: `correspondence-payments-events` (for `RefundCompleted`)

5. **Integration Tests**
   - 6 new scenarios (one per new handler)
   - SMS channel tests (opt-in / opt-out)

6. **Customer Preference UI (Customer Identity BC)**
   - Add `SmsOptIn` field to Customer entity
   - Add SMS preference toggle to Storefront account settings page

**Estimated Effort:** 2-3 sessions (PSA)

**Success Criteria:**
- ✅ All 10 integration events trigger correspondence (email or SMS)
- ✅ SMS opt-in customers receive SMS for high-priority events
- ✅ SMS messages are under 160 characters (no truncation)

---

### Phase 3: Observability + Push Notifications (Cycle 30+)

**Goal:** Add observability dashboard + push notification support (requires mobile app).

**Priority:** 🟢 P2 — Nice-to-have; mobile app may not exist in Cycle 30

**Deliverables:**

1. **DeliveryMetricsView Projection**
   - Daily metrics: success rate, average delivery time, error counts
   - Aggregated by channel (Email, SMS, Push)

2. **Backoffice Integration**
   - Consume `CorrespondenceFailed` events → alert CS agents to investigate
   - Display message history for customer (via `GET /api/correspondence/messages/{customerId}`)

3. **Push Notification Channel**
   - `FirebaseCloudMessagingChannel` implementation (FCM for Android, APNS for iOS)
   - Push-enabled events: `OrderPlaced`, `ShipmentDelivered`
   - Requires mobile app to register device tokens

4. **Operations Dashboard Integration**
   - Consume `CorrespondenceQueued` events → real-time queue depth monitoring
   - Consume `CorrespondenceDelivered` events → throughput metrics

**Estimated Effort:** 2-3 sessions (PSA)

**Success Criteria:**
- ✅ CS agents can view customer message history in Backoffice
- ✅ Delivery success rate dashboard shows >95% for email
- ✅ Push notifications delivered to mobile app (if app exists)

---

## Phase Dependency Graph

```
Phase 1 (Cycle 28)
  └─> Customer Identity BC: GET /api/customers/{customerId} (already exists ✅)
  └─> Returns BC: ReturnApproved, ReturnCompleted events (already exists ✅)
  └─> Orders BC: OrderPlaced event (already exists ✅)
  └─> Fulfillment BC: ShipmentDispatched event (already exists ✅)

Phase 2 (Cycle 29)
  └─> Depends on Phase 1 (Correspondence.Api must exist)
  └─> Customer Identity BC: SmsOptIn field (new)
  └─> Storefront.Web: SMS preference toggle UI (new)
  └─> Twilio account setup (new)

Phase 3 (Cycle 30+)
  └─> Depends on Phase 2 (all email + SMS handlers complete)
  └─> Backoffice BC must exist (new — Cycle 30)
  └─> Operations Dashboard must exist (new — future)
  └─> Mobile app must exist (new — may not happen)
```

---

## Technology Stack

| Component | Technology | Rationale |
|-----------|------------|-----------|
| **Database** | PostgreSQL + Marten | Event sourcing, inline projections |
| **Messaging** | RabbitMQ + Wolverine | Durable queues, at-least-once delivery |
| **Email Provider** | SendGrid | Industry standard, 100 free emails/day, $0.0001/email after |
| **SMS Provider (Phase 2)** | Twilio | Industry standard, $0.0075/SMS in US |
| **Push Provider (Phase 3)** | Firebase Cloud Messaging (FCM) | Cross-platform (Android + iOS), free |
| **Templates** | Razor (ASP.NET Core) | Type-safe, compile-time errors, version control |
| **HTTP Client** | IHttpClientFactory + Polly | Resilience (timeout, circuit breaker) |
| **Testing** | Alba + TestContainers | Integration tests with real Postgres + RabbitMQ |

---

## Open Questions for Owner Review

1. **Template editing workflow:** Should marketing team be able to edit email templates without deploying code?
   - **Option A:** Razor templates in code (Phase 1)
   - **Option B:** SendGrid Dynamic Templates (Phase 2 migration)
   - **Recommendation:** Start with A (simpler), migrate to B if marketing requests self-service editing

2. **Email "From" address:** What should the sender address be?
   - **Recommendation:** `noreply@crittersupply.com` (requires domain verification in SendGrid)

3. **Unsubscribe link:** Should all transactional emails include an unsubscribe link?
   - **Legal requirement:** CAN-SPAM Act (US) requires unsubscribe link for **commercial** emails. **Transactional** emails (order confirmations, shipping updates) are exempt.
   - **Recommendation:** No unsubscribe link in Phase 1 (transactional only). Add in Phase 2 if marketing emails are added.

4. **Delivery SLA:** How quickly must emails be sent after the triggering event?
   - **Recommendation:** 1-minute target for 95th percentile, 5-minute max for 99th percentile

5. **Email content approval process:** Should UXE or marketing review email templates before Phase 1 release?
   - **Recommendation:** Yes — UXE reviews templates in Cycle 28 planning session

---

## Implementation Checklist (Phase 1)

**Pre-Implementation:**
- [ ] Create GitHub Milestone: "Cycle 28: Correspondence BC Phase 1"
- [ ] Create GitHub Issues for 14 Phase 1 deliverables
- [ ] Reserve port 5248 in CLAUDE.md port allocation table
- [ ] Set up SendGrid account + verify `noreply@crittersupply.com` domain

**Implementation:**
- [ ] Scaffold Correspondence.Api project (Web SDK)
- [ ] Scaffold Correspondence project (regular SDK)
- [ ] Add projects to CritterSupply.slnx
- [ ] Configure Marten (`DatabaseSchemaName = "correspondence"`)
- [ ] Configure Wolverine (RabbitMQ queues)
- [ ] Implement Message aggregate + domain events
- [ ] Implement MessageListView projection
- [ ] Implement 4 integration event handlers
- [ ] Implement SendGrid channel + templates
- [ ] Implement Customer Identity HTTP client + Polly
- [ ] Implement idempotency logic
- [ ] Implement retry logic
- [ ] Implement HTTP endpoints
- [ ] Write 12 integration tests
- [ ] Write 8 unit tests
- [ ] Update CLAUDE.md port allocation table
- [ ] Create skill file: `docs/skills/correspondence-patterns.md`

**Testing:**
- [ ] All unit tests pass (100%)
- [ ] All integration tests pass (100%)
- [ ] Manual testing: order confirmation email received
- [ ] Manual testing: tracking email received
- [ ] Manual testing: return approval email received
- [ ] Manual testing: idempotency verified (duplicate event → single email)

**Documentation:**
- [ ] Update CONTEXTS.md (already done ✅)
- [ ] Update CURRENT-CYCLE.md (already done ✅)
- [ ] Create cycle retrospective: `docs/planning/cycles/cycle-28-correspondence-bc-phase-1.md`

**Sign-Offs:**
- [ ] PSA: Implementation complete, all tests passing
- [ ] UXE: Email templates approved
- [ ] QA: Test plan executed, no critical bugs
- [ ] PO: Success criteria met, ready for production

---

## Summary

The Correspondence BC is a **low-risk, high-value** addition to CritterSupply:

- **Low risk:** Pure choreography (no orchestration sagas), well-defined integration contracts, battle-tested technology stack (SendGrid, Twilio, Marten, Wolverine)
- **High value:** Closes the single biggest customer experience gap (no post-checkout communication)

**Key design decisions:**
- Event-sourced Message aggregate for full audit trail
- Idempotency via Wolverine MessageId
- 3-retry exponential backoff for transient failures
- Polly resilience for Customer Identity BC calls
- Razor templates for type-safe email composition
- Pure choreography (no sagas required)

**Risks mitigated:**
- Schema change detection via contract tests
- Customer Identity BC downtime via circuit breaker
- Duplicate sends via idempotency checks
- Scope creep via boundary enforcement in ADR 0030

**Ready for implementation:** Cycle 28 (Phase 1: 4 events + email channel)
