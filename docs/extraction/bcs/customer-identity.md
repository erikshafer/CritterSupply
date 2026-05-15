# Customer Identity

> **Source folder:** `src/Customer Identity/`
> **Status:** Implemented
> **Most recent material milestone:** M28.0 — Initial customer identity + storefront authentication
> **Stub depth:** S1 — to be deepened in S2

## Purpose

Customer Identity owns customer accounts and the data that travels with them: profile fields, login credentials, the customer address book, and the address-snapshot mechanism that hands a frozen address to Orders at checkout. It is the source of identity for the customer-facing storefront and supplies address data to Orders, Customer Experience, and (in later phases) Correspondence.

## Top-level structure

### Aggregates

- EF Core entity model — not event-sourced. Tables: `Customers`, `Addresses` (`CustomerIdentityDbContext`).

### Commands

- `CreateCustomer`
- `Login`
- `Logout`
- `AddAddress`
- `UpdateAddress`
- `SetDefaultAddress`

### Domain events

Not applicable — Customer Identity is implemented on EF Core and does not emit domain events at S1 stub depth.

### Projections

Not applicable — read models are EF Core queries against the entity tables.

### Integration events

- `CustomerIdentity.AddressSnapshot` — bidirectional helper contract surfaced to Orders for snapshotting addresses at checkout

### HTTP / API surface (one line)

`CustomerIdentity.Api` exposes login / logout, customer profile reads, and address-book CRUD plus an `AddressSnapshot` query consumed by Orders.

### Frontend surface (if applicable)

Not applicable — no frontend in this BC; consumed by the Customer Experience storefront.

### Identity / auth posture (if applicable)

Cookie-based session authentication (the BC issues the cookie that Storefront and Storefront.Api carry).

## Prior event modeling

None on file.

## ADRs

- ADR 0002 — EF Core for Customer Identity BC
- ADR 0012 — Simple Session-Based Authentication (Dev-Friendly)

## Source citations (S1 stub)

- `src/Customer Identity/`
- `src/Shared/Messages.Contracts/CustomerIdentity/`
- `CONTEXTS.md` (section: `Customer Identity`)
- `docs/decisions/0002-ef-core-for-customer-identity.md`
- `docs/decisions/0012-simple-session-based-authentication.md`
