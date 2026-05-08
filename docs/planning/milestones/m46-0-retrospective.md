# M46.0 — Reliability Workshop Follow-Through (J → D → H → A) — Retrospective

> **Date:** 2026-05-08
> **Status:** ✅ Complete (single session)
> **Source:** Workshop part 2 in
> [`docs/research/state-of-repo-2026-05.md`](../../research/state-of-repo-2026-05.md)
> §7.4 — top-four priorities the PO/UXE/FE/QA/PA group voted up
> **Branch / PR:** `copilot/review-state-of-repo-may-2026`

---

## What we set out to do

Workshop part 2 produced a 9-item priority list (A–I). The room voted four
items above the line as the working set for this milestone, executed in the
order **J → D → H → A** (J was a late-add follow-on from the Inventory M43.1
DLQ work and was sequenced first because it unblocked confidence in
integration-message tests):

| # | Item | One-line description |
|---|------|----------------------|
| **J** | Integration-message test honesty | Stop relying on `tracked.Sent.MessagesOf<T>()` for unrouted integration messages — it's silently empty. |
| **D** | DLQ visibility | Surface `wolverine_dead_letters` rows to a human operator (not just a log line nobody reads). |
| **H** | SignalR mapper extraction | Promote `OrderConfirmation`'s shipment/return helpers into a shared module; route `Cart` and `InteractiveAppBar` through it. |
| **A** | Cross-product exchange honesty | Tag the Gherkin scenarios that aren't end-to-end implementable as `@pending`, with a skipped Alba placeholder per scenario linked back to the gap memo. |

---

## What landed

### J — `IntegrationMessageAssertions` helper + skill doc

- New `tests/Shared/CritterSupply.TestUtilities/IntegrationMessageAssertions.cs`
  with two helpers:
  - `AssertNotInTracked<T>(tracked, becauseHint)` — fails fast on the
    misleading green path (where the test "passed" but the message was never
    really routed).
  - `AssertOnEventStreamOrSent<T>(...)` — encodes the recommended pattern
    (verify domain effect on the Marten event stream OR set up explicit test
    routes) so people don't reinvent it inconsistently.
- New skill doc
  [`docs/skills/integration-message-test-assertions.md`](../../skills/integration-message-test-assertions.md)
  with a decision tree and a 22-call audit table of the existing
  `tracked.Sent.MessagesOf<T>()` usages flagged as "verify or replace".

### D — `GET /api/backoffice/operations/dead-letters/summary`

- Read-only HTTP endpoint at
  `src/Backoffice/Backoffice.Api/OperationsHealth/GetDeadLetterSummary.cs`
  authorized by `[Authorize(Policy = "OperationsManager")]`.
- Aggregates row counts and top message types per BC schema over the last
  *N* hours (1–168, default 24).
- 18-schema default with `OperationsHealth:DeadLetterSchemas` config override.
- Defense in depth: a strict identifier allow-list rejects any schema name
  that isn't a plain Postgres identifier (regex `^[a-z_][a-z0-9_]*$`),
  preventing operator-config-injection.
- Schema-not-found and table-not-found are silent skips (the operationally
  correct read — Wolverine creates the DLQ table lazily on first failure).
- 4 integration tests cover empty, hour-clamping, aggregation across schemas,
  and identifier validation. Full Backoffice integration suite: 99/99 green.

### H — `Storefront.Web.RealTime.StorefrontStatusMapper` + `StorefrontEventReader`

- Three pure-function helpers (`MapShipmentStatus`, `BuildShipmentMessage`,
  `BuildReturnMessage`) lifted out of `OrderConfirmation.razor` into
  `src/Customer Experience/Storefront.Web/RealTime/StorefrontStatusMapper.cs`.
- New `StorefrontEventReader` companion class with `GetEventType`,
  `GetString`, and `GetInt32` — the defensive null-safe `JsonElement`
  property-extraction pattern the SignalR consumers had been re-implementing
  inconsistently.
- `OrderConfirmation`, `Cart`, and `InteractiveAppBar` Razor pages all route
  their `OnSseEvent` callbacks through the new module.
- Existing 43 pure-function tests for the helpers were re-pointed at the new
  class (mechanical rename); 6 new `StorefrontEventReader` tests were added
  covering null/missing/wrong-type behaviour explicitly.
- 49 H-related tests green. (5 unrelated `OrderHistoryTests` failures were
  pre-existing on baseline — confirmed via `git stash` baseline run.)

### A — Cross-product exchange honesty pass

- 5 of 9 scenarios in
  `docs/features/returns/cross-product-exchange.feature` tagged `@pending`
  with inline pointers to
  [`docs/planning/milestones/m45-1-cross-product-exchange-gap-memo.md`](./m45-1-cross-product-exchange-gap-memo.md).
  The 4 still-implementable scenarios remain unmarked.
- Feature-file header explains the convention so the next reader doesn't
  treat `@pending` as personal preference.
- New `tests/Returns/Returns.Api.IntegrationTests/CrossProductExchangePendingTests.cs`
  with 5 skipped Alba placeholders. Each `Skip` reason names the missing
  capability *and* cites the row of the gap memo, so the gap stays
  discoverable from raw `dotnet test` output.

---

## Validation

- Full solution build: **0 errors**, 359 warnings (all pre-existing
  `NU1902` advisories on the OpenTelemetry 1.15.1 chain).
- `Backoffice.Api.IntegrationTests`: **99 / 99 green** (4 new + 95 existing).
- `Storefront.Web.UnitTests` (J/H subset): **49 / 49 green**.
- `Returns.Api.IntegrationTests` placeholder set: **5 / 5 skipped** with
  citation-bearing reasons; full Returns suite unaffected.

---

## Lessons learned

The four items in this milestone share a single underlying lesson, surfaced
four ways:

> **Reliability is mostly about removing false confidence and creating
> humans-in-the-loop, not about adding new behaviour.**

Concretely:

### 1. Test infrastructure that "passes when there's nothing to verify" is worse than no test

`tracked.Sent.MessagesOf<T>()` had been quietly returning empty collections
for unrouted integration messages across at least 22 test sites. We had been
treating "no messages of type X were sent" assertions as positive evidence,
when in test environments where external transports are disabled, they can
mean "the message had nowhere to go and was silently dropped." The cure
is two-pronged:
- **Make the silence loud.** `IntegrationMessageAssertions.AssertNotInTracked`
  refuses to swallow the case and demands a positive reason.
- **Document the trap once, in a place people will find it.** A skill doc
  with a decision tree saves the next person two hours of head-scratching.

**For the future:** Any time a test framework exposes "absence of evidence"
as a primary assertion API, treat it as a foot-gun. Wrap it in a helper that
forces the caller to acknowledge what they're really asserting.

### 2. A DLQ that no human reads is just a slow-leak garbage can

The `wolverine_dead_letters` tables had been growing across 18 schemas with
zero operator visibility. The M43.1 work made them *populate correctly*, but
that's only half the story — populated DLQs without a window into them are
indistinguishable from working systems until they aren't. Adding a single
read-only endpoint, even a basic aggregator with no UI, is disproportionately
valuable: it converts an opaque failure mode into a query.

**For the future:** When we add new at-least-once or DLQ-bound paths,
*always* pair the producer with at least a count-and-top-types aggregator
that an operator can hit with a curl. The cost is trivial; the cost of *not*
having it is "the first we hear about a poison-pill message is the
customer-support escalation."

### 3. Helpers that live inside a single component are invisible to everyone else

The `OrderConfirmation` shipment/return mappers had been hiding inside the
Razor file as `internal static`. They worked beautifully there but were
literally undiscoverable from `Cart.razor` or `InteractiveAppBar.razor`.
Both of those components had re-implemented the camelCase
`JsonElement.TryGetProperty` pattern by hand, and one of them shipped a
small bug in null handling because of it.

**For the future:** When the same shape of code appears in two component
files, lift it into a sibling module *immediately* — even if the second
caller is just three lines. The cost of the lift is a file move and an
import; the cost of *not* lifting is a third caller in six months who
re-invents it slightly wrong.

### 4. Specs that quietly outpace implementation lie to the next reader

`cross-product-exchange.feature` had been describing an end-to-end flow
that only existed within the Returns BC. A new reader (or a new agent
session, or a future PO-driven prioritization meeting) would have read the
feature file, assumed the capability existed, and started building
*on top of it*. The honesty pass — `@pending` tags + skipped placeholder
tests with citation-bearing Skip reasons — costs nothing to add and
permanently inoculates the spec against this drift.

**For the future:** Any time a Gherkin feature file describes behaviour that
spans BCs and only the originating BC is implemented, tag the cross-BC
scenarios `@pending` *in the same PR* that ships the partial implementation,
and add at least one skipped placeholder test in the integration suite that
points at the gap memo. This is the QA-engineer-facing version of "TODOs
should always have an issue number."

---

## Process / mechanics lessons

- **`IDocumentSession.Connection` looks tempting for "share Marten's
  connection from a Wolverine HTTP endpoint" — don't reach for it.** Once a
  `PostgresException` fires inside that connection's transaction context,
  the connection enters error state and subsequent statements all fail until
  rollback. We tried it for the DLQ summary endpoint and got 16 false
  `SkippedSchemas` per call. The right pattern is to open a fresh
  `NpgsqlConnection` from the configured connection string for read-only
  operations endpoints — and to teach the test fixture to overlay
  `ConnectionStrings:postgres` in `IConfiguration` so the endpoint targets
  the test container rather than the dev-default `localhost:5433`.
- **Wolverine's auto-created `wolverine_dead_letters` schema has more
  columns than the public docs suggest.** Tests that seed the table need to
  introspect `information_schema.columns` and conditionally include any
  NOT NULL columns the table has acquired (e.g. `execution_time`, `body`,
  `replayable`). Hardcoding a positional `INSERT … VALUES (...)` will fail
  on the first version bump.
- **xUnit `Skip = "..."` reasons are first-class documentation in this
  codebase.** Treat them like commit messages: name the missing capability,
  cite the gap memo and row, and describe the exact unblocking condition.
  When the placeholder finally runs green, the Skip reason becomes the
  acceptance criteria writeup for free.

---

## What we explicitly did not do

The workshop's below-the-line items (B, C, E, F, G, I from
`state-of-repo-2026-05.md` §7.4) are deferred. None of them blocked the
current set; they remain candidates for the next reliability milestone.

The M45.1 cross-BC choreography deferred items
(`m45-1-cross-product-exchange-gap-memo.md` "What is missing" rows) remain
deferred. The honesty pass in this milestone is the *signal* to the next
prioritization round that those rows exist; landing them is its own
multi-BC implementation milestone (Inventory replacement reservation,
Payments delta capture/refund, Orders saga consumption).
