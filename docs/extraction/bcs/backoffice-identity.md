# Backoffice Identity

> **Source folder:** `src/Backoffice Identity/`
> **Status:** Implemented
> **Most recent material milestone:** M32.0 — Backoffice Phase 1 (admin identity + policy-based RBAC)
> **Stub depth:** S1 — to be deepened in S3

## Purpose

Backoffice Identity owns authentication and authorization for internal admin users. It issues JWT bearer tokens that Backoffice (and any other BC requiring admin auth) validates, and it maintains the seven-role RBAC model that Backoffice's policy authorization keys off. Roles range from CopyWriter to SystemAdmin; users carry exactly one role in the current implementation phase.

## Top-level structure

### Aggregates

- EF Core entity model — not event-sourced. Tables: `Users` (`BackofficeUser`).

### Commands

- Authentication: `Login`, `Logout`, `RefreshToken`
- User management: `CreateBackofficeUser`, `ChangeBackofficeUserRole`, `DeactivateBackofficeUser`, `ResetBackofficeUserPassword`
- Read: `GetBackofficeUsers`

### Domain events

Not applicable — Backoffice Identity is implemented on EF Core and does not emit domain events at S1 stub depth.

### Projections

Not applicable — read models are EF Core queries against the entity table.

### Integration events

None at S1 stub depth — Backoffice Identity is queried synchronously by Backoffice; no integration messages are published.

### HTTP / API surface (one line)

`BackofficeIdentity.Api` exposes login / logout / refresh endpoints and admin-user CRUD endpoints consumed by Backoffice.

### Frontend surface (if applicable)

Not applicable — no frontend in this BC.

### Identity / auth posture (if applicable)

JWT Bearer issuer for admin users (separate from Customer Identity's cookie session and Vendor Identity's vendor JWT).

## Prior event modeling

None on file.

## ADRs

- ADR 0031 — Admin Portal Role-Based Access Control Model
- ADR 0032 — Multi-Issuer JWT Strategy

## Source citations (S1 stub)

- `src/Backoffice Identity/`
- `CONTEXTS.md` (section: `Backoffice Identity`)
- `docs/decisions/0031-admin-portal-rbac-model.md`
- `docs/decisions/0032-multi-issuer-jwt-strategy.md`
