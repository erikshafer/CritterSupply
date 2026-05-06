# M43.1 — Inventory Concurrency-Exhaustion Gap #13 (Plan)

> **Author:** Principal Architect
> **Date:** 2026-05-06
> **Scope:** Top-tier item **1B** from `docs/research/state-of-repo-2026-05.md` §4.
> **Carryover from:** `inventory-remaster-s2-retrospective.md` §"Gap #13 Resolution"
> (the gap was *documented* in S2 and *deferred* through S3/S4).
> **Predecessor:** M43.0 — Slice 12 (`OrderPlacedHandler` retirement) — closed.
> **Spirit:** Same as M39.0 — small, surgical, idiom-and-reliability work; no new BCs, no new features.

---

## 1. Goal

Close the only documented reliability gap remaining on the Inventory remaster
scoreboard: **`ConcurrencyException` retry exhaustion in Inventory must never
silently drop a message.** Exhausted messages must land in Wolverine's dead
letter envelope store (`wolverine_dead_letters`) where the existing
`DeadLetterQueueLogSink` (M42.4) will surface them — giving Operations a real
signal during flash-sale contention rather than the original silent `Discard`.

This is **execution-and-test work**. The architectural decision is already made
(see Gap #13 entry in `inventory-remaster-s2-retrospective.md`); the production
policy in `Inventory.Api/Program.cs` already terminates with `MoveToErrorQueue()`.
What's missing is:

1. **Test coverage** that proves the policy works end-to-end (not just that
   it compiles).
2. **A clean, deterministic exhaustion-path test** that does not depend on
   racing the Marten optimistic-concurrency check (which is non-deterministic
   in CI).
3. **Documentation closure** — the S2 retrospective still lists Gap #13 as
   "deferred"; the test file still carries an obsolete TODO referencing
   `.Discard()` and `MoveToDeadLetterQueue()`.

No production code in handlers changes. Only the Wolverine policy comment,
tests, and the retrospective entries change.

---

## 2. Current state (verified read of code 2026-05-06)

| Concern | Today | Target |
|---|---|---|
| `OnException<ConcurrencyException>` policy in `Inventory.Api/Program.cs` | `RetryOnce → RetryWithCooldown(100ms, 250ms) → MoveToErrorQueue()` | Same — but with a clear "Gap #13 resolved in M43.1" comment so future readers don't reopen the question. |
| Existing test `ConcurrentReservations_LastUnitContention_SecondReservationFails` | Asserts only "at least one reservation succeeded; total ≤ 10". Carries a stale comment block stating the policy is `Discard` and a TODO suggesting `MoveToDeadLetterQueue()`. | Comment block rewritten to reflect current policy. Asserts the **post-fix invariant**: every input message either (a) produced a `StockReserved` event, (b) produced a `ReservationFailed` integration message, or (c) ended up in `wolverine_dead_letters`. Nothing silently disappears. |
| Deterministic DLQ-on-retry-exhaustion coverage | None. The `Discard` → `MoveToErrorQueue()` change is currently un-tested. Reproducing `ConcurrencyException` deterministically via the production handler requires racing TestContainers' Postgres, which is flaky. | A new, focused integration test that uses a **test-only message + handler that always throws `ConcurrencyException`** to exercise the *Wolverine policy chain* directly. Verify that after `RetryOnce + RetryWithCooldown` exhaust, the envelope appears in `inventory.wolverine_dead_letters`. |
| `DeadLetterQueueLogSink` query | Reads `inventory.wolverine_dead_letters` on a 60s interval; logs at `Warning`. | Unchanged. The new test reuses the same SQL shape so any future Wolverine table-name drift is caught in one place. |
| `inventory-remaster-s2-retrospective.md` — "Deferred Items" | Lists `Gap #13 .MoveToDeadLetterQueue()` as deferred to S3. | Adds a "Resolved in M43.1" addendum at the bottom of the Gap #13 section pointing to this plan and the new tests. |
| `CURRENT-CYCLE.md` Roadmap / debt list | Still lists "Concurrency-exhaustion gap #13 (Inventory)" as carry-over debt. | Either marked resolved (if a M43.0 + M43.1 entry is added in the same touch) or left for the next CURRENT-CYCLE refresh. This plan only touches the gap-itself entry. |

---

## 3. Deliverables

### D1. Code-comment hardening — `Inventory.Api/Program.cs`

Replace the bare policy block with a short explanatory comment that:
- Names Gap #13 explicitly.
- States the rationale (exhausted concurrency conflicts must be visible
  to Operations, not silently dropped).
- Points at the M43.1 plan and the `DeadLetterQueueLogSink` for the consumption
  side.

No behavior change.

### D2. Test-only DLQ exhaustion proof — new integration test

Add a new test class in `tests/Inventory/Inventory.Api.IntegrationTests/Reliability/`:

- A test-only message (e.g., `ConcurrencyExhaustionProbe`) and a corresponding
  Wolverine handler **defined in the test project** (so it ships only to
  test runs and is discovered via the Inventory assembly's discovery rules
  through Alba's host configuration).
- The handler unconditionally throws `Marten.Exceptions.ConcurrencyException`.
- The test sends the probe via `IMessageBus.InvokeAsync` (or
  `PublishAsync` if invoke surfaces the exception inline), waits for retries
  to exhaust (≤ ~2 seconds with the 100/250ms cooldown), then queries the
  `inventory.wolverine_dead_letters` table directly via `NpgsqlDataSource`.
- Assertion: exactly one new dead letter envelope exists for the probe's
  message type, with an `explanation` mentioning `ConcurrencyException`.

This proves end-to-end that **the policy chain terminates in DLQ rather than
`Discard`**, independent of whether real production code paths can race.

### D3. Polish the existing race test

In `ReservationExpiryTests.ConcurrentReservations_LastUnitContention_SecondReservationFails`:

- Rewrite the comment block to reflect today's `MoveToErrorQueue()` policy
  (no more `Discard` references; no more TODO).
- Strengthen assertions: collect the tracked outgoing messages
  (`ReservationConfirmed` + `ReservationFailed`) **and** query
  `wolverine_dead_letters` for the probe SKU. The combined count of
  (confirmed + failed + dead-lettered) for the two `OrderId`s must equal **2**
  — i.e. neither input is silently lost. The `ReservedQuantity ≤ 10` invariant
  is kept.
- The test stays a "race scenario" test; it is **not** the determinism test
  for the policy. D2 is.

### D4. Retrospective addendum

Append a short "Resolved in M43.1" subsection to the Gap #13 section of
`inventory-remaster-s2-retrospective.md`, plus update the "Deferred Items"
bullet to mark it ✅ resolved with a link back to this plan.

### D5. Build, test, store memory

- `dotnet build` clean.
- `dotnet test --filter FullyQualifiedName~Inventory.Api.IntegrationTests`
  green; all pre-existing ReservationExpiry tests still pass.
- `store_memory` updates so the next session sees Gap #13 as resolved
  (overrides the existing "Gap #13 DOCUMENTED in S2" memory).

---

## 4. Out of scope

- The other Tier 2 reliability items (D — Vendor Portal cold-start flakes,
  E — Returns saga skipped tests, F — projection async→inline audit). They
  belong to subsequent M43.x sessions if we keep the "Reliability" sub-arc
  going.
- DLQ alerting / monitoring beyond the existing log sink. That was explicitly
  handed off to a future Operations BC concern in the S4 retrospective.
- Any change to the `RetryOnce → RetryWithCooldown` numbers themselves —
  100 / 250 ms is fine for the contention pattern observed in the existing
  race test; tuning them requires production telemetry we don't have.
- `OrderPlacedHandler`-related cleanup (handled in M43.0).

---

## 5. Risk

- **Test-only handler discovery.** Wolverine discovers handlers through assembly
  scanning. The test message + handler must live in the
  `Inventory.Api.IntegrationTests` assembly **and** that assembly must be
  included in discovery via Alba's `ConfigureServices` block (or via
  `opts.Discovery.IncludeAssembly(typeof(...).Assembly)` in a test extension).
  Mitigation: if discovery proves fragile, fall back to registering the handler
  inline in the test fixture instead of relying on convention.
- **DLQ table schema.** `DeadLetterQueueLogSink` already queries
  `inventory.wolverine_dead_letters` for `id, message_type, explanation, source,
  sent_at`. The new test reads from the same projection — any Wolverine schema
  rename will surface in both the sink and the test simultaneously, which is
  the desired coupling.
- **CI nondeterminism on the race test.** The existing race test is non-flaky
  because its assertions are loose; tightening them with a DLQ count must not
  introduce flakiness. We mitigate this by **not** asserting that the second
  reservation specifically went to DLQ — only that the *sum* (confirmed +
  failed + dead-lettered) equals the number of inputs.

---

## 6. Done criteria

- [ ] `Inventory.Api/Program.cs` policy block carries a "Gap #13 resolved in M43.1" comment.
- [ ] `tests/Inventory/Inventory.Api.IntegrationTests/Reliability/ConcurrencyExhaustionDlqTests.cs` exists, asserts a dead letter envelope is written after exhaustion, and is green.
- [ ] `ReservationExpiryTests.ConcurrentReservations_…` no longer references `.Discard()` or carries the obsolete TODO; its assertions enforce the no-silent-drop invariant.
- [ ] `inventory-remaster-s2-retrospective.md` — Gap #13 marked resolved.
- [ ] `dotnet build` clean and `dotnet test` for the Inventory integration test project green locally.
- [ ] Memory updated so future sessions see Gap #13 as resolved.

---

*End of plan.*
