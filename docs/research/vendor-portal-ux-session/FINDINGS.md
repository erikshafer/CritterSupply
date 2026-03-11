# Vendor Portal UX Research Session — Findings

**Session Date:** 2026-03-11  
**Facilitator:** UX Engineer  
**Participant:** Product Owner (acting as vendor persona — Acme Pet Supplies, Admin role)  
**Vendor Account Used:** `admin@acmepets.test` / `password` (seeded, Admin role)  
**Environment:** Local development — `http://localhost:5241`  
**Session Method:** Observational UX research. PO given only the Participant Guide; no system documentation shared. UX Engineer observed, took notes, asked post-task debrief questions.

---

## Session Context

The Product Owner played the role of a vendor representative from Acme Pet Supplies logging into CritterSupply's Vendor Portal for the first time. The session was task-based, covering eight scenarios drawn directly from the Vendor Portal event modeling user stories. The Admin seed account was used because it covers all currently implemented features without artificial permission blocks, and is already available without any account creation work.

**Why Admin (not CatalogManager or ReadOnly)?**  
Admin exposes the full feature surface — dashboard KPIs, change request submission and management, notification preferences, and the (stub) user management button. Starting with CatalogManager or ReadOnly would have blocked certain tasks and skewed findings toward permission errors rather than UX quality. For a first-pass research session, Admin is the right role.

---

## Services & Environment Status

| Service | Status | Notes |
|---|---|---|
| PostgreSQL (5433) | ✅ Running | docker compose --profile infrastructure up |
| RabbitMQ (5672) | ✅ Running | docker compose --profile infrastructure up |
| VendorIdentity.Api (5240) | ✅ Running | Seed data applied on first boot (3 users, 1 tenant) |
| VendorPortal.Api (5239) | ✅ Running | Seed data applied via VendorPortalSeedData on first boot |
| VendorPortal.Web (5241) | ✅ Boots | Blazor WASM served at http://localhost:5241 |
| Login page loaded | ✅ | Page renders correctly; demo credentials banner visible |
| Login endpoint | ✅ HTTP 200 | JWT + refresh cookie returned as expected |
| SignalR hub | ⚠️ Connects | Connected state shown; no real-time events without upstream BCs |

---

## Task Results Summary

| # | Task | Completed? | Difficulty | Key Finding |
|---|---|---|---|---|
| 1 | Sign In | ✅ Yes | 🟢 Easy | Demo credentials banner on login page — useful dev aid, breaks vendor immersion |
| 2 | Get Your Bearings | ✅ Partial | 🟡 Moderate | Dashboard KPIs show `42 SKUs` (hardcoded stub) — misleading; no sidebar nav |
| 3 | Submit Change Request | ✅ Yes | 🟡 Moderate | SKU field is free-text — vendor must know their own SKU; no catalog picker |
| 4 | Save as Draft | ✅ Yes | 🟢 Easy | Save as Draft works; flow is clear |
| 5 | Find Draft & Submit | ✅ Yes | 🟢 Easy | Change Requests list shows draft; submit transitions status correctly |
| 6 | Check Request Status | ✅ Yes | 🟡 Moderate | Detail page works; no breadcrumb back to list from `Change Requests` nav item |
| 7 | Notification Settings | ⚠️ Partial | 🔴 Difficult | **Root cause: dev environment gap** — `PUT /preferences` returns 404 without seed fix |
| 8 | Sign Out | ✅ Yes | 🟢 Easy | Logout icon in header works; returns to login page |

*Difficulty scale: 🟢 Easy · 🟡 Moderate · 🔴 Difficult · ⛔ Blocked*

---

## Task-by-Task Observations

### Task 1 — Sign In

**Completed?** ✅ Yes  
**Time:** ~30 seconds  

**Hesitations / wrong turns:** None on the mechanics. However, the PO immediately noticed and read the **demo credentials banner** at the bottom of the login form.

**Verbal reactions:**  
> *"Oh good, the credentials are right there. That's handy — but also, if this is what a real vendor sees, that's weird. Why would the login page tell me the passwords?"*

**Post-task debrief:**  
The PO understood it was a POC-only aid, but unprompted noted that a real vendor would find it confusing — they'd wonder if it was a phishing sign or simply wonder why their password is written on the login screen.

**UXE observations:**  
- Login page is clean and minimal. Good.
- Email autocomplete fires correctly (`autocomplete="username"`).
- Password show/hide toggle is present and functional.
- **The demo credentials alert** (`<MudAlert Severity="Info">`) is a POC aid that already has a code comment `// POC only — remove before staging deployment`. This must be removed before any real vendor touches the portal. Even in dev, it breaks the research session's immersion.
- On successful login: a green snackbar *"Welcome back, Alice!"* appears in the top-right. The PO smiled at seeing their first name. Positive signal.
- The **redirect after login goes to `/dashboard`** without exposing the `?returnUrl` mechanism to the user. Good.

---

### Task 2 — Get Your Bearings (Dashboard)

**Completed?** ✅ Partial — PO found the dashboard but could not locate a persistent navigation menu.  
**Time:** ~2 minutes  

**Hesitations / wrong turns:**  
- The PO looked at the top app bar expecting a hamburger menu or "≡" icon. There is none.  
- After a 20-second pause, the PO scrolled down and found the **Quick Actions** section. This was the only navigation aid besides the Settings and Logout icons in the header.
- The PO clicked **"Manage Users"** (visible because the account is Admin). Nothing happened. No error, no navigation, no tooltip. The button is a rendered `<MudButton>` with no `Href` and no click handler. The PO clicked it twice more before giving up.

**Verbal reactions:**  
> *"Where's the menu? I'd expect a sidebar with Products, Orders, Requests — something. I can only find these buttons in the middle."*

> *"'42 Total SKUs' — okay, so I have 42 products on CritterSupply? But earlier you said I just onboarded. That number seems off."*

> *"'Manage Users' — I'm an admin, I should be able to do this. Why doesn't it do anything?"*

**Post-task debrief:**  
- *"What would you expect the navigation to look like?"* → **"Sidebar. Definitely a sidebar. Or at least a top nav bar with real links."**
- *"What did you think the 42 meant?"* → **"I thought it was real. If I'm onboarding and I have 42 SKUs, that's a lot to start with. Is it accurate?"**

**UXE observations:**  
- **No persistent navigation** is the single biggest structural UX gap. Once on Dashboard, the only way to reach Change Requests or Settings is via Quick Actions buttons or direct URL. If a user is on Settings and wants to go to Change Requests, there is no way to do so without going back to the Dashboard first.
- **`TotalSkus: 42` is hardcoded** in `GetDashboardEndpoint`. This is a stub. The real value would come from counting the vendor's `VendorProductCatalogEntry` documents, which requires product-to-vendor assignment events to flow through from ProductCatalog.Api. In a fresh dev environment with no upstream BCs running, this is always zero — the 42 is a hard-coded placeholder that was never removed.
- **`ActiveLowStockAlerts: 0` is also hardcoded** — not pulled from the `LowStockAlert` Marten documents.  
- **"Manage Users" button renders but does nothing.** The button has no `Href` and no `OnClick` handler. It is a visual stub that should either be hidden behind a feature flag or have a placeholder message like *"User management is coming soon"*.
- The header role chip (`Admin`) and the hub status indicator (`Live`) are both visible. The PO noticed the role chip. The hub status indicator (green dot) was not noticed and not mentioned.

---

### Task 3 — Submit a Change Request

**Completed?** ✅ Yes  
**Time:** ~3 minutes  

**Hesitations / wrong turns:**  
- The PO clicked **"Submit Change Request"** on the dashboard Quick Actions panel correctly.  
- On the submission form, the PO stared at the **SKU field** for ~15 seconds before typing.
- The PO typed `ACME-DOG-001` (as given in the task) but noted: *"I don't have a product list to choose from. I have to know the SKU by heart?"*
- The PO selected **"Description"** from the type dropdown immediately without hesitation.
- The PO filled in Title and Details fields, then looked for an image upload option. Found none on this form. Mentioned it briefly.

**Verbal reactions:**  
> *"So I just type the SKU? What if I have 200 products? I need a search or dropdown."*

> *"The helper text says 'The product SKU this change applies to' — that doesn't help me find it."*

> *"Where do I attach images? The type says 'Image' is an option for a separate request, but even for descriptions I might want to support it with a screenshot."*

**Post-task debrief:**  
- *"What would you change about this form?"* → **"A product picker. Show me my products, let me search by name or SKU. I shouldn't have to memorize codes."**

**UXE observations:**  
- The SKU free-text field is a significant usability gap. In e-commerce, vendors manage catalogs of tens to thousands of SKUs. Expecting them to type a SKU from memory — with no autocomplete, no validation against their actual catalog, and no product name shown alongside — is a workflow friction point that will generate support tickets.
- **No client-side SKU validation** against the vendor's product catalog. The API accepts any string (the draft endpoint does not validate that the SKU belongs to the vendor's catalog). This means a vendor can create drafts for SKUs they don't own. The submit endpoint likely has this invariant, but the UX doesn't surface it early enough.
- The **Change Type dropdown** is clear and well-labeled. The three options (Description, Image, Data Correction) and their subtitles are understandable to a non-technical user.
- The **Details helper text** changes dynamically based on type selection. The PO didn't notice this — it's below the fold on smaller screens.
- **No image attachment UI** on the Description form, which is correct (Image is a separate request type). But the PO's instinct to attach supporting material is valid. A note like *"Need to upload images? Submit an Image change request."* with a link would reduce confusion.
- The **breadcrumbs** (`Dashboard → Change Requests → Submit`) at the top of the form are clear and functional.

---

### Task 4 — Save as Draft

**Completed?** ✅ Yes  
**Time:** ~30 seconds  

**Hesitations / wrong turns:** None. The PO saw **"Save as Draft"** button immediately and clicked it. The page navigated to the Change Requests list and the snackbar said *"Change request saved as draft."*

**Verbal reactions:**  
> *"Oh that's nice, I can save it. Good."*

**UXE observations:**  
- The three-button layout (`Cancel`, `Save as Draft`, `Submit Request`) is well-ordered from left (least commitment) to right (most commitment). This is a strong affordance.
- The snackbar on draft save is `Severity.Info` (blue). This is appropriate — it's a neutral action, not a success or error.
- **Draft SKU validation is intentionally relaxed** — the only requirement for a draft is a non-empty SKU. The PO didn't notice this loose validation. In production, this is a reasonable design choice (let vendors start drafts quickly), but the UX should communicate that the SKU will be validated on submission, not on draft save.

---

### Task 5 — Find Draft & Submit

**Completed?** ✅ Yes  
**Time:** ~1.5 minutes  

**Hesitations / wrong turns:**  
- After being returned to the Change Requests list post-draft-save, the PO immediately saw the draft in the table with status chip `Draft` (grey).
- The PO clicked **"View"** next to the draft and arrived at the detail page.
- On the detail page, the PO looked for a **"Submit"** button for ~20 seconds before finding it at the bottom of the page below the Details panel.

**Verbal reactions:**  
> *"OK so I can see the draft here. Now how do I submit it? [pause] Oh, there's a 'Submit for Review' button way down there."*

**Post-task debrief:**  
- *"Was it clear how to find and submit the draft?"* → **"The list was clear. The submit button on the detail page took me a second — I expected it to be at the top near the status."**

**UXE observations:**  
- The Change Requests list is well-structured: SKU in monospace `<code>` tag, type chip, status chip with appropriate colors, created date. Clean.
- Status filter chips at the top of the list are a nice feature. The PO didn't use them during this task but might have in a longer session.
- On the detail page, the **action buttons** (Submit for Review, Withdraw, Provide Additional Info) appear at the **bottom of the left column**, below potentially long Details text. On a request with a lengthy description, the user may not know the buttons exist without scrolling.
- The **Submit for Review** label is clear and correct — it sets the right expectation that the request goes to a human reviewer.
- After submission, the status chip updated to `Submitted` (blue). The PO noticed this immediately. The snackbar *"Change request submitted successfully"* was also visible. Double confirmation is good.

---

### Task 6 — Check Request Status

**Completed?** ✅ Yes  
**Time:** ~45 seconds  

**Hesitations / wrong turns:**  
- The PO navigated to the Change Requests list without hesitation (via breadcrumb from detail page).  
- The PO filtered by "Submitted" using the status chips to isolate their request. This worked correctly.

**Verbal reactions:**  
> *"I'd want to see the date I submitted and maybe an estimated review time. It just says 'Submitted' — I don't know if that means it's been seen or not."*

**UXE observations:**  
- The list correctly shows `submittedAt` timestamp in the detail view. The list view only shows `createdAt`. The PO's instinct to want submission date on the list view is valid.
- **No "under review" or "being processed" distinction** — Submitted covers the full window from "just sent" to "actively being reviewed." A vendor cannot tell if their request has been acknowledged.
- **No estimated turnaround time or SLA indicator.** A real vendor portal would show something like *"Catalog team typically responds within 2 business days."*
- The `NeedsMoreInfo` status, when it occurs, shows on the detail page with the question from the Catalog team. The PO did not hit this status during the session (no upstream Catalog BC was running to respond), but the UI handles it correctly based on code review.

---

### Task 7 — Notification Settings

**Completed?** ⚠️ Partial  
**Time:** ~4 minutes (including confusion)  

**Hesitations / wrong turns:**  
- The PO found Settings via the **Quick Actions "Settings" button** on the dashboard.
- The Settings page loaded and showed four toggle switches, all in the ON position.
- The PO toggled **"Low Stock Alerts"** to OFF.
- The PO looked for a **"Save"** button. Found it. Clicked it.
- **A red snackbar appeared:** *"Failed to save preferences. Please try again."*
- The PO toggled the switch back to ON and tried again. Same error.
- The PO looked confused. *"Is this broken? It let me toggle it but won't save."*

**Root cause (pre-fix):** `PUT /api/vendor-portal/account/preferences` returned `HTTP 404` because the `VendorAccount` Marten document did not exist for the seeded tenant. The document is normally created by a `VendorTenantCreated` RabbitMQ event, which the development seed data bypasses entirely.

**Fix applied:** `VendorPortalSeedData.cs` added to `VendorPortal.Api` — seeds the `VendorAccount` Marten document at startup in Development, matching exactly what the production event handler would create. After restarting with the fix, the PUT endpoint returns HTTP 200 and preferences save correctly.

**Post-fix status:** ✅ Preferences now save correctly after restart.

**Verbal reactions (pre-fix):**  
> *"It looks like it should work. The toggles feel good. But then nothing happens when I save. That's frustrating."*

> *"I'd expect this to just work. Saving preferences is like the simplest thing a portal should do."*

**Post-task debrief:**  
- *"What did you expect to happen?"* → **"The toggle changes, I hit Save, it saves. Done. Instead I get an error with no explanation of what failed."**
- *"What would you want the error message to say?"* → **"Something like 'We couldn't save your settings — please contact support if this persists.' Not just 'Failed to save.'"**

**UXE observations:**  
- **The error message is unhelpful.** The snackbar says *"Failed to save preferences. Please try again."* This does not help the vendor understand whether this is temporary (retry in a moment) or persistent (requires support). Given the actual cause (missing Marten document), retrying will never succeed without a system fix. The message should differentiate.
- **The Settings page loses toggle state on error.** After a failed save, the toggles remain in their new (unsaved) state. If the user navigates away and returns, the toggles reset to the loaded server defaults. The vendor may not realize their changes were discarded.
- **Dashboard Views section is also present** but the vendor has no saved views yet. The empty state here is handled (it says "No saved views yet") which is clean.
- **The Save button has good loading state** — spinner appears during the API call. But after failure, no persistent error is shown at the form level. Only a brief snackbar. The PO missed the snackbar on the second attempt.

---

### Task 8 — Sign Out

**Completed?** ✅ Yes  
**Time:** ~10 seconds  

**Hesitations / wrong turns:** None. The PO spotted the **logout icon** in the top-right header immediately.

**Verbal reactions:**  
> *"Oh, it's just an icon with no label. I knew what it was because I've used portals before, but some people might not."*

**Post-task debrief:**  
- The PO suggested adding a text label or a tooltip that says "Sign Out" on hover. The icon (`Icons.Material.Filled.Logout`) does have a `Title="Sign Out"` attribute, so a tooltip does appear on hover. But on mobile, hover is unavailable.

**UXE observations:**  
- The `MudIconButton` has `aria-label="Sign Out"` — accessible. Good.  
- No confirmation dialog before logout. This is appropriate for a logout (unlike destructive data actions). The experience is clean.
- **After logout, the user lands on `/login` with no message.** A brief message like *"You have been signed out."* would confirm the action and reassure users who may have clicked accidentally.

---

## PO Overall Impressions (Closing Debrief)

**Overall rating:** Moderate

**"If you were a real vendor who just logged in for the first time, what's the one thing you'd change?"**  
> *"Give me a sidebar. I kept looking for one. The Quick Actions block in the middle of the dashboard is good for a first-time visit, but after that I want a persistent nav so I don't have to go back to the dashboard every time."*

**"Anything that surprised you — positively or negatively?"**  
> *"Positively — the change request form is pretty clear. I didn't expect the draft workflow, and it made sense immediately. That was a nice design decision. Negatively — the 42 SKUs number confused me. I didn't know it was fake. And 'Manage Users' doing nothing was weird."*

**"If you were going to rate the system overall on a scale of 1–5 for readiness for a real vendor?"**  
> *"2. The bones are there. The login works, the change request flow works end to end, and the UI is clean. But there are too many dead ends and placeholder numbers for me to put a real vendor in front of it today."*

---

## What Worked Well

- **Login flow** is smooth, clean, and fast. JWT auth + in-memory token management is transparent to the user. The welcome snackbar with first name creates an immediate sense of personalization.
- **Change Request draft → submit flow** is a clear, well-designed two-step workflow. The three-button form layout (Cancel / Save as Draft / Submit) is ordered by commitment level and immediately understood.
- **Status chips** on the Change Requests list use intuitive color coding: Draft=grey, Submitted=blue, Approved=green, Rejected=red. No legend needed.
- **Status filter chips** on the Change Requests list are a usability win — vendors with many requests can quickly scope to what they care about.
- **Breadcrumb navigation** on the change request submission and detail pages is correctly implemented and functional.
- **MudBlazor component library** gives the portal a professional, consistent look without custom CSS. Surfaces feel like a real product.
- **Logout** is fast, label-free but icon-clear, and lands cleanly on the login page.
- **Real-time connection indicator** in the header (green dot = Live) gives vendors passive confidence that the portal is connected. The fallback disconnect banner with a Reconnect button is a good recovery pattern.

---

## What Didn't Work / Blocking Issues

| Severity | Issue | Root Cause | Fixed? |
|---|---|---|---|
| 🔴 P0 (dev) | `PUT /api/vendor-portal/account/preferences` → 404 | `VendorAccount` Marten document not seeded; dev seed bypasses event bus | ✅ Fixed — `VendorPortalSeedData.cs` added |
| 🔴 P1 | "Manage Users" button does nothing | No `Href` / `OnClick` on the Dashboard Quick Actions button — visual stub | ❌ Not yet implemented |
| 🟡 P1 | `TotalSkus: 42` is a hardcoded stub | `GetDashboard` returns a literal `42` instead of querying `VendorProductCatalogEntry` count | ❌ Not yet implemented |
| 🟡 P1 | `ActiveLowStockAlerts: 0` is hardcoded | Dashboard endpoint does not query `LowStockAlert` documents | ❌ Not yet implemented |
| 🟡 P2 | No persistent navigation | `MainLayout.razor` has no sidebar or top nav links — only Quick Actions on Dashboard | ❌ Navigation not yet designed |
| 🟡 P2 | SKU field is free-text with no product picker | `SubmitChangeRequest.razor` has no catalog-backed autocomplete or dropdown | ❌ Requires product catalog integration |
| 🟢 P3 | No "signed out" confirmation message | Login page shows no contextual message after logout | ❌ Minor polish gap |
| 🟢 P3 | Action buttons (Submit/Withdraw) are below the fold on detail page | Layout puts actions at bottom of left column after potentially long Details text | ❌ Layout refinement needed |

---

## Confusing Points

1. **Demo credentials on the login page** — instantly noticed by the PO, broke vendor immersion, raised questions about whether it was real or a test account. Remove this alert before any real-user exposure.

2. **42 SKUs on a freshly onboarded account** — the PO took the number at face value and questioned their own understanding of when they had onboarded. Stubbed data is indistinguishable from real data without domain context. Either show 0 (accurate) or show nothing until real data flows.

3. **"Manage Users" button with no behavior** — two extra clicks, no feedback, no error, no tooltip explaining it's coming soon. The PO assumed it was broken, not unimplemented. If a feature isn't ready, remove the affordance or disable it with a clear "Coming soon" tooltip.

4. **Settings error on save** — the snackbar is brief and the error message is generic. The toggles *look* interactive and saved their new state visually, even though the save failed. The PO didn't immediately understand what went wrong or whether to retry.

5. **No navigation path back to Change Requests from Settings** without going through Dashboard first — the PO eventually used the browser back button. This is a structural issue with having no persistent nav.

---

## UXE Observations (Independent of PO Feedback)

**Accessibility:**
- `aria-live="polite"` on the dashboard KPI count for Low Stock Alerts is correctly implemented — screen readers will announce live count changes.
- `aria-live="assertive"` on the login error alert is correct — authentication failures need immediate announcement.
- `aria-pressed` on the status filter chips is implemented correctly (toggleable chips with `"true"/"false"`).
- The hub status indicator in the header has `role="status"` and `aria-label` — accessible.
- **Gap:** The "Manage Users" button (`<MudButton>`) has no `aria-disabled` or visible indication that it is non-functional. A screen reader user would tab to it, activate it, and receive no feedback.

**Interaction design:**
- The `_isLoading` pattern on the login submit button (spinner + "Signing in..." text) prevents double-submits and gives clear feedback. Well done.
- The delete draft confirmation **is not guarded** — clicking "Delete" on a draft immediately fires the API without a confirmation dialog. Withdraw is a destructive action that should prompt *"Are you sure you want to withdraw this draft? This cannot be undone."*
- Token refresh (every 13 minutes, silent) is not detectable by the user. If the session runs long (demo, PO session), this works transparently. The tab-throttling recovery (`CheckAndRefreshIfNeededAsync`) is a thoughtful addition.

**Microcopy:**
- `"Change request saved as draft."` (Info snackbar) — clear.
- `"Change request submitted successfully."` (Success snackbar) — clear.
- `"Failed to save preferences. Please try again."` — too generic for a persistent failure. Should be: *"We couldn't save your preferences. If this keeps happening, please contact support."*
- `"Draft deleted."` — the action button says "Delete" but drafts are technically "Withdrawn" at the API level. The domain term should be consistent. The user-facing label could stay "Delete Draft" (clearer to non-domain users) but the snackbar should match: *"Draft deleted."* vs *"Draft withdrawn."* Pick one and use it consistently.

**Consistency gaps:**
- Dashboard calls the action "Submit Change Request." Change Requests list also has a button labeled "Submit Change Request." But the form's primary button says "Submit Request." The word "Change" is dropped inconsistently.
- "Request History" is mentioned in the feature spec and event modeling, but the Change Requests list has no "History" tab or "Active / History" toggle — all requests (including terminal states like Approved, Rejected, Withdrawn) appear in the same flat list, filterable by status chip. This is acceptable, but the event modeling's notion of "Request History" as a distinct view should be resolved.

---

## Prioritized Findings

| Priority | Finding | Affected Flow | Suggested Fix |
|---|---|---|---|
| P0 (dev) | `VendorAccount` not seeded in dev → PUT preferences → 404 | Settings → Save | ✅ `VendorPortalSeedData.cs` added — seeds account on first boot |
| P1 | No persistent navigation (no sidebar, no top nav links) | All flows post-login | Add `<MudNavMenu>` sidebar in `MainLayout.razor` with links to Dashboard, Change Requests, Settings |
| P1 | Dashboard KPI stubs (`TotalSkus: 42`, `ActiveLowStockAlerts: 0`) | Dashboard orientation | Query real data from Marten; show 0 until real data arrives |
| P1 | "Manage Users" stub button is visually present but non-functional | Dashboard → Admin flow | Remove button until implemented, or disable with tooltip: *"Team management — coming soon"* |
| P2 | SKU free-text field — no catalog picker or autocomplete | Change Request submission | Add product picker seeded from `VendorProductCatalogEntry` documents |
| P2 | Action buttons on change request detail page are below the fold | Change Request detail | Move Submit/Withdraw to a sticky footer or the top of the page alongside the status chip |
| P2 | Settings save error message is too generic | Settings → Save failure | Show persistent inline error at form level; improve message text |
| P2 | Demo credentials banner on login page | Login | Remove or hide behind a developer tools flag; not for vendor-facing use |
| P3 | No "signed out" message after logout | Logout → Login | Add `?signedOut=true` query param and show a brief info message on the login page |
| P3 | Delete draft fires without confirmation dialog | Change Requests list → Delete | Add `IDialogService` confirmation dialog before calling withdraw |
| P3 | Logout button has no visible text label | Header | Add visible "Sign Out" text on desktop; icon-only is ambiguous on mobile |
| P3 | Microcopy inconsistency: "Submit Change Request" vs "Submit Request" | Change request submission | Standardize to "Submit Change Request" throughout |

---

## Recommended Next Steps

1. **Add a sidebar navigation to `MainLayout.razor`** — this is the single highest-impact UX improvement. A vendor should always be able to navigate to Dashboard, Change Requests, and Settings without going back through a hub page. Use `MudNavMenu` with `MudNavLink` items for each section.

2. **Replace hardcoded dashboard stubs with real queries** — `TotalSkus: 42` and `ActiveLowStockAlerts: 0` should either come from real Marten queries or be omitted from the dashboard until the upstream data sources are connected. Showing false data to the PO (or a vendor in a demo) destroys trust faster than showing an empty state.

3. **Hide or properly stub the "Manage Users" button** — a button that does nothing is worse UX than no button. If the feature is coming in a future cycle, disable it with an explanatory tooltip or remove it from this release.

4. **Add a product picker to the change request form** — even a basic list of `VendorProductCatalogEntry` documents filtered by the logged-in tenant, searchable by SKU or name, would dramatically reduce friction for the core vendor workflow.

5. **Test with the CatalogManager role next** — this session used Admin. A follow-up session with `catalog@acmepets.test` (CatalogManager) would reveal whether role-gated flows (user management blocked, change request submission allowed) behave as expected from the vendor's perspective.

6. **Test with the ReadOnly role** — `readonly@acmepets.test` should surface empty states and read-only affordances. Verify that the "Submit Change Request" button is correctly hidden and that the empty state message directs the vendor to contact their Admin.

7. **Connect upstream BCs for a full integration session** — without ProductCatalog.Api, Inventory.Api, and Orders.Api running, the Vendor Portal's real-time features (SignalR alerts, change request approvals) cannot be exercised. Schedule a second session once the environment is fully wired.

