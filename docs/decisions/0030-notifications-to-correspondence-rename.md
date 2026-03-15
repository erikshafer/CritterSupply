# ADR 0030: Notifications BC Renamed to Correspondence BC

**Status:** ✅ Accepted

**Date:** 2026-03-13

**Cycle:** 28 (planning phase)

---

## Context

The bounded context originally named "Notifications" was intended to own all customer-facing transactional communication — order confirmations, shipping updates, delivery confirmations, return status changes, and refund notices. However, as the architecture matured and the team conducted detailed Event Modeling sessions, the name "Notifications" proved to be problematic for several reasons:

### Problems with "Notifications"

1. **Namespace collision with real-time UI updates:** The Customer Experience BC (Storefront) has a `Notifications/` folder containing 16 integration message handlers that broadcast events to the UI via SignalR. These are real-time, in-app notifications — fundamentally different from transactional emails and SMS messages. The name "Notifications" could refer to either concern, creating ambiguity in conversations and documentation.

2. **Technical jargon leakage:** "Notification" is a technical term that doesn't reflect the domain language. It's a generic software concept (push notifications, toast notifications, system notifications) rather than a business capability. E-commerce stakeholders talk about "customer communications," "transactional emails," "shipping confirmations," and "return updates" — not "notifications."

3. **Scope ambiguity:** The term "notification" is too broad. It could include marketing emails, promotional SMS campaigns, in-app alerts, browser push notifications, and even internal alerts for operations teams. The actual scope of this BC is narrower and more specific: **transactional correspondence triggered by business events**.

4. **Conceptual collision with future BCs:** A future Promotions BC will send marketing emails. A future Admin Portal might send internal alerts. These are all "notifications" in the generic sense, but they are not the responsibility of this BC. The generic name creates confusion about boundaries.

### Why "Correspondence" is Better

The term **Correspondence** comes from the e-commerce domain and has a precise meaning:

- **Dictionary definition:** "Communication by exchange of letters" — traditionally postal mail, now extended to email and SMS
- **E-commerce usage:** Amazon uses "Message Center" for customer correspondence. Shopify uses "Notifications" (confusingly) but distinguishes "transactional emails" from "marketing emails." The general industry pattern is "transactional communications" or "customer correspondence."
- **Domain fit:** Correspondence implies bidirectional, event-triggered, transactional communication — exactly what this BC does. It sends messages **in response to** business events (orders placed, shipments dispatched, returns approved).
- **Boundary clarity:** Correspondence excludes:
  - Real-time UI updates (Customer Experience BC via SignalR)
  - Marketing campaigns (future Promotions BC)
  - Internal operational alerts (future Admin Portal or Operations Dashboard)
  - System monitoring notifications (observability layer)

---

## Decision

Rename the Notifications bounded context to **Correspondence BC**.

- All references to "Notifications" as a BC name in architectural documentation (CONTEXTS.md, planning docs, ADRs) will be updated to "Correspondence."
- The folder `src/Customer Experience/Storefront/Notifications/` in the Customer Experience BC will remain unchanged — it contains integration message handlers for real-time UI updates, not correspondence logic. The name collision is resolved because "Notifications" is no longer a BC name.
- Integration events will follow the naming pattern:
  - Published events: `CorrespondenceQueued`, `CorrespondenceDelivered`, `CorrespondenceFailed`
  - Internal domain events: `MessageQueued`, `MessageDelivered`, `MessageFailed` (aggregate events)
- The BC folder structure will be: `src/Correspondence/Correspondence/` and `src/Correspondence/Correspondence.Api/`

---

## Rationale

### Alignment with Ubiquitous Language

Eric Evans (Domain-Driven Design) emphasizes that bounded context names should reflect the language of the domain experts, not technical abstractions. E-commerce stakeholders refer to "customer communications," "transactional emails," "shipping confirmations," and "return updates" — all forms of correspondence. The term "Correspondence" captures this family of concepts without being overly specific (like "Emails BC") or overly generic (like "Notifications BC").

### Namespace Clarity

By eliminating "Notifications" as a BC name, we avoid confusion with:
- The `Storefront/Notifications/` folder (real-time UI update handlers)
- Future "notification" features in other BCs (promotional emails, admin alerts)
- Generic software concepts (push notifications, toast messages)

### Precedent in CritterSupply Architecture

Other BCs in CritterSupply use domain-specific names rather than technical abstractions:
- **Shopping** (not "Carts" or "Pre-Purchase")
- **Fulfillment** (not "Shipping" or "Logistics")
- **Returns** (not "ReverseLogistics" or "Refunds")
- **Correspondence** fits this pattern — a domain-appropriate term that is neither too narrow nor too broad.

### Integration with Future BCs

- **Promotions BC** will own marketing email campaigns (discount codes, seasonal sales)
- **Correspondence BC** will own transactional emails triggered by business events
- **Customer Identity BC** already owns notification preferences (which channels the customer has opted into)
- **Backoffice BC** (future) may own internal operational alerts

Renaming Notifications → Correspondence establishes clear boundaries before these future BCs are implemented.

---

## Consequences

### Positive

- **Eliminates ambiguity:** "Correspondence" has a specific, domain-appropriate meaning
- **Clarifies scope:** Transactional, event-triggered customer communications — not real-time UI updates, not marketing campaigns
- **Aligns with ubiquitous language:** Matches how e-commerce stakeholders describe this capability
- **Prevents future collisions:** Distinct from future "notification" features in other BCs
- **Preserves existing code:** No changes required to `Storefront/Notifications/` folder (it's not a BC name anymore)

### Negative

- **Disrupts draft planning documents:** References to "Notifications BC" in CURRENT-CYCLE.md, retrospectives, and CONTEXTS.md must be updated
- **One-time update cost:** All architectural documentation referencing "Notifications" as a BC name must be reviewed and updated
- **Slightly longer name:** "Correspondence" has 14 characters vs "Notifications" has 13 characters (negligible impact)

### Neutral

- **No code changes required yet:** The BC has not been implemented (scheduled for Cycle 28), so this is a planning-only rename
- **Integration contracts remain the same:** The BC still receives the same 10 events and publishes the same 3 events (with renamed event types)

---

## Alternatives Considered

### Keep "Notifications BC"

**Rejected.** Creates persistent ambiguity with real-time UI updates and generic software concepts. Does not align with domain language.

### "Emails BC"

**Rejected.** Too narrow — excludes SMS, push notifications (future), and postal mail (far future, e.g., printed return labels). Also, the responsibility is not "owning email" but "managing transactional customer communications."

### "Messaging BC"

**Rejected.** Conflicts with RabbitMQ messaging infrastructure. "Messaging" is a technical term for asynchronous communication between services, not customer-facing communications.

### "Communications BC"

**Considered.** More accurate than "Notifications," but still generic. "Communications" could include:
- Internal team communications (Slack, Teams)
- API communications between services
- Customer support chat (future)

"Correspondence" is more specific: it implies formal, asynchronous, event-triggered messages to customers.

### "Transactional Emails BC"

**Rejected.** Too specific — excludes SMS and future channels. Also, "transactional emails" is an adjective-noun pair, not a domain concept name. CritterSupply BCs use single-word or compound nouns (Orders, Fulfillment, Shopping, CustomerIdentity).

---

## References

- CONTEXTS.md lines 3018-3083 — Original Notifications BC definition (to be updated)
- ADR 0004 — SSE over SignalR (discusses real-time notification patterns in Customer Experience BC)
- ADR 0013 — SignalR migration from SSE (references Customer Experience BC notification handling)
- Cycle 27 retrospective — Identifies "Notifications BC Phase 1" as P0 priority (will be updated to "Correspondence BC Phase 1")
- Event Modeling workshop (2026-03-13) — Planning session that surfaced naming collision

---

## Implementation Notes

The following documentation must be updated to reflect the rename:

1. **CONTEXTS.md** — Section starting at line 3018: rename "Notifications" → "Correspondence"
2. **docs/planning/CURRENT-CYCLE.md** — Update Cycle 28 references
3. **docs/planning/cycles/** — Scan retrospectives for "Notifications BC" references (historical docs may retain original name with "(now Correspondence BC)" clarification)
4. **Port allocation table in CLAUDE.md** — Reserve port for Correspondence.Api (next available: 5248)
5. **Future ADRs** — Reference this ADR when describing Correspondence BC design decisions

No code changes are required at this time, as the BC has not been implemented yet.
