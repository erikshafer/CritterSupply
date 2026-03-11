# Storefront UX Research Session — Facilitator Guide

**Role:** UX Engineer (Session Facilitator & Observer)  
**Companion document:** [`PARTICIPANT-GUIDE.md`](./PARTICIPANT-GUIDE.md) — give this to the Product Owner  
**Feedback capture:** [`FINDINGS.md`](./FINDINGS.md) — record observations and PO feedback here

---

## Session Purpose

This is an observational UX research session. The Product Owner plays the role of a **real customer** who has an existing CritterSupply account and is visiting the storefront to browse products and complete a purchase. Your job is to:

- Set up the environment and hand the PO only the Participant Guide
- Observe without intervening (let them struggle — that's data)
- Note every hesitation, wrong click, question, and emotional reaction
- Ask probing questions after each task, not during
- Record everything in `FINDINGS.md`

> **Prime directive:** Do not explain how the system works. Do not point to other documentation. Do not answer questions with more than "What would you expect to happen?" or "What are you trying to do right now?"

---

## Pre-Session Checklist

Complete all of these steps before handing the Participant Guide to the PO.

### 1. Start Infrastructure

```bash
cd /path/to/CritterSupply
docker-compose --profile infrastructure up -d
```

Wait for PostgreSQL (port 5433) and RabbitMQ (port 5672) to be healthy before proceeding.

```bash
# Verify both containers are running
docker-compose ps
```

Expected output: `postgres` and `rabbitmq` containers with `Up` status.

---

### 2. Start Customer Identity API (Port 5235)

```bash
dotnet run --project "src/Customer Identity/CustomerIdentity.Api/CustomerIdentity.Api.csproj"
```

**Expected terminal output:**
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5235
```

> **Important:** The Customer Identity API applies EF Core migrations on startup in the Development environment, which seeds the three test users (`alice@critter.test`, `bob@critter.test`, `charlie@critter.test`) automatically. Wait for the migration to complete before proceeding.

---

### 3. Start Product Catalog API (Port 5133)

In a new terminal:

```bash
dotnet run --project "src/Product Catalog/Catalog.Api/Catalog.Api.csproj"
```

**Expected terminal output:**
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5133
```

---

### 4. Start Shopping API (Port 5236)

In a new terminal:

```bash
dotnet run --project "src/Shopping/Shopping.Api/Shopping.Api.csproj"
```

**Expected terminal output:**
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5236
```

---

### 5. Start Orders API (Port 5231)

In a new terminal:

```bash
dotnet run --project "src/Orders/Orders.Api/Orders.Api.csproj"
```

**Expected terminal output:**
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5231
```

---

### 6. Start Storefront BFF (Port 5237)

In a new terminal:

```bash
dotnet run --project "src/Customer Experience/Storefront.Api/Storefront.Api.csproj"
```

**Expected terminal output:**
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5237
```

---

### 7. Start Storefront Web UI (Port 5238)

In a new terminal:

```bash
dotnet run --project "src/Customer Experience/Storefront.Web/Storefront.Web.csproj"
```

**Expected terminal output:**
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5238
```

---

### 8. Seed Product Data

The product catalog starts empty. Before the session, seed at least a handful of products using the Product Catalog API so that the browsing and add-to-cart flows are exercisable.

Use the `.http` file at `docs/DATA-SEEDING.http` in Rider, or run the following curl commands to seed a few representative products:

```bash
# Seed a dog bowl
curl -X POST http://localhost:5133/api/products \
  -H "Content-Type: application/json" \
  -d '{
    "sku": "DOG-BOWL-01",
    "name": "Ceramic Dog Bowl",
    "description": "A premium ceramic dog bowl, dishwasher safe, in three sizes.",
    "category": "Dogs",
    "price": 19.99,
    "stockQuantity": 50
  }'

# Seed a cat toy
curl -X POST http://localhost:5133/api/products \
  -H "Content-Type: application/json" \
  -d '{
    "sku": "CAT-TOY-05",
    "name": "Interactive Cat Laser",
    "description": "Automatic rotating laser toy keeps your cat entertained for hours.",
    "category": "Cats",
    "price": 29.99,
    "stockQuantity": 30
  }'

# Seed a dog treat
curl -X POST http://localhost:5133/api/products \
  -H "Content-Type: application/json" \
  -d '{
    "sku": "DOG-TREAT-10",
    "name": "All-Natural Dog Treats",
    "description": "Grain-free training treats made with real chicken. 6 oz bag.",
    "category": "Dogs",
    "price": 12.99,
    "stockQuantity": 100
  }'

# Seed a bird feeder
curl -X POST http://localhost:5133/api/products \
  -H "Content-Type: application/json" \
  -d '{
    "sku": "BIRD-FEED-03",
    "name": "Classic Bird Feeder",
    "description": "Squirrel-proof hanging tube feeder for small seed mixes.",
    "category": "Birds",
    "price": 24.99,
    "stockQuantity": 20
  }'
```

**Verify:** Navigate to `http://localhost:5238/products` and confirm products load. If the page shows products, seeding was successful.

---

### 9. Seed a Saved Address for the Demo Account

The checkout flow (Step 1) requires the customer to have at least one saved shipping address. The test customer `alice@critter.test` does not automatically have saved addresses — you must seed one before the session.

First, log in as Alice to get her Customer ID, or use the known ID from the Customer Identity seed:

**Alice's Customer ID:** `aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa`

```bash
# Add a home address for Alice
curl -X POST http://localhost:5235/api/customers/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/addresses \
  -H "Content-Type: application/json" \
  -d '{
    "nickname": "Home",
    "street": "742 Evergreen Terrace",
    "city": "Seattle",
    "state": "WA",
    "postalCode": "98101",
    "country": "US",
    "addressType": "Shipping"
  }'

# Add a work address for Alice
curl -X POST http://localhost:5235/api/customers/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/addresses \
  -H "Content-Type: application/json" \
  -d '{
    "nickname": "Work",
    "street": "1 Infinite Loop",
    "city": "Bellevue",
    "state": "WA",
    "postalCode": "98004",
    "country": "US",
    "addressType": "Shipping"
  }'
```

> **If the Customer Identity API does not yet expose an address endpoint**, you may skip this step and note it as a finding: the checkout flow is unexercisable without pre-seeded addresses, and there is no in-app way for a customer to add addresses during checkout.

---

### 10. Verify Everything is Working

Open `http://localhost:5238` in the browser. You should see the CritterSupply home page.

- [ ] Home page loads (pet icon, trust strip, quick-link cards visible)
- [ ] No browser console errors blocking the page
- [ ] You can log in with `alice@critter.test` / `password`
- [ ] Products page shows at least 4–7 products
- [ ] Cart badge appears in the navigation bar (shows 0 on first load)
- [ ] SignalR / real-time connection is established (no visible error)

If any step fails, note it for the session. Do not attempt to fix the problem during the session — blocked tasks are findings.

---

### 11. Log Out Before Handing to the PO

After verifying the environment, log out of any authenticated session so the PO starts from the login page.

---

## Test Account for the Product Owner

Hand the PO their credentials as part of the Participant Guide — they are already printed there. For your reference:

| Field | Value |
|---|---|
| **URL** | `http://localhost:5238` |
| **Email** | `alice@critter.test` |
| **Password** | `password` |
| **Customer Name** | Alice Anderson |

**Why this account?** Alice is the standard development seed customer created automatically by the Customer Identity service on first boot. She represents a returning customer with an existing account, which covers the login → browse → checkout → order history flows we care about in this session.

---

## Session Protocol

### Before You Begin

1. Explain the session goal briefly: *"We're going to look at a shopping site for pet supplies. I want to understand what's intuitive and what isn't. I'll give you a sheet with your login info and a few tasks. Please think out loud as you go — say what you're looking for, what you expected to see, what confused you."*
2. Remind them: *"There are no wrong answers. If you can't figure something out, that's a finding — not a failure on your part."*
3. Hand them **only** the `PARTICIPANT-GUIDE.md`. Do not show them any other documentation.
4. Start your observation notes in `FINDINGS.md`.

### During the Session

- Stay quiet. Take notes.
- If they ask "Is this right?" respond: *"What do you think?"*
- If they get truly stuck and cannot proceed at all: note it, then gently say *"Let's move to the next task."*
- Time each task. Note when they hesitate, backtrack, or look confused.
- Mark any moments when they say something positive out loud — those are also data.

### After Each Task

Ask 1–2 debrief questions:
- *"What were you expecting to see there?"*
- *"Was that what you were looking for?"*
- *"What felt confusing or unexpected?"*
- *"What would have made that easier?"*

### Session Wrap-Up

1. Ask the PO to give a general rating: **"How would you describe the overall experience? Easy, moderate, or difficult?"**
2. Ask: *"If you were a real online shopper, what's the one thing you'd change?"*
3. Ask: *"Anything that surprised you — positively or negatively?"*
4. Record all responses in `FINDINGS.md`.

---

## What to Watch For

These are known areas of interest going into the session. Do not prime the PO about any of these — observe naturally.

| Area | Watch for |
|---|---|
| **Demo credentials banner** | The login page shows demo account emails and the shared password in plain text. Does the PO read it? React to it? Does it break shopper immersion? |
| **Home page orientation** | Does the PO understand the navigation? Are the quick-link cards intuitive? Does the PO find the navigation drawer icon? |
| **Products page browsing** | Does category filtering make sense? Do the product cards provide enough information? Are product images present or broken? |
| **Cart initialization** | The cart is silently initialized on the Products page. Does the PO notice? Does the cart badge appear and feel correct? |
| **Cart badge real-time updates** | After adding items, the badge in the header updates without a page reload. Does the PO notice this? Do they find it reassuring or surprising? |
| **Checkout — address selection** | Does the MudSelect dropdown for saved addresses work correctly in the browser? Is the interaction clear? |
| **Checkout — payment token field** | The payment field label says "Payment Token (stub)." Does the PO understand what to enter? Is the developer language confusing? |
| **Order History stub data** | The Order History page shows hardcoded mock orders. Does the PO realize the data is fake? Does it confuse them? |
| **No address management** | There is no flow in the Storefront to add or edit addresses. If the PO looks for this, note it. |
| **SignalR reconnection** | If the connection drops, a reconnect modal may appear. Note the PO's reaction. |

---

## Known Limitations for This Session

Be aware of these before the session so you can explain them as "known limitations" if they block the PO completely:

1. **No product images in local dev**: Product cards may show a broken image placeholder rather than real photography. This is expected in a development environment.

2. **Payment token is a stub**: The payment field does not connect to a real payment processor. Use `tok_visa_test_12345` as the token value. This is a development-only stand-in for real payment integration (Stripe/PayPal).

3. **Order History is mock data**: The Order History page renders three hardcoded sample orders. These are not real orders from the session and will not include any orders just placed.

4. **Checkout requires saved addresses**: If address seeding failed or the Customer Identity API does not expose an address endpoint, the checkout Step 1 will show "No saved addresses" and block further progress. Note this as a finding.

5. **Order confirmation real-time updates**: Seeing status updates on the order confirmation page requires all downstream BCs (Orders, Payments, Fulfillment) to be running and communicating over RabbitMQ. If only some services are running, the status may not advance past "Placed."

6. **SignalR may briefly show a connection error**: This resolves once the hub connection is established. If it persists, it indicates the BFF is not running.

7. **No account management**: The Account page is read-only. Customers cannot update their name, email, or password from within the Storefront.

---

## Facilitator Observation Prompts

Use these in your notes to structure what you capture:

- **Task completed?** Yes / No / Partially
- **Time to complete:** (seconds / minutes)
- **Hesitations:** Where did the PO pause or re-read?
- **Wrong turns:** What did they click/navigate to that wasn't the intended path?
- **Verbal reactions:** What did they say out loud?
- **Emotional state:** Confident / Uncertain / Frustrated / Surprised / Delighted
- **Post-task quote:** Best verbatim quote from the debrief
