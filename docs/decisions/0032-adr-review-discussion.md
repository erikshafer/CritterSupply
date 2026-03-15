# ADR 0032: Multi-Issuer JWT Strategy — Review Discussion

**Date:** 2026-03-15
**Meeting Type:** Asynchronous ADR Review (Multi-Persona)
**Attendees:** Principal Software Architect (PSA), Product Owner (PO)
**Facilitator:** AI Agent (Claude Sonnet 4.5)
**Duration:** 45 minutes
**Document:** [ADR 0032: Multi-Issuer JWT Authentication Strategy](./0032-multi-issuer-jwt-strategy.md)

> **Note:** "Admin Portal" was renamed to "Backoffice" and "Admin Identity" to "BackofficeIdentity" in [ADR 0033](./0033-admin-portal-to-backoffice-rename.md).

---

## Review Process

This document captures a multi-persona review discussion between the PSA and PO to evaluate ADR 0032 for technical correctness, alignment with M32.0 scope, and readiness for implementation.

**Review Objectives:**
1. PSA: Verify technical correctness of named JWT Bearer schemes pattern
2. PO: Confirm alignment with M32.0 Admin Portal Phase 1 requirements
3. Both: Reach consensus on sign-off or request revisions

---

## Discussion Transcript

### Opening Remarks

**PSA:** Thanks for the ADR draft. I've reviewed the technical approach and have a few questions before sign-off. Overall, the named schemes pattern is solid—it's a standard ASP.NET Core approach for multi-issuer scenarios. Let me walk through my technical review first, then I'd like to hear PO's perspective on scope alignment.

**PO:** Sounds good. I've read through the ADR with a focus on M32.0 deliverables. My main concern is making sure this unblocks Phase 1 without introducing scope creep. Let's hear your technical assessment first.

---

### Technical Review (PSA)

**PSA:** Starting with the **Decision** section—the named schemes pattern (`"Admin"` and `"Vendor"`) is architecturally sound. I particularly like these design choices:

1. **Scheme naming:** `"Admin"` and `"Vendor"` are concise and match the identity BC names. I've seen teams use verbose names like `"AdminJwtBearer"` which just clutters endpoint annotations.

2. **RoleClaimType consistency:** Both issuers using the standard `"role"` claim is critical. This makes `policy.RequireRole()` work out of the box and keeps SignalR hub group routing simple. Good call documenting this explicitly.

3. **Policy-based authorization:** The `AuthenticationSchemes.Add("Admin")` pattern is the right way to restrict endpoints to specific issuers. I notice you're including `"SystemAdmin"` in all admin policies—that aligns with ADR 0031's superuser pattern. Consistent.

**Questions and Concerns:**

**PSA:** My **first concern** is the self-referential audience in Phase 1. The ADR states:

> Admin Identity BC issues tokens with `aud: "https://localhost:5249"` (self-referential)

This means domain BCs are validating tokens intended for Admin Identity BC, not for themselves. While technically secure (tokens are still validated against Admin Identity's signing key), it's not ideal. The ADR acknowledges this as a "transitional pattern" and defers audience evolution to Phase 2+.

**My question:** Is the Phase 2 migration path clear enough? We'll need to coordinate updates across Admin Identity BC + 9 domain BCs when we change the audience to Admin Portal API (port 5243). I'd like to see a migration plan added to the ADR—at least a high-level checklist.

**PO:** That's a fair concern. Let me add my perspective: for Phase 1, we're building read-only dashboards and CS tooling. There's no Admin Portal API yet—it's just admin users calling domain BCs directly. So the self-referential audience is a pragmatic choice. I'm okay with deferring the migration plan to Phase 2 planning, but we should document it as a known limitation.

**PSA:** Agreed. I'd like to see a "Known Limitations" subsection added to the **Consequences** section that explicitly calls out the audience evolution requirement. Something like:

```markdown
### Known Limitations (Phase 1)

⚠️ **Self-referential audience pattern** — Domain BCs validate tokens with `aud: "https://localhost:5249"` (Admin Identity BC), not their own addresses. This is a transitional pattern acceptable for Phase 1 because:
- Admin Portal API (port 5243) does not exist yet
- All admin tokens originate from a single issuer (Admin Identity BC)
- Tokens are still validated against Admin Identity's signing key (secure)

**Phase 2 Migration Requirement:** When Admin Portal API ships, we must:
1. Update Admin Identity BC to issue tokens with `aud: "https://localhost:5243"`
2. Update all domain BCs to configure `options.Audience = "https://localhost:5243"`
3. Coordinate deployment to avoid token validation failures during rollout
4. Consider supporting multiple audiences temporarily (backward compatibility)
```

**PO:** I like that addition. It makes the trade-off explicit and sets expectations for Phase 2. PSA, does that address your concern?

**PSA:** Yes, that works. Adding that subsection would satisfy my requirement for Phase 2 migration visibility.

---

**PSA:** **Second concern**—the Product Catalog policy rename. The ADR states:

> Rename existing `"Admin"` policy to `"VendorAdmin"` in Product Catalog.Api/Program.cs

This is a breaking change for existing vendor-facing endpoints. I want to make sure we:
1. Identify all 3 endpoints that use `[Authorize(Policy = "Admin")]`
2. Run existing vendor JWT integration tests **before and after** the rename
3. Verify no vendor functionality breaks

The ADR mentions this in the **Implementation Checklist**, but I'd like to see it called out more prominently in the **Consequences** section as a migration risk.

**PO:** Good catch. We have vendor partners using Product Catalog APIs today, so this rename can't break their workflows. I'd recommend adding a subsection to **Negative Consequences**:

```markdown
### Migration Risk: Product Catalog Policy Rename

⚠️ **Product Catalog policy rename** — Existing `"Admin"` policy (validates vendor tokens) must be renamed to `"VendorAdmin"` and 3 endpoints must be updated to `[Authorize(Policy = "VendorAdmin")]`.

**Risk:** Breaking change for vendor-facing endpoints if tests don't cover all scenarios.

**Mitigation:**
1. Run existing vendor JWT integration tests before rename (establish baseline)
2. Perform rename in a single atomic commit
3. Run tests again after rename to verify no regressions
4. If tests fail, investigate and fix before proceeding with M31.5 Session 4
```

**PSA:** Perfect. That makes the risk visible and provides a clear mitigation strategy. With that addition, I'm satisfied with the Product Catalog handling.

---

**PSA:** **Third observation**—the ADR does a good job of explaining *why* alternatives (single default scheme, dynamic issuer validation) were rejected. The **Rationale** section is thorough. I have no concerns there.

**Overall Technical Assessment:** With the two additions I've requested (Known Limitations subsection and Product Catalog Migration Risk subsection), the ADR is **technically sound and ready for implementation**. The named schemes pattern is proven, the policy-based authorization aligns with ASP.NET Core best practices, and the implementation checklist is actionable.

**My sign-off:** ✅ **Approved pending minor revisions** (add two subsections to Consequences section).

---

### Scope Alignment Review (PO)

**PO:** Thanks, PSA. Let me review from a product perspective. My lens is: **Does this ADR unblock M32.0 Phase 1 without introducing scope creep?**

**M32.0 Phase 1 Requirements (from planning docs):**
1. **Read-only dashboards:** CS agents, WarehouseClerk, OperationsManager, Executive need to view real-time metrics
2. **CS tooling:** CS agents need to cancel orders, approve/deny returns, view customer data, track shipments
3. **Warehouse operations:** WarehouseClerk needs to query stock levels and see low-stock alerts

**Alignment Check:**

**PO:** The ADR explicitly lists the Phase 1 endpoints that require admin JWTs:
- `GET /api/customers?email={email}` (CS customer search) ✅
- `GET /api/orders?customerId={id}` (CS order lookup) ✅
- `POST /api/orders/{id}/cancel` (CS order cancellation) ✅
- `GET /api/returns?orderId={id}` (CS return lookup) ✅
- `POST /api/returns/{id}/approve` (CS return approval) ✅
- `POST /api/returns/{id}/deny` (CS return denial) ✅
- `GET /api/correspondence/messages/customer/{id}` (CS message history) ✅
- `GET /api/inventory/{sku}` (WH stock queries) ✅
- `GET /api/fulfillment/shipments?orderId={id}` (CS shipment tracking) ✅

All 9 Phase 1 endpoints are accounted for. The ADR scopes multi-issuer JWT configuration to exactly these requirements. **No scope creep detected.**

**PO:** I also appreciate that the ADR defers audience evolution to Phase 2+. We're not trying to build the Admin Portal API in M31.5—we're just unblocking Phase 1 endpoints. That's exactly the right scope boundary.

**Concerns:**

**PO:** My **one concern** is implementation complexity. The ADR states:

> Configuration duplication — Each domain BC must configure both schemes (8 BCs × 2 schemes = 16 configurations)

That's a lot of copy-paste. PSA, is there a risk of configuration drift (e.g., one BC configures the wrong audience or forgets `RoleClaimType = "role"`)?

**PSA:** That's a valid concern. Configuration duplication is a known downside of named schemes. Mitigation strategies:
1. **Use a code snippet template** — Document the exact pattern in CLAUDE.md so agents can copy-paste consistently
2. **Integration tests** — The ADR includes multi-issuer JWT acceptance tests (Session 5). These will catch configuration errors early.
3. **CI enforcement** — We could add a CI check that validates all domain BCs have both schemes configured (future improvement, not Phase 1 scope).

For M31.5, I'm comfortable with copy-paste + integration tests. If configuration drift becomes a problem in Phase 2+, we can refactor to a shared configuration helper class.

**PO:** That works for me. The integration tests are the key safety net. As long as we validate that admin JWTs are accepted by all 5 BCs (Orders, Returns, Customer Identity, Correspondence, Fulfillment), we'll catch configuration mistakes before M32.0 starts.

---

**PO:** **Second observation**—the ADR includes a detailed **Implementation Checklist** with 5 phases. I like the granularity. It maps directly to the M31.5 session-by-session plan. This makes it easy to track progress during implementation.

**Overall Scope Assessment:** The ADR **aligns perfectly with M32.0 Phase 1 requirements**. It scopes multi-issuer JWT to exactly the 5 domain BCs that need it (Orders, Returns, Customer Identity, Correspondence, Fulfillment) and defers non-Phase-1 concerns (audience evolution, additional BCs) to future milestones.

**My sign-off:** ✅ **Approved** (no revisions needed from a product perspective).

---

### Consensus and Decision

**PSA:** So we're aligned: I'll approve pending two minor revisions (add Known Limitations and Product Catalog Migration Risk subsections to the Consequences section). PO has approved as-is. Should we finalize the ADR now or request the revisions first?

**PO:** Let's be pragmatic—those two subsections are clarifications, not changes to the core decision. I'd recommend:
1. AI agent updates the ADR with the two subsections (takes 5 minutes)
2. Both of us give final ✅ Approved status
3. Mark ADR status as ✅ Accepted
4. Begin M31.5 Session 1 implementation

That way we don't block Session 1 on wordsmithing. PSA, does that work for you?

**PSA:** Agreed. Let's update the ADR with the two subsections, then both sign off.

---

## Revisions Requested

### 1. Add "Known Limitations" Subsection

**Location:** **Consequences** section, after **Negative** subsection

**Content:**
```markdown
### Known Limitations (Phase 1)

⚠️ **Self-referential audience pattern** — Domain BCs validate tokens with `aud: "https://localhost:5249"` (Admin Identity BC), not their own addresses. This is a transitional pattern acceptable for Phase 1 because:
- Admin Portal API (port 5243) does not exist yet
- All admin tokens originate from a single issuer (Admin Identity BC)
- Tokens are still validated against Admin Identity's signing key (secure)

**Phase 2 Migration Requirement:** When Admin Portal API ships, we must:
1. Update Admin Identity BC to issue tokens with `aud: "https://localhost:5243"`
2. Update all domain BCs to configure `options.Audience = "https://localhost:5243"`
3. Coordinate deployment to avoid token validation failures during rollout
4. Consider supporting multiple audiences temporarily (backward compatibility)
```

---

### 2. Expand "Product Catalog Policy Rename" Consequence

**Location:** **Negative** subsection of **Consequences** section

**Replace:**
```markdown
⚠️ **Product Catalog policy rename** — Breaking change for existing `"Admin"` policy (must rename to `"VendorAdmin"` and update 3 endpoints)
```

**With:**
```markdown
⚠️ **Product Catalog policy rename** — Existing `"Admin"` policy (validates vendor tokens) must be renamed to `"VendorAdmin"` and 3 endpoints must be updated to `[Authorize(Policy = "VendorAdmin")]`.

**Migration Risk:** Breaking change for vendor-facing endpoints if tests don't cover all scenarios.

**Mitigation:**
1. Run existing vendor JWT integration tests before rename (establish baseline)
2. Perform rename in a single atomic commit
3. Run tests again after rename to verify no regressions
4. If tests fail, investigate and fix before proceeding with M31.5 Session 4
```

---

## Final Sign-Off

**After revisions are applied:**

**PSA (Principal Software Architect):** ✅ **Approved**
- **Technical Assessment:** Named JWT Bearer schemes pattern is architecturally sound and follows ASP.NET Core best practices
- **Security:** Token validation is correct; self-referential audience is acceptable for Phase 1 with documented migration path
- **Implementation:** Checklist is actionable; integration tests will catch configuration errors
- **Date:** 2026-03-15

**PO (Product Owner):** ✅ **Approved**
- **Scope Assessment:** Aligns perfectly with M32.0 Phase 1 requirements (read-only dashboards + CS tooling)
- **Risk Assessment:** Configuration duplication mitigated by integration tests; Product Catalog migration risk clearly documented
- **Business Value:** Unblocks M32.0 Phase 1 implementation without scope creep
- **Date:** 2026-03-15

---

## Decision Record

**ADR 0032 Status:** ⚠️ Proposed → ✅ **Accepted** (after revisions applied)

**Decision:** CritterSupply adopts named JWT Bearer schemes for multi-issuer authentication as specified in ADR 0032.

**Implementation:** Proceed with M31.5 Session 1 (Customer Identity email search endpoint) after ADR revisions are committed.

**Next Steps:**
1. AI agent applies two subsection revisions to ADR 0032
2. Update ADR status from ⚠️ Proposed to ✅ Accepted
3. Commit revised ADR: `(M31.5) ADR 0032 Multi-Issuer JWT Strategy - Accepted`
4. Begin M31.5 Session 1 implementation

---

## Meeting Summary

**Duration:** 45 minutes (async review + discussion)

**Outcome:** ✅ **Consensus reached** — ADR 0032 approved pending minor revisions

**Key Decisions:**
1. Named JWT Bearer schemes pattern is technically sound and approved
2. Self-referential audience is acceptable for Phase 1 with documented Phase 2 migration path
3. Product Catalog policy rename risk is acceptable with clear mitigation strategy
4. Proceed with M31.5 implementation after revisions are applied

**Action Items:**
- [x] PSA technical review completed
- [x] PO scope alignment review completed
- [ ] AI agent applies two subsection revisions to ADR 0032
- [ ] Update ADR status to ✅ Accepted
- [ ] Commit revised ADR
- [ ] Begin M31.5 Session 1

---

**Document Created:** 2026-03-15
**Facilitator:** AI Agent (Claude Sonnet 4.5)
**Review Method:** Multi-persona simulation (PSA + PO personas)
**Outcome:** ADR 0032 approved with minor revisions
