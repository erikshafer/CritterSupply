# ADR 0033: Admin Portal / Admin Identity → Backoffice / BackofficeIdentity Rename

**Status:** ✅ Accepted

**Date:** 2026-03-15

**Participants:** Principal Software Architect (PSA), Product Owner (PO), UX Engineer (UXE)

---

## Context

The `Admin Portal` and `Admin Identity` bounded context names have caused real, recurring problems during implementation:

### Problems with "Admin"

1. **JWT scheme name collision:** ADR 0032 (Multi-Issuer JWT Strategy) introduced a named JWT Bearer scheme called `"Admin"` for the Admin Identity BC. Product Catalog.Api already has a policy called `"Admin"` that validates *vendor* tokens (not admin tokens). This naming collision creates confusion about what "Admin" means in authorization context — is it the authentication scheme (Admin Identity BC) or the vendor role (`VendorAdmin`)?

2. **Ambiguity with generic "admin" concepts:** The word "admin" appears in role names (`SystemAdmin`), policy names, JWT claim values, permission identifiers, and general programming vocabulary (e.g., "admin panel", "admin user", "administrator"). Every use of "admin" as a BC identifier requires mental disambiguation from these generic uses.

3. **Shorthand collisions:** In code, conversations, and documentation, `admin` as a shorthand could mean:
   - The Admin Identity BC (authentication provider)
   - The Admin Portal BFF (planned gateway)
   - A `SystemAdmin` role
   - Generic administrative privileges
   - System administration (sysadmin) tasks

4. **Concrete impact in recent sessions:** During M31.5 implementation (Admin Portal prerequisites), agent sessions encountered repeated confusion between:
   - The `"Admin"` JWT authentication scheme name
   - The `"Admin"` authorization policy in Product Catalog (which validates vendor tokens)
   - The `SystemAdmin` role
   - Generic "admin" endpoint prefixes

These are not theoretical concerns — they surfaced in active implementation work and caused concrete debugging overhead.

### Why "Backoffice"

The replacement name **Backoffice** (one word) accurately describes the BC's domain:

- **Domain meaning:** "Back office" is a universally understood term in retail and e-commerce, referring to the internal operational side of the business — order management, inventory, customer service, merchandising, and reporting.
- **User language:** Internal staff at CritterSupply will naturally say "check the backoffice" or "update it in the backoffice" — this is how business users refer to their operational tools.
- **Zero collision risk:** The word "backoffice" does not appear anywhere else in the codebase, in role names, in JWT claims, or in common programming vocabulary. It is unambiguous.
- **Semantic completeness:** Unlike "Vendor" (which needs "Portal" to convey its dashboard nature), "Backoffice" is a self-contained compound noun that already means "internal operations area." No suffix needed.

---

## Naming Deliberation: Backoffice vs. BackofficePortal

Before deciding, the team evaluated whether the BC should be named `Backoffice` or `BackofficePortal`. All three participants (PSA, PO, UXE) examined the codebase and documentation from their respective perspectives.

### Evidence Examined

**Existing BFF naming patterns in the codebase:**

| BFF | Name | Uses "Portal"? | Why? |
|-----|------|----------------|------|
| Customer Experience | `Storefront` | No | "Storefront" is semantically complete — it inherently means "customer-facing shop" |
| Vendor Portal | `VendorPortal` | Yes | "Vendor" alone is ambiguous — needs "Portal" to convey "vendor-facing dashboard" |
| Admin → ? | `Backoffice` or `BackofficePortal` | ? | Evaluated below |

**The naming principle is consistent:** Use the shortest name that is semantically complete. "StorefrontPortal" would be redundant; "Vendor" alone is incomplete. "Backoffice" is already a specific, self-contained business concept.

### PSA Assessment (Code & Architecture)

1. **Shorter identifiers everywhere:** `Backoffice.Api` vs `BackofficePortal.Api`, `backoffice-*` vs `backoffice-portal-*` queues, `/hub/backoffice` vs `/hub/backoffice-portal`. None of these comparisons favor the longer name.

2. **Clean BFF/Identity pair:** `Backoffice` / `BackofficeIdentity` parallels `VendorPortal` / `VendorIdentity` and `Storefront` / `CustomerIdentity`. The `-Identity` suffix is the disambiguator — the BFF doesn't need "Portal" to distinguish itself.

3. **JWT scheme naming:** Both options produce the same scheme name: `"Backoffice"`. The scheme is named for the *actor* (internal staff), not the BFF. This is consistent with the `"Vendor"` scheme name.

4. **BC-NAMING-ANALYSIS.md precedent:** The codebase already adopted shorter domain-meaningful names (Orders, Payments, Shopping) over verbose alternatives (Order Management, Payment Processing). `Backoffice` follows this philosophy.

### PO Assessment (Domain & Business)

1. **Business language:** Internal staff say "the backoffice," not "the backoffice portal." The naming philosophy says to prefer terms business users understand.

2. **Domain-first identity:** This BC is the operational domain that happens to have a web interface, not a "portal" that happens to serve operational needs. The BFF serves 6 internal personas with distinct operational responsibilities — that's a business domain, not just a UI.

3. **Consistency in philosophy, not suffixes:** The three BFFs each use the natural business language for their context: "storefront" (customer), "vendor portal" (partner), "backoffice" (internal staff). That IS the consistency — domain language, not a shared suffix.

### UXE Assessment (User Experience)

1. **Users call it "the backoffice":** In e-commerce, "back office" is a universally understood term for internal operational tools. Nobody says "the backoffice portal."

2. **Cognitive load:** Shorter names reduce cognitive load in navigation, browser tabs, login screens, and documentation. "Portal" adds reading time without adding understanding.

3. **Branding/navigation:** The UI would display "Backoffice" in the header/nav — "Backoffice Portal" would be trimmed in practice. Name the BC what the product will be called.

4. **Screen reader benefit:** Shorter, more meaningful names improve the experience for assistive technology users.

### Consensus

**3/3 — Use `Backoffice` (not `BackofficePortal`)**

---

## Decision

Rename the bounded contexts:

| Current Name | New Name | Scope |
|---|---|---|
| **Admin Identity** | **BackofficeIdentity** | Implemented BC — JWT auth, RBAC, user management |
| **Admin Portal** | **Backoffice** | Planned BFF — internal operations gateway |

All identifiers, configuration, documentation, and infrastructure references will be updated accordingly:

| Artifact | Current | New |
|---|---|---|
| BC folder (identity) | `src/Admin Identity/` | `src/Backoffice Identity/` |
| BC folder (BFF) | *(planned)* | `src/Backoffice/` |
| Project names | `AdminIdentity`, `AdminIdentity.Api` | `BackofficeIdentity`, `BackofficeIdentity.Api` |
| Namespaces | `AdminIdentity.*` | `BackofficeIdentity.*` |
| JWT scheme name | `"Admin"` | `"Backoffice"` |
| JWT issuer | `https://localhost:5249` | `https://localhost:5249` *(unchanged — issuer is the service URL, configured in appsettings.json)* |
| JWT secret key prefix | `AdminIdentity-Development-...` | `BackofficeIdentity-Development-...` |
| Docker service | `adminidentity-api` | `backofficeidentity-api` |
| Docker container | `crittersupply-adminidentity` | `crittersupply-backofficeidentity` |
| Database name | `adminidentity` | `backofficeidentity` |
| Database schema | `adminidentity` | `backofficeidentity` |
| Route prefix | `/api/admin-identity/` | `/api/backoffice-identity/` |
| Aspire resource | `crittersupply-aspire-adminidentity-api` | `crittersupply-aspire-backofficeidentity-api` |
| GitHub label | `bc:admin-portal` | `bc:backoffice` |
| Feature files folder | `docs/features/admin-portal/` | `docs/features/backoffice/` |
| Port allocation | 5249 (identity), 5243/5244 (BFF) | *(unchanged)* |

---

## Rationale

### Why Rename Now (Before Implementation Proceeds)

1. **Admin Portal BFF is not yet implemented** — Renaming a planned BC has zero code migration cost for the BFF itself.
2. **Admin Identity BC is recently implemented** (Cycle 29 Phase 1) — The codebase is small enough (22 C# files, 1 Docker service, 1 database) that renaming now is tractable. Waiting longer increases migration cost.
3. **M31.5 is in progress** — The multi-issuer JWT strategy (ADR 0032) is actively being implemented across domain BCs. Renaming the `"Admin"` scheme to `"Backoffice"` now avoids a second migration later.
4. **The problems are real** — JWT scheme name collision with Product Catalog's `"Admin"` policy has already caused confusion. Fixing it now prevents accumulation of technical debt.

### Why "Backoffice" and Not Other Names

| Alternative | Why Rejected |
|---|---|
| `Operations` | Too generic — could mean DevOps/SRE, not business operations |
| `InternalPortal` | "Internal" is redundant (all admin tools are internal); "Portal" adds noise |
| `BackOffice` (two words) | CamelCase ambiguity in code (`BackOffice` vs `Backoffice`). One word is cleaner for identifiers. |
| `AdminOps` | Still uses "Admin" — defeats the purpose of the rename |
| `StaffPortal` | "Staff" is generic; "Portal" adds noise (same reasoning as BackofficePortal) |
| `HQ` | Too informal; not immediately clear to new developers |

---

## Consequences

### Positive

- ✅ **Eliminates JWT scheme name collision** — `"Backoffice"` scheme has zero overlap with any existing policy or role name
- ✅ **Unambiguous in all contexts** — "Backoffice" cannot be confused with roles, permissions, or generic admin concepts
- ✅ **Consistent naming philosophy** — Follows the codebase's preference for shorter, domain-meaningful BC names
- ✅ **Clean BFF/Identity pairing** — `Backoffice` / `BackofficeIdentity` reads naturally
- ✅ **Future-proof** — The name accurately describes the domain regardless of future UI technology choices

### Negative

- ⚠️ **Migration effort for Admin Identity BC** — 22 C# files, 1 Dockerfile, docker-compose entries, Aspire config, solution file, database scripts, and EF Core migrations must be renamed
- ⚠️ **JWT scheme rename in domain BCs** — Orders.Api and Returns.Api already reference `"Admin"` scheme (from M31.5 Session 3). These must be updated to `"Backoffice"`.
- ⚠️ **Documentation update scope** — 40+ planning documents, 5 ADRs, 5 feature files, CONTEXTS.md, README.md, and CLAUDE.md reference "Admin Portal" or "Admin Identity"
- ⚠️ **EF Core migration history** — The existing `adminidentity` schema and migration files must be carefully handled to avoid data loss in any existing development databases

### Mitigation

- **Single atomic rename session** — Execute all code changes in one focused session to avoid partial renames
- **EF Core migration strategy** — Generate a new migration that renames the schema from `adminidentity` to `backofficeidentity`, or reset migrations (acceptable for pre-production code)
- **Documentation updates in same session** — Rename all docs atomically so no references are left dangling
- **Grep verification** — After rename, run `grep -ri "admin" --include="*.cs" --include="*.json" --include="*.yml" --include="*.md"` to verify no BC-specific references remain

---

## References

- [ADR 0030: Notifications → Correspondence Rename](./0030-notifications-to-correspondence-rename.md) — Precedent for BC rename
- [ADR 0031: Admin Portal RBAC Model](./0031-admin-portal-rbac-model.md) — Defines roles and policies (will need "Admin Portal" references updated)
- [ADR 0032: Multi-Issuer JWT Strategy](./0032-multi-issuer-jwt-strategy.md) — Defines `"Admin"` scheme name (will be renamed to `"Backoffice"`)
- [BC-NAMING-ANALYSIS.md](../BC-NAMING-ANALYSIS.md) — Naming philosophy that informed shorter domain-meaningful names
- [CONTEXTS.md](../../CONTEXTS.md) — BC ownership reference (will be updated)
- [Admin Portal Event Modeling](../planning/admin-portal-event-modeling.md) — Domain analysis that established the 6 internal personas

---

**Approval:** Consensus decision (3/3) by PSA, PO, and UXE.
