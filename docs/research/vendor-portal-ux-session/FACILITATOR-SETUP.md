# Vendor Portal UX Research Session — Facilitator Guide

**Role:** UX Engineer (Session Facilitator & Observer)  
**Companion document:** [`PARTICIPANT-GUIDE.md`](./PARTICIPANT-GUIDE.md) — give this to the Product Owner  
**Feedback capture:** [`FINDINGS.md`](./FINDINGS.md) — record observations and PO feedback here

---

## Session Purpose

This is an observational UX research session. The Product Owner plays the role of a **real vendor user** logging in to the CritterSupply Vendor Portal for the first time. Your job is to:

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

### 2. Start VendorIdentity.Api (Port 5240)

```bash
dotnet run --project "src/Vendor Identity/VendorIdentity.Api/VendorIdentity.Api.csproj"
```

Wait for startup output confirming the app is listening. The seed data (`VendorIdentitySeedData`) runs automatically on first startup in the `Development` environment, creating the test tenant and users.

**Expected terminal output:**
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5240
```

> **Verify seed data:** After startup, navigate to `http://localhost:5240/api/vendor-identity/...` or just proceed — the seed runs silently on first-time initialization.

### 3. Start VendorPortal.Api (Port 5239)

In a new terminal:

```bash
dotnet run --project "src/Vendor Portal/VendorPortal.Api/VendorPortal.Api.csproj"
```

**Expected terminal output:**
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5239
```

### 4. Start VendorPortal.Web (Port 5241)

In a new terminal:

```bash
dotnet run --project "src/Vendor Portal/VendorPortal.Web/VendorPortal.Web.csproj"
```

**Expected terminal output:**
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5241
```

### 5. Verify Everything is Working

Open `http://localhost:5241` in the browser. You should see the login page for the Vendor Portal.

- [ ] Login page loads
- [ ] No browser console errors blocking the page
- [ ] You can log in with `admin@acmepets.test` / `password`
- [ ] Dashboard loads after login
- [ ] Header shows a green dot ("Live") indicating SignalR is connected

If SignalR shows "Disconnected" — that is fine for this session. Make a note of it. VendorPortal.Api must be running for SignalR to work.

### 6. Log Out

Log out of the admin account before handing the browser to the PO.

---

## Test Account for the Product Owner

Hand the PO their credentials as part of the Participant Guide — they are already printed there. For your reference:

| Field | Value |
|---|---|
| **URL** | `http://localhost:5241` |
| **Email** | `admin@acmepets.test` |
| **Password** | `password` |
| **Role** | Admin |
| **Company** | Acme Pet Supplies |

**Why Admin?** The Admin role gives the PO access to all current features — dashboard, change requests, submission, settings. Using Admin ensures they don't hit permission walls during the core user journey, which would cut the research session short before we observe the important flows.

---

## Session Protocol

### Before You Begin

1. Explain the session goal briefly: *"We're going to look at a new vendor-facing portal. I want to understand what's intuitive and what isn't. I'll give you a sheet with your login info and a few tasks. Please think out loud as you go — say what you're looking for, what you expected to see, what confused you."*
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
2. Ask: *"If you were a real vendor who just logged in for the first time, what's the one thing you'd change?"*
3. Ask: *"Anything that surprised you — positively or negatively?"*
4. Record all responses in `FINDINGS.md`.

---

## What to Watch For

These are known areas of interest going into the session. Do not prime the PO about any of these — observe naturally.

| Area | Watch for |
|---|---|
| **Navigation** | The app has no sidebar. Does the PO know where to go after the dashboard? |
| **Empty state** | The vendor has no products assigned yet. Does the PO understand why the dashboard shows zeros? |
| **Demo credentials banner** | The login page shows demo account emails. Does the PO read it? React to it? Does it break immersion? |
| **Browser refresh = logout** | If the PO refreshes the page, they will be logged out (WASM limitation). Observe their reaction. |
| **"Manage Users" button** | The dashboard shows a "Manage Users" button for Admins, but it links nowhere. Does the PO click it? |
| **SignalR status indicator** | Header shows a colored dot with "Live" label. Does the PO notice it? Know what it means? |
| **Submit Change Request flow** | The SKU field is free-text. Does the PO know what SKU to enter? What happens with an unknown SKU? |
| **Token expiry** | After 15 minutes, the token is refreshed automatically. If the session runs long, does the refresh happen transparently? |

---

## Known Limitations for This Session

Be aware of these before the session so you can explain them as "known limitations" if they block the PO completely:

1. **No products in catalog**: The Acme Pet Supplies tenant has no products assigned to it in this environment. Dashboard will show `0 SKUs`. Change requests can be drafted for any SKU, but the system may reject submission if the SKU isn't in the vendor's catalog.

2. **Manage Users is a stub**: The "Manage Users" button on the dashboard does not navigate anywhere yet. Clicking it does nothing.

3. **Browser refresh = re-login required**: Blazor WASM stores auth in memory. Refreshing the page clears it.

4. **No email notifications**: The notification preference settings are functional UI, but no real emails are sent in this environment.

5. **SignalR may briefly show "Disconnected"**: This resolves once the hub connection is established after login.

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
