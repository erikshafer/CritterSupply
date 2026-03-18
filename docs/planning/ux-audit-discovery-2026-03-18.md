# UX Audit & Discovery — 2026-03-18

## Scope and inputs reviewed

- `CLAUDE.md`
- `CONTEXTS.md`
- `docs/planning/CURRENT-CYCLE.md`
- `docs/planning/milestones/m32-0-retrospective.md` (canonical M32.0 retrospective)
- `docs/planning/milestones/m32.1-retrospective.md` (canonical M32.1 retrospective)
- Bounded context roots under `src/`
- Product Catalog migration/discovery drafts:
  - `docs/planning/catalog-listings-marketplaces-cycle-plan.md`
  - `docs/planning/catalog-listings-marketplaces-evolution-plan.md`
  - `docs/planning/catalog-listings-marketplaces-discovery.md`
  - `src/Product Catalog/ProductCatalog/Products/Product.cs`

## Discovery summary

Recent work is concentrated in **Backoffice** (formerly Admin Portal), with M32.1 emphasizing endpoint closure, WASM shell, and E2E infrastructure over full operator write UX. Product Catalog is still document-store based today, while roadmap docs position event sourcing as a dependency for richer operator-facing auditability and listing/marketplace workflows.

## Prioritized UX opportunities

| Priority | Area | Current state | Opportunity | Why it matters | Complexity | Gap type | Backend dependencies |
|---|---|---|---|---|---|---|---|
| P0 | Backoffice authorization consistency | `Alerts.razor` authorizes `warehouse-manager` while app policies define `warehouse-clerk` role language | Align role vocabulary across route attributes, policies, and JWT role assumptions | Prevents silent access failures for operations users | Low | New discovery | Backoffice.Web auth attributes/policies; BackofficeIdentity role mapping/tests |
| P0 | Backoffice Alerts workflow completeness | Alert feed is visible, but no in-list acknowledge action despite API support | Add acknowledge CTA, unread/acknowledged states, and optimistic interaction | Converts passive feed into actionable operations workflow | Medium | Known gap + new mismatch | `Backoffice.Api` acknowledge endpoint/projection fields; optional event for ack state push |
| P0 | Session-expiry and recovery UX | Errors mostly surfaced by snackbars; no durable session-expired recovery overlay | Implement modal/session-expired state with return-to-context behavior | Reduces confusion and task loss in long-running internal workflows | Medium | Known gap | BackofficeAuthService + token refresh behavior; standardized 401 handling contract |
| P0 | Network/conflict/error state standards | UX standards doc defines persistent banners and conflict handling, but pages still lean on transient snackbars | Apply shared error-state patterns (network banner, 409 conflict banner, retry affordances) | Better operator trust and lower repeated actions in async flows | Medium | Known gap | Consistent ProblemDetails/error codes from Backoffice API and composition endpoints |
| P1 | Backoffice navigation dead ends | UI links navigate to routes not yet implemented (e.g., `/customers/{id}`, `/admin`) | Gate hidden routes, add placeholder route shells, or disable links with clear status copy | Prevents users hitting dead ends during live tasks | Low | New discovery | Route registration and role-aware nav guards in Backoffice.Web |
| P1 | Async freshness visibility (cross-BC) | Manual refresh exists, but data freshness is implicit in event-driven views | Add "last synced", "updating", and delayed-update messaging on composed dashboards/feeds | Clarifies eventual consistency and reduces duplicate operator actions | Medium-High | Known gap | Projection metadata (`GeneratedAt`/`LastUpdated`), BFF response timestamps, SignalR correlation hooks |
| P1 | Product Catalog ES history/audit UX | Product Catalog is currently Marten document model without event-history UI | Design and ship Product History tab with significance filters, actor/time/cause, and diff display | Core trust benefit of event sourcing is operator-visible accountability | High | Known gap | Product Catalog ES migration (events + projections) must land first |
| P1 | Product discontinuation/review safety | High-impact catalog actions lack pre-flight impact UX | Add pre-flight modal ("X listings across Y marketplaces will be force-ended") + grouped post-action notification | Prevents panic, accidental destructive actions, and support burden | High | Known gap | Listings/Marketplaces count query and grouped notification support |
| P1 | Migration artifact hygiene (`ProductMigrated`) | Plans identify bootstrap event noise risk in history views | Explicitly classify/filter system migration events from default user history | Maintains clarity and avoids trust erosion during transition | Medium | Known gap | Projection classification (`System` significance) in catalog history read model |
| P2 | Operator vs customer language consistency | BC boundaries are strong, but terminology/copy drift risk rises across Backoffice + Storefront | Establish shared term glossary and copy checks for role-specific surfaces | Reduces cognitive load, training cost, and support mistakes | Low | New discovery | Mostly governance; minor DTO/display-label normalization |
| P2 | Catalog/Listings startup/backfill UX | Planning flags empty/stale projection windows during transition | Add explicit bootstrap/backfill admin flow and empty-state messaging | Avoids ambiguous "missing data" interpretation during rollout | Medium | Known gap | Backfill endpoint/job and projection readiness telemetry |

## Product Catalog: target UX for full event sourcing

The minimum viable operator UX for a fully event-sourced catalog should include:

1. **History tab with significance filter**
   - Default: significant events only
   - Optional: all events
   - Includes who changed what, when, and (if provided) why

2. **Cause capture on edits**
   - Mutation forms should require or strongly encourage rationale for sensitive updates (price-affecting fields, discontinuations)
   - Rationale should be queryable in history/audit views

3. **Version-aware comparisons**
   - Field-level before/after comparisons for description/name/variant/status changes
   - Human-readable labels instead of raw event names

4. **Safety rails for cascading operations**
   - Pre-flight impact summaries before destructive/cascade operations
   - Grouped, comprehensible post-action notifications

5. **Trust and consistency signals**
   - "History updating…" optimistic status after writes
   - Last-processed timestamp and eventual-consistency hinting
   - Exclusion of migration/system noise by default

## Notes on dependencies and sequencing

- Several P1 Catalog UX items are **blocked** until Product Catalog ES migration introduces event streams and dedicated history projections.
- Cross-BC operator confidence patterns (freshness indicators, grouped cascade notifications) require coordination between Backoffice BFF and upstream BC read models/events.
- Backoffice P0 fixes (auth consistency, acknowledgment UX, recovery states) can be delivered independently and should be addressed first.
