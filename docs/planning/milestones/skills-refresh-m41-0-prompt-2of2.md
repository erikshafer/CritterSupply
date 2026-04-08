# Skills Refresh — Post-M41.0 Fulfillment Remaster (2 of 2)
## Integration Messaging and Testing Pattern Skills

## Context

The Fulfillment BC Remaster (M41.0, S1–S5) introduced patterns not yet documented in:

- `docs/skills/integration-messaging.md` — missing Lesson 16 (dual-publish migration strategy)
  and dead consumer detection guidance
- `docs/skills/critterstack-testing-patterns.md` — missing ISystemClock/FrozenSystemClock
  pattern and injectable failure stub pattern

This is a documentation-only session. No `src/` or `tests/` files are touched.

**Prompt 1 of 2** covers `wolverine-message-handlers.md` and `wolverine-sagas.md`.

---

## Required Reading

1. `docs/planning/milestones/fulfillment-remaster-s3-retrospective.md` — ICarrierLabelService
   + ISystemClock test infrastructure (Part 1A and Part 1B)
2. `docs/planning/milestones/fulfillment-remaster-s4-retrospective.md` — dual-publish removal;
   dead consumer grep table
3. `docs/planning/milestones/fulfillment-remaster-s5-retrospective.md` — dead handler cleanup;
   Track A classification pattern

---

## Mandatory Bookend

```bash
dotnet build
# Expected: 0 errors, 17 warnings (unchanged — no source files modified)
```

---

## Track C — `docs/skills/integration-messaging.md`

Two changes: update the "Adding a New Integration" checklist and add Lesson 16.

### C1: Update the "Adding a New Integration" checklist

Find `### Checklist` under `## Adding a New Integration`. The current list ends with:

```markdown
7. **Write cross-BC smoke test** to verify RabbitMQ pipeline end-to-end
8. **Update `CONTEXTS.md`** only if new BC integration is introduced (not for additional messages in existing integration)
```

Replace steps 7 and 8 with the following three steps:

```markdown
7. **Write cross-BC smoke test** to verify RabbitMQ pipeline end-to-end
8. **When retiring a contract:** Run `grep -r "RetiredEventName" src/` for every message
   type being removed. Classify every result as:
   - **Active** — still has a publisher; not being retired
   - **Dead-needs-migration** — should consume the replacement event; update the handler
   - **Dead-no-publisher** — no publisher emits this anymore; delete or add `[Obsolete]`
   Never close a milestone with unresolved dead handlers — they compile silently but serve
   no purpose and mislead future sessions.
9. **Update `CONTEXTS.md`** when:
   - A **new BC-to-BC integration direction** is introduced (e.g., first-ever message from
     Pricing to Inventory)
   - **Contract names change** — retiring `ShipmentDispatched` in favour of
     `ShipmentHandedToCarrier` updates the Fulfillment entry even though the direction
     (Fulfillment → Orders) hasn't changed
   - **Legacy contracts are retired** — remove them from the relevant BC entry

   Do NOT update for adding new messages to an existing integration direction with no
   naming changes.
```

### C2: Add Lesson 16

Find `### Lesson 15: Message Enrichment Tradeoffs Must Be Documented`. Add the following
new lesson immediately after Lesson 15 (before the `---` separator and `## Appendix`):

```markdown
### Lesson 16: Dual-Publish Migration Strategy for Contract Retirement (M41.0)

**Use Case:** Retiring or renaming an integration contract when multiple consumers depend on it.

**Problem:** A hard cutover (removing the old event and publishing only the new one in the
same session) requires all consumers to migrate simultaneously. For a remaster series spanning
multiple sessions, this is impractical and high-risk — especially when one of the consumers
is the Orders saga, which coordinates payments, inventory, and fulfillment.

**Pattern — Dual-Publish during the migration period:**

The publisher temporarily emits both the old and new event. A migration comment marks the
temporary dual-publish clearly:

```csharp
// MIGRATION: Dual-publish for backward compatibility with Orders saga.
// Remove after Orders saga gains ShipmentHandedToCarrier handler (M41.0 S4).
outgoing.Add(new LegacyMessages.ShipmentDispatched(shipmentId, orderId, carrier, trackingNumber, at));
// New contract:
outgoing.Add(new ShipmentHandedToCarrier(shipmentId, orderId, carrier, trackingNumber, at));
```

This keeps all existing consumers working without modification. The S1 session activates
the dual-publish. A later coordinated migration session retires it.

**The coordinated migration session (S4 in the Fulfillment Remaster):**
1. Add new handlers in all consumers — **add first**
2. Verify all consumer test suites pass with the new handlers
3. Remove legacy handlers from consumers — **remove second**
4. Remove the dual-publish from the publisher
5. Run the full cross-BC test suite as the final gate

**Strict sequencing: add → verify → remove. Never remove before adding.**

**What the migration comment must contain:**
- What it maintains compatibility with: `// Orders saga`
- When it can be removed: `// Remove after Orders saga gains ShipmentHandedToCarrier handler`
- The session where retirement happens: `// M41.0 S4`

**After the dual-publish is removed — dead consumer verification:**

Run `grep -r "ShipmentDispatched" src/` (and equivalent for every retired contract name).
Classify each result as active vs. dead. Dead handlers must be migrated to the replacement
event or deleted before the milestone closes. See the "Adding a New Integration" checklist
Step 8 for the classification process.

**CritterSupply example (M41.0):**
- `ShipmentDispatched` → `ShipmentHandedToCarrier`: S1 dual-publish, S4 retirement
- `ShipmentDeliveryFailed` → `ReturnToSenderInitiated`: S1 dual-publish, S4 retirement
- Consumer BCs affected: Orders, Correspondence, Customer Experience, Backoffice
- Dead consumers found post-S4: 8 handlers/projections (Customer Experience + Backoffice)
- Cleaned up in: M41.0 S5 (milestone closure session)

**Reference:** [Fulfillment Remaster S1 Retrospective](../../planning/milestones/fulfillment-remaster-s1-retrospective.md) · [S4 Retrospective](../../planning/milestones/fulfillment-remaster-s4-retrospective.md)
```

Also update the document version footer at the very bottom of the file.

**Find:**
```
**Document Version:** 1.0
**Last Updated:** 2026-03-15
```

**Replace with:**
```
**Document Version:** 1.1
**Last Updated:** 2026-04-07
```

---

## Track D — `docs/skills/critterstack-testing-patterns.md`

Two new sections added before `## Key Principles`, plus a TOC update.

### D1: Update Table of Contents

Find the last TOC entry:
```
15. [Key Principles](#key-principles)
```

Replace it with:
```
15. [Testing Time-Dependent Handlers — ISystemClock Pattern](#testing-time-dependent-handlers--isystemclock-pattern) ⭐ *M41.0 Addition*
16. [Testing Failure Paths — Injectable Failure Stub Pattern](#testing-failure-paths--injectable-failure-stub-pattern) ⭐ *M41.0 Addition*
17. [Key Principles](#key-principles)
```

### D2: Insert new section 15 — ISystemClock Pattern

Insert the following immediately **before** `## Key Principles`:

```markdown
## Testing Time-Dependent Handlers — ISystemClock Pattern

⭐ *M41.0 S3 Addition*

Handlers that check elapsed time (scheduled jobs, SLA monitoring, lost-in-transit detection)
cannot use `DateTimeOffset.UtcNow` directly — tests would need to wait real time or introduce
race conditions. The solution is an injectable clock abstraction.

### The Infrastructure

**`ISystemClock` interface (domain project — production code):**

```csharp
public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Production implementation — wraps DateTimeOffset.UtcNow.</summary>
public class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
```

**`FrozenSystemClock` (integration test project — test infrastructure only):**

```csharp
/// <summary>Test implementation — settable for time-based scenario control.</summary>
public class FrozenSystemClock : ISystemClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}
```

**In `Program.cs` (production):**
```csharp
builder.Services.AddSingleton<ISystemClock, SystemClock>();
```

**In `TestFixture.cs` (test infrastructure):**
```csharp
// Expose the clock as a public property so test classes can advance it
public FrozenSystemClock Clock { get; private set; } = new FrozenSystemClock();

// During host initialization:
Host = await AlbaHost.For<Program>(builder =>
{
    builder.ConfigureServices(services =>
    {
        services.RemoveAll<ISystemClock>();
        services.AddSingleton<ISystemClock>(Clock); // Share the fixture's instance
    });
});
```

### Usage in Tests

**Reset the clock in `InitializeAsync()` of each test class** — the `FrozenSystemClock` is
a singleton shared across the entire xUnit collection. One class advancing the clock by 8
days will affect subsequent classes unless they reset it.

```csharp
public class TimeBasedMonitoringTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public TimeBasedMonitoringTests(TestFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.CleanAllDocumentsAsync();
        _fixture.Clock.UtcNow = DateTimeOffset.UtcNow; // Reset to real "now"
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CheckForLostShipment_After_8_Days_Detects_Lost()
    {
        var shipmentId = await SeedInTransitShipment();

        _fixture.Clock.UtcNow = DateTimeOffset.UtcNow.AddDays(8); // Advance past threshold
        await _fixture.ExecuteAndWaitAsync(new CheckForLostShipment(shipmentId));

        using var session = _fixture.GetDocumentSession();
        var shipment = await session.Events.AggregateStreamAsync<Shipment>(shipmentId);
        shipment!.Status.ShouldBe(ShipmentStatus.LostInTransit);
    }

    [Fact]
    public async Task CheckForLostShipment_At_5_Days_Does_Not_Detect_Lost()
    {
        var shipmentId = await SeedInTransitShipment();

        _fixture.Clock.UtcNow = DateTimeOffset.UtcNow.AddDays(5); // Below threshold
        await _fixture.ExecuteAndWaitAsync(new CheckForLostShipment(shipmentId));

        using var session = _fixture.GetDocumentSession();
        var shipment = await session.Events.AggregateStreamAsync<Shipment>(shipmentId);
        shipment!.Status.ShouldBe(ShipmentStatus.InTransit); // Unchanged
    }
}
```

### Handler Pattern Using ISystemClock

```csharp
public static class CheckForLostShipmentHandler
{
    public static async Task<OutgoingMessages?> Handle(
        CheckForLostShipment message,
        IDocumentSession session,
        ISystemClock clock)  // Injected — never DateTimeOffset.UtcNow directly
    {
        var shipment = await session.Events.AggregateStreamAsync<Shipment>(message.ShipmentId);
        if (shipment is null || shipment.Status != ShipmentStatus.InTransit)
            return null;

        var daysSinceLastScan = (clock.UtcNow - shipment.LastCarrierScanAt).TotalDays;
        if (daysSinceLastScan < 7) return null;

        session.Events.Append(shipment.Id, new ShipmentLostInTransit(shipment.Id, clock.UtcNow));
        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Fulfillment.ShipmentLostInTransit(...));
        return outgoing;
    }
}
```

**CritterSupply reference:**
- `src/Fulfillment/Fulfillment/ISystemClock.cs`
- `tests/Fulfillment/Fulfillment.Api.IntegrationTests/FrozenSystemClock.cs`
- `tests/Fulfillment/Fulfillment.Api.IntegrationTests/Shipments/TimeBasedMonitoringTests.cs`

**Reference:** [Fulfillment Remaster S3 Retrospective — Part 1B](../../planning/milestones/fulfillment-remaster-s3-retrospective.md)

---

## Testing Failure Paths — Injectable Failure Stub Pattern

⭐ *M41.0 S3 Addition*

When a handler wraps an external service and the default test fixture uses a stub that always
succeeds, testing failure paths requires injecting a failing implementation without affecting
the happy-path test classes.

### The Pattern

**Step 1: Extract the interface (production code in the domain project):**

```csharp
public interface ICarrierLabelService
{
    Task<LabelResult> GenerateLabelAsync(ShipmentDetails details, CancellationToken ct);
}

/// <summary>Default stub — always succeeds in Development and CI.</summary>
public class StubCarrierLabelService : ICarrierLabelService
{
    public Task<LabelResult> GenerateLabelAsync(ShipmentDetails details, CancellationToken ct)
        => Task.FromResult(new LabelResult(
            TrackingNumber: $"STUB-{Guid.NewGuid():N}",
            LabelUrl: "https://stub.example.com/label.pdf",
            Success: true));
}
```

**Step 2: Register the stub in `Program.cs`:**
```csharp
builder.Services.AddSingleton<ICarrierLabelService, StubCarrierLabelService>();
```

**Step 3: Create the always-failing stub (test infrastructure only):**

```csharp
/// <summary>
/// Always-failing stub. Only used in <see cref="LabelFailureTestFixture"/>.
/// Never register in the main TestFixture.
/// </summary>
public class AlwaysFailingCarrierLabelService : ICarrierLabelService
{
    public Task<LabelResult> GenerateLabelAsync(ShipmentDetails details, CancellationToken ct)
        => Task.FromResult(new LabelResult(
            TrackingNumber: null,
            LabelUrl: null,
            Success: false,
            ErrorMessage: "Carrier API unavailable (test stub)"));
}
```

**Step 4: Create a dedicated test fixture with its own xUnit collection:**

```csharp
[CollectionDefinition(Name)]
public class LabelFailureTestCollection : ICollectionFixture<LabelFailureTestFixture>
{
    public const string Name = "Label Failure Tests";
}

public class LabelFailureTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("fulfillment_failure_test_db") // Different DB name from main fixture
        .WithName($"fulfillment-failure-postgres-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.ConfigureMarten(opts => opts.Connection(connectionString));
                services.DisableAllExternalWolverineTransports();

                // Swap the default stub for the always-failing version
                services.RemoveAll<ICarrierLabelService>();
                services.AddSingleton<ICarrierLabelService, AlwaysFailingCarrierLabelService>();
            });
        });
    }

    public async Task DisposeAsync()
    {
        // Standard DisposeAsync pattern — see TestFixture template
    }

    public IDocumentSession GetDocumentSession() =>
        Host.Services.GetRequiredService<IDocumentStore>().LightweightSession();

    public async Task CleanAllDocumentsAsync()
    {
        var store = Host.Services.GetRequiredService<IDocumentStore>();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();
    }

    public async Task<ITrackedSession> ExecuteAndWaitAsync<T>(T message, int timeoutSeconds = 15)
        where T : class
    {
        return await Host.TrackActivity(TimeSpan.FromSeconds(timeoutSeconds))
            .DoNotAssertOnExceptionsDetected()
            .AlsoTrack(Host)
            .ExecuteAndWaitAsync(async ctx => await ctx.InvokeAsync(message));
    }
}
```

**Step 5: Write failure-path tests bound to the dedicated collection:**

```csharp
[Collection(LabelFailureTestCollection.Name)] // NOT the main collection
public class LabelGenerationFailureTests : IAsyncLifetime
{
    private readonly LabelFailureTestFixture _fixture;

    public LabelGenerationFailureTests(LabelFailureTestFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GenerateShippingLabel_WhenCarrierFails_AppendsLabelGenerationFailed()
    {
        var shipmentId = await SeedShipmentReadyForLabel();

        await _fixture.ExecuteAndWaitAsync(new GenerateShippingLabel(shipmentId));

        using var session = _fixture.GetDocumentSession();
        var shipment = await session.Events.AggregateStreamAsync<Shipment>(shipmentId);
        shipment!.Status.ShouldBe(ShipmentStatus.LabelGenerationFailed);
    }
}
```

### Why a Separate xUnit Collection?

xUnit collections share a single fixture instance. Injecting `AlwaysFailingCarrierLabelService`
into the main fixture would break every label-related test in every class in that collection.

A separate `[CollectionDefinition]` creates a fully isolated fixture with its own Postgres
container and DI configuration. The cost is a second container startup, acceptable for a
small targeted set of failure-path tests.

### Decision Guide

| Situation | Approach |
|---|---|
| Main stub "never fails"; need failure-path coverage | Separate fixture + separate xUnit collection |
| Stub has a conditional failure mode (toggle flag) | `RemoveAll + AddSingleton` within main fixture per test class |
| Time advancement needed across tests | `FrozenSystemClock` singleton on main fixture; reset in `InitializeAsync()` |

**CritterSupply reference:**
- `src/Fulfillment/Fulfillment/Shipments/ICarrierLabelService.cs`
- `tests/Fulfillment/Fulfillment.Api.IntegrationTests/AlwaysFailingCarrierLabelService.cs`
- `tests/Fulfillment/Fulfillment.Api.IntegrationTests/LabelFailureTestFixture.cs`
- `tests/Fulfillment/Fulfillment.Api.IntegrationTests/Shipments/LabelGenerationFailureTests.cs`

**Reference:** [Fulfillment Remaster S3 Retrospective — Part 1A](../../planning/milestones/fulfillment-remaster-s3-retrospective.md)

---

## Session Bookend

```bash
dotnet build
# Required: 0 errors, 17 warnings (unchanged)
```

## Commit

```
skills: integration-messaging — Lesson 16 (dual-publish migration); dead consumer grep step; CONTEXTS.md guidance updated to v1.1
skills: critterstack-testing-patterns — ISystemClock/FrozenSystemClock pattern; injectable failure stub + separate xUnit collection pattern
```

## Role

**@PSA — Principal Software Architect**
Precision edits to large files. Read the current file content before each change to confirm
the exact insertion point. The checklist update in Track C expands 2 items into 3 — verify
the final list has 9 numbered items.

**@DOE — Documentation Engineer**
Verify all cross-reference links use relative paths. The `FrozenSystemClock` and
`LabelFailureTestFixture` examples should be consistent with the actual implementations
in `tests/Fulfillment/Fulfillment.Api.IntegrationTests/`.
