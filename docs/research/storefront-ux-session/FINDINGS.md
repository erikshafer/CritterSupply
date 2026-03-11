# Storefront UX Research Session — Findings

**Session Date:** 2026-03-11  
**Facilitator:** UX Engineer  
**Participant:** Product Owner (acting as customer persona — Alice, returning shopper)  
**Customer Account Used:** `alice@critter.test` / `password` (auto-seeded by Customer Identity service on first boot)  
**Environment:** Local development — `http://localhost:5238`  
**Session Method:** Observational UX research. PO given only the Participant Guide; no system documentation shared. UX Engineer observed, took notes, asked post-task debrief questions.

---

## Session Context

The Product Owner played the role of a returning customer — Alice, a pet owner shopping for dog and cat supplies — visiting the CritterSupply Storefront for the first time as a research participant. The session covered ten tasks drawn from the core customer user stories established in previous Event Storming and Event Modeling sessions, and mirroring the scenarios covered by the E2E test suite (`Storefront.E2ETests`).

The session was designed to evaluate the end-to-end shopping experience: authentication, product discovery, cart management, checkout, order confirmation, and account access.

**Why Alice (not a generic test user)?**  
Alice is the standard development seed customer created automatically by the Customer Identity service. She represents a returning customer with an existing account. Her credentials are consistent across all development environments, and her account is always present without additional setup steps. Using Alice ensures the session reflects what any developer or stakeholder would encounter when running the stack locally.

---

## Services & Environment Status

| Service | Status | Notes |
|---|---|---|
| PostgreSQL (5433) | ✅ Running | `docker-compose --profile infrastructure up` |
| RabbitMQ (5672) | ✅ Running | `docker-compose --profile infrastructure up` |
| Customer Identity API (5235) | ✅ Running | EF Core migrations ran on startup; Alice seeded automatically |
| Product Catalog API (5133) | ✅ Running | Products manually seeded via curl before session (4 products) |
| Shopping API (5236) | ✅ Running | No seed data needed; creates carts on demand |
| Orders API (5231) | ✅ Running | No seed data needed; creates orders on demand |
| Storefront.Api BFF (5237) | ✅ Running | SignalR hub active; event handlers subscribed to RabbitMQ |
| Storefront.Web (5238) | ✅ Running | Blazor Server; home page loads correctly |
| Saved addresses for Alice | ⚠️ Seeded manually | Customer Identity API address endpoint used to seed 2 addresses pre-session |
| Login endpoint | ✅ HTTP 200 | Session cookie (`CritterSupply.Auth`, 7-day expiration) returned as expected |
| SignalR hub | ✅ Connects | Real-time cart badge updates confirmed working before session handoff |

> **Note on address seeding:** The Customer Identity service does not automatically seed saved addresses for development users. The facilitator seeded two addresses for Alice (`Home` — 742 Evergreen Terrace, Seattle, WA; `Work` — 1 Infinite Loop, Bellevue, WA) using the Customer Identity API before the session. Without this step, the Checkout Step 1 would have been blocked, skipping the core checkout flow.

---

## Task Results Summary

| # | Task | Completed? | Difficulty | Key Finding |
|---|---|---|---|---|
| 1 | Sign In | ✅ Yes | 🟢 Easy | Demo credentials banner on login page — visible, functional, breaks customer immersion |
| 2 | Get Your Bearings | ✅ Partial | 🟡 Moderate | Hamburger menu icon (☰) for nav drawer not intuitive; quick-link cards served as primary navigation |
| 3 | Browse Products | ✅ Yes | 🟢 Easy | Category filtering works; product cards are clear; no product images in dev (broken placeholder) |
| 4 | Add to Cart | ✅ Yes | 🟢 Easy | Real-time cart badge update delighted the PO; toast confirmation is clear |
| 5 | Review Cart | ✅ Yes | 🟢 Easy | Quantity adjusters and remove work correctly; totals update correctly |
| 6 | Checkout | ✅ Yes | 🟡 Moderate | MudSelect address dropdown worked in browser; payment token field confused the PO |
| 7 | Order Confirmation | ⚠️ Partial | 🟡 Moderate | Order placed and ID shown; real-time status did not advance past "Placed" (Orders/Payments BC latency) |
| 8 | Order History | ⛔ Blocked | 🔴 Difficult | Page shows hardcoded mock orders — not the order just placed; PO was misled |
| 9 | Account Details | ✅ Yes | 🟢 Easy | Read-only account page is clear; PO expected to be able to edit details |
| 10 | Sign Out | ✅ Yes | 🟢 Easy | Account menu logout works; landing page after sign-out is the home page (not login) |

*Difficulty scale: 🟢 Easy · 🟡 Moderate · 🔴 Difficult · ⛔ Blocked*

---

## Task-by-Task Observations

### Task 1 — Sign In

**Completed?** ✅ Yes  
**Time:** ~45 seconds  

**Hesitations / wrong turns:** None on the mechanics. The PO navigated to `http://localhost:5238`, saw the home page, and found the **Sign In** button in the top right navigation bar immediately. They clicked through to the login page without hesitation.

However, the PO immediately read the **demo credentials block** at the bottom of the login form and paused.

**Verbal reactions:**  
> *"Oh — there's a box here that says the usernames and the password right on the screen. That's strange. If I were actually a customer, I'd wonder what's going on. Is this a test site? Is this my actual account?"*

> *"I mean it's helpful that I know what to type, but it feels like I'm looking behind the curtain."*

**Post-task debrief:**  
The PO understood it was a development aid but noted that even in a demo or stakeholder session, this banner would break immersion and undermine trust in the product's polish.

**UXE observations:**  
- The login form itself is clean: email, password, show/hide toggle, and a single "Sign In" CTA. No unnecessary complexity.
- The `autocomplete="username"` attribute on the email field works correctly — browser password managers would pre-fill credentials in production. Good.
- On successful login, a green snackbar *"Welcome back, Alice!"* appears. The PO smiled and noted it felt personal. Positive signal.
- The **demo accounts block** (`<MudAlert Severity="Info">`) displays the email addresses and shared password for all seeded development users in plaintext. It carries a code comment `// POC only — remove before staging deployment`. This must be removed before any real customer interaction with the site, even in a demo or stakeholder review. It destroys the research session's immersion and signals to any real user that something unusual is happening.
- **Post-login redirect:** The app redirects to the home page (`/`), not a "your account" landing page. This is acceptable for a storefront, where the typical entry point after login is the product catalog.

---

### Task 2 — Get Your Bearings

**Completed?** ✅ Partial — PO oriented themselves via the home page cards but did not discover the navigation drawer independently.  
**Time:** ~2.5 minutes  

**Hesitations / wrong turns:**  
- After login the PO landed on the home page and looked at the top navigation bar. They noted the cart badge (showing 0), the account menu, and then looked to the left side of the screen expecting a sidebar or nav links.
- The PO looked at the hamburger icon (`☰`) in the top-left of the app bar for ~10 seconds but did not click it. *"That icon — is that a menu? Maybe."* Eventually the PO clicked the four **quick-link cards** in the center of the home page to understand navigation options (Browse Products, Shopping Cart, Checkout, Order History).
- The PO did not discover the hamburger drawer nav until Task 3, when they accidentally clicked it while heading for "Browse Products."

**Verbal reactions:**  
> *"Okay, so there's a header with my cart and my name. And then there's these tiles in the middle — Browse Products, Cart, Checkout, Orders. That's kind of like my menu, I guess."*

> *"The little icon up top left — is that the navigation? [pause] I'll just use these cards for now."*

> *"Oh it's a dog with a paw print. [pointing at the logo] That's cute. I get the brand."*

**Post-task debrief:**  
- *"Did you feel like you understood how to navigate the site?"* → **"Partially. The cards in the middle helped. But I'd expect a real nav bar with links across the top or a sidebar menu I can always see. The hidden drawer felt accidental."**

**UXE observations:**  
- The **home page quick-link cards** (Browse Products, Shopping Cart, Checkout, Order History) function as the primary navigation for first-time visitors, which is fine for a landing hub. But once the user is on any other page, those cards disappear — there is no persistent secondary navigation to fall back on.
- The **hamburger icon** (`☰`) in the top-left opens a `<MudDrawer>` slide-out panel with nav links: Home, Browse Products, Cart, Checkout, Orders. This is a common mobile pattern but feels unconventional on desktop, where users expect a persistent sidebar or a top nav bar with labeled links. The icon is small and lacks a label; several users would not discover it without prompting.
- The **top app bar** has: hamburger icon (left), CritterSupply logo + wordmark (center), cart badge icon, and account menu (right). The layout is reasonable, but on a 1080p desktop display, there is a lot of unused horizontal space where top-level navigation links would normally live.
- The **trust strip** (Free Shipping / Easy Returns / Secure Checkout / Expert Support) is visible below the hero text. The PO glanced at it but didn't comment. It serves the right purpose but is generic — no specific value propositions for a pet supply store.
- **The pet icon on the hero** — the PO noticed and reacted warmly. The branding is landing.

---

### Task 3 — Browse Products

**Completed?** ✅ Yes  
**Time:** ~1.5 minutes  

**Hesitations / wrong turns:**  
- The PO navigated to the Products page from the home page quick-link cards without difficulty.
- On the Products page, the PO immediately spotted the **category filter chips** at the top and clicked **"Dogs"** successfully.
- The PO paused for a moment when looking at the product cards. *"These don't have pictures."* The product images showed broken `<img>` placeholders — gray boxes with no alt text shown in browser default broken-image style.

**Verbal reactions:**  
> *"Good — I can filter by Dogs. That makes sense. What categories are there? Dogs, Cats, Fish, Birds, All. Okay."*

> *"Why don't the products have pictures? That's kind of important for shopping."*

> *"The prices are there. The names are there. The categories are there. But there's no photo. In real e-commerce, photos are the first thing I look at."*

**Post-task debrief:**  
- *"Could you make a purchasing decision from these cards?"* → **"Not really, no. Name and price are there, but I need to see what I'm buying. Even a placeholder illustration would help. The broken image icon looks broken."**
- *"What else would you want to see?"* → **"Star ratings would be nice. And whether it's in stock — oh, I see there's a stock status badge, good. But I'd want reviews."**

**UXE observations:**  
- The **product cards** include: product name, category chip, price, stock status badge (Active/InStock), and an "Add to Cart" button. This is a functional minimum for a dev environment but would not be usable in production without product photography.
- The **broken image placeholder** is rendered with a generic browser broken-link icon and no alt text to explain the missing image. This is a visual regression — even in development, a styled placeholder image (e.g., a pet silhouette or the CritterSupply logo mark) would be more professional and realistic for a research session.
- **Category filter chips** are clear and functional. Selecting "Dogs" filters correctly. Deselecting returns to All. The active chip styling (filled vs. outlined) is readable.
- **Pagination** exists (20 items per page) but with only 4 seeded products, it was not exercised. The pagination UI was not visible with fewer than one page of results.
- **No search bar** is visible on the Products page. The PO did not explicitly ask for it during this task, but searching by name is a core e-commerce pattern. Its absence will surface in future sessions with more products.
- **Stock status badge** shows "Active" which is a domain term, not a consumer-facing term. A customer would expect to see "In Stock" or "Available." The ubiquitous language for domain events should not bleed directly into consumer-facing copy.

---

### Task 4 — Add Items to Cart

**Completed?** ✅ Yes  
**Time:** ~1 minute  

**Hesitations / wrong turns:** None. The PO clicked **"Add to Cart"** on the Ceramic Dog Bowl card immediately. A green success snackbar appeared. Then the PO looked up at the cart badge in the top navigation bar.

**Verbal reactions:**  
> *"Oh! The number on the cart icon went from 0 to 1 without me refreshing the page. I like that. That's how it should work."*

> *"Let me grab this cat laser too."* [clicks Add to Cart on the cat toy] *"Cart shows 2 now. Great."*

**Post-task debrief:**  
- *"Was adding to cart as expected?"* → **"Yes, actually better than expected. The live cart counter update is a nice touch. On some sites I have to go back to the cart to see if it worked."**

**UXE observations:**  
- The **Add to Cart snackbar** is green (`Severity.Success`) with a clear message. Good.
- The **real-time cart badge update** via SignalR (or SSE) is working as intended. From a shopper's perspective, this is a genuinely delightful interaction — the number increments instantly without page reload. The PO specifically called it out as a positive surprise.
- **One caution:** The real-time update mechanism (as documented in `Storefront.Api/README.md`) broadcasts cart events to **all connected clients**, not just the authenticated customer. In a single-user local dev session this is not observable as a problem, but in production, Customer A would see their cart badge update when Customer B adds to their own cart. This is a P0 privacy/security finding that must be resolved before any multi-user exposure.
- The cart initialization happens **silently** on first load of the Products page — the BFF creates a new cart and stores the `cartId` in `localStorage`. The PO was not aware this was happening, which is correct — it should be transparent. But it means a customer who navigates to the Cart page **without first visiting Products** may encounter an empty cart state (no `cartId` in `localStorage`). This was not tested in this session.

---

### Task 5 — Review Cart

**Completed?** ✅ Yes  
**Time:** ~1.5 minutes  

**Hesitations / wrong turns:**  
- The PO navigated to the Cart page via the cart badge icon. The cart loaded quickly with both items.
- The PO tested the **quantity adjuster** (+ and − buttons on the line items). Incrementing a quantity updated the line total and the cart summary correctly. The PO tested removing an item with the trash icon — a red snackbar confirmed removal and the item disappeared.

**Verbal reactions:**  
> *"This is pretty standard cart UI. I can see my items, the quantities, individual prices, and a total. Good."*

> *"The quantity plus/minus works. Does the cart total update? [clicks +] Yes it does."*

> *"Where's the 'Save for Later' option? Or a wishlist? I sometimes don't want to buy something right now but I don't want to lose it either."*

**Post-task debrief:**  
- *"Did anything feel missing?"* → **"Wishlist or save for later would be useful. Also, I don't see an estimated delivery date anywhere — even a rough range. That often influences the purchase decision."**
- *"Was the cart summary clear?"* → **"Yes, subtotal and total are clearly labeled. I notice shipping isn't calculated until checkout. That's industry standard, but a 'Shipping from $X.XX' hint would be nice."**

**UXE observations:**  
- Cart layout follows a standard e-commerce pattern: line items (image, name, price, quantity adjuster, remove) on the left; order summary (subtotal, shipping TBD, total) on the right. Structurally correct.
- The **broken product image** issue from the Products page carries through to the cart. Cart line items show the same broken-image placeholder.
- The **"Proceed to Checkout" button** is prominent and clearly labeled. Good.
- No **estimated delivery range**, no **"save for later"**, and no **promo code / coupon field** — all omissions that are acceptable for the current stage but would be expected in a production consumer storefront.
- The cart's **real-time SSE updates** (updating when items change in the background) were not directly tested by the PO in this task. This feature exists and was partially verified by the UXE during setup.

---

### Task 6 — Checkout

**Completed?** ✅ Yes  
**Time:** ~4 minutes  

**Hesitations / wrong turns:**  

**Step 1 — Shipping Address:**  
- The PO clicked "Proceed to Checkout" from the Cart page and arrived at the Checkout page.
- The checkout showed a **MudStepper** with 4 steps: Shipping Address, Shipping Method, Payment, Review & Submit.
- Step 1 displayed a **MudSelect dropdown** labeled "Saved Addresses." The PO opened the dropdown and saw two options: `Home — 742 Evergreen Terrace, Seattle WA` and `Work — 1 Infinite Loop, Bellevue WA`. Correctly selected "Home."
- The PO clicked "Save & Continue" without hesitation.

**Step 2 — Shipping Method:**  
- The PO saw three radio options: Standard Ground ($5.99), Express Shipping ($12.99), Next Day Air ($24.99).
- The PO selected Express without hesitation, then paused: *"Does the total update somewhere? I don't see it changing."*
- The order summary panel on the right **did update** the shipping cost when the method was changed. The PO had not noticed the right panel. After the UXE silently pointed toward the panel by glancing at it, the PO saw it and noted the update.
- Clicked "Save & Continue."

**Step 3 — Payment:**  
- The PO saw a text field labeled **"Payment Token (stub)"** with placeholder text `tok_visa_test_12345`.
- The PO paused for ~30 seconds.

**Verbal reactions (Step 3):**  
> *"Payment Token? What is a payment token? Is this like a gift card code? Is it a card number?"*

> *"It says 'stub' in the label — does that mean it's not real? So I just type whatever?"*

> *"I see the placeholder says 'tok_visa_test_12345' — I'll just use that. But honestly, if I were a real shopper I would have closed this page by now."*

- The PO typed `tok_visa_test_12345` and clicked "Save & Continue."

**Step 4 — Review & Submit:**  
- The order summary showed selected address, shipping method, items, and total. The PO reviewed it.
- *"This is a good summary. I can see everything before I commit. Nice."*
- The PO clicked "Place Order" and received a success confirmation.

**Post-task debrief:**  
- *"What part of checkout gave you trouble?"* → **"That payment field. 'Payment Token (stub)' is developer language. A real shopper would have no idea what to type. It should look like a card number field, or at least say 'Enter your payment method.' And the word 'stub' should never appear in a UI that a customer sees."**
- *"The step process — was it clear?"* → **"Yes. Four steps, I always knew where I was. The stepper UI is good. I liked seeing the review at the end before committing."**

**UXE observations:**  
- The **MudStepper** (4-step linear wizard) is well-suited for checkout. The linear progression enforces correct ordering (address before shipping before payment) and gives the user a clear sense of progress. The PO found this intuitive.
- **Step 1 — Address selection:** The MudSelect dropdown works correctly in a real browser (Blazor Server + browser interaction). The E2E tests note that this dropdown fails in Playwright headless mode, but in a real browser session it opens and selects correctly. The auto-selection of the first saved address (`_selectedAddressId = _checkoutView.SavedAddresses.First().AddressId`) is a good UX choice — reduces friction for users with a primary address.
- **Step 2 — Shipping method:** The radio group for shipping options is the right pattern. However, the order summary panel's shipping cost update is not sufficiently surfaced. The panel is on the right column but the stepper and its "Save & Continue" button are on the left. A customer focused on the left column would not naturally look right after changing a radio selection. Consider adding inline cost feedback near the radio button labels (e.g., "Express Shipping — $12.99 added to your total").
- **Step 3 — Payment token field:** This is the most significant usability failure in the checkout flow. The field label "Payment Token (stub)" exposes two developer-facing concepts to the end user:
  - **"Token"**: a technical term for a payment instrument identifier. Real shoppers think "card number," "PayPal," or "Apple Pay," not "token."
  - **"(stub)"**: explicitly signals that the feature is not real, which destroys the session's authenticity and would alarm any real shopper who encountered it.
  - The placeholder `tok_visa_test_12345` compounds the problem — it looks like a developer test value, not a real payment entry.
  - **Minimum fix:** Change the label to "Payment Method" and the placeholder to something like "Credit card (payment integration coming soon)." Hide the word "stub" from the UI entirely.
- **Step 4 — Review & Submit:** Clean and complete. Showing the full order summary before the final submit is a best practice and the PO explicitly praised it.

---

### Task 7 — Order Confirmation

**Completed?** ⚠️ Partial  
**Time:** ~2 minutes  

**Hesitations / wrong turns:**  
- After placing the order, the PO was redirected to `/order-confirmation/{orderId}`.
- The page displayed the order ID, a status indicator labeled **"Placed"**, and a "What's Next?" section describing the review and shipping process.
- The PO waited for ~60 seconds expecting the status to advance. It remained at "Placed."

**Verbal reactions:**  
> *"Okay, my order number is here. That's good. Status says 'Placed.' I guess that makes sense."*

> *"Does this page update on its own? Or do I need to refresh? [waits] It's just sitting at 'Placed.' Is that normal?"*

> *"The 'What's Next' section is helpful — it tells me what to expect. But I'd also want to know: when will I get an email? Where can I go to track this later?"*

**Post-task debrief:**  
- *"Did the confirmation give you confidence that your order went through?"* → **"Yes, the order number is there and it says 'Placed,' so I believe it. But 'Placed' and then nothing — I expected to see it move to 'Processing' or 'Payment Confirmed' pretty quickly. On Amazon or similar sites that happens within seconds."**
- *"What was missing?"* → **"A 'you'll receive a confirmation email' line. Even if emails aren't set up, I expect that message. And a 'Continue Shopping' button so I don't have to navigate away manually."**

**UXE observations:**  
- The order confirmation page is structurally correct: order ID, status, "What's Next" copy. The SignalR connection for real-time status updates (`OrderStatusChanged`, `ShipmentStatusChanged`) is wired, but advancing past "Placed" requires the Orders BC to emit a `PaymentConfirmed` event, which requires the Payments BC to process and respond. In this dev session with only partial BC coverage, that chain did not fire within the session window.
- The **status label "Placed"** is the initial domain state. From a consumer perspective, a more active-sounding label like "Order Received" or "We've Got Your Order!" would feel more reassuring than the internal domain state name.
- **No "Continue Shopping" affordance** on the confirmation page. After a purchase, many customers want to keep browsing. A prominent secondary link back to Products would serve this intent.
- **No email confirmation message** — even the expectation-setting copy ("We'll send you a confirmation email") is absent. In a production storefront, email is a primary order status channel. Its absence on the confirmation page leaves a trust gap.
- **No "Track Your Order" link** on the confirmation page. Order History is the closest analog, but the PO could not navigate there from the confirmation page without using the drawer nav.

---

### Task 8 — Order History

**Completed?** ⛔ Blocked (by misleading stub data)  
**Time:** ~3 minutes  

**Hesitations / wrong turns:**  
- The PO navigated to Order History via the navigation drawer or header menu.
- The page loaded and showed **three hardcoded order rows**: order numbers like `#1001`, `#1002`, `#1003` with dates from several months ago, statuses (Delivered, Shipped, Processing), and dollar amounts.
- The PO looked at the list carefully. *"Wait, these aren't the orders I just placed."*
- The PO scrolled down looking for their recent order. It was not there.

**Verbal reactions:**  
> *"These orders are from October and November. I just placed an order a minute ago. Where is it?"*

> *"Is this showing someone else's orders? Or are these examples? I'm confused."*

> *"If this is my account, why are there orders I didn't make? Is this a demo? Did I log into a shared account?"*

**Post-task debrief:**  
- *"What did you expect to see on this page?"* → **"My orders. The one I just placed. Maybe some prior ones if the account has history. Not three random orders that I didn't make."**
- *"Would a real customer understand what they're looking at?"* → **"No. They'd think something is wrong with their account. This is the kind of thing that causes support calls."**

**UXE observations:**  
- **Order History is a hardcoded stub.** The page renders three sample rows with fixed order IDs, dates, statuses, and totals. These are not database-backed — they are hard-coded in the Razor component. There is no integration with the Orders BC.
- This is **the single most disorienting finding of the session.** Stub data that looks like real historical order data — with realistic order numbers, dates in the recent past, and dollar amounts — is indistinguishable from real data without domain context. A real shopper would question the integrity of their account.
- The appropriate interim solution is one of:
  1. **Remove the Order History page** from the navigation until it is backed by real data.
  2. **Show an empty state** ("You haven't placed any orders yet. Start shopping →") until the Orders BC integration is implemented.
  3. **Add a clearly visible development banner** ("⚠️ Sample data only — not connected to real orders") visible only in the Development environment.
- Neither option 1 nor 3 is production-acceptable. Option 2 — an honest empty state — is the correct customer-facing behavior until real data flows. Even with a real order just placed, the Storefront BFF does not currently query the Orders BC to compose the Order History view.

---

### Task 9 — Account Details

**Completed?** ✅ Yes  
**Time:** ~30 seconds  

**Hesitations / wrong turns:** The PO navigated to the Account page via the top-right account menu (`Account` link). The page loaded quickly with the customer's name, email, and customer ID displayed.

**Verbal reactions:**  
> *"Okay, this shows my name and email. Alice Anderson. alice@critter.test. My Customer ID is a big GUID, which is weird to show a customer — I don't need to know my internal ID."*

> *"Can I change my email here? Or update my password? [looks around] No. It's all just displayed, nothing's editable."*

> *"Where are my saved addresses? I have two addresses set up — I used one in checkout — but they're not showing here."*

**Post-task debrief:**  
- *"What would you expect an Account page to have?"* → **"Name, email, password change, saved addresses, maybe payment methods. This is just a profile card. It's a start, but there's no way to manage anything."**
- *"The Customer ID being shown — what do you think of that?"* → **"I'd hide it. Customers don't know or care about their internal database ID. It's fine for a dev environment but it needs to go."**

**UXE observations:**  
- The Account page is explicitly described as read-only in the code (`@* Read-only account details *@`). It extracts claims from the auth cookie and displays name, email, and `CustomerId`.
- Displaying the **raw `CustomerId` GUID** to a consumer is a UX and arguably a minor security-hygiene issue. The internal identifier is not meaningful to a user and exposes internal system details unnecessarily.
- **No address management** is available anywhere in the Storefront. The two saved addresses that Alice has (seeded by the facilitator before the session) are invisible to her outside of the checkout address dropdown. A customer has no way to add, edit, or remove addresses without contacting support.
- **No password change, no email update, no saved payment methods.** For a v1 authenticated storefront these are acceptable gaps, but they represent the minimum viable Account section a customer expects.
- The link to Order History and Cart from the Account page (if present) is a helpful secondary affordance but does not compensate for the lack of account management features.

---

### Task 10 — Sign Out

**Completed?** ✅ Yes  
**Time:** ~15 seconds  

**Hesitations / wrong turns:** The PO opened the account menu in the top-right of the navigation bar and clicked **"Logout."** The session was cleared and the browser navigated to the home page.

**Verbal reactions:**  
> *"Easy. The Logout option is right in the account menu where I'd expect it."*

> *"It went back to the home page, not the login page. That's fine — I was done anyway. But on most sites, sign-out goes to login so I can sign in as someone else if needed."*

**Post-task debrief:**  
- *"Was the sign-out experience complete?"* → **"Yes, except there was no 'You have been signed out' message. It just quietly went to the homepage. A confirmation would be reassuring — especially if I wasn't sure I'd clicked the right thing."*

**UXE observations:**  
- The logout endpoint clears the authentication cookie and redirects to `/` (home page). This is a reasonable choice for a consumer storefront — returning to the home page is more natural than returning to a login wall.
- **No sign-out confirmation message.** Post-logout, the home page shows with no contextual message. Adding a brief snackbar ("You have been signed out") or a query parameter (`?signedOut=true`) that triggers a transient message would confirm the action.
- The account menu (accessed via the user's name in the top-right) is well-organized: Account, Order History, Logout. Correct affordance grouping.
- **The home page after sign-out** still shows the CritterSupply branding and quick-link cards, but now presents the "Sign In" button instead of the account menu. The transition is seamless.

---

## PO Overall Impressions (Closing Debrief)

**Overall rating:** Moderate — better than expected for a first look, but several rough edges that matter

**"If you were a real online shopper, what's the one thing you'd change?"**  
> *"Fix the payment field. 'Payment Token (stub)' would have driven me away. That's the most important thing between here and something I'd show to a real customer."*

**"Anything that surprised you — positively or negatively?"**  
> *"Positively: the live cart badge update. That was genuinely impressive and felt like a polished feature, not a prototype. It works the way I'd expect a modern e-commerce site to work.*

> *Negatively: the Order History page. I expected to see the order I just placed. Finding fake orders from last October was jarring. If I were a real customer I'd be on the phone asking what happened to my account."*

**"If you were going to rate the system overall on a scale of 1–5 for readiness for a real customer?"**  
> *"2.5. The shopping flow — browse, add to cart, checkout — works end-to-end and feels reasonably polished. Login, cart management, and the checkout stepper are solid. But the Order History stub, the payment token label, and the missing product images would all need to be fixed before I'd show this to anyone outside the team."*

---

## What Worked Well

- **Login flow** is smooth, fast, and immediately personalizing. The "Welcome back, Alice!" snackbar on login is a small but effective touch. The email/password form is clean and uncluttered.
- **Real-time cart badge updates** (via SignalR/SSE) worked exactly as expected and delighted the PO. The live count increment without a page reload is a modern e-commerce expectation, and this implementation delivers it correctly.
- **Category filtering on the Products page** is intuitive. Chip-style filter buttons, clear active state, instant filter response — all well executed.
- **Add to Cart snackbar confirmations** give immediate, clear feedback for every cart action (add, remove, quantity change). The color coding (green for success, red for removal) is appropriate.
- **Cart quantity adjusters** (+ / − controls per line item) work correctly and update the order summary in real time. Removal with the trash icon also works and is confirmed with a toast.
- **Checkout stepper (4 steps)** is the right UX pattern for a multi-stage checkout. The PO never lost their place in the process. The step titles (Shipping Address, Shipping Method, Payment, Review & Submit) are correctly sequenced.
- **Order review step** (Step 4 of checkout) shows a complete summary of the order before submission. The PO explicitly called this out as a positive trust signal.
- **Account menu** in the top-right is correctly organized (Account, Order History, Logout). Discoverable and correctly labeled.
- **MudBlazor component library** gives the storefront a professional, consistent look. The material design language is familiar to shoppers from other modern consumer apps.

---

## What Didn't Work / Issues Found

| Severity | Issue | Root Cause | Fixed? |
|---|---|---|---|
| 🔴 P0 (security) | SignalR/SSE cart events broadcast to ALL connected clients | `StorefrontHub` has no customer isolation — all subscribers receive all `cart-updated` events regardless of which customer's cart changed. See `src/Customer Experience/Storefront.Api/StorefrontHub.cs` and the Production Blockers section of `Storefront.Api/README.md` | ❌ Not yet fixed — documented in `Storefront.Api/README.md` as production blocker |
| 🔴 P1 | Order History shows hardcoded fake orders | `OrderHistory.razor` renders three static mock rows; no integration with Orders BC | ❌ Not yet implemented |
| 🟡 P1 | Payment field label "Payment Token (stub)" — developer language visible to consumer | Field uses internal terminology; placeholder `tok_visa_test_12345` reinforces test/dev framing | ❌ Not yet fixed |
| 🟡 P1 | Product images broken / missing in dev environment | No image seeding; `<img src>` points to placeholder or non-existent URLs in development | ❌ Requires image seeding or styled fallback |
| 🟡 P2 | Demo credentials banner on the login page | `<MudAlert>` block on `Login.razor` shows all dev account emails + shared password in plaintext | ❌ Marked as POC-only in code; must be removed before any stakeholder/customer exposure |
| 🟡 P2 | No "Continue Shopping" link on Order Confirmation page | Page has no secondary affordance back to Products after a successful order | ❌ Missing |
| 🟡 P2 | Customer ID (GUID) displayed on Account page | `Account.razor` displays the internal `CustomerId` claim, which is not meaningful to a consumer | ❌ Should be hidden |
| 🟡 P2 | No address management in the Storefront | Customers cannot add, edit, or remove saved addresses from within the Storefront UI | ❌ Not yet implemented |
| 🟡 P2 | Order status does not advance from "Placed" in a typical dev session | Advancing order status requires Orders BC → Payments BC → back to Orders → event emission over RabbitMQ — all services must run and events must flow | ❌ Service integration gap; expected in current development stage |
| 🟢 P3 | Nav drawer (hamburger icon) is not discoverable on desktop | The `☰` icon is small, unlabeled, and unconventional on desktop; most users expect a persistent sidebar or top nav links | ❌ Navigation architecture needs revisiting |
| 🟢 P3 | No sign-out confirmation message | Logout returns to `/` with no feedback message confirming the session ended | ❌ Minor polish gap |
| 🟢 P3 | "Active" stock status label is domain language, not consumer language | `Active` is shown on product cards; customers expect "In Stock" or "Available" | ❌ Copy/label gap |
| 🟢 P3 | Shipping cost not previewed before checkout | Cart page shows "Shipping: TBD" with no "from $X.XX" hint; customer must enter checkout to see cost | ❌ Minor friction |
| 🟢 P3 | No "save for later" / wishlist feature | Customers cannot save items without adding to cart; no wishlist affordance | ❌ Not yet designed |
| 🟢 P3 | Shipping method cost update not visually connected to stepper action | Changing shipping radio selection updates the right-side summary panel, but the panel is not in the PO's primary focus area while interacting with the stepper | ❌ Layout/feedback gap |

---

## Confusing Points

1. **"Payment Token (stub)" label** — the single most disorienting moment of the session. A real customer would not know what to enter. The word "stub" signals a broken or incomplete feature. This must be resolved before any real-user demonstration.

2. **Order History with fake/hardcoded data** — the PO questioned the integrity of their account when they saw three unfamiliar orders. This is worse than an empty state — it implies the system already has data about you that you didn't create. An honest empty state is always preferable to misleading placeholder data.

3. **Demo credentials on the login page** — immediately noticed and called out. It breaks shopper immersion and raises questions about whether this is a real site or a test environment. At minimum, remove it from any session where someone is role-playing as a customer.

4. **Cart badge update with no cart page** — the PO added items and noticed the badge update, but if they had navigated directly to the Cart page without first visiting Products (which initializes the cart), they would have hit an empty cart state. The cart initialization side effect being hidden inside the Products page load is a fragile UX dependency.

5. **Shipping method cost change not highlighted** — when the PO selected Express shipping, the order summary panel updated, but the PO didn't notice until prompted. The feedback loop between a stepper action and its side effect on the summary panel needs stronger visual connection.

6. **No post-confirmation path** — after placing the order, the PO was "stuck" on the confirmation page with no obvious next action. No "Continue Shopping," no "Track Order," no "View Order History" button visible on the page.

---

## UXE Observations (Independent of PO Feedback)

**Accessibility:**
- The navigation drawer (`<MudDrawer>`) should have `aria-label="Main navigation"` on its toggle button. The current hamburger `<MudIconButton>` may not have an accessible label.
- The cart badge (`<MudBadge>`) should have an `aria-label` like "Shopping cart, 2 items" so screen readers announce the count. Currently, a screen reader user tabbing to the cart icon would hear "Cart" without the count.
- The checkout stepper steps should be announced with `aria-live` or `role="status"` when they advance, so keyboard/screen reader users know they've moved to the next step.
- Color is used to differentiate order statuses on the Order History page (green for Delivered, yellow for Shipped, grey for Processing). There is no icon or text-only fallback for colorblind users.
- The `<MudAlert>` demo credentials banner on Login has `Severity="Info"` but no `aria-live` attribute. Screen readers may not announce it on page load.

**Interaction design:**
- The `_isProcessing` loading state on checkout step buttons (spinner + `Disabled="@_isProcessing"`) correctly prevents double-submits. Well done.
- The Product card "Add to Cart" button lacks a `data-testid` attribute consistent with other E2E-testable elements. This is a test hygiene gap.
- The cart quantity adjuster (+ / −) does not have a minimum value guard visible in the UI — can the quantity go below 1? If so, does that remove the item? If not, is the − button disabled at 1? This edge case was not tested during the session.
- The checkout "Place Order" button on Step 4 does not disable immediately on click — if the API is slow, a fast user might double-click. The `_isProcessing` guard should be applied here as it is on earlier step buttons.

**Microcopy:**
- `"Active"` on product stock status badges → should be `"In Stock"` for a consumer-facing store.
- `"Payment Token (stub)"` → minimum fix: `"Payment Method"` (remove "stub" entirely; add a helper line: "Enter your payment card token").
- `"Placed"` order status on the confirmation page → consider `"Order Received"` or `"Processing"` for a warmer, more active-sounding consumer label.
- Cart snackbar: `"Item added to cart"` — clear. ✅
- Cart snackbar: `"Item removed"` — consider `"Removed from cart"` for specificity.

**Consistency gaps:**
- The Home page quick-link cards say "Shopping Cart" but the nav drawer and app bar both say "Cart." Pick one term and use it consistently.
- "Order History" is used in the account menu, nav drawer, and page title — good consistency. But the confirmation page has no link to it.
- The `MainLayout.razor` footer just shows copyright text. No standard e-commerce footer links (Help, Returns, Contact Us, About). These don't need to be functional now, but their total absence makes the site feel thin.

---

## Prioritized Findings

| Priority | Finding | Affected Flow | Suggested Fix |
|---|---|---|---|
| P0 (security) | SignalR/SSE broadcasts cart events to all clients — Customer A sees Customer B's updates | Cart real-time updates | Add customer ID scoping to `StorefrontHub` — send events only to the authenticated customer's connection group |
| P1 | Order History shows hardcoded fake orders | Order History | Replace stub rows with either a real Orders BC query or an honest empty state |
| P1 | "Payment Token (stub)" label on checkout Step 3 | Checkout — Payment | Change label to "Payment Method"; remove word "stub"; update placeholder to neutral text |
| P1 | Product images broken/missing | Products, Cart | Add styled placeholder image (e.g., category silhouette) for dev; require real image seeding for demos |
| P2 | Demo credentials banner on login page | Login | Remove `<MudAlert>` dev credentials block before any non-developer session |
| P2 | No "Continue Shopping" or "Track Order" on Order Confirmation | Post-checkout UX | Add secondary CTAs: "Continue Shopping → /products" and "View Order History → /orders" |
| P2 | Customer ID (GUID) shown on Account page | Account | Remove or hide `CustomerId` from the Account view; it is not a consumer-meaningful field |
| P2 | No address management in the Storefront | Account / Checkout | Build an address management section on the Account page (add, edit, remove saved addresses) |
| P2 | Nav drawer not discoverable on desktop | Global navigation | Add visible, labeled navigation links to the top app bar; retain drawer for mobile |
| P3 | No sign-out confirmation message | Sign out | Add a brief snackbar or query-param-triggered message on the home page post-logout |
| P3 | "Active" stock status — domain language exposed to consumer | Product cards | Replace "Active" with "In Stock"; reserve domain terms for internal/admin views |
| P3 | Shipping cost not previewed on Cart page | Cart → Checkout | Add "Shipping from $5.99" hint line in the cart order summary |
| P3 | Shipping method selection feedback not visible | Checkout — Shipping | Add inline cost label update near radio buttons when shipping method changes |
| P3 | Cart quantity below 1 behavior undefined in UI | Cart | Disable − button at quantity = 1; or prompt to remove item; test this edge case |
| P3 | No wishlist / save for later | Products, Cart | Design wishlist feature (future cycle) |

---

## Recommended Next Steps

1. **Fix the Order History page.** Replace the hardcoded mock rows with either:
   - A real query from the Orders BC (via the BFF) scoped to the authenticated customer's ID, or
   - An honest empty state until the query is implemented: *"Your order history will appear here once you've placed an order."*
   
   This is the most trust-damaging issue in the current Storefront. Misleading data is worse than no data.

2. **Fix the payment field label.** Change "Payment Token (stub)" to something a customer can understand. The placeholder should not be `tok_visa_test_12345` in any session where someone outside the dev team will interact with the UI. This is a label change, not a feature — it can be done in minutes.

3. **Resolve the SignalR/SSE customer isolation issue.** The `StorefrontHub` currently broadcasts all events to all subscribers. In multi-user environments this is a privacy and data integrity issue. Scope events to the authenticated customer by adding the customer to a connection group keyed by their customer ID, and publishing only to that group.

4. **Add a styled placeholder for missing product images.** Even a simple SVG placeholder (cat silhouette, dog paw, or the CritterSupply logo mark) is dramatically more professional than the broken-image browser default. This applies to both the Products page cards and Cart line items.

5. **Remove the demo credentials banner from `Login.razor`** before any session where someone is role-playing as a real user. The code already has a comment marking it as POC-only. The `<MudAlert>` block is the only change needed to remove it for non-developer sessions.

6. **Add post-checkout navigation to the Order Confirmation page.** At minimum: a "Continue Shopping" button linking to `/products` and a "View Order History" link to `/orders`. Leaving a customer with no next action after a successful purchase is a UX dead end.

7. **Begin Address Management design.** Customers currently have no way to add or edit shipping addresses from within the Storefront. This is not blocking for early demo sessions (addresses can be seeded), but it blocks production readiness. The Account page is the right location for a "Saved Addresses" section.

8. **Revisit the global navigation architecture.** The current hamburger drawer is appropriate for mobile but insufficient for desktop. Consider adding labeled navigation links to the top app bar (Products, Cart, Orders), retaining the drawer for narrow viewports.

9. **Run this session with a new participant and no prior e-commerce product context.** The Product Owner's industry experience made them gracious about developer artifacts. A participant with less e-commerce background would likely fail earlier and more abruptly on the payment token field and the Order History stub data.
