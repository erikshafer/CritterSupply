# M38.0 Session 1 Retrospective

**Date:** 2026-04-03
**Milestone:** M38.0 — Marketplaces Phase 4: Async Lifecycle + Resilience
**Session:** Session 1 — Walmart Polling + Listings BC Bidirectional Feedback

---

## Baseline

- Build: 0 errors, 12 warnings (pre-existing)
- Marketplaces integration tests: 70 (baseline)
- Listings integration tests: 35 (baseline)
- Combined: 105 tests

---

## Items Completed

| Item | Description |
|------|-------------|
| P-7 | ADR 0055 committed — Walmart submission status polling architecture |
| P-1 | `WalmartMarketplaceAdapter.CheckSubmissionStatusAsync` — real implementation |
| Define | `CheckWalmartFeedStatus` sealed record (internal Marketplaces.Api message) |
| P-2 | `ListingApprovedHandler` Walmart path — schedules `CheckWalmartFeedStatus` instead of publishing `MarketplaceListingActivated` |
| P-3 | `CheckWalmartFeedStatusHandler` — poll → resolve or reschedule with max-attempt guard |
| P-4 | `Listings.Api/Program.cs` — DeclareExchange bindings for `marketplaces-listing-activated` and `marketplaces-submission-rejected` |
| P-5 | `MarketplaceListingActivatedHandler` — `Submitted → Live` with idempotency guards |
| P-6 | `MarketplaceSubmissionRejectedHandler` — `Submitted → Ended (SubmissionRejected)` with idempotency guards |
| Tests | 79 Marketplaces tests (+9 from 70), 41 Listings tests (+6 from 35) |

---

## P-1: Walmart Feed Status Polling

`WalmartMarketplaceAdapter.CheckSubmissionStatusAsync` strips the `wmrt-` prefix from
`externalSubmissionId` to extract the raw Walmart feed ID, then calls
`GET https://marketplace.walmartapis.com/v3/feeds/{feedId}` with the standard Walmart auth
headers (WM_SEC.ACCESS_TOKEN, WM_CONSUMER.ID, WM_SVC.NAME, WM_QOS.CORRELATION_ID).

The four `feedStatus` values and their mappings:

| feedStatus | IsLive | IsFailed | Notes |
|------------|--------|----------|-------|
| `PROCESSED` | true | false | Feed fully processed → activate listing |
| `ERROR` | false | true | Feed error → reject submission |
| `RECEIVED` | false | false | Pending — reschedule |
| `INPROGRESS` | false | false | Pending — reschedule |

HTTP 4xx/5xx errors return `IsFailed: true` with the status code in `FailureReason`.
Exceptions return `IsFailed: true` and never propagate — the handler decides whether to retry.

---

## P-2: ListingApprovedHandler Modification

The Walmart branch is guarded by:

```csharp
if (string.Equals(message.ChannelCode, "WALMART_US", StringComparison.OrdinalIgnoreCase))
{
    var rawFeedId = result.ExternalSubmissionId!.Replace("wmrt-", "", StringComparison.OrdinalIgnoreCase);
    await bus.ScheduleAsync(
        new CheckWalmartFeedStatus(message.ListingId, message.Sku, message.ChannelCode, rawFeedId, AttemptCount: 1),
        TimeSpan.FromMinutes(2));
    return outgoing; // Do NOT add MarketplaceListingActivated here for Walmart
}
// Amazon and eBay fall through to the existing outgoing.Add(MarketplaceListingActivated) line
```

The Amazon and eBay paths are unchanged — they continue to publish `MarketplaceListingActivated`
immediately. Verified by updated integration test
`ListingApproved_WalmartChannel_SchedulesPollInsteadOfPublishingActivated` (Walmart produces no
immediate outcome) and the existing `ListingApproved_AmazonChannel_CallsAdapterAndPublishesActivated`
(Amazon path unchanged, still passes at +0).

`IMessageBus bus` parameter is injected by Wolverine — this is the one sanctioned use of
`IMessageBus` injection in a handler (per Guard Rail 6), used only for `bus.ScheduleAsync()`.

---

## P-3: CheckWalmartFeedStatusHandler

`AttemptCount` approach: starts at 1 (first poll, 2 min after submission). On each reschedule,
`message with { AttemptCount = message.AttemptCount + 1 }` is scheduled with an escalating delay.

Retry delay schedule (per ADR 0055):

| Attempt | Delay |
|---------|-------|
| 1 | 2 min |
| 2 | 5 min |
| 3 | 10 min |
| 4 | 20 min |
| 5+ | 30 min |

Max-attempt timeout: `MaxAttempts = 10`. When `AttemptCount >= 10` and feed is still pending,
publishes `MarketplaceSubmissionRejected` with reason:
`"Walmart feed processing timed out after N attempts"`.

HTTP errors from `CheckSubmissionStatusAsync` set `IsFailed: true` with the status code in
`FailureReason`. The handler treats this as a terminal failure (publishes rejection).

---

## P-4: Queue Name and Exchange Binding Configuration

Queue name: `listings-marketplace-outcome-events`

Exchange bindings were added using the Wolverine.RabbitMQ `DeclareExchange` API (chained from
`UseRabbitMq(...).AutoProvision()`) rather than the non-existent `BindExchange` extension on
`RabbitMqListenerConfiguration`:

```csharp
.DeclareExchange("marketplaces-listing-activated", ex => ex.BindQueue("listings-marketplace-outcome-events", ""))
.DeclareExchange("marketplaces-submission-rejected", ex => ex.BindQueue("listings-marketplace-outcome-events", ""))
```

Combined with `opts.ListenToRabbitQueue("listings-marketplace-outcome-events")`, the `AutoProvision()`
creates the queue, declares the exchanges, and creates the bindings. Handlers for both
`MarketplaceListingActivated` and `MarketplaceSubmissionRejected` are auto-discovered from the
domain assembly (already registered via `opts.Discovery.IncludeAssembly(typeof(Listings.Listing.Listing).Assembly)`).

---

## P-5: MarketplaceListingActivatedHandler — Idempotency Guards

The handler loads the aggregate via `session.Events.AggregateStreamAsync<Listing>`. Guard sequence:

1. `listing is null` → silent no-op (listing not found; possibly already processed or orphaned)
2. `listing.Status == ListingStatus.Live` → silent no-op (duplicate activation message — already Live)
3. `listing.Status != ListingStatus.Submitted` → silent no-op (unexpected state; do not throw)
4. Only if `Submitted`: appends `ListingActivated` domain event + publishes `IntegrationMessages.ListingActivated`

This correctly handles the scenario where a duplicate `MarketplaceListingActivated` arrives after
the listing is already Live (e.g., from a retry or a race between Amazon/eBay immediate path and
a Walmart poll completing late).

---

## P-6: MarketplaceSubmissionRejectedHandler — Idempotency Guards

Guard sequence:

1. `listing is null` → silent no-op
2. `listing.Status == ListingStatus.Ended` → silent no-op (already ended — duplicate rejection)
3. `listing.Status != ListingStatus.Submitted` → silent no-op
4. Only if `Submitted`: appends `ListingEnded(EndedCause.SubmissionRejected)` + publishes `IntegrationMessages.ListingEnded`

---

## Idempotency Considerations

`MarketplaceListingActivated` can be delivered multiple times in edge cases:
- Walmart: feed polling completes, handler publishes activated; if the poll message is redelivered, the guard `Status == Live` prevents double-append.
- Amazon/eBay: `ListingApprovedHandler` publishes activated immediately; if redelivered, same guard applies.

`MarketplaceSubmissionRejected` can be delivered if:
- The Walmart timeout fires and the feed was already rejected by a previous attempt. Guard `Status == Ended` handles this.

All guards use silent no-ops (not exceptions) to ensure the message is ACKed and not requeued.

---

## Outbound Publishing Gap Fixed

During P-5/P-6 implementation, a pre-existing gap was discovered: `Listings.Api` had no
`opts.PublishMessage<T>().ToRabbitExchange(...)` configuration for `ListingActivated` or
`ListingEnded`. Without this, Wolverine drops the messages after `OutgoingMessages` is returned,
meaning downstream consumers (and tests via `tracked.Sent`) would never see them.

Fix: added publisher routes in `Listings.Api/Program.cs`:
```csharp
opts.PublishMessage<Messages.Contracts.Listings.ListingActivated>()
    .ToRabbitExchange("listings-listing-activated");
opts.PublishMessage<Messages.Contracts.Listings.ListingEnded>()
    .ToRabbitExchange("listings-listing-ended");
```

---

## Test Counts

| BC | Start | End | Delta |
|----|-------|-----|-------|
| Marketplaces | 70 | 79 | +9 |
| Listings | 35 | 41 | +6 |
| **Combined** | **105** | **120** | **+15** |

### Marketplaces test delta breakdown
- `WalmartMarketplaceAdapterTests`: replaced 1 skeleton with 5 real tests (+4)
- `WalmartPollingHandlerTests`: 5 new unit-style tests (+5)
- `ListingSubmissionFlowTests`: updated Walmart test (1→1, behavior change)
- Total: +9 (70 → 79)

### Listings test delta breakdown
- `MarketplaceListingActivatedHandlerTests`: 3 new tests (+3)
- `MarketplaceSubmissionRejectedHandlerTests`: 3 new tests (+3)
- Total: +6 (35 → 41)

---

## Build State at Session Close

- Errors: 0
- Warnings: 12 (all pre-existing; no new warnings introduced)

---

## Known Notes

1. **Wolverine `ScheduleAsync` recording in unit tests**: Wolverine's `ScheduleAsync` extension
   method routes through the Wolverine runtime durable persistence layer. When called on a plain
   `IMessageBus` stub outside a running host, the scheduled message is issued but cannot be
   intercepted via `SendAsync`/`PublishAsync` captures. The reschedule test verifies the
   observable behavior (no outcome messages) rather than the scheduling side-effect.

2. **Wolverine `DeclareExchange` API for exchange binding**: The correct API in Wolverine.RabbitMQ 5.27
   is `RabbitMqTransportExpression.DeclareExchange(name, configure)` with `configure.BindQueue(queueName, "")`.
   The `BindExchange` extension on `RabbitMqListenerConfiguration` does not exist in this version.

---

## Session 2 Pickup

Session 2 should start with:
1. **P-8: Polly resilience on `CheckSubmissionStatusAsync`** — wrap the real Walmart HTTP call
   in a retry + circuit-breaker policy so transient HTTP errors don't immediately publish rejection.
2. **P-9 through P-12: Listing deactivation** — `DeactivateListing` command handler, Marketplaces
   BC reaction to `ListingEnded`, and `DeactivateListingAsync` on each adapter.
