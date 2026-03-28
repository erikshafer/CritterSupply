# gRPC Viability Evaluation — PSA-Led Technical Assessment

**Date:** 2026-03-28  
**Status:** Research Complete  
**Lead Reviewer:** Principal Software Architect  
**Secondary Reviewer:** UX Engineer  
**Scope:** Evaluation only — no implementation

> **Assumption for this evaluation:** Wolverine gRPC support is treated as available. The question is not whether CritterSupply can support gRPC, but whether it should, and where it earns its complexity.

---

## Documents Reviewed

- `CLAUDE.md`
- `CONTEXTS.md`
- `docs/planning/CURRENT-CYCLE.md`
- `docs/skills/wolverine-message-handlers.md`
- `docs/planning/spikes/pricing-promotions-domain-spike.md`
- `docs/planning/backoffice-research-discovery.md`
- `docs/planning/milestones/m32-3-session-7-retrospective.md`
- `docs/planning/fulfillment-evolution-plan.md`

---

## PSA Assessment

### 1. Viability verdict

**Conditional yes — gRPC has a meaningful place in CritterSupply, but only for a few internal synchronous seams.**

CritterSupply should **not** add gRPC broadly or treat it as a replacement for RabbitMQ-driven workflows. It does make sense where the codebase already has a clean request/response seam, where the caller needs an answer inside the current request, and where the interaction is narrow enough that protobuf contracts and a second transport would pay for themselves.

Why this is viable in this repository:

- CritterSupply already keeps transport concerns at the edges and keeps Wolverine handlers focused on pure business logic. The handler model does not care whether a downstream client is backed by HTTP or gRPC (`docs/skills/wolverine-message-handlers.md`).
- The platform already has service discovery and resilient client defaults in `src/CritterSupply.ServiceDefaults/Extensions.cs`, so synchronous service-to-service calls are already a supported pattern.
- The repository explicitly treats interface-first client abstractions as the standard and already notes future flexibility to swap transports in `docs/planning/milestones/m32-3-session-7-retrospective.md`.

### 2. Top candidates

#### Candidate 1 — Shopping → Promotions

**Verdict:** Best first candidate.

**Communication need**

- Shopping needs synchronous coupon validation and discount calculation during cart operations.
- Current code uses `IPromotionsClient` from Shopping handlers and endpoints:
  - `src/Shopping/Shopping/Cart/ApplyCouponToCart.cs`
  - `src/Shopping/Shopping.Api/Clients/PromotionsClient.cs`
- Promotions already exposes focused query surfaces:
  - `src/Promotions/Promotions.Api/Queries/ValidateCoupon.cs`
  - `src/Promotions/Promotions.Api/Queries/CalculateDiscount.cs`

**Why async messaging is a poor fit**

- Coupon validity is a customer-facing decision that must be known immediately.
- The Pricing/Promotions spike is explicit that async coupon validation would defer failure to checkout and create avoidable cart abandonment risk (`docs/planning/spikes/pricing-promotions-domain-spike.md`).

**How gRPC would fit the Wolverine model**

- Keep `ApplyCouponToCart` and its Wolverine endpoint exactly as they are.
- Replace the HTTP-backed `IPromotionsClient` implementation with a gRPC-backed one.
- On the Promotions side, expose gRPC methods that terminate in Wolverine handlers the same way HTTP endpoints do today.

**Blast radius**

- Low.
- Touches one client interface seam, one client implementation, Shopping DI registration, and two Promotions query entry points.

#### Candidate 2 — Shopping → Pricing

**Verdict:** Strong second candidate.

**Communication need**

- Shopping performs a synchronous authoritative price lookup when adding an item to the cart:
  - `src/Shopping/Shopping/Cart/AddItemToCart.cs`
  - `src/Shopping/Shopping.Api/Clients/PricingClient.cs`
- Pricing exposes a focused query endpoint for that lookup:
  - `src/Pricing/Pricing.Api/Pricing/GetPrice.cs`

**Why async messaging is a poor fit**

- Add-to-cart needs a server-authoritative price in the current request.
- ADR 0017 explicitly chose a synchronous call at add-to-cart and explicitly rejected moving that synchronous dependency into checkout/order orchestration (`docs/decisions/0017-price-freeze-at-add-to-cart.md`).

**How gRPC would fit the Wolverine model**

- Keep the Shopping handler and `IPricingClient` abstraction.
- Swap the transport implementation behind `IPricingClient`.
- Add a gRPC endpoint on the Pricing side that routes into the existing Wolverine-style handler flow.

**Blast radius**

- Low.
- One narrow query seam, one client, one DI registration block, and one upstream consumer path.

#### Candidate 3 — Backoffice → selected domain BCs (phase only after evidence)

**Verdict:** Possible, but only as a measured pilot — not a first-wave decision.

**Communication need**

- Backoffice is a BFF that composes data and proxies commands across multiple BCs:
  - `src/Backoffice/Backoffice.Api/Program.cs`
  - `src/Backoffice/Backoffice.Api/CustomerService/GetCustomerServiceView.cs`
  - `src/Backoffice/Backoffice.Api/OrderManagement/GetOrderDetailView.cs`
  - `src/Backoffice/Backoffice.Api/Clients/InventoryClient.cs`
  - `src/Backoffice/Backoffice.Api/Clients/PricingClient.cs`
- `CONTEXTS.md` shows Backoffice querying and commanding Orders, Returns, Product Catalog, Pricing, Inventory, Fulfillment, Correspondence, and Customer Identity.

**Why async messaging is a poor fit**

- These are interactive admin workflows. Search, detail views, stock checks, and adjustment confirmations need in-request answers.
- Backoffice is a BFF composition problem, not a saga/orchestration problem.

**Why this is not a first-wave recommendation**

- The current pain may be transport overhead, but it may also be chatty composition or missing BFF-owned read models.
- DX cost is materially higher here because auth propagation, testing, and operational surface area all expand at once.

**How gRPC would fit the Wolverine model**

- Keep Wolverine endpoints and Backoffice client interfaces unchanged.
- If pursued, pilot one or two internal client seams first, likely inventory- or order-adjacent reads where response time matters most.

**Blast radius**

- Medium to high.
- Multiple BCs, auth concerns, larger test surface, and broader observability impact.

### 3. Integration sketch

#### Shopping ↔ Promotions

- **Client side:** `ApplyCouponToCart` continues to call `IPromotionsClient`.
- **Transport:** Replace `PromotionsClient` HTTP calls with gRPC client calls.
- **Server side:** Promotions adds gRPC methods for `ValidateCoupon` and `CalculateDiscount`, each landing in the same Wolverine message-handling model used by current HTTP endpoints.

#### Shopping ↔ Pricing

- **Client side:** `AddItemToCart` continues to call `IPricingClient`.
- **Transport:** Replace `PricingClient` HTTP implementation with gRPC.
- **Server side:** Pricing exposes a gRPC method for price lookup and routes it into the existing Wolverine handler flow.

#### Backoffice pilot

- **Client side:** Keep existing client interfaces such as `IInventoryClient`, `IOrdersClient`, and `ICustomerIdentityClient`.
- **Transport:** Add a gRPC-backed implementation only for the one or two seams being piloted.
- **Server side:** Domain BCs expose internal gRPC endpoints while preserving current HTTP endpoints during adoption.

### 4. Risks and tradeoffs

#### Operational complexity

CritterSupply currently has an HTTP-first shared platform. Service discovery and resilience are already wired for `HttpClient` in `src/CritterSupply.ServiceDefaults/Extensions.cs`, while gRPC client instrumentation is still commented out. That means gRPC is not a zero-cost swap; observability and operational defaults would need deliberate work.

#### Second transport tax

Adding gRPC introduces a second transport story into a reference architecture that is deliberately approachable. That cost is acceptable only if the number of adopted seams stays small and the repository clearly explains why those seams deserve gRPC while others do not.

#### Schema coupling and debugging ergonomics

Protobuf contracts can improve clarity for narrow internal seams, but they also add another contract artifact and another debugging mode. For a learning-oriented reference repo, that is only worth it where the seam is stable, narrow, and obviously request/response in nature.

#### Backoffice may have a design problem, not a transport problem

Backoffice fan-out queries may benefit from gRPC, but transport alone will not fix chatty BFF composition. Before piloting gRPC there, the team should confirm that latency and reliability issues are coming from transport overhead rather than from too many synchronous downstream calls.

### 5. Recommendation

**Recommendation: pursue gRPC only as a narrow, internal reference implementation.**

Recommended scope:

1. **First:** Shopping → Promotions
2. **Second:** Shopping → Pricing
3. **Optional later pilot:** one narrowly chosen Backoffice downstream seam, only after measuring actual pain

What not to do:

- Do **not** replace RabbitMQ workflows with gRPC.
- Do **not** start with Storefront or browser-facing edges.
- Do **not** convert Backoffice broadly without proof that transport is the bottleneck.

If CritterSupply wants to validate Wolverine gRPC support in a real repo, **Shopping → Promotions is the right showcase**: it is synchronous by business need, already abstracted behind an interface, and small enough to keep the reference architecture understandable.

---

## UX / DX Review

### UXE response to PSA assessment

The PSA recommendation is directionally sound, but the DX cost needs to be taken more seriously.

#### Specific concerns / questions

1. **The platform is still HTTP-first.**  
   Shared defaults in `src/CritterSupply.ServiceDefaults/Extensions.cs` are built around `HttpClient`, and gRPC instrumentation is not turned on yet. The team should not describe gRPC as a drop-in transport swap without budgeting for observability, retries, and debugging work.

2. **Backoffice is not yet proven to be a transport problem.**  
   The current Backoffice fan-out flows in `GetCustomerServiceView` and `GetOrderDetailView` may be better solved by reducing synchronous composition or adding read models, not by introducing a second transport.

3. **Shopping seams are good candidates, but the value should still be explicit.**  
   Shopping → Promotions and Shopping → Pricing are structurally clean, but the team should still be clear about what it expects to gain: performance validation, contract rigor, or Wolverine feature coverage. “It maps cleanly to RPC” is not enough by itself.

#### Where UXE agrees

- RabbitMQ-driven workflows should remain as-is.
- Shopping → Promotions is the cleanest candidate.
- Browser-facing surfaces should not be the first place CritterSupply introduces gRPC.

#### Overall UXE read

**Sound recommendation, but narrower than the PSA initially implies.**  
The UX/DX-safe move is to pilot gRPC on **Shopping → Promotions** first, optionally add **Shopping → Pricing** second, and treat any Backoffice rollout as a separate decision that requires measured evidence.

---

## Documentation gap noted during evaluation

- No repository-local gRPC skill or transport strategy document currently exists.
- If CritterSupply adopts even a small gRPC footprint, a focused guidance document should be added so future contributors understand:
  - when to prefer gRPC vs HTTP vs RabbitMQ,
  - how Wolverine handlers are expected to sit behind gRPC endpoints,
  - how local development, tracing, and tests should work for gRPC-enabled paths.
