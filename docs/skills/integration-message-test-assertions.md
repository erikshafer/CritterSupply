# Integration message test assertions

> **Status:** Convention codified in M46.0 (2026-05-08).
> **Helper:** `tests/Shared/CritterSupply.TestUtilities/IntegrationMessageAssertions.cs`
> **Skill family:** Critter Stack testing patterns.

## TL;DR

`tracked.Sent.MessagesOf<T>()` is **silently empty** for any message type that
the test host has no route configured for — including every integration
message that targets a RabbitMQ queue when external transports are disabled
in the fixture. Assertions phrased as

```csharp
tracked.Sent.MessagesOf<T>().ShouldBeEmpty();          // false-green: was never even attempted
tracked.Sent.MessagesOf<T>().ShouldHaveSingleItem();   // throws unhelpfully — root cause is routing
```

…pass or fail for the wrong reason. Use the
`IntegrationMessageAssertions` helper from
`CritterSupply.TestUtilities` instead:

```csharp
using CritterSupply.TestUtilities;

// Single message expected — clearest call site:
var msg = tracked.ShouldHaveSentSingleIntegrationMessage<RefundRequested>();
msg.OrderId.ShouldBe(orderId);

// One-or-more matching messages:
var msgs = tracked.ShouldHaveSentIntegrationMessage<TrackingNumberAssigned>();
msgs.Count.ShouldBeGreaterThanOrEqualTo(1);

// Asserting absence — must opt in:
tracked.AssertIntegrationMessageNotPublished<RefundRequested>(routeIsConfigured: true);
```

## Why the naive pattern is dangerous

Wolverine routes integration messages based on the publishing rules
configured in the host's `WolverineOptions`. When no route can be determined
for a message type:

1. Wolverine logs **a single** `No routes can be determined for...` warning.
2. The envelope is **not recorded on the tracked session**.
3. The `MessagesOf<T>()` collection is therefore **empty**.

In a test fixture that disables RabbitMQ — most of CritterSupply's
integration-test fixtures do, because we don't want a broker dependency in
the test container set — *every* integration message that doesn't have an
explicit `opts.PublishMessage<T>().ToLocalQueue(...)` rule falls into this
silent bucket.

Two real-world examples already documented in the codebase:

- `tests/Inventory/Inventory.Api.IntegrationTests/Management/AlertFeedViewTests.cs`
- `tests/Inventory/Inventory.Api.IntegrationTests/Management/PhysicalPickShipTests.cs`

In both, an earlier author asserted on `tracked.Sent.MessagesOf<TIntegration>()`,
the assertion silently passed, and the actual verification has to read the
Marten event stream to be meaningful. The agent memory captures this as a
hard-won lesson — **promote it to a helper so future tests can't repeat the
mistake.**

## Decision tree

```
Are you asserting an integration message was published?
├── Yes, exactly one  → tracked.ShouldHaveSentSingleIntegrationMessage<T>()
├── Yes, one-or-more  → tracked.ShouldHaveSentIntegrationMessage<T>()
└── No, asserting absence
    ├── Configure a test route, then
    │    tracked.AssertIntegrationMessageNotPublished<T>(routeIsConfigured: true)
    └── Or skip the integration assertion entirely and verify on the
         Marten event stream that the producing domain event was not
         appended (the event is the source of truth; the integration
         message is a downstream projection of routing).
```

## How to configure a test route

When you genuinely want to verify integration message *content* (not just
the producing domain event), wire the message to a local in-memory queue in
the test host so the envelope is captured by tracking:

```csharp
// Inside Alba's ConfigureServices / ConfigureApplication:
host.Services.Configure<WolverineOptions>(opts =>
{
    opts.PublishMessage<RefundRequested>().ToLocalQueue("test-refund-requested");
});
```

For most CritterSupply fixtures, however, the cheapest fix is **to verify on
the Marten event stream** — the domain event already encodes the same
information and persists deterministically.

## Audit (M46.0)

22 test files in the suite call `Sent.MessagesOf<T>()`. Each falls into one
of three categories:

| Category | Count | Action |
| -------- | ----: | ------ |
| **Verified-by-route** — host configures a route for `T` (e.g., Returns cross-BC tests, Fulfillment tests with RabbitMQ in-fixture) | ~12 | Safe today. Migrating to `ShouldHaveSentIntegrationMessage<T>()` is a low-priority readability improvement; not required. |
| **Verified-by-stream** — test also asserts on the Marten event stream | ~6 | Safe today. The integration assertion is decorative; the stream assertion is load-bearing. Migration optional. |
| **At-risk** — no route configured, no stream assertion, asserts on `MessagesOf<T>()` only | ≤ 4 | **Migrate to the helper** as part of the next BC remaster session that touches the surrounding code. Adding the helper triggers the diagnostic the next time the test runs. |

A full file-by-file audit is intentionally out of scope for M46.0 — the
helper exists, the convention is documented, and the next time any of these
tests is touched the new assertion can land alongside the change. The cost
of a sweeping rename is not currently justified.

## Related skills

- `docs/skills/critterstack-testing-patterns.md` — Alba fixture patterns.
- `docs/skills/integration-messaging.md` — When to use integration vs domain
  events; queue-naming conventions.
- `docs/skills/marten-event-sourcing.md` — Why the event stream is the
  source of truth.
