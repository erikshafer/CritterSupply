# bUnit Component Testing — Retrospective

**Date:** 2026-03-11
**Scope:** Storefront.Web Blazor component testing with bUnit
**Participants:** QA Engineer (Agent)

---

## Summary

Introduced bUnit v2 component-level unit testing for the Storefront.Web Blazor Server project. This fills a gap between the existing API integration tests (Alba + TestContainers) and browser E2E tests (Playwright + Reqnroll), providing millisecond-fast feedback on component rendering logic.

## What Was Done

### Infrastructure
- Added `bunit 2.6.2` to `Directory.Packages.props` (central package management)
- Created `Storefront.Web.Tests` project (`Microsoft.NET.Sdk.Razor`, xUnit, Shouldly)
- Added project to `CritterSupply.slnx` solution under `Customer Experience`

### Tests Written (44 total, all passing)

| Component        | Tests | Key Patterns Exercised                                     |
|------------------|-------|------------------------------------------------------------|
| **Counter**      | 5     | Basic rendering, click events, state changes               |
| **Home**         | 8     | Static content, CSS class selectors, link href assertions  |
| **NotFound**     | 4     | Static content, navigation links                           |
| **Login**        | 8     | MudForm rendering, field labels, demo account info         |
| **Account**      | 4     | Auth state emulation, claim-based rendering, loading state |
| **OrderHistory** | 5     | MudTable + MudPopoverProvider, status chips, data display  |
| **Products**     | 10    | Mocked HttpClient, async loading, empty/populated states   |

### Skill File
- Created `docs/skills/bunit-component-testing.md` (comprehensive guide)
- Updated `CLAUDE.md` skill invocation guide to include bUnit reference

## What Went Well

1. **bUnit v2 API is clean** — `BunitContext` base class, strongly-typed `Render<T>()`, and `AddAuthorization()` make tests concise
2. **MudBlazor compatibility** — After discovering the `MudPopoverProvider` and `IAsyncLifetime` requirements, the setup is straightforward and reusable via the `BunitTestBase` base class
3. **Speed** — 44 tests execute in under 1 second (vs. 30+ seconds for Playwright E2E), enabling fast TDD cycles
4. **MockHttpMessageHandler pattern** — Works well for testing components that call downstream APIs, with per-path response configuration

## What Was Challenging

1. **MudPopoverProvider requirement** — MudBlazor v9+ requires a `MudPopoverProvider` in the component tree for popover-based controls (MudSelect, MudMenu, MudTable). This isn't documented in MudBlazor's getting-started guide and required trial-and-error. The `RenderTree.TryAdd<>()` approach doesn't work because `MudPopoverProvider` lacks a `ChildContent` parameter — pre-rendering separately is the correct pattern.

2. **IAsyncDisposable / IAsyncLifetime** — MudBlazor 9+ registers services like `PointerEventsNoneService` that only implement `IAsyncDisposable`. xUnit's synchronous `Dispose()` throws. The fix is implementing `IAsyncLifetime` on the test base class and calling `base.DisposeAsync()`.

3. **Currency formatting in CI** — `ToString("C")` produces `$129.99` locally (en-US) but `¤129.99` in CI environments (C.UTF-8 locale). Assertions should check numeric values only, not formatted currency strings.

4. **bUnit v1 → v2 API changes** — Many online examples use v1 APIs (`AddTestAuthorization`, `RenderComponent`). In v2, these are `AddAuthorization()` and `Render<T>()` respectively. The bUnit migration guide is the canonical reference.

## Components Not Tested with bUnit (By Design)

Per ADR 0015, these components are better served by Playwright E2E tests:

| Component               | Reason                                                   |
|-------------------------|----------------------------------------------------------|
| **Checkout.razor**      | MudStepper JS interop, multi-step state machine          |
| **Cart.razor**          | SignalR WebSocket subscription, localStorage JS calls    |
| **InteractiveAppBar**   | SignalR subscription, `authHelper.logout` JS call        |
| **OrderConfirmation**   | SignalR status updates, `[JSInvokable]` callbacks        |
| **ReconnectModal**      | Pure JS interop component                                |

## Lessons for Other Blazor Projects

These patterns are directly applicable to the upcoming **Vendor Portal (Blazor WASM)** and any future Blazor projects:

1. **Always create a `BunitTestBase`** — Centralizes MudBlazor setup and avoids repeating boilerplate
2. **`JSInterop.Mode = JSRuntimeMode.Loose`** — Default for any MudBlazor project
3. **`IAsyncLifetime`** — Required whenever MudBlazor services are registered
4. **`RenderWithMud<T>()`** — Use for any component with popover-based controls
5. **Mock at the `IHttpClientFactory` level** — Cleanest pattern for testing API-calling components
6. **Avoid asserting on formatted strings** — Currency, dates, and other locale-dependent formatting will break in CI

## Recommendations for Next Steps

1. **Add bUnit to CI pipeline** — Include `Storefront.Web.Tests` in the standard `dotnet test` workflow (no additional infrastructure needed)
2. **Extend Products tests** — Add tests for authenticated user with cart (add-to-cart flow via mock API)
3. **Apply to Vendor Portal** — When `VendorPortal.Web` (Blazor WASM) development begins, start with this same base class pattern
4. **Consider property-based testing** — For components with parametric rendering (product cards, status chips), FsCheck + bUnit could verify rendering across all input ranges
