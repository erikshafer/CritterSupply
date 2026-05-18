# Correspondence

> **Source folder:** `src/Correspondence/`
> **Status:** Implemented
> **Most recent material milestone:** M31.0 — Phase 2 (7 additional integration events + SMS channel infrastructure)
> **Dossier depth:** S2 — full

## Purpose

Correspondence owns transactional customer messaging — the email and SMS notifications triggered by business events from across the rest of the system. It subscribes to lifecycle events from Orders, Fulfillment, Returns, and Payments, opens a `Message` event stream per inbound trigger, queues an internal `SendMessage` command, calls out to a provider abstraction, and records the delivery outcome on the same stream. Outbound integration events (`CorrespondenceQueued`, `CorrespondenceDelivered`, `CorrespondenceFailed`) are republished onto monitoring, analytics, and Backoffice queues. Real provider integrations (SendGrid, Twilio) are stubbed at this milestone; the production wiring is deferred.

## Aggregates

### `Message`

- **Stream ID:** UUID v7. The aggregate exposes no static `StreamId(...)` derivation method; the stream identifier is the `MessageId` generated inside `MessageFactory.Create` via `Guid.CreateVersion7()` (`src/Correspondence/Correspondence/Messages/Message.cs#L75`) and passed through to Marten by the inbound handlers via `MartenOps.StartStream<Message>(message.Id, messageQueued)` (e.g. `src/Correspondence/Correspondence/Messages/OrderPlacedHandler.cs#L43`). Each inbound integration event therefore opens a fresh stream — there is no upstream-key-derived stream id and no idempotency key on the inbound boundary other than the message-bus delivery semantics.
- **Marten configuration:** `opts.Projections.Snapshot<Message>(SnapshotLifecycle.Inline)` (`src/Correspondence/Correspondence.Api/Program.cs#L45`). Schema name `correspondence` (`Program.cs#L39`, sourced from `Constants.Correspondence`).
- **Key state:** `Id`, `CustomerId`, `Channel` (string — `"Email"` is the only value emitted by current handlers), `TemplateId`, `Subject`, `Body`, `Status` (`MessageStatus` enum), `AttemptCount`, `QueuedAt`, `DeliveredAt?`, `Attempts` (list of `DeliveryAttempt` value records) (`src/Correspondence/Correspondence/Messages/Message.cs#L5-L17`).
- **Lifecycle stages** (the `MessageStatus` enum at `src/Correspondence/Correspondence/Messages/MessageStatus.cs#L5-L10`):
  - `Queued` — set by `MessageFactory.Create` (`Message.cs#L67-L100`) and the `Apply(MessageQueued)` projector (`Message.cs#L19-L29`). Also re-entered by `Apply(DeliveryFailed)` for attempts 1 and 2 (`Message.cs#L42-L44`).
  - `Delivered` — terminal-on-success; set by `Apply(MessageDelivered)` (`Message.cs#L31-L40`).
  - `Failed` — terminal-on-exhaustion; set by `Apply(DeliveryFailed)` when `AttemptNumber >= 3` (`Message.cs#L42`).
  - `Skipped` — terminal-pre-send; set by `Apply(MessageSkipped)` (`Message.cs#L57-L60`) and by `MessageFactory.Skip` (`Message.cs#L102-L114`). The `Skip` factory is exercised only by `tests/Correspondence/Correspondence.UnitTests/MessageAggregateTests.cs#L66-L82`; no production handler instantiates `MessageSkipped` (see §Declared, not emitted).
- **Value object:** `DeliveryAttempt` (`src/Correspondence/Correspondence/Messages/DeliveryAttempt.cs#L3-L10`) — appended by the `Apply(MessageDelivered)` and `Apply(DeliveryFailed)` projectors and persisted as part of the `Message` snapshot.
- **File:** `src/Correspondence/Correspondence/Messages/Message.cs`.

## Commands

Direct enumeration via `grep -rn "public sealed record" --include="*.cs" src/Correspondence/` against the handler set yields **one** command record, matching the S1 stub.

- `SendMessage(Guid MessageId)` — internal, message-bus only. Defined at `src/Correspondence/Correspondence/Messages/SendMessage.cs#L7`. Triggered by every inbound integration handler immediately after `MartenOps.StartStream<Message>` (e.g. `OrderPlacedHandler.cs#L57`) and re-scheduled by `SendMessageHandler` itself for delayed retries via `outgoing.Add(new SendMessage(message.Id).DelayedFor(delay))` (`SendMessageHandler.cs#L94`). The retry schedule is `5 min`, `30 min`, `2 hr` for attempts 1/2/3, hard-coded in the `switch` at `SendMessageHandler.cs#L88-L93`. After three retriable failures, the handler emits `CorrespondenceFailed` instead of re-queuing (`SendMessageHandler.cs#L97-L104`).
- Compound handler shape: `Before(SendMessage, Message?)` returns `HandlerContinuation.Stop` for missing aggregate or terminal `Delivered` / `Skipped` status (`SendMessageHandler.cs#L14-L20`); `Handle` is async, returns `(Events, OutgoingMessages)`, takes `Message` via `[WriteAggregate]`, and depends on `IEmailProvider` (`SendMessageHandler.cs#L22-L30`).

There is no public HTTP command surface; all writes are message-bus driven by the inbound integration handlers.

## Domain events

All four events are records under `src/Correspondence/Correspondence/Messages/`. Persisted in the `correspondence` Marten schema. The `Message` snapshot is inline-projected; the `MessageListView` projection is inline (`src/Correspondence/Correspondence.Api/Program.cs#L45-L47`).

- `MessageQueued(Guid MessageId, Guid CustomerId, string Channel, string TemplateId, string Subject, string Body, DateTimeOffset QueuedAt)` (`src/Correspondence/Correspondence/Messages/MessageQueued.cs#L7-L15`). Emitted by `MessageFactory.Create` (`Message.cs#L78-L86`); the factory is the call target of every inbound integration handler (12 sites, listed under §Inbound). Aggregate apply: `Message.cs#L19-L29`. Projection branch: `MessageListView.cs#L25-L36`.
- `MessageDelivered(Guid MessageId, DateTimeOffset DeliveredAt, int AttemptNumber, string ProviderResponse)` (`src/Correspondence/Correspondence/Messages/MessageDelivered.cs#L7-L11`). Emitted by `SendMessageHandler.Handle` on `ProviderResult.Success == true` (`SendMessageHandler.cs#L42-L50`). Apply: `Message.cs#L31-L40`. Projection branch: `MessageListView.cs#L38-L46`.
- `DeliveryFailed(Guid MessageId, int AttemptNumber, DateTimeOffset FailedAt, string ErrorMessage, string ProviderResponse)` (`src/Correspondence/Correspondence/Messages/DeliveryFailed.cs#L7-L13`). Emitted by `SendMessageHandler.BuildFailureResult` on every provider failure (success-`false` result or thrown non-`OperationCanceledException`) (`SendMessageHandler.cs#L75-L82`). Apply: `Message.cs#L42-L55`. Projection branch: `MessageListView.cs#L48-L57`.
- `MessageSkipped(Guid MessageId, string Reason)` (`src/Correspondence/Correspondence/Messages/MessageSkipped.cs#L7-L10`). Apply: `Message.cs#L57-L60`. Projection branch: `MessageListView.cs#L59-L66`. **Emitter:** none in production code; only `MessageFactory.Skip` (`Message.cs#L102-L114`) constructs the event, and that factory is invoked only by the unit test at `tests/Correspondence/Correspondence.UnitTests/MessageAggregateTests.cs#L66-L82`. See §Declared, not emitted.

Total: 4 events (matches S1). Three of the four are reachable by the production write path; `MessageSkipped` is wired into both the aggregate `Apply` set and the inline projection but has no production emitter — the customer-opt-out / channel-disabled path it represents is not yet exercised.

## Projections

Two projections registered in `Program.cs` (`src/Correspondence/Correspondence.Api/Program.cs#L45-L47`); both run inline.

- **`Message` snapshot** — inline; keyed by stream ID (`Guid`); registered via `opts.Projections.Snapshot<Message>(SnapshotLifecycle.Inline)`. Source events: all four. Loaded by `SendMessageHandler` via the `[WriteAggregate]` parameter and by `GetMessageDetails` via `session.Events.AggregateStreamAsync<Message>(messageId)` (`src/Correspondence/Correspondence.Api/Queries/GetMessageDetails.cs#L20`).
- **`MessageListViewProjection`** — inline `SingleStreamProjection<MessageListView, Guid>` (`src/Correspondence/Correspondence/Messages/MessageListView.cs#L23-L67`). Keyed by `MessageId`. Source events: all four (`Create(MessageQueued)` at `L25`, `Apply(MessageDelivered)` at `L38`, `Apply(DeliveryFailed)` at `L48`, `Apply(MessageSkipped)` at `L59`). Read by `GetMessagesForCustomer` via `session.Query<MessageListView>().Where(m => m.CustomerId == customerId)` (`src/Correspondence/Correspondence.Api/Queries/GetMessagesForCustomer.cs#L20-L25`).

## Integration events

### Outbound (published)

Three contracts under `src/Shared/Messages.Contracts/Correspondence/`:

- `Correspondence.CorrespondenceQueued(Guid MessageId, Guid CustomerId, string Channel, DateTimeOffset QueuedAt)` (`src/Shared/Messages.Contracts/Correspondence/CorrespondenceQueued.cs#L7-L12`). Emitted by every inbound integration handler immediately after the `MartenOps.StartStream<Message>` call (12 emit sites, e.g. `OrderPlacedHandler.cs#L49-L54`).
- `Correspondence.CorrespondenceDelivered(Guid MessageId, Guid CustomerId, string Channel, DateTimeOffset DeliveredAt, int AttemptCount)` (`src/Shared/Messages.Contracts/Correspondence/CorrespondenceDelivered.cs#L7-L13`). Emitted by `SendMessageHandler.Handle` on the success branch (`SendMessageHandler.cs#L52-L59`).
- `Correspondence.CorrespondenceFailed(Guid MessageId, Guid CustomerId, string Channel, string FailureReason, DateTimeOffset FailedAt)` (`src/Shared/Messages.Contracts/Correspondence/CorrespondenceFailed.cs#L7-L13`). Emitted by `SendMessageHandler.BuildFailureResult` on attempt 3 or non-retriable failure (`SendMessageHandler.cs#L97-L104`).

Each of the three is routed twice in `Program.cs` (`src/Correspondence/Correspondence.Api/Program.cs#L109-L125`): once onto the original monitoring/analytics/admin queues (`monitoring-correspondence-events`, `analytics-correspondence-events`, `admin-correspondence-failures`) and once — added in M33.0 Session 2 — onto Backoffice's three operations-dashboard queues (`backoffice-correspondence-queued`, `backoffice-correspondence-delivered`, `backoffice-correspondence-failed`).

### Inbound (subscribed)

Four RabbitMQ queues are listened to (`src/Correspondence/Correspondence.Api/Program.cs#L96-L106`), all with `.ProcessInline()`:

- `correspondence-orders-events`
- `correspondence-fulfillment-events`
- `correspondence-returns-events`
- `correspondence-payments-events`

Direct enumeration of integration-handler files (`ls src/Correspondence/Correspondence/Messages/*Handler.cs` minus `SendMessageHandler.cs`) yields **12** inbound handlers, against the S1 stub's claim of 13. Each handler reads an integration event from `Messages.Contracts.<owner>`, calls `MessageFactory.Create` with `channel: "Email"`, returns `(IStartStream, OutgoingMessages)` containing the `MartenOps.StartStream<Message>` op, a `CorrespondenceQueued` integration event, and a `SendMessage` command. Grouped by source BC:

- **Orders (1):** `OrderPlacedHandler` (`src/Correspondence/Correspondence/Messages/OrderPlacedHandler.cs#L14-L60`) — consumes `Messages.Contracts.Orders.OrderPlaced`. **No `OrderCancelled` handler exists**; the S1 stub claim of two Orders subscriptions does not match the code. The drift is documented in §CONTEXTS.md drift below.
- **Fulfillment (6):** `BackorderCreatedHandler` (`BackorderCreated`), `DeliveryAttemptFailedHandler` (`DeliveryAttemptFailed`), `ReturnToSenderInitiatedHandler` (`ReturnToSenderInitiated`), `ShipmentDeliveredHandler` (`ShipmentDelivered`), `ShipmentHandedToCarrierHandler` (`ShipmentHandedToCarrier`), `ShipmentLostInTransitHandler` (`ShipmentLostInTransit`). All under `src/Correspondence/Correspondence/Messages/`.
- **Returns (4):** `ReturnApprovedHandler` (`ReturnApproved`), `ReturnCompletedHandler` (`ReturnCompleted`), `ReturnDeniedHandler` (`ReturnDenied`), `ReturnExpiredHandler` (`ReturnExpired`).
- **Payments (1):** `RefundCompletedHandler` (`RefundCompleted`).

Total inbound: 12 integration message types across 4 queues.

### Routes-without-instantiator

- `ISmsProvider` and `StubSmsProvider` are registered in DI (`src/Correspondence/Correspondence/Providers/ISmsProvider.cs#L8-L11`, `src/Correspondence/Correspondence/Providers/StubSmsProvider.cs#L8-L41`, registration `src/Correspondence/Correspondence.Api/Program.cs#L57`) but no handler resolves the interface. `SendMessageHandler.Handle` takes `IEmailProvider` only (`SendMessageHandler.cs#L25`), and every inbound handler hard-codes `channel: "Email"` (12 sites, e.g. `OrderPlacedHandler.cs#L36`). The SMS channel infrastructure described in M31.0 exists at the type and DI levels but has no caller.
- The provider abstraction also defines `PushMessage` (`src/Correspondence/Correspondence/Providers/ProviderTypes.cs#L29-L34`); no `IPushProvider` interface or implementation exists.

### Declared, not emitted

- `MessageSkipped` event — instantiated only via `MessageFactory.Skip` (`Message.cs#L106`), which is called only from `tests/Correspondence/Correspondence.UnitTests/MessageAggregateTests.cs#L69`. The opt-out / channel-disabled path it documents is wired through projector and aggregate but has no production trigger; the customer-preferences integration with Customer Identity it depends on is marked with TODO comments in every inbound handler (e.g. `OrderPlacedHandler.cs#L16-L18`).

### CONTEXTS.md drift

Code-authoritative findings to forward to S5:

- `CONTEXTS.md` line 250 names `ShipmentDispatched` as the consumed Fulfillment event. The actual subscription is `ShipmentHandedToCarrier` (the M41.0 successor event referenced in the same line); no `ShipmentDispatched` handler exists in `src/Correspondence/`.
- `CONTEXTS.md` line 250 names three Fulfillment subscriptions (`ShipmentDispatched`, `ShipmentDelivered`, `ReturnToSenderInitiated`); the code subscribes to six (`BackorderCreated`, `DeliveryAttemptFailed`, `ReturnToSenderInitiated`, `ShipmentDelivered`, `ShipmentHandedToCarrier`, `ShipmentLostInTransit`). The three additions arrived with the M41.0 / M31.0 lineage but are not reflected in CONTEXTS.md.
- `CONTEXTS.md` line 249 lists only `OrderPlaced` for the Orders edge — consistent with code. The S1 stub for this BC claimed an `OrderCancelled` subscription; CONTEXTS.md does not, and code does not.
- The M33.0 Session 2 outbound routes to the three `backoffice-correspondence-*` queues are present in code (`Program.cs#L120-L125`) but only described tangentially in CONTEXTS.md's Backoffice entry (line 289) as "Correspondence history for CS workflows."

## Provider abstraction

- **Interfaces:** `IEmailProvider.SendEmailAsync(EmailMessage, CancellationToken) -> Task<ProviderResult>` (`src/Correspondence/Correspondence/Providers/IEmailProvider.cs#L7-L9`); `ISmsProvider.SendSmsAsync(SmsMessage, CancellationToken) -> Task<ProviderResult>` (`src/Correspondence/Correspondence/Providers/ISmsProvider.cs#L8-L10`).
- **Result type:** `ProviderResult(bool Success, string? ProviderId, string? FailureReason, bool IsRetriable)` (`src/Correspondence/Correspondence/Providers/ProviderTypes.cs#L4-L9`). The `IsRetriable` flag drives the retry/permanent-fail branch in `SendMessageHandler.BuildFailureResult` (`SendMessageHandler.cs#L88`).
- **Message records:** `EmailMessage` (`ProviderTypes.cs#L12-L19`), `SmsMessage` (`ProviderTypes.cs#L22-L26`), `PushMessage` (`ProviderTypes.cs#L29-L34`).
- **Stub implementations:** `StubEmailProvider` (`src/Correspondence/Correspondence/Providers/StubEmailProvider.cs#L8-L48`) — simulates a SendGrid `202 Accepted` with a synthetic `sendgrid-{guid:N}.filter001` provider id; exposes `SimulateFailureFor(string email)` and `ClearFailureSimulation()` for test injection (`L40-L48`). `StubSmsProvider` (`src/Correspondence/Correspondence/Providers/StubSmsProvider.cs#L9-L41`) — logs at `Information` and returns success with a synthetic 34-char Twilio-shaped SID.
- **DI registration:** `builder.Services.AddSingleton<IEmailProvider, StubEmailProvider>()` and `AddSingleton<ISmsProvider, StubSmsProvider>()` at `src/Correspondence/Correspondence.Api/Program.cs#L56-L57`. No production `SendGridEmailProvider` / `TwilioSmsProvider` types exist in the tree; no feature-flag or environment-conditional swap is wired (the registrations are unconditional).

## Sagas / orchestration

Not applicable — this BC is not a saga orchestrator. The "retry" lifecycle on a `Message` is implemented by re-queuing the `SendMessage` command via Wolverine durable scheduled messaging (`SendMessageHandler.cs#L94`), not by a stateful saga.

## HTTP / API surface

Two read endpoints under `src/Correspondence/Correspondence.Api/Queries/`. No HTTP write endpoints.

- `GET /api/correspondence/messages/{messageId}` — `GetMessageDetails`. `[Authorize(Policy = "CustomerService")]`. Returns `Ok<Message>` from `session.Events.AggregateStreamAsync<Message>(messageId)` or `NotFound`. Source: `src/Correspondence/Correspondence.Api/Queries/GetMessageDetails.cs#L13-L28`. Used by Backoffice for customer-service investigation of delivery issues.
- `GET /api/correspondence/messages/customer/{customerId}` — `GetMessagesForCustomer`. `[Authorize(Policy = "CustomerService")]`. Returns `IReadOnlyList<MessageListView>` ordered by `QueuedAt DESC` from `session.Query<MessageListView>()`. Source: `src/Correspondence/Correspondence.Api/Queries/GetMessagesForCustomer.cs#L13-L26`. Comment at `L9-L10` records the intended Customer Experience consumption (`View My Messages` page).

### Health and ops

- `GET /health`, `GET /alive` — Aspire defaults via `MapDefaultEndpoints()` (`Program.cs#L181`).
- `GET /api/v1/health` — explicit health check, development-only (`Program.cs#L186`).
- `GET /` — `301 Moved Permanently` to `/api` (`Program.cs#L194-L198`).
- `GET /api/v1/swagger.json` and `/api` Swagger UI — development-only (`Program.cs#L165-L177`).

## Frontend surface

Not applicable — no frontend in this BC. Customer Experience and Backoffice are the two BCs that surface `Message` data to users.

## Identity / auth posture

- **Scheme:** Single JWT Bearer registration named `"Backoffice"` (`src/Correspondence/Correspondence.Api/Program.cs#L130-L148`). `Authority = https://localhost:5249`, `Audience = https://localhost:5249`, `RequireHttpsMetadata = false` in development, `RoleClaimType = "role"`. There is no Vendor or Customer Identity scheme registered.
- **Policies:** Two — `CustomerService` (allows roles `CustomerService`, `OperationsManager`, `SystemAdmin`) and `OperationsManager` (allows roles `OperationsManager`, `SystemAdmin`) (`Program.cs#L151-L162`). Both are pinned to the `Backoffice` scheme. `CustomerService` is applied to the two read endpoints; `OperationsManager` is registered but not referenced by any in-tree endpoint attribute.
- **Source:** `src/Correspondence/Correspondence.Api/Program.cs`.

## Tests as behavioral evidence

No Gherkin features under `docs/features/correspondence/`.

Unit tests under `tests/Correspondence/Correspondence.UnitTests/` (xUnit; in-process; no infrastructure):

- `MessageAggregateTests` (12) — every `Message` aggregate `Apply(...)` branch and `MessageFactory.Create`/`Skip` factory rule, including the three lifecycle paths `Queued → Delivered`, `Queued → Failed → Delivered` (retry), and `Queued → Failed (permanent)` (`tests/Correspondence/Correspondence.UnitTests/MessageAggregateTests.cs#L17-L300`).

Integration tests under `tests/Correspondence/Correspondence.Api.IntegrationTests/` (xUnit, Alba + Testcontainers via `IntegrationTestCollection`; `TestFixture.cs` provisions Postgres only and disables external Wolverine transports):

- `OrderPlacedHandlerTests` (5) — `OrderPlaced` opens a `Message` stream and the stub provider delivers; `CorrespondenceQueued` is published; the `SendMessage` command is scheduled; duplicate-`OrderId` idempotency does not double-create; `GET /api/correspondence/messages/customer/{customerId}` returns the order-confirmation message.
- `FulfillmentEventHandlerTests` (7) — `DeliveryAttemptFailed` (attempts 1 and 3), `BackorderCreated`, and `ShipmentLostInTransit` each create the appropriate `Message` and publish `CorrespondenceQueued`.

Total: 12 unit + 12 integration = 24 tests. Shared lifecycle: `tests/Correspondence/Correspondence.Api.IntegrationTests/TestFixture.cs` and `IntegrationTestCollection.cs`. Eight of the twelve inbound handlers (`ReturnApproved`, `ReturnCompleted`, `ReturnDenied`, `ReturnExpired`, `RefundCompleted`, `ReturnToSenderInitiated`, `ShipmentDelivered`, `ShipmentHandedToCarrier`) have no dedicated integration test class.

A second cross-BC test class lives outside this BC's folder: `tests/Backoffice/Backoffice.Api.IntegrationTests/CustomerService/CorrespondenceHistoryTests.cs` — exercises the Backoffice consumer of `GET /api/correspondence/messages/customer/{customerId}` against a stub correspondence client (`tests/Backoffice/Backoffice.E2ETests/Stubs/StubCorrespondenceClient.cs`).

## ADRs

- **ADR 0030** — Notifications BC Renamed to Correspondence BC. Establishes the "Correspondence" name to disambiguate from the real-time UI updates owned by Customer Experience. The folder name `src/Correspondence/`, the Marten schema name `correspondence` (`Constants.cs#L5`), and the contract namespace `Messages.Contracts.Correspondence` reflect the rename. File: `docs/decisions/0030-notifications-to-correspondence-rename.md`.

## Prior event modeling

- `docs/planning/correspondence-event-model.md` (737 lines, dated 2026-03-13, marked "Approved for implementation") — defines the pure-choreography pattern, the `Message` aggregate, the channel abstraction, and the integration-event subscription set. The artifact's executive summary cites "10 integration event subscriptions"; the current handler set is 12 (see §Inbound). The `MessageSkipped` event and `MessageFactory.Skip` factory described in the model are present in code but unwired on the production write path (see §Declared, not emitted).
- `docs/planning/correspondence-risk-analysis-roadmap.md` (559 lines) — risk-and-roadmap document. Forward-looking content (production provider wiring, customer-preferences integration, SMS rollout) is out of scope for this dossier; only the elements present in the current code (stub providers, `Email`-only channel hard-coding, retry schedule, idempotency framing) are reflected here.

## Source citations (S2 full)

- `src/Correspondence/` (folder root)
- `src/Correspondence/Correspondence/Messages/Message.cs`, `MessageStatus.cs`, `DeliveryAttempt.cs`
- `src/Correspondence/Correspondence/Messages/MessageQueued.cs`, `MessageDelivered.cs`, `DeliveryFailed.cs`, `MessageSkipped.cs`
- `src/Correspondence/Correspondence/Messages/SendMessage.cs`, `SendMessageHandler.cs`
- `src/Correspondence/Correspondence/Messages/OrderPlacedHandler.cs`, `BackorderCreatedHandler.cs`, `DeliveryAttemptFailedHandler.cs`, `ReturnToSenderInitiatedHandler.cs`, `ShipmentDeliveredHandler.cs`, `ShipmentHandedToCarrierHandler.cs`, `ShipmentLostInTransitHandler.cs`, `ReturnApprovedHandler.cs`, `ReturnCompletedHandler.cs`, `ReturnDeniedHandler.cs`, `ReturnExpiredHandler.cs`, `RefundCompletedHandler.cs`
- `src/Correspondence/Correspondence/Messages/MessageListView.cs`
- `src/Correspondence/Correspondence/Providers/IEmailProvider.cs`, `ISmsProvider.cs`, `StubEmailProvider.cs`, `StubSmsProvider.cs`, `ProviderTypes.cs`
- `src/Correspondence/Correspondence/Constants.cs`, `AssemblyAttributes.cs`
- `src/Correspondence/Correspondence.Api/Program.cs`
- `src/Correspondence/Correspondence.Api/Queries/GetMessageDetails.cs`, `GetMessagesForCustomer.cs`
- `src/Shared/Messages.Contracts/Correspondence/CorrespondenceQueued.cs`, `CorrespondenceDelivered.cs`, `CorrespondenceFailed.cs`
- `src/Shared/Messages.Contracts/Orders/OrderPlaced.cs`
- `src/Shared/Messages.Contracts/Fulfillment/BackorderCreated.cs`, `DeliveryAttemptFailed.cs`, `ReturnToSenderInitiated.cs`, `ShipmentDelivered.cs`, `ShipmentHandedToCarrier.cs`, `ShipmentLostInTransit.cs`
- `src/Shared/Messages.Contracts/Returns/ReturnApproved.cs`, `ReturnCompleted.cs`, `ReturnDenied.cs`, `ReturnExpired.cs`
- `src/Shared/Messages.Contracts/Payments/RefundCompleted.cs`
- `CONTEXTS.md` (section: `Correspondence`, lines 241–255)
- `docs/decisions/0030-notifications-to-correspondence-rename.md`
- `docs/planning/correspondence-event-model.md`
- `docs/planning/correspondence-risk-analysis-roadmap.md`
- `tests/Correspondence/Correspondence.UnitTests/MessageAggregateTests.cs`
- `tests/Correspondence/Correspondence.Api.IntegrationTests/OrderPlacedHandlerTests.cs`, `FulfillmentEventHandlerTests.cs`, `TestFixture.cs`, `IntegrationTestCollection.cs`
- `tests/Backoffice/Backoffice.Api.IntegrationTests/CustomerService/CorrespondenceHistoryTests.cs`
