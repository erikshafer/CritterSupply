# Skills Refresh Inventory — M35.0 through M36.1

**Created:** 2026-03-31
**Scope:** Retrospectives from M35.0, M36.0, and M36.1 milestones
**Purpose:** Decision record for which skill files need updates and what lessons apply

---

## Sources Reviewed

### Milestone Closure Retrospectives (Primary)
- `m35-0-milestone-closure-retrospective.md` — Product Expansion Begins (7 sessions)
- `m36-0-milestone-closure-retrospective.md` — Engineering Quality (6 sessions)
- `m36-1-milestone-closure-retrospective.md` — Listings + Marketplaces (10 sessions)

### Session Retrospectives (Supplement)
- M35.0 Sessions 1–6
- M36.0 Sessions 1–6
- M36.1 Sessions 1–10

---

## Inventory by Skill File

### 1. `wolverine-message-handlers.md`
- **Current last tag:** ⭐ M32 Addition (Pattern 8: Async vs Sync Return Types)
- **Action:** UPDATE
- **Findings:**
  - M36.0 S2: `bus.PublishAsync()` in HTTP endpoints bypasses transactional outbox — must use `OutgoingMessages` return. This is the most pervasive Critter Stack idiom violation found in M36.0.
  - M36.1 S7–8: `(IResult, OutgoingMessages)` tuple return from HTTP endpoints is the established pattern for handlers that publish integration events alongside HTTP responses.
  - M36.1 S8: Idempotent guard — if aggregate is already in target state, `OutgoingMessages` remains empty; prevents duplicate events from idempotent HTTP calls.
- **Sections affected:** "Handler Return Patterns" (HTTP endpoint tuple pattern), "Anti-Patterns to Avoid" (bus.PublishAsync DO NOT)

### 2. `critterstack-testing-patterns.md`
- **Current last tag:** No explicit milestone tags
- **Action:** UPDATE
- **Findings:**
  - M36.1 S8: `TestAuthHandler` was unconditionally authenticating all requests — silent bug since M36.0. Fix: check for `Authorization` header, return `AuthenticateResult.NoResult()` if absent.
  - M36.1 S8: `AddDefaultAuthHeader()` extension on `IAlbaHost` — now required after host creation for all fixtures using `AddTestAuthentication()`.
  - M36.1 S8: Seed data isolation — dedicated `SeedDataTests` class calls `ReseedAsync()` in `InitializeAsync()`, does NOT call `CleanAllDocumentsAsync()` in `DisposeAsync()`.
  - M36.1 S8: `TrackedHttpCall()` helper on TestFixture for HTTP + message tracking.
  - M36.1 S7: `ExecuteAndWaitAsync<T>()` already documented in file — verify completeness.
- **Sections affected:** "Integration Test Fixture" (auth handler fix, AddDefaultAuthHeader), new anti-pattern entries (unconditional auth, seed data race), "TestFixture Helper Methods" (TrackedHttpCall)

### 3. `integration-messaging.md`
- **Current last tag:** ⭐ M32 Addition (Lesson 12)
- **Action:** UPDATE
- **Findings:**
  - M36.0 S2: `bus.PublishAsync()` in HTTP endpoints publishes outside Wolverine transaction envelope — message sent even if handler fails. `OutgoingMessages` is the only safe publishing mechanism from HTTP endpoints.
  - M36.1 S7: `OutgoingMessages` from Wolverine message handlers (not just HTTP) — same pattern applies. `ListingApprovedHandler` returns `OutgoingMessages`.
  - M36.1 S7: `ListingApproved` message enrichment tradeoff — deliberate shortcut carrying `ProductName`, `Category`, `Price` in integration message. Documented in ADR 0049 risk section.
  - M36.1 S8: Idempotent integration event publishing — if aggregate already in target state, do not publish event. `OutgoingMessages` stays empty.
- **Sections affected:** "Publishing Integration Messages" (OutgoingMessages emphasis), "Critical Warnings" (bus.PublishAsync hazard), "Lessons Learned" (new lessons 13–15)

### 4. `marten-document-store.md`
- **Current last tag:** No explicit milestone tags
- **Action:** UPDATE
- **Findings:**
  - M36.0 closure: `opts.Policies.AutoApplyTransactions()` is mandatory for every Marten BC — absence caused silent projection failures in Product Catalog (5 tests). Most impactful M36.0 correctness fix.
  - M36.1 S7: Composite key identity for `CategoryMapping` — `{ChannelCode}:{InternalCategory}` as string Id. Enables single `LoadAsync` without queries.
  - M36.1 S6: Natural key as document identity — `ChannelCode` as `Marketplace` document Id. When to use natural vs surrogate keys.
- **Sections affected:** "Document Identity" (composite key, natural key patterns), new "Critical Configuration" content (AutoApplyTransactions)

### 5. `marten-event-sourcing.md`
- **Current last tag:** References Cycle 27 (no explicit M-tag)
- **Action:** UPDATE
- **Findings:**
  - M36.0 closure: `AutoApplyTransactions()` required — without it, `session.Events.Append()` calls are silently discarded.
  - M36.0 S2–3: `SaveChangesAsync()` removal — redundant when `AutoApplyTransactions()` is configured. 34 calls removed across 4 BCs.
  - M35.0: Incremental ES migration pattern — define events first, build aggregate + projection, migrate handlers one by one while keeping tests green.
- **Sections affected:** "Marten Configuration" (AutoApplyTransactions), "Lessons Learned" (new entries), "Wolverine Integration Patterns" (SaveChangesAsync removal)

### 6. `event-sourcing-projections.md`
- **Current last tag:** References M32.0 (implicit)
- **Action:** UPDATE
- **Findings:**
  - M36.0 closure: Missing `AutoApplyTransactions()` caused projection failures misclassified as async projection timing issues. The symptom — projection state not reflecting handler-appended events — is identical to a race condition. Diagnosis required understanding Wolverine's middleware pipeline.
- **Sections affected:** "Common Pitfalls and Warnings" or "Production Lessons Learned" (AutoApplyTransactions impact on projection timing)

### 7. `adding-new-bounded-context.md`
- **Current last tag:** No explicit milestone tags
- **Action:** UPDATE
- **Findings — missing checklist items:**
  - M36.0: `opts.Policies.AutoApplyTransactions()` in `Program.cs` — mandatory before writing any handler
  - M36.0 D11: `[Authorize]` on all non-auth endpoints from first commit
  - M36.1: `OutgoingMessages` routing in `Program.cs` for integration events
  - M36.1 S8: Seed data class isolation — dedicated test class with `ReseedAsync()` in `InitializeAsync()`
  - M36.1 S9: E2E dynamic `appsettings.json` — new BC API URL must be added to `WasmStaticFileHost`
- **Sections affected:** Quick Checklist (5 new items)

### 8. `e2e-playwright-testing.md`
- **Current last tag:** References M32.4 Session 1
- **Action:** UPDATE
- **Findings:**
  - M36.1 S9: `WasmStaticFileHost` dynamic `appsettings.json` gap — `MarketplacesApiUrl` was missing from E2E test fixture override. Tests would silently call non-existent default URL.
  - M36.1 S8–9: `StubMarketplacesApiHost` pattern — `WebApplication`-based stub serving specific endpoints. `SeedX()` methods for per-scenario data. `Clear()` for cleanup. `E2ETestFixture` starts and disposes the stub.
  - M36.1 S10: E2E shard tag requirement — `.feature` files must have `@shard-N` tag on line 1 or CI runners silently skip them.
- **Sections affected:** New "DO NOT" entry (dynamic appsettings gap), new content (stub API host pattern for external BC APIs, shard tag requirement)

### 9. `external-service-integration.md`
- **Current last tag:** No explicit milestone tags
- **Action:** UPDATE
- **Findings:**
  - M36.1 S7: `IVaultClient` / `DevVaultClient` pattern — interface with `GetSecretAsync(string path)`, stub reads from `IConfiguration` via `Vault:{path}` key, production safety guard throws at startup in non-Development environments.
  - M36.1 S7: `IMarketplaceAdapter` keyed DI dictionary — `IReadOnlyDictionary<string, IMarketplaceAdapter>` registered in DI, keyed by `ChannelCode`. Resolves adapters without service locator.
- **Sections affected:** New "Credential Management Stubs" section (IVaultClient), new "Strategy Pattern with Keyed DI" section (IMarketplaceAdapter dictionary), "Production Safety" content enhancement

### 10. `vertical-slice-organization.md`
- **Current last tag:** References ADR 0023
- **Action:** UPDATE
- **Findings:**
  - M36.1 S10: `*ES` suffix removal — 13 files renamed in Product Catalog. Implementation-detail suffixes in handler names are an anti-pattern.
  - M36.0 Track C: Vertical slice splits (VP Team Management, PC Vendor Assignment) and validator colocation. DDD naming: `*Requested` reserved for integration events, internal commands use imperative verb form.
- **Sections affected:** "File Naming Conventions" (anti-pattern: implementation-detail suffixes), "Lessons Learned" (new entry)

---

## Lower-Priority Files — Assessed as NO CHANGE

| Skill File | Rationale |
|------------|-----------|
| `wolverine-sagas.md` | No major saga work in M35.0–M36.1 |
| `event-modeling-workshop.md` | Process skill; no drift detected |
| `bff-realtime-patterns.md` | No significant BFF changes in scope |
| `blazor-wasm-jwt.md` | Stable; CORS config note is current |
| `reqnroll-bdd-testing.md` | Stable |
| `testcontainers-integration-tests.md` | Stable |
| `modern-csharp-coding-standards.md` | Stable |
| `efcore-wolverine-integration.md` | No EF Core work in M35–M36 |
| `efcore-marten-projections.md` | Stable |
| `dynamic-consistency-boundary.md` | No DCB usage in M35–M36 |
| `wolverine-signalr.md` | No SignalR work in M35–M36 |
| `bunit-component-testing.md` | Stable |
| `copilot-session-prompt.md` | Maintained separately |

---

## Final Results

**Completed: 2026-03-31**

### Files Updated (10 total)

| Skill File | What Was Added |
|------------|---------------|
| `wolverine-message-handlers.md` | Pattern 7: `(IResult, OutgoingMessages)` tuple for HTTP endpoints; Anti-Pattern #11: `bus.PublishAsync()` in HTTP endpoints; idempotent guard pattern; ToC updated |
| `critterstack-testing-patterns.md` | `TestAuthHandler` auth header check fix; `AddDefaultAuthHeader()` requirement; seed data isolation pattern with `ReseedAsync()`; warning signs updated |
| `integration-messaging.md` | Warning 7: `bus.PublishAsync()` bypasses outbox; Pattern 2 annotation corrected; Lessons 13–15 (OutgoingMessages mandate, idempotent publishing, message enrichment tradeoffs); ToC updated |
| `marten-document-store.md` | Natural key as document identity pattern; composite key identity pattern; `AutoApplyTransactions()` mandatory policy with M36.0 context |
| `marten-event-sourcing.md` | Lessons L6–L8 (AutoApplyTransactions silent failure, SaveChangesAsync redundancy, incremental ES migration); last-updated note |
| `event-sourcing-projections.md` | Lesson 7: missing `AutoApplyTransactions()` mimics projection timing issues |
| `adding-new-bounded-context.md` | 5 new checklist items: AutoApplyTransactions, [Authorize], OutgoingMessages routing, seed data isolation, E2E dynamic appsettings |
| `e2e-playwright-testing.md` | Stub API Host pattern for external BC APIs; DO NOT: omit API URLs from dynamic appsettings; DO NOT: create .feature files without @shard-N tags; M36.1 reference files |
| `external-service-integration.md` | Credential management stubs (IVaultClient/DevVaultClient); production safety guard; strategy pattern with keyed DI dictionary |
| `vertical-slice-organization.md` | Anti-pattern: implementation-detail suffixes (*ES); Lesson L6: migration artifact cleanup |

### Files Reviewed — No Change Required (13 total)

All lower-priority files were assessed and confirmed to be current. No changes needed.

### Deliberately Excluded Lessons

| Retro Finding | Why Excluded from Skills |
|---------------|------------------------|
| E2E shard tags missing on 3 specific .feature files (M36.1 S10) | Documented as a DO NOT pattern in `e2e-playwright-testing.md`; the specific files are a code fix, not a skill update |
| `SaveChangesAsync` removal from specific handler files | The pattern is documented in `marten-event-sourcing.md` L7; specific file changes are implementation, not skill content |
| VP Team Management / PC Vendor Assignment vertical slice splits (M36.0) | File-level reorganization; the vertical slice principle was already well-documented in L1 |

### README Updated

- `external-service-integration.md` description expanded to include credential management stubs and keyed DI dictionaries
- Last Updated date set to 2026-03-31
