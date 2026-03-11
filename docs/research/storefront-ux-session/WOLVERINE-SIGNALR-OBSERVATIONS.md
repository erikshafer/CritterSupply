# Wolverine + SignalR: Observations and Open-Source Contribution Notes

> **Audience:** CritterSupply engineers and future WolverineFx.SignalR contributors.
>
> This document captures practical observations, API gaps, and behavior surprises discovered
> while implementing customer-scoped SignalR event delivery in the Storefront BFF.
> These findings are candidates for upstream issues/PRs against [JasperFx/wolverine](https://github.com/JasperFx/wolverine).

---

## Context

CritterSupply's Storefront BFF uses Wolverine's SignalR transport to push real-time events
(cart updates, order status, shipment status) to individual customers via the Blazor web app.
During Cycle 19, we fixed a **P0 security bug** where `opts.Publish(...).ToSignalR()` was
broadcasting all events to every connected client — regardless of which customer's cart or order
it belonged to. Fixing this led to several non-obvious discoveries about the Wolverine.SignalR API.

**Package version tested:** `WolverineFx.SignalR` 5.17.0

---

## Observation 1: `opts.Publish(...).ToSignalR()` Broadcasts to ALL Connected Clients

### What We Expected

`opts.Publish(x => { x.MessagesImplementing<IStorefrontWebSocketMessage>(); x.ToSignalR(); })`
combined with hub `OnConnectedAsync` group enrollment (e.g., `Groups.AddToGroupAsync(connectionId, "customer:{id}")`)
would automatically scope delivery to the appropriate group based on the `CustomerId` property
on the message.

### What Actually Happens

`.ToSignalR()` calls `IHubContext<T>.Clients.All.SendAsync(...)` — it sends to **every connected
client** on the hub, regardless of group membership. The hub's group enrollment in `OnConnectedAsync`
is irrelevant to this broadcast path.

### Why This Matters

This is a **serious security gap** in the most common configuration: developers who follow the
pattern of enrolling connections into groups in `OnConnectedAsync` naturally expect that the
publish rule respects those groups. The documentation does not clearly state that `.ToSignalR()`
broadcasts to all, not to message-appropriate groups.

### The Fix

Return `SignalRMessage<T>` from the handler using `.ToWebSocketGroup("group-name")`:

```csharp
// ❌ WRONG — broadcasts CartUpdated to ALL connected customers
public static CartUpdated Handle(ItemAdded message) =>
    new CartUpdated(message.CartId, message.CustomerId, ...);

// ✅ CORRECT — scopes CartUpdated only to the customer who owns the cart
public static SignalRMessage<CartUpdated> Handle(ItemAdded message)
{
    var cartUpdated = new CartUpdated(message.CartId, message.CustomerId, ...);
    return cartUpdated.ToWebSocketGroup($"customer:{message.CustomerId}");
}
```

### Potential Upstream Contribution

- **Documentation PR:** Add a prominent "⚠️ Security Note" in the Wolverine SignalR docs
  clarifying that `.ToSignalR()` in publish rules broadcasts to all clients and does NOT
  respect hub group membership. Point to `.ToWebSocketGroup()` as the group-targeting API.

- **API improvement consideration:** Could Wolverine automatically route to a named group
  by convention — e.g., if a message has a `GroupName` property, default to group targeting?
  Or add a fluent option: `.ToSignalR(groupBy: msg => $"customer:{msg.CustomerId}")`.

---

## Observation 2: Mismatch Between "Return Type" and "Wolverine Tracking" for SignalR Messages

### What We Expected

When a handler returns `CartUpdated` directly (not group-scoped), Wolverine tracking records a
`CartUpdated` in `tracked.Sent`. When changed to return `SignalRMessage<CartUpdated>`, we expected
`tracked.Sent.MessagesOf<CartUpdated>()` to still work (Wolverine would "unwrap" the wrapper).

### What Actually Happens

`tracked.Sent.MessagesOf<CartUpdated>()` returns **empty** when the handler returns
`SignalRMessage<CartUpdated>`. The tracking API records the **wrapper type** `SignalRMessage<CartUpdated>`,
not the inner payload type. To retrieve the message in tests, you must use:

```csharp
// ❌ Does not work after switching to ToWebSocketGroup
tracked.Sent.MessagesOf<CartUpdated>(); // Returns empty

// ✅ Correct — look for the wrapper type
var messages = tracked.Sent.MessagesOf<SignalRMessage<CartUpdated>>();
var payload = messages.Single().Message; // Access inner CartUpdated
```

This means **all existing Wolverine tracking tests** that tested handler return types need to be
updated when switching from direct returns to group-scoped SignalR returns.

### Potential Upstream Contribution

- **Documentation PR:** Add a test patterns section showing how to assert on group-scoped
  SignalR messages using `MessagesOf<SignalRMessage<T>>()` and `signalRMessage.Message`.

- **API improvement consideration:** `ITrackedSession.Sent.MessagesOf<T>()` could be made
  "SignalR-aware" — unwrapping `SignalRMessage<T>` transparently so existing assertions
  don't break when switching from broadcast to group-targeted delivery.

---

## Observation 3: `SignalRMessage<T>.Locator` Is an Internal Type

### Behavior

`SignalRMessage<T>` exposes two public properties:
- `Message` — the inner payload (of type T)
- `Locator` — the routing target (returns `Wolverine.SignalR.Internals.WebSocketRouting+ILocator`)

`ILocator` is an **internal interface** with a single method `Find`. The concrete implementation
(`WebSocketRouting+Group`) has a public `GroupName` property, but it is not accessible through
the interface.

To access the group name in test code, you must either:
1. Use **reflection** to access `GroupName` on the concrete implementation
2. Use `signalRMessage.Locator.ToString()` which returns `"Group=customer:{id}"` (usable for `ShouldContain` assertions)
3. Not assert on the group name at all (assert only that `SignalRMessage<T>` was produced)

We chose option 2 for CritterSupply's integration tests as a pragmatic middle ground.

### Example

```csharp
// Current approach in CritterSupply tests:
var signalRMessage = tracked.Sent.MessagesOf<SignalRMessage<CartUpdated>>().Single();
// Locator.ToString() returns "Group=customer:{guid}"
signalRMessage.Locator.ToString()!.ShouldContain($"customer:{customerId}");
// Inner payload
signalRMessage.Message.CartId.ShouldBe(cartId);
```

### Potential Upstream Contribution

- **API improvement:** Expose `GroupName` via a public property on `SignalRMessage<T>` itself:
  ```csharp
  public sealed class SignalRMessage<T>
  {
      public T Message { get; }
      public string? GroupName { get; } // NEW — null if broadcast, group name if group-targeted
      // existing Locator kept for backward compat
  }
  ```
  This would make the group targeting contract testable without reflection or `ToString()` parsing.

- **Alternatively:** Promote `ILocator` to a public interface with a `string GroupName { get; }`
  contract so consumer code can type-check and assert cleanly.

---

## Observation 4: Hub Group Management and Publish Rules Are Orthogonal Concerns

### Clarification for Architecture Documentation

Wolverine SignalR has two independent concepts that are easy to conflate:

| Concept | Where | Purpose |
|---------|-------|---------|
| Hub group enrollment (`Groups.AddToGroupAsync`) | `OnConnectedAsync` | Organizes connections into named groups for future targeted sends |
| Publish rule target (`.ToSignalR()` vs `.ToWebSocketGroup()`) | Handler return / publish rules | Controls WHERE outgoing messages are delivered |

These two are **not connected**. Group enrollment only matters if a sender explicitly targets that
group. `.ToSignalR()` sends to `Clients.All` regardless of any group enrollment.

**Rule of thumb for CritterSupply:**
- `OnConnectedAsync` group enrollment → correct, keep it (used by group-targeted sends)
- `opts.Publish(x.ToSignalR())` global publish rule → fine for truly global broadcasts only
- Per-customer events → must use `.ToWebSocketGroup($"customer:{customerId}")` in handler return

---

## Observation 5: Domain Project Dependency on WolverineFx.SignalR

### Problem Encountered

The `Storefront` domain project (a plain `Microsoft.NET.Sdk` project, no Web SDK) initially had
no `WolverineFx.SignalR` reference. The notification handlers lived there and needed `ToWebSocketGroup`.

### Options Evaluated

1. **Add `WolverineFx.SignalR` to the domain project** — simplest fix; the project already
   defines `IStorefrontWebSocketMessage` and `StorefrontEvent` types that are tightly coupled
   to SignalR concepts, so the transport dependency is arguably justified.

2. **Move notification handlers to the API project** — cleanest from a pure domain/infra
   separation standpoint, but requires significant structural refactoring.

3. **Use `IHubContext<T>` directly in the API project** — bypasses Wolverine's transport,
   loses tracking and outbox guarantees.

### Decision

We chose **Option 1** for CritterSupply. The `Storefront` domain project already contains
SignalR-coupled types (`IStorefrontWebSocketMessage`, `CartUpdated`, etc.), making the
transport dependency pragmatic rather than a violation of clean architecture.

**For future BFFs:** design the notification handler layer in the API project from the start
if SignalR group targeting is needed, to keep the domain project transport-free.

---

## Summary: Recommended Upstream Issues / PRs

| # | Type | Description | Priority |
|---|------|-------------|----------|
| 1 | Docs | Document that `.ToSignalR()` broadcasts to ALL clients (not group-scoped) with security warning | 🔴 High |
| 2 | Docs | Add test patterns for asserting on `SignalRMessage<T>` wrapper in `ITrackedSession.Sent` | 🟡 Medium |
| 3 | API | Expose `GroupName` as a public property on `SignalRMessage<T>` | 🟡 Medium |
| 4 | Docs | Clarify that hub group enrollment and publish rule targeting are orthogonal concerns | 🟡 Medium |
| 5 | API | Consider adding fluent group-by option to publish rules: `.ToSignalR(groupBy: msg => ...)` | 🟢 Enhancement |

---

*Discovered during CritterSupply Cycle 19 — Storefront UX Improvements. WolverineFx.SignalR 5.17.0.*

---

## Observation 6: `SignalRMessage<T>` Returned from Handlers Is NOT Tracked in `ITrackedSession.Sent` When Transport Is Disabled

### Behavior

When `DisableAllExternalWolverineTransports()` is called in test setup (the standard Alba test pattern),
the SignalR transport is disabled alongside RabbitMQ and other external transports.

When a handler returns `SignalRMessage<T>` (from `.ToWebSocketGroup()`), Wolverine routes it through
the SignalR transport. With the transport disabled, the message is **not recorded** in
`ITrackedSession.Sent` — it is dropped silently. As a result:

```csharp
// ❌ Always returns empty when SignalR transport is disabled in tests
var tracked = await fixture.Host.InvokeMessageAndWaitAsync(message);
var published = tracked.Sent.MessagesOf<SignalRMessage<CartUpdated>>();
published.ShouldNotBeEmpty(); // FAILS — published is empty
```

This caused 7 failing integration tests in CI after switching handlers from raw `T` to
`SignalRMessage<T>` returns.

Contrast with the old pattern (raw `T` + publish rule routing to SignalR):

```csharp
// ✅ Worked with the old approach (raw T returned, publish rule routes to stubbed transport)
var tracked = await fixture.Host.InvokeMessageAndWaitAsync(message);
var published = tracked.Sent.MessagesOf<CartUpdated>();
published.ShouldNotBeEmpty(); // PASSES — raw T is tracked before transport delivery
```

### Why They Behave Differently

When a handler returns raw `T` and a publish rule routes it to SignalR, Wolverine records the
outgoing message as an envelope in `Sent` **before** attempting to deliver to the (stubbed) transport.

When a handler returns `SignalRMessage<T>`, Wolverine processes this as a direct SignalR transport
operation. The SignalR transport, when disabled, appears to discard the message without recording it
in the tracking session.

### Recommended Approach in Tests

Call the static handler method directly and assert on the return value:

```csharp
// ✅ Correct approach for testing SignalRMessage<T> handler return values
var result = await ItemAddedHandler.Handle(message, shoppingClient, CancellationToken.None);

result.ShouldNotBeNull();
result!.Locator.ToString()!.ShouldContain($"customer:{customerId}"); // group assertion
result.Message.CartId.ShouldBe(cartId);                              // payload assertion
```

This directly tests handler logic (correct group name, correct payload) without depending on
Wolverine's transport tracking infrastructure.

### Potential Upstream Contribution

- **Bug report / docs:** `DisableAllExternalWolverineTransports()` disables the SignalR transport,
  causing `SignalRMessage<T>` returned from handlers to be silently dropped from `ITrackedSession.Sent`.
  This is surprising behavior and should be documented or fixed so that stubbed SignalR transport
  still records messages in the tracking session (consistent with how raw `T` + publish rule behaves).

- **API improvement:** `ITrackedSession.Sent` should capture `SignalRMessage<T>` envelopes even
  when the SignalR transport is disabled in test mode, for parity with how other stubbed transports
  are tracked.

*Discovered during CritterSupply Cycle 19 CI failure investigation.*
