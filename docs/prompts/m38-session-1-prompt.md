# M38.0 — Session 1: Walmart Polling + Listings BC Bidirectional Feedback

## Where We Are

The M38.0 planning session is complete. Read these documents before writing a single line:

- `docs/planning/milestones/m38-0-plan.md` — **mandatory first read** — all five planning
  decisions, the scope table (P-1 through P-14), and the full Session 1 scope detail (Section 4)
- `docs/planning/milestones/m38-0-planning-session-retrospective.md` — Phase A research
  findings (especially the adapter polling behavior audit confirming Walmart-only polling)

**Baseline at session start:**
- 105 integration tests (35 Listings + 70 Marketplaces), 0 failures
- Build: 0 errors, 12 warnings (all pre-existing)
- CI Run #872 (green), E2E Run #455 (green)

**The critical gap this session closes:** The Listings BC has no handler for
`MarketplaceListingActivated` or `MarketplaceSubmissionRejected`. Every marketplace listing
approved and submitted in M37.0 stays in `Submitted` status forever — the feedback loop from
Marketplaces BC back to Listings BC was never wired. Session 1 closes that gap and adds
Walmart-specific async polling so the Walmart path resolves its feed-based submissions.

**Decisions locked in planning (do not reopen):**
- Q1: Marketplaces BC owns polling (Listings BC only consumes outcome events)
- Q2: Per-submission scheduled message via `bus.ScheduleAsync()`
- Q3: Walmart polling only; Amazon and eBay are synchronous (no polling needed)
- Q4: Bidirectional feedback (this session) before deactivation (Session 2)
- Q5: Orphaned eBay draft deferred to M38.1

---

Read before starting:
- `docs/planning/milestones/m38-0-plan.md` — Sections 2 (decisions), 3 (scope table), 4 (Session 1 detail)
- `src/Listings/Listings/Listing/ActivateListing.cs` — handler already exists; understand its pattern before writing `MarketplaceListingActivatedHandler`
- `src/Listings/Listings/Listing/Events.cs` — `ListingActivated`, `ListingEnded`, and `EndedCause.SubmissionRejected` all exist
- `src/Listings/Listings/Listing/Listing.cs` — `Apply(ListingActivated)` and `Apply(ListingEnded)` already on aggregate
- `src/Listings/Listings.Api/Program.cs` — add the new listener here; note existing queue pattern
- `src/Marketplaces/Marketplaces.Api/Program.cs` — `MarketplaceListingActivated` publishes to exchange `marketplaces-listing-activated`; `MarketplaceSubmissionRejected` publishes to exchange `marketplaces-submission-rejected`
- `src/Marketplaces/Marketplaces.Api/Listings/ListingApprovedHandler.cs` — modify this handler for Walmart path
- `src/Shared/Messages.Contracts/Marketplaces/MarketplaceIntegrationMessages.cs` — contracts already defined; `MarketplaceListingActivated` carries `Guid ListingId, string Sku, string ChannelCode, string ExternalListingId`
- `docs/skills/wolverine-message-handlers.md` — **mandatory before touching any handler**
- `docs/skills/marten-event-sourcing.md`

---

## What This Session Does

Session 1 delivers P-1 through P-7 from the M38.0 plan — all seven items in scope. Two
workstreams run in parallel after the ADR is committed:

| Item | BC | Description |
|------|----|-------------|
| P-1 | Marketplaces | Real `WalmartMarketplaceAdapter.CheckSubmissionStatusAsync` |
| P-2 | Marketplaces | Modify `ListingApprovedHandler` — Walmart path schedules poll; Amazon/eBay unchanged |
| P-3 | Marketplaces | New `CheckWalmartFeedStatusHandler` — polls, publishes outcome, reschedules if pending |
| P-4 | Listings | Subscribe Listings.Api to Marketplaces outcome exchanges |
| P-5 | Listings | `MarketplaceListingActivatedHandler` — transitions listing `Submitted → Live` |
| P-6 | Listings | `MarketplaceSubmissionRejectedHandler` — transitions listing `Submitted → Ended (SubmissionRejected)` |
| P-7 | ADR | ADR 0055 — Submission Status Polling Architecture |

**Session 2 items (Polly, deactivation) are not in scope for this session.** Do not begin
P-8 through P-12 even if time permits.

---

## Guard Rails — Non-Negotiable

1. **ADR 0055 must be committed before any polling code is written.** P-1, P-2, and P-3
   depend on design decisions in that ADR (message shape, retry count, delay schedule).
   The ADR is the first implementation commit of the session.

2. **`MarketplaceListingActivatedHandler` must NOT call `IMessageBus.InvokeAsync(new ActivateListing(...))`.** The `InvokeAsync` + manual event append anti-pattern applies even when used across handler boundaries. Instead, the handler should directly append the `ListingActivated` domain event using the same pattern as `ActivateListingHandler` — load aggregate, guard on state, append, return `OutgoingMessages`. The `ActivateListing` command handler exists for the HTTP endpoint; reuse the pattern, not the dispatch.

3. **The `CheckWalmartFeedStatus` message is internal to Marketplaces BC.** It must NOT be added to `Messages.Contracts`. Define it as a simple sealed record in `src/Marketplaces/Marketplaces/` (or `Marketplaces.Api/`). It is a Wolverine durable local message, not a cross-BC integration contract.

4. **`ListingApprovedHandler` changes must not affect the Amazon or eBay paths.** The modification is channel-specific: for `WALMART_US`, after a successful feed submission, schedule `CheckWalmartFeedStatus` instead of publishing `MarketplaceListingActivated`. For all other channels, the existing behavior is unchanged. Verify this with a targeted code review before committing.

5. **The max-attempt guard on `CheckWalmartFeedStatusHandler` is non-negotiable.** Feeds that never resolve must not reschedule indefinitely. Plan Section 4 specifies the approach: include an `AttemptCount` in the scheduled message; after N attempts (e.g., 10) publish `MarketplaceSubmissionRejected` with a timeout reason.

6. **`OutgoingMessages` for all integration events; `bus.ScheduleAsync()` is the only justified `IMessageBus` injection.** The `ListingApprovedHandler` and `CheckWalmartFeedStatusHandler` may both inject `IMessageBus` for scheduling — this is the one sanctioned use.

7. **Commit each item separately.** P-7 (ADR) is one commit. P-1 (`CheckSubmissionStatusAsync`) is one commit. P-2 (handler modification) is one commit. P-3 (polling handler) is one commit. P-4 (Listings.Api wiring) is one commit. P-5 and P-6 (Listings handlers) can be one or two commits — your judgment — but separate from the Marketplaces work.

---

## Execution Order

```
P-7: Author and commit ADR 0055 — establishes message shape and retry strategy
  ↓
P-1: Implement WalmartMarketplaceAdapter.CheckSubmissionStatusAsync
     (polls GET /v3/feeds/{feedId}, maps feedStatus to SubmissionStatus)
  ↓
Define CheckWalmartFeedStatus sealed record (internal message)
  ↓
P-2: Modify ListingApprovedHandler — Walmart path uses bus.ScheduleAsync()
     instead of publishing MarketplaceListingActivated
  ↓
P-3: Implement CheckWalmartFeedStatusHandler
     (poll → resolve or reschedule; max-attempt guard)
  ↓
[Marketplaces integration tests — @QAE parallel after P-3]

P-4: Add listener + exchange bindings to Listings.Api/Program.cs
  ↓
P-5: Implement MarketplaceListingActivatedHandler in Listings BC domain
  ↓
P-6: Implement MarketplaceSubmissionRejectedHandler in Listings BC domain
  ↓
[Listings integration tests — @QAE parallel after P-5/P-6]
```

The Marketplaces workstream (P-1 through P-3) and Listings workstream (P-4 through P-6) are
independent of each other and can proceed in parallel after P-7 is committed. @PSA owns the
Marketplaces work; @QAE can begin Listings test scaffolding once P-4 is done.

---

## Mandatory Session Bookends

**First act:** Run `dotnet build` from solution root — confirm 0 errors, 12 warnings unchanged.
Run `dotnet test` on `Listings.Api.IntegrationTests` (35 passing) and
`Marketplaces.Api.IntegrationTests` (70 passing). Record as session baseline.

**Last acts — all required:**

**1. Commit `docs/planning/milestones/m38-0-session-1-retrospective.md`**

Must cover:
- P-1: Walmart feed status polling — the four `feedStatus` values handled (RECEIVED, INPROGRESS, PROCESSED, ERROR) and how each maps to `SubmissionStatus`
- P-2: How `ListingApprovedHandler` was modified — confirm Amazon and eBay paths are untouched (cite the guard clause or branch used)
- P-3: `CheckWalmartFeedStatusHandler` — the `AttemptCount` approach, retry delay schedule chosen, max-attempt timeout behavior
- P-4: Queue name and exchange binding configuration used in Listings.Api/Program.cs
- P-5/P-6: How `MarketplaceListingActivatedHandler` and `MarketplaceSubmissionRejectedHandler` load the aggregate and guard on state (what happens if the listing is already `Live` or `Ended` when the message arrives?)
- Any idempotency considerations (duplicate `MarketplaceListingActivated` messages)
- Test counts per affected BC at session start and session close
- Build state at session close (errors, warnings)
- CI run number confirming green
- What Session 2 should pick up first (Polly resilience)

**2. Update `CURRENT-CYCLE.md`**

Add "M38.0 Session 1 Progress" block. Record completed items (P-1 through P-7), updated
test counts, and CI run number. Update Last Updated timestamp.

**3. Run and record the full test suite**

Both the retrospective and CURRENT-CYCLE.md must reference the same CI run number.

---

## Roles

### @PSA — Principal Software Architect

Primary owner of P-7 (ADR), P-1 through P-3 (Marketplaces), and P-4 through P-6 (Listings).

---

**P-7 — ADR 0055: Submission Status Polling Architecture**

Location: `docs/decisions/0055-submission-status-polling-architecture.md`

Cover:
- Q1 decision rationale (Marketplaces BC owns polling — adapter concepts and outcome messages stay within the adapter BC)
- Q2 decision rationale (per-submission scheduled message via `bus.ScheduleAsync()` — simpler than batch saga for single-adapter need)
- Q3 decision rationale (Walmart only — Amazon SP-API synchronous ACCEPTED response confirmed; eBay offer publish confirmed synchronous)
- `CheckWalmartFeedStatus` internal message shape (see P-3 section below)
- Retry delay schedule and max-attempt count (e.g., attempts 1–5 retry at 2 min, 5 min, 10 min, 20 min, 30 min; after 10 attempts publish rejection)
- What happens on timeout (publish `MarketplaceSubmissionRejected` with reason `"Walmart feed processing timed out after N attempts"`)
- Known limitation: Amazon and eBay `CheckSubmissionStatusAsync` remain skeleton; M38.1 enhancement path documented

---

**P-1 — `WalmartMarketplaceAdapter.CheckSubmissionStatusAsync`**

Location: `src/Marketplaces/Marketplaces/Adapters/WalmartMarketplaceAdapter.cs`

Replace the skeleton implementation:

```csharp
// Current skeleton — replace this:
public Task<SubmissionStatus> CheckSubmissionStatusAsync(
    string externalSubmissionId, CancellationToken ct = default)
{
    return Task.FromResult(new SubmissionStatus(
        ExternalSubmissionId: externalSubmissionId,
        IsLive: false,
        IsFailed: false));
}
```

The `externalSubmissionId` follows the convention `wmrt-{feedId}`. Strip the `wmrt-` prefix to get the raw Walmart feed ID, then call:

```
GET https://marketplace.walmartapis.com/v3/feeds/{feedId}
```

Headers required (same auth pattern as `SubmitListingAsync` — reuse cached token):
- `WM_SEC.ACCESS_TOKEN`: cached LWA token
- `WM_CONSUMER.ID`: seller ID from vault
- `WM_SVC.NAME`: `"Walmart Marketplace"`
- `WM_QOS.CORRELATION_ID`: new `Guid.NewGuid().ToString()`

Parse the `feedStatus` field from the response:
- `"PROCESSED"` → `new SubmissionStatus(externalSubmissionId, IsLive: true, IsFailed: false)`
- `"ERROR"` → `new SubmissionStatus(externalSubmissionId, IsLive: false, IsFailed: true, FailureReason: "Feed processing error")`
- `"RECEIVED"` or `"INPROGRESS"` → `new SubmissionStatus(externalSubmissionId, IsLive: false, IsFailed: false)` (pending — reschedule)

HTTP errors (4xx, 5xx) should return `IsFailed: true` with the status code in the reason, not throw. The `CheckWalmartFeedStatusHandler` decides whether to retry or fail based on the returned `SubmissionStatus`, not exceptions.

---

**Define `CheckWalmartFeedStatus` internal message**

This is an internal Marketplaces BC message — not an integration contract. Place it alongside
`CheckWalmartFeedStatusHandler`:

```csharp
// Location: src/Marketplaces/Marketplaces.Api/Listings/CheckWalmartFeedStatus.cs
namespace Marketplaces.Api.Listings;

/// <summary>
/// Internal scheduled message for polling Walmart feed status.
/// Not a cross-BC integration message — do not add to Messages.Contracts.
/// </summary>
public sealed record CheckWalmartFeedStatus(
    Guid ListingId,
    string Sku,
    string ChannelCode,
    string ExternalFeedId,   // raw feed ID without wmrt- prefix
    int AttemptCount);
```

The `AttemptCount` starts at 1 (first poll scheduled after feed submission). Max attempts from
ADR 0055 determines the timeout threshold.

---

**P-2 — Modify `ListingApprovedHandler` for Walmart path**

Location: `src/Marketplaces/Marketplaces.Api/Listings/ListingApprovedHandler.cs`

Add `IMessageBus bus` to the handler parameters. For the Walmart channel (`WALMART_US`) only,
after a successful `SubmitListingAsync`:

```csharp
// Walmart: feed-based submission is async — schedule a status poll instead of publishing activated
if (string.Equals(message.ChannelCode, "WALMART_US", StringComparison.OrdinalIgnoreCase))
{
    var rawFeedId = result.ExternalSubmissionId!.Replace("wmrt-", "");
    await bus.ScheduleAsync(
        new CheckWalmartFeedStatus(message.ListingId, message.Sku, message.ChannelCode, rawFeedId, AttemptCount: 1),
        TimeSpan.FromMinutes(2));
    // Do NOT add MarketplaceListingActivated here for Walmart — the poll handler publishes it
    return outgoing;
}

// Amazon and eBay: synchronous activation — publish immediately (unchanged)
outgoing.Add(new MarketplaceListingActivated(
    message.ListingId, message.Sku, message.ChannelCode, result.ExternalSubmissionId!, now));
```

Verify the existing `return outgoing;` statements for Amazon and eBay paths are unchanged.
A targeted code review of the full handler before committing is required per Guard Rail 4.

---

**P-3 — `CheckWalmartFeedStatusHandler`**

Location: `src/Marketplaces/Marketplaces.Api/Listings/CheckWalmartFeedStatusHandler.cs`

```csharp
namespace Marketplaces.Api.Listings;

public static class CheckWalmartFeedStatusHandler
{
    private const int MaxAttempts = 10;

    public static async Task<OutgoingMessages> Handle(
        CheckWalmartFeedStatus message,
        IReadOnlyDictionary<string, IMarketplaceAdapter> adapters,
        IMessageBus bus)
    {
        var outgoing = new OutgoingMessages();
        var now = DateTimeOffset.UtcNow;

        // Resolve Walmart adapter
        if (!adapters.TryGetValue(message.ChannelCode, out var adapter))
        {
            // Adapter gone — publish rejection
            outgoing.Add(new MarketplaceSubmissionRejected(
                message.ListingId, message.Sku, message.ChannelCode,
                $"No adapter found for channel '{message.ChannelCode}' during feed poll", now));
            return outgoing;
        }

        var externalSubmissionId = $"wmrt-{message.ExternalFeedId}";
        var status = await adapter.CheckSubmissionStatusAsync(externalSubmissionId);

        if (status.IsLive)
        {
            outgoing.Add(new MarketplaceListingActivated(
                message.ListingId, message.Sku, message.ChannelCode, externalSubmissionId, now));
            return outgoing;
        }

        if (status.IsFailed || message.AttemptCount >= MaxAttempts)
        {
            var reason = status.IsFailed
                ? status.FailureReason ?? "Walmart feed processing failed"
                : $"Walmart feed processing timed out after {message.AttemptCount} attempts";
            outgoing.Add(new MarketplaceSubmissionRejected(
                message.ListingId, message.Sku, message.ChannelCode, reason, now));
            return outgoing;
        }

        // Still pending — reschedule with escalating delay
        var delay = GetDelay(message.AttemptCount);
        await bus.ScheduleAsync(
            message with { AttemptCount = message.AttemptCount + 1 },
            delay);
        return outgoing;
    }

    private static TimeSpan GetDelay(int attempt) => attempt switch
    {
        1 => TimeSpan.FromMinutes(2),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(10),
        4 => TimeSpan.FromMinutes(20),
        _ => TimeSpan.FromMinutes(30)
    };
}
```

Adjust the delay schedule per ADR 0055 if the panel chose different values.

---

**P-4 — Subscribe Listings.Api to Marketplaces outcome exchanges**

Location: `src/Listings/Listings.Api/Program.cs`

Add after the existing `opts.ListenToRabbitQueue("listings-product-recall")` line:

```csharp
// Listen for marketplace listing outcome events (M38.0 bidirectional feedback)
opts.ListenToRabbitQueue("listings-marketplace-outcome-events")
    .BindExchange("marketplaces-listing-activated")
    .BindExchange("marketplaces-submission-rejected");
```

The `AutoProvision()` already configured on the RabbitMQ transport will create the queue and
bind it to both exchanges. Handlers for `MarketplaceListingActivated` and
`MarketplaceSubmissionRejected` in the domain assembly will be auto-discovered because
`opts.Discovery.IncludeAssembly(typeof(Listings.Listing.Listing).Assembly)` is already registered.

---

**P-5 — `MarketplaceListingActivatedHandler`**

Location: `src/Listings/Listings/Listing/MarketplaceListingActivatedHandler.cs`

Do NOT dispatch `new ActivateListing(...)` via `IMessageBus.InvokeAsync`. Apply the pattern
directly — the `ActivateListing` command handler exists for the HTTP endpoint path; this handler
is the integration event path. Duplicate the pattern, not the dispatch.

```csharp
using Marten;
using Wolverine;
using Messages.Contracts.Marketplaces;
using IntegrationMessages = Messages.Contracts.Listings;

namespace Listings.Listing;

/// <summary>
/// Consumes MarketplaceListingActivated from the Marketplaces BC.
/// Transitions the Listing aggregate from Submitted to Live.
/// </summary>
public static class MarketplaceListingActivatedHandler
{
    public static async Task<OutgoingMessages> Handle(
        MarketplaceListingActivated message,
        IDocumentSession session)
    {
        var outgoing = new OutgoingMessages();
        var now = DateTimeOffset.UtcNow;

        var listing = await session.Events.AggregateStreamAsync<Listing>(message.ListingId);

        // Idempotency guard: if already Live (duplicate message), skip
        if (listing is null || listing.Status == ListingStatus.Live)
            return outgoing;

        // Only Submitted listings can be activated by marketplace feedback
        if (listing.Status != ListingStatus.Submitted)
        {
            // Log or record unexpected state — do not throw; let the message succeed
            return outgoing;
        }

        var @event = new ListingActivated(message.ListingId, message.ChannelCode, now);
        session.Events.Append(message.ListingId, @event);

        outgoing.Add(new IntegrationMessages.ListingActivated(
            message.ListingId, message.ChannelCode, now));

        return outgoing;
    }
}
```

---

**P-6 — `MarketplaceSubmissionRejectedHandler`**

Location: `src/Listings/Listings/Listing/MarketplaceSubmissionRejectedHandler.cs`

```csharp
using Marten;
using Wolverine;
using Messages.Contracts.Marketplaces;
using IntegrationMessages = Messages.Contracts.Listings;

namespace Listings.Listing;

/// <summary>
/// Consumes MarketplaceSubmissionRejected from the Marketplaces BC.
/// Transitions the Listing aggregate from Submitted to Ended (SubmissionRejected cause).
/// </summary>
public static class MarketplaceSubmissionRejectedHandler
{
    public static async Task<OutgoingMessages> Handle(
        MarketplaceSubmissionRejected message,
        IDocumentSession session)
    {
        var outgoing = new OutgoingMessages();
        var now = DateTimeOffset.UtcNow;

        var listing = await session.Events.AggregateStreamAsync<Listing>(message.ListingId);

        // Idempotency guard: if already Ended (duplicate message), skip
        if (listing is null || listing.Status == ListingStatus.Ended)
            return outgoing;

        if (listing.Status != ListingStatus.Submitted)
            return outgoing;

        var @event = new ListingEnded(
            message.ListingId,
            message.Sku,
            message.ChannelCode,
            EndedCause.SubmissionRejected,
            now);

        session.Events.Append(message.ListingId, @event);

        outgoing.Add(new IntegrationMessages.ListingEnded(
            message.ListingId,
            message.Sku,
            message.ChannelCode,
            now));

        return outgoing;
    }
}
```

**Verify `Messages.Contracts.Listings.ListingEnded` exists** before using it. If it is not yet
defined, add it to the Listings contracts file following the same pattern as existing contracts.

**Skills:** `wolverine-message-handlers`, `marten-event-sourcing`

---

### @QAE — QA Engineer

Primary owner of integration tests for P-1 through P-6. Can begin test work after each
item is committed — do not wait until all items are done before starting tests.

**After P-1 is committed — Walmart adapter polling tests**

In `tests/Marketplaces/Marketplaces.Api.IntegrationTests/WalmartMarketplaceAdapterTests.cs`,
replace the existing skeleton test:

Replace `CheckSubmissionStatus_ReturnsPendingStatus` with three real tests:
- `CheckSubmissionStatus_ReturnsLive_WhenFeedStatusIsProcessed` — mock `GET /v3/feeds/{feedId}` returning `{ "feedStatus": "PROCESSED" }`; assert `IsLive: true, IsFailed: false`
- `CheckSubmissionStatus_ReturnsFailed_WhenFeedStatusIsError` — mock returning `{ "feedStatus": "ERROR" }`; assert `IsFailed: true, IsLive: false`
- `CheckSubmissionStatus_ReturnsPending_WhenFeedStatusIsInProgress` — mock returning `{ "feedStatus": "INPROGRESS" }`; assert both `false` (pending)

Use `FakeHttpMessageHandler.EnqueueResponseForUrl` from `MarketplaceAdapterTestHelpers` to
stage the feed status response for the correct URL.

**After P-3 is committed — polling handler tests**

New file: `tests/Marketplaces/Marketplaces.Api.IntegrationTests/WalmartPollingHandlerTests.cs`

- `CheckWalmartFeedStatus_PublishesActivated_WhenFeedProcessed` — handler with adapter returning `IsLive: true`; assert `MarketplaceListingActivated` in `OutgoingMessages`
- `CheckWalmartFeedStatus_PublishesRejected_WhenFeedError` — adapter returns `IsFailed: true`; assert `MarketplaceSubmissionRejected`
- `CheckWalmartFeedStatus_Reschedules_WhenFeedPending` — adapter returns both `false`; verify `bus.ScheduleAsync` was called (use a test double for `IMessageBus` or verify via Wolverine test harness)
- `CheckWalmartFeedStatus_PublishesRejected_WhenMaxAttemptsReached` — send message with `AttemptCount = 10`; even if adapter returns pending, assert `MarketplaceSubmissionRejected` published
- `CheckWalmartFeedStatus_PublishesRejected_WhenAdapterMissing` — no adapter registered for channel; assert `MarketplaceSubmissionRejected`

**After P-5/P-6 are committed — Listings BC handler tests**

New file: `tests/Listings/Listings.Api.IntegrationTests/MarketplaceListingActivatedHandlerTests.cs`

- `MarketplaceListingActivated_TransitionsToLive_WhenSubmitted` — create listing, approve it (so it's Submitted), fire handler; verify listing status becomes Live; verify `Messages.Contracts.Listings.ListingActivated` is in outgoing
- `MarketplaceListingActivated_IsNoOp_WhenAlreadyLive` — listing in Live state; fire handler; verify no new events appended
- `MarketplaceListingActivated_IsNoOp_WhenListingNotFound` — fire handler with non-existent ListingId; verify no exception

New file: `tests/Listings/Listings.Api.IntegrationTests/MarketplaceSubmissionRejectedHandlerTests.cs`

- `MarketplaceSubmissionRejected_TransitionsToEnded_WhenSubmitted` — listing in Submitted state; fire handler; verify `ListingEnded` domain event appended with `EndedCause.SubmissionRejected`; verify integration message in outgoing
- `MarketplaceSubmissionRejected_IsNoOp_WhenAlreadyEnded` — listing in Ended state; fire handler; verify no new events
- `MarketplaceSubmissionRejected_IsNoOp_WhenListingNotFound`

For the Listings tests, use the existing `ListingStreamId.Compute(sku, channelCode)` to create
listings with predictable stream IDs, and seed them into `Submitted` state by appending the
domain events directly (draft created → submitted for review → approved → listing approved).
Follow the pattern of existing listing lifecycle tests in the test project.

**Test count targets:**
- Marketplaces: from 70 → ~82 (+12: 3 polling adapter tests replacing 1 skeleton, 5 handler tests, 4 deactivation tests carried forward from existing)
- Listings: from 35 → ~44 (+9: 3 activated handler tests + 3 rejected handler tests + others)
- Record exact counts in the retrospective

**Skills:** `critterstack-testing-patterns`

---

## Session Habits

Commit frequently and atomically in execution order. ADR commits before implementation.
Each handler is its own commit. Each test file is its own commit.

Suggested commit message format: `M38.0 P-N: {BC} — {description}`

Examples:
- `M38.0 P-7: docs — ADR 0055 Walmart submission status polling architecture`
- `M38.0 P-1: Marketplaces — WalmartMarketplaceAdapter.CheckSubmissionStatusAsync real implementation`
- `M38.0 P-2: Marketplaces.Api — ListingApprovedHandler Walmart path schedules CheckWalmartFeedStatus`
- `M38.0 P-3: Marketplaces.Api — CheckWalmartFeedStatusHandler polling with max-attempt guard`
- `M38.0 P-3 tests: Marketplaces.IntegrationTests — Walmart polling handler tests`
- `M38.0 P-4: Listings.Api — Subscribe to marketplaces-listing-activated and marketplaces-submission-rejected exchanges`
- `M38.0 P-5: Listings — MarketplaceListingActivatedHandler`
- `M38.0 P-6: Listings — MarketplaceSubmissionRejectedHandler`
- `M38.0 P-5/P-6 tests: Listings.IntegrationTests — Marketplace feedback handler tests`

The most important quality constraint for this session: **the idempotency guards on P-5 and P-6
must be in place before the handlers are committed.** The Listings BC will receive
`MarketplaceListingActivated` for both Amazon/eBay (immediate) and eventually Walmart (after
polling). A duplicate message must be a silent no-op, not an exception or a double-append.
The guards shown in the role section above (check `listing.Status == ListingStatus.Live`
before appending `ListingActivated`) are the correct approach — verify them before committing.
