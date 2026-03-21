# CritterSupply Post-Audit Discussion — 2026-03-21

> **Participants:** PSA (Principal Software Architect) · QAE (Quality Assurance Engineer) · UXE (UX Engineer) · PO (Product Owner)
> **Source of truth:** `docs/audits/CODEBASE-AUDIT-2026-03-21.md`
> **Date:** 2026-03-21

---

## Cross-Agent Review

### PSA Cross-Review

#### What surprised me in the QAE findings

**F-7 and F-8 together surprised me more than either did alone.** I expected a coverage matrix with some gaps. What I didn't anticipate was *which* BCs were missing `TrackedHttpCall()`. Returns (F-7) is the BC where `ApproveReturn` fans out into the most consequential Wolverine cascade in the system: `ApproveReturn` → publish `RefundRequested` to Payments → publish to Correspondence. The catch is correct and the fix is trivial, but the timing risk is not just a test quality issue — it means that if the Returns refactor (R-4) accidentally breaks cascade ordering, the existing integration tests can pass on a race and ship the regression.

What also surprised me: Promotions (F-6) running **four PostgreSQL containers** instead of one per test run. Promotions was the BC I called out as "exemplary" in structural terms. Clean code, quietly sabotaged CI performance. A good reminder that vertical slice discipline and test infrastructure quality are independent axes.

#### What surprised me in the UXE findings

**`CheckoutCompleted` existing in both Orders and Shopping contracts with different payloads** (UXE priority list #18) is ranked too generously as medium. This is not a naming inconsistency — it is a latent deserialization failure. When `Messages.Contracts` is referenced by multiple BCs and two records share a fully-qualified name with different shapes, the first time a consumer deserializes the wrong one it will either silently discard fields or throw at runtime. The convergence discussion correctly labels it "a subtle collision waiting to cause deserialisation bugs" and then ranks it at position 18. That framing and that ranking are inconsistent. I would move it to position 4, ahead of the broad Returns structural refactor — a one-record rename costs an hour; a deserialization bug in the checkout flow costs an incident.

**`AppliedDiscount` as a misidentified value object** (UXE, Orders BC) was sharp. I rated Orders' domain as "clean," which it is structurally. But a value object named like an event, sitting in `Placement/` alongside true domain events, is the kind of conceptual error that produces handler discovery surprises when Wolverine scans for event types.

#### Priority list ranking disagreements

**F-8 (item #26, 🟢 Low)** should be elevated to 🟡 Medium, ranked alongside #11 (missing Backoffice projections). The note on #26 already says "Needed before Backoffice gets RabbitMQ-triggered projection tests" — that is literally the work described in #11. A projection you cannot write a passing test for is a projection that will not get shipped. These two findings are a dependency pair; ranking them 15 positions apart obscures that dependency.

**F-2 (Vendor Portal E2E globally `@ignore`, ranked #2)** is correctly elevated, but the effort estimate of S (half day) reflects only the tag removal, not the step implementation work. If the step definitions don't exist for the unbound feature files (`product-management.feature`, `vendor-analytics-dashboard.feature`, `vendor-hub-connection.feature`), removing `@ignore` will produce failing tests rather than meaningful coverage. I'd revise the effort estimate to M–L.

**Priority list item #19 (command-masquerade events):** the six "Requested"-suffix messages are not equivalent in migration cost. `RefundRequested` and `ReservationCommitRequested` are already persisted in Marten event streams across every order ever processed. Renaming them requires a Marten stream alias migration, not just a file rename. `FulfillmentRequested` flows through the same committed stream. The priority list groups all six together; the implementation team must triage by **persisted domain event** (high migration cost) vs. **transient integration message** (low cost) before any rename work begins.

#### Single most important cross-agent observation

**INV-3 + F-8 + UXE Backoffice stubs form a causality chain the convergence discussion identified but did not rank to match.** The chain is longer than stated: `AdjustInventoryEndpoint` bypasses the Wolverine handler → `LowStockDetected` never reaches RabbitMQ → `BackofficeTestFixture` has no `ExecuteAndWaitAsync()` on the RabbitMQ path (F-8) → there is no test that would prove the alert feed *works even if INV-3 were fixed* → the dashboard's `LowStockAlerts` field is stubbed `0` and operators see a permanently inert tile. INV-3 and F-8 are not independent findings at different priorities. They are the same defect seen from opposite ends of the stack. Fix INV-3 first; immediately write the integration test via a properly instrumented `BackofficeTestFixture`; verify the alert feed receives the event. If F-8 stays at #26, that second step will be deferred, and the fix for #1 will ship with no regression protection.

---

### QAE Cross-Review

#### What surprised me in the PSA findings

**INV-3 exposed a blind spot in my coverage assessment.** Inventory showed green in my coverage matrix — both unit and integration tests present. But if `Inventory.Api.IntegrationTests` drives `AdjustInventoryEndpoint` directly, those tests are verifying a code path that silently suppresses `InventoryAdjusted` and `LowStockDetected`. Passing tests. Wrong behavior. That is worse than a test gap: it is false confidence. I was evaluating test *presence*, not test *fidelity to the production path*. INV-3 is the first fix for exactly that reason.

**XC-3 and F-8 together describe the same future failure from opposite ends.** PSA flagged that `AcknowledgeAlert`'s manual `session.SaveChangesAsync()` will cause ordering issues when it needs to publish an outbox message. I flagged F-8 — `BackofficeTestFixture` has no `ExecuteAndWaitAsync()` or `TrackedHttpCall()`. The handler has a transaction anti-pattern that hasn't triggered yet; the test fixture has no ability to detect the cascade when it does. Each finding is medium severity alone. Together they are a silent failure waiting for a single new integration event.

#### What surprised me in the UXE findings

**The `ReturnDenied`/`ReturnRejected` and `ExchangeDenied`/`ExchangeRejected` pairs are a concrete test assertion problem, not just a readability issue.** When asserting on `mt_events` after a return inspection failure, I need to know whether to expect `ReturnRejected` or `ReturnDenied`. If a developer asserts the wrong one, the test can pass against a broken handler or fail against a correct one, with no type-system guard. These names live in `ReturnEvents.cs` — already a 140-line omnibus file (R-2). The UXE rename and PSA's R-2 refactor need to ship as one changeset.

**`CheckoutCompleted` with two different payloads in `Messages.Contracts`** deserves to be pulled out of `⚠️` territory and treated as a correctness risk. Two records with the same name and divergent shapes is a deserialization bug waiting to fire when a consumer binds the wrong one.

#### Priority list items I'd re-rank

- **F-8 (item #26, 🟢 Low) → 🟡 Medium.** The Convergence Summary notes the missing projections are partly unbuilt *because* there is no test harness to verify them. F-8 must come before the projections, not after.
- **Item #18 (`CheckoutCompleted` duplicate payload) → 🔴.** This is not a rename, it is a deserialization collision.
- **XC-1 (item #23)** resolution should explicitly include normalization in Returns and Vendor Portal, not just an ADR — four validator placement conventions means four places a test author must search when writing a sad-path test.

#### Single most important cross-agent observation

Reading all three sections together, the Vendor Portal failure mode is more complete than any single section shows: structurally fragmented (PSA), all E2E globally `@ignore` + no bUnit (QAE), all outbound integration events are command-masquerades (UXE). `SubmitChangeRequest` receives an unvalidated payload, flows through a fragmented handler, and emits a command-named event into Product Catalog — and **not a single automated test at any layer would fail if the entire flow broke silently**. VP-6 is a correctness risk only on paper until F-2 and F-4 are resolved. That compound failure mode is invisible unless you read all three sections together.

---

### UXE Cross-Review

#### What surprised me in the PSA findings

**INV-3's consequence for the dashboard KPI fix is not just architectural — it breaks the sequencing of M32.4's own deliverables.** My UXE section framed the stub KPIs as a build gap ("just build the projections"). INV-3 breaks that framing: the `LowStockAlerts` tile depends on `LowStockDetected` events that the bypassed endpoint never publishes. Build the projections without fixing INV-3 first, and the tile still shows zero with no obvious reason why. That unwritten sequencing dependency is the genuinely alarming thing — it is not captured anywhere in the priority list.

**XC-3 (`AcknowledgeAlert` manual transaction) deserves more weight from a CS operator perspective.** The PSA correctly identifies it as a future transaction-ordering risk. What it also means today: if an alert acknowledgment silently fails inside a manually-managed transaction, the CS operator sees no feedback and the alert remains in the feed. This is a present-tense UX failure mode, not just a hypothetical future bug.

#### What surprised me in the QAE findings

**F-2's framing matters: this is deliberate suppression, not accidental omission.** A `@ignore` at scenario level signals a known infrastructure blocker. A `@ignore` at *feature level* signals a decision that the whole surface area does not need automated validation. Combined with VP-6 (no validators on any Vendor Portal command) and F-4 (no bUnit project), the Vendor Portal is in a genuinely dark state — structurally fragmented, unvalidated at runtime, and unmonitored at every test layer.

**F-5 (missing `cart-real-time-updates.feature` in Customer Experience E2E)** is where QAE coverage absence most directly compounds a UX risk. The Shopping cart's real-time update behavior — price freezing at add-to-cart, coupon application reflected live — is one of the highest-value customer-facing guarantees in the system. E2E is the only test layer where SignalR cart badge updates are observable. The gap is ranked medium and I agree, but it should be the *first* medium item addressed in the Customer Experience track.

#### Priority list items I'd re-rank

- **Item #11 (missing Backoffice projections + stub KPIs) → 🔴 High, or at minimum flag "already broken for users today."** The current 🟡 medium framing sounds like future debt. Two of five executive KPIs display hard-coded zeros *today*. The framing should be: these are currently-broken screens, not upcoming features.
- **Item #12 (no Order Search or Return Management page) → 🔴 High.** No dedicated Order Search path means every CS agent call that starts with an order number incurs a multi-step workaround. This is a measurable productivity cost on every interaction, not a future-risk item.

#### Single most important cross-agent observation

The dashboard zero-KPI problem is a **three-fault system**: missing projections (UXE), suppressed upstream events (PSA), and no cascade-aware test harness to verify fixes (QAE). Each fault looks manageable in isolation. The risk is that M32.4 closes only the first fault (builds the projections), the milestone ships "done," and partial zeros persist with no clear owner. INV-3 → F-8 → projection build is a sequenced dependency chain, not three parallel work items. The priority list does not reflect that sequencing explicitly, and it should.

---

## Product Owner Briefing Response

### 1. Business Reaction to the Top Findings

**INV-3 — This one is already hurting us and nobody has noticed yet.** The audit calls it a correctness bug. From a product perspective, it is a trust problem. We shipped an inventory management UI in M32.3 — WarehouseClerk can now adjust stock through the Backoffice. What the audit tells me is that every one of those adjustments silently discards the `LowStockDetected` and `InventoryAdjusted` integration events. The alert feed — which CS agents and warehouse managers are supposed to rely on — is blind to every adjustment made through our own tool. The UXE cross-review nails it: even if we build all three missing projections in M32.4, the `LowStockAlerts` tile will still read zero until INV-3 is fixed. These cannot be sequenced the wrong way.

**Missing Marten projections + stub KPIs — Broken screens are not backlog items.** Executives using the dashboard today see two tiles permanently displaying `0` — `PendingReturns` and `LowStockAlerts`. The code comment literally says `// STUB`. This is not a roadmap gap; it is a currently-broken screen. The engineering framing of "build projections in M32.4" is appropriate, but only if INV-3 is fixed first; otherwise building them achieves nothing for one of those tiles.

**No Return Management page, no Order Search page — Our highest-impact CS workflow gap.** Every order lookup currently requires navigating through Customer Search. In a real CS environment, that adds 30–60 seconds to average handle time on every call. Returns-related calls are the most time-sensitive — customers are already frustrated, and agents are burning time on an indirect path. A CS agent who receives a ticket with an order number but no customer email has no direct entry point in the current UI. This is not a future risk; it is an operational bottleneck on every interaction today.

**Vendor Portal E2E globally `@ignore` — Silent failure is the worst kind.** We have no automated signal if vendor auth breaks, if a change request workflow regresses, or if SignalR disconnects on login. Vendor relationships are high-stakes. A vendor who cannot log in or submit a change request does not call support — they escalate commercially. Zero browser-level coverage for a vendor-facing interface is not an acceptable state.

**Returns BC structural violations (R-1 through R-6) — Engineering hygiene with a business deadline attached.** Returns BC is currently Phase 1: same-SKU exchanges only. Phase 2 — cross-SKU exchanges, partial returns, refund-to-store-credit — is coming. If we carry R-1 through R-6 into Phase 2 development, every sprint will fight merge conflicts in a 387-line handler file. The time to clean up is before the next wave of feature investment.

---

### 2. Context the Engineering Agents Don't Have

**Returns BC is the next major feature investment.** The 30-day eligibility window and same-SKU exchange constraint are explicit Phase 1 limitations. Business is pushing for Phase 2. R-1 through R-6 are not abstract debt — they are the foundation we will be building on in M33+. Deferring the structural cleanup means negotiating complexity into every future sprint in that track.

**Vendor Portal has a commercial commitment attached to it.** Vendors using the portal to submit change requests and monitor analytics are not beta testers — they are active commercial relationships. F-2 (globally `@ignore`) combined with VP-6 (no validators) means we have an unvalidated, unmonitored external-facing interface. That is a vendor support ticket waiting to happen.

**The Backoffice executive dashboard is being used to make inventory decisions today.** We have communicated to stakeholders that the dashboard reflects live stock signals. INV-3 means it does not — and has not since the inventory write endpoints were added in M32.3. Stakeholders need to know the `LowStockAlerts` tile is unreliable until this is fixed. Better to surface that now than after a missed reorder point.

---

### 3. Challenge to the Priority List

The engineering agents ranked: **#1 INV-3 → #2 F-2 (Vendor Portal E2E) → #3/#4 Returns structural violations.**

I agree with INV-3 at #1 — no argument there. Correctness bug with immediate user-visible downstream consequence.

**I would move the missing Backoffice pages (Order Search + Return Management) ahead of F-2.** The engineering ranking treats F-2 as high because test coverage is structurally absent. The Vendor Portal E2E gap is a risk we can detect manually today. A CS agent with no direct Order Search path cannot work around that gap without measurable time loss on every interaction. User-facing operational friction outranks test infrastructure gaps in my ordering.

**I agree with keeping Returns structural violations at #3**, but only because Phase 2 feature work is not yet in the active sprint. The moment Returns Phase 2 enters planning, this jumps to #1 for that track.

**Revised order from a business perspective:**
1. INV-3 (correctness bug, blocks dashboard fix)
2. Missing projections + dashboard KPIs (build *after* INV-3 fix — sequenced dependency)
3. Order Search + Return Management pages in Backoffice (CS workflow impact, measurable handle time)
4. Vendor Portal E2E `@ignore` removal (external-facing coverage gap)
5. Returns BC structural cleanup (prerequisite for Phase 2 investment)

---

### 4. The Three Things the Business Cannot Afford to Delay in M32.4

**1. Fix INV-3 before touching anything else on the dashboard.** Build the projections after, not before, not in parallel. If we ship the projections without fixing INV-3 first, we close M32.4 with a partially-broken dashboard and no clear trace of why. That is a credibility problem with stakeholders.

**2. Build the Order Search and Return Management pages.** M32.4 is "UX polish" — and there is no polish more impactful than giving CS agents a direct path to the tools they use most. If M32.4 ships without them, we have done UX polish around the edges while leaving the most-used workflows broken.

**3. Remove the `@ignore` tags from Vendor Portal E2E feature files.** This is not a request to write new tests — it is a request to stop suppressing the tests we already have. Individual scenarios with documented infrastructure blockers can remain `@ignore` at the scenario level. But feature-level suppression of an entire external-facing interface must end in M32.4.

---

## Collaborative Discussion Summary

### Where the Panel Aligned

**INV-3 at the top is unanimous.** All four participants independently converged on this being the single most urgent item — not because the code violation is the worst in the codebase structurally, but because it is a live correctness bug with a user-visible downstream chain. The PSA surfaced it. The UXE connected it to the stub KPIs. The QAE identified the false-confidence test coverage it hides behind. The PO confirmed the stakeholder trust dimension.

**The INV-3 → F-8 → projection build dependency chain was the most important synthesis of the session.** No single agent had the full picture. PSA saw the bypass. QAE saw the missing test harness. UXE saw the broken dashboard tiles. PO confirmed the stakeholder credibility exposure. Only together did the chain become clear: INV-3 must be fixed, then a properly-instrumented `BackofficeTestFixture` must be used to write a cascade-aware integration test, then the three missing Marten projections can be built with confidence they will actually receive the upstream events they depend on.

**Vendor Portal is the most dangerous BC in the codebase.** PSA, QAE, and UXE each flagged it independently from different angles. The PO added commercial context. The combination — unvalidated inputs, fragmented handlers, globally-suppressed E2E tests, no bUnit coverage, command-masquerade integration events — means a change request workflow regression could ship and stay shipped with no automated detection at any layer.

**The Backoffice CS workflow gaps (Order Search + Return Management) deserve higher business priority than the engineering list reflects.** The engineers ranked them at #12; the PO argued for top-5. The panel settled on top-3 in the business-adjusted list. The engineering list weights architectural cleanliness; the business-adjusted list weights operational cost.

### Where the Panel Diverged

**F-2 effort estimate (S vs. M–L):** The original audit estimated S (half day) for removing Vendor Portal E2E `@ignore` tags. PSA challenged this, noting that unbound feature files would produce failing rather than passing tests without step definition work. The panel consensus is M with a two-phase approach: (a) remove feature-level `@ignore` and fix or `@ignore` at scenario level for unbound steps within one session; (b) write step definitions for unbound scenarios as a separate M sprint item.

**`CheckoutCompleted` duplicate payload ranking:** QAE and PSA both argued for escalating this from `⚠️ Medium` to 🔴. UXE and PO deferred to the engineers' judgment on migration risk. The panel did not reach consensus. The disagreement is captured in the Top 10: it is ranked #4 with a dissent note from UXE/PO (they accept the ranking but do not drive it).

**Returns structural cleanup vs. Backoffice missing pages:** PSA and QAE held the Returns structural violations at top-3. PO and UXE argued for pushing Backoffice CS workflow pages higher. The final Top 10 reflects a compromise: both appear in the top 5, with INV-3 and its dependent chain occupying #1–2 as uncontested.

**F-8 re-ranking:** PSA and QAE independently argued F-8 should be elevated from 🟢 Low to 🟡 Medium. PO was neutral; UXE agreed. The final Top 10 surfaces F-8 as item #6 with explicit dependency framing relative to INV-3.

---

## Top 10 Priority List

> **Risk:** 🔴 High (likely bugs, data issues, near-term) · 🟡 Medium (technical debt that compounds) · 🟢 Low
> **Effort:** S = half day or less · M = 1–2 days · L = 3–5 days · XL = 5+ days

---

### #1 — Fix `AdjustInventoryEndpoint` Bypass of Wolverine Aggregate Workflow

**Finding:** PSA INV-3 · **Owning agent:** PSA + UXE
**Risk:** 🔴 High · **Effort:** M

**Why it's here:** This is a live correctness bug, not architectural debt. Every inventory adjustment made through the Backoffice WarehouseClerk UI since M32.3 silently suppresses `LowStockDetected` and `InventoryAdjusted` integration events, making the Backoffice alert feed and dashboard `LowStockAlerts` KPI permanently blind to write-path changes. Fixing INV-3 is the prerequisite for all three missing Marten projections — build them first and the `LowStockAlerts` tile will still read zero. The PO confirmed this is a live stakeholder trust problem: the dashboard has been communicating false inventory signal since M32.3.

**Dissent:** None. All four participants rated this #1.

---

### #2 — Build the Three Missing Backoffice Marten Projections + Resolve Stub KPIs

**Finding:** UXE Backoffice Status Report (FulfillmentPipelineView, ReturnMetricsView, CorrespondenceMetricsView) · **Owning agent:** UXE + QAE
**Risk:** 🔴 High · **Effort:** L

**Why it's here:** Two of five executive dashboard KPIs (`PendingReturns`, `LowStockAlerts`) currently display hard-coded `0` with `// STUB` comments. Three planned Marten projections that would power these KPIs were scoped in M32.0 and never built. The engineering audit ranked this 🟡 Medium because it is "addressable in M32.4." The PO re-elevated it: broken screens being used by executives for inventory and operations decisions are not backlog items. **This item must be sequenced after #1** — fixing INV-3 first ensures the projections will actually receive the upstream events they depend on.

**Dissent:** PSA noted that F-8 (BackofficeTestFixture missing `ExecuteAndWaitAsync()`) must be addressed as part of this item — not separately. The projections should be built with a properly instrumented test fixture from day one.

---

### #3 — Add Dedicated Order Search and Return Management Pages to Backoffice

**Finding:** UXE Backoffice Status Report (absent pages) · **Owning agent:** UXE
**Risk:** 🔴 High (PO-adjusted) · **Effort:** M

**Why it's here:** The original audit ranked this #12 (🟡 Medium). The PO elevated it to top-3: every CS agent call that begins with an order number currently requires navigating through Customer Search first, adding 30–60 seconds to average handle time. Returns are the most time-sensitive CS workflow (customer frustration peaks here), yet there is no dedicated return queue page — agents must approach returns through a customer search. The PO's input changed this ranking explicitly: operational cost on every interaction outranks architectural style violations that have no user-facing symptom.

**Dissent:** PSA and QAE accepted the re-ranking without objection. No dissent.

---

### #4 — Rename `CheckoutCompleted` to Eliminate Dual-Payload Deserialization Collision

**Finding:** UXE Event Naming (Orders BC + Messages.Contracts/Shopping) · **Owning agent:** UXE (escalated by PSA + QAE)
**Risk:** 🔴 High · **Effort:** S

**Why it's here:** Two records named `CheckoutCompleted` exist in different namespaces with different payload shapes. The original audit ranked this ⚠️ Medium (priority list #18). PSA and QAE independently argued for escalation: this is a live deserialization collision risk, not a naming style issue. When a consumer binds the wrong `CheckoutCompleted`, it either silently drops fields or throws at runtime. The fix is a single record rename (≤1 hour), which makes the severity/effort ratio the best in the list.

**Dissent:** UXE and PO initially deferred to the engineers on migration risk. The panel consensus is 🔴 given that the fix is trivial and the failure mode is non-obvious at runtime. UXE and PO accepted this but do not drive it.

---

### #5 — Returns BC Structural Refactor (R-1 through R-6)

**Finding:** PSA R-1, R-2, R-3, R-4, R-5, R-6 · **Owning agent:** PSA + QAE
**Risk:** 🔴 High · **Effort:** L

**Why it's here:** The Returns BC is the canonical anti-pattern in `vertical-slice-organization.md`: 11 commands in one file, 17 events in one file, 4 validators extracted into a bulk file, 5 handlers in a 387-line file. This is also the BC where the QAE found no `TrackedHttpCall()` timing guarantee (F-7), meaning test reliability is also degraded. The PO elevated this above the original engineering ranking (#3/#4) specifically because **Returns Phase 2 is the next major feature investment** — cross-SKU exchanges, partial returns, and refund-to-store-credit will all require extending these files. The refactor must happen before Phase 2 development begins; doing it after means fighting structural debt on every sprint.

**Dissent:** None. R-4 (`ReturnCommandHandlers.cs`, 387 lines) and R-1 (`ReturnCommands.cs`, 11 commands) should be addressed first within this item; R-6 (missing validators) and F-7 (`TrackedHttpCall()`) should ship as part of the same changeset.

---

### #6 — Fix `BackofficeTestFixture`: Add `ExecuteAndWaitAsync()` and `TrackedHttpCall()`

**Finding:** QAE F-8 · **Owning agent:** QAE (re-ranked by PSA + UXE)
**Risk:** 🟡 Medium (re-ranked from 🟢 Low) · **Effort:** S

**Why it's here:** The original audit ranked F-8 at #26 (🟢 Low). PSA and QAE independently argued for elevation: the fixture gap is the reason the three missing Marten projections (#2) will be difficult to build correctly, and it is the reason INV-3's fix (#1) cannot be regression-tested without manual verification. Without `ExecuteAndWaitAsync()`, there is no way to write an integration test that proves the full chain — `AdjustInventory` command → `LowStockDetected` event → `AlertFeedView` projection update — completes correctly. This item is ranked immediately after the Returns refactor because it is a prerequisite for the projection build work (#2), not an isolated cleanup task.

**Dissent:** PO was neutral. No agent dissented.

---

### #7 — Remove Feature-Level `@ignore` Tags from All Vendor Portal E2E Feature Files

**Finding:** QAE F-2 · **Owning agent:** QAE
**Risk:** 🔴 High · **Effort:** M (re-estimated from S)

**Why it's here:** All three Vendor Portal E2E feature files carry `@ignore` at the Feature tag level, silencing 100% of browser-level tests in CI including P0 auth scenarios. The original audit ranked this #2 with effort S. The panel revised the effort estimate to M after PSA noted that removing feature-level `@ignore` without bound step definitions will produce failing tests rather than passing coverage for the three unbound feature files. The remediation is two-phase: (a) remove feature-level tags and convert known-broken scenarios to scenario-level `@ignore` with comments; (b) implement step definitions for unbound scenarios. Phase (a) is S; phase (b) is M. The PO noted that vendor relationships make this a commercial risk, not just a coverage metric.

**Dissent:** Original audit ranked this #2; the panel moved it to #7 after elevating the Backoffice CS workflow gaps (#3) and the `CheckoutCompleted` collision (#4) on business-impact grounds. PSA and PO noted that the Vendor Portal's structural violations (VP-1, VP-2, VP-6) remain unaddressed and will make step definition work harder until that refactor happens.

---

### #8 — Vendor Portal Structural Refactor: Commands/Handlers Split + Missing Validators (VP-1, VP-2, VP-4, VP-6)

**Finding:** PSA VP-1, VP-2, VP-4, VP-6 · **Owning agent:** PSA + QAE
**Risk:** 🔴 High · **Effort:** L

**Why it's here:** The Vendor Portal has a Commands/Handlers folder split within every feature folder (VP-1, VP-2), seven catalog response handlers packed in one file (VP-4), and zero `AbstractValidator<T>` on any command (VP-6). QAE confirmed that no automated test at any layer would fail if the `SubmitChangeRequest` flow broke silently — the structural fragmentation and missing validators together make this the most dangerous code surface in the codebase. This is ranked #8 rather than higher because the E2E coverage gap (#7) must be resolved first: fixing the structure while the E2E tests are suppressed means the refactor ships without observable regression protection.

**Dissent:** PO noted that the commercial risk of the Vendor Portal's broken state argues for accelerating both #7 and #8 into the same sprint. Engineers agreed this is the ideal sequence; the ranking reflects prerequisite order rather than relative urgency.

---

### #9 — Promotions BC: Create Unit Test Project + Fix Collection Fixture Pattern

**Finding:** QAE F-1 (missing unit tests), F-6 (non-standard fixture) · **Owning agent:** QAE
**Risk:** 🟡 Medium · **Effort:** M

**Why it's here:** Promotions has no unit test project despite significant domain logic (coupon validation rules, discount calculation, usage limits, optimistic concurrency guards). The integration tests use `IClassFixture<TestFixture>` (4 separate PostgreSQL containers per run) instead of the standard `ICollectionFixture` pattern (1 shared container), adding unnecessary CI overhead. While Promotions was rated "exemplary" by PSA for its vertical slice structure, these two test infrastructure gaps mean it is the cleanest BC with the leakiest test setup. The PO had no specific concern here; this is engineering health work.

**Dissent:** PSA noted that combining F-1 and F-6 into a single sprint item is the right sequencing — the new `Promotions.UnitTests` project should establish the fixture pattern correctly from creation, making F-6 a zero-cost fix alongside F-1.

---

### #10 — Write ADR for Canonical Validator Placement Convention + Normalize Returns and Pricing

**Finding:** PSA XC-1 (four validator placement conventions) + PR-1 (Pricing three-way split) · **Owning agent:** PSA
**Risk:** 🟡 Medium · **Effort:** S (ADR) + S (Pricing normalization) = M total

**Why it's here:** Four distinct validator placement patterns exist across the codebase (nested class, top-level same file, separate file, bulk file). This is an onboarding and correctness risk: new contributors follow whichever BC they encounter first. The ADR should declare the canonical pattern (top-level `AbstractValidator<T>` class in the same file as the command record and handler — not nested, not separate, not bulk). Once the ADR is written, the Pricing three-way split (PR-1: two commands each split across three files) is a trivial mechanical fix. The Returns validator pattern (R-3) will be resolved as part of #5. This item is ranked last because the ADR has no user-facing consequence and the Pricing normalization is low-risk, but the ADR is a prerequisite for any future validator-placement review comment to have a canonical reference to point at.

**Dissent:** None. All agents agreed this belongs in the Top 10 as the "institutional knowledge" item that prevents future inconsistency, even if it has no near-term business consequence.

---

*Discussion conducted by PSA · QAE · UXE · PO on 2026-03-21. References: `docs/audits/CODEBASE-AUDIT-2026-03-21.md`.*
