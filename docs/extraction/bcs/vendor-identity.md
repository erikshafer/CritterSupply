# Vendor Identity

> **Source folder:** `src/Vendor Identity/`
> **Status:** Implemented
> **Most recent material milestone:** M44.0 — Vendor Portal test-fixture hardening (Vendor Identity contract surface stable)
> **Stub depth:** S1 — to be deepened in S3

## Purpose

Vendor Identity owns vendor authentication, multi-tenant vendor accounts, and the user-invitation workflow that brings new vendor users onto a tenant. It issues JWT bearer tokens consumed by Vendor Portal and (via the multi-issuer JWT setup) any other BC that needs vendor identity. Tenants can be created, suspended, reinstated, or terminated; users can be invited, activated, deactivated, reactivated, or have their role changed; invitations can be resent or revoked.

## Top-level structure

### Aggregates

- EF Core entity model — not event-sourced. Tables: `Tenants` (`VendorTenant`), `Users` (`VendorUser`), `Invitations` (`VendorUserInvitation`).

### Commands

- Tenant management: `CreateVendorTenant`, `SuspendVendorTenant`, `ReinstateVendorTenant`, `TerminateVendorTenant`
- User invitations: `InviteVendorUser`, `ResendVendorUserInvitation`, `RevokeVendorUserInvitation`
- User management: `ChangeVendorUserRole`, `DeactivateVendorUser`, `ReactivateVendorUser`

### Domain events

Not applicable — Vendor Identity is implemented on EF Core and does not emit domain events at S1 stub depth.

### Projections

Not applicable — read models are EF Core queries against the entity tables.

### Integration events

- `VendorIdentity.VendorTenantCreated` — publishes
- `VendorIdentity.VendorTenantSuspended` — publishes
- `VendorIdentity.VendorTenantReinstated` — publishes
- `VendorIdentity.VendorTenantTerminated` — publishes
- `VendorIdentity.VendorUserInvited` — publishes
- `VendorIdentity.VendorUserInvitationResent` — publishes
- `VendorIdentity.VendorUserInvitationRevoked` — publishes
- `VendorIdentity.VendorUserActivated` — publishes
- `VendorIdentity.VendorUserDeactivated` — publishes
- `VendorIdentity.VendorUserReactivated` — publishes
- `VendorIdentity.VendorUserRoleChanged` — publishes

### HTTP / API surface (one line)

`VendorIdentity.Api` exposes login / token endpoints and tenant + user + invitation administration endpoints consumed by Vendor Portal.

### Frontend surface (if applicable)

Not applicable — no frontend in this BC.

### Identity / auth posture (if applicable)

JWT Bearer issuer for vendor users; invitation tokens are SHA-256 hashed at rest.

## Prior event modeling

- `docs/planning/vendor-portal-event-modeling.md`

## ADRs

- ADR 0024 — SHA-256 Token Hashing for Vendor User Invitations
- ADR 0028 — JWT Bearer Tokens for Vendor Identity
- ADR 0032 — Multi-Issuer JWT Strategy

## Source citations (S1 stub)

- `src/Vendor Identity/`
- `src/Shared/Messages.Contracts/VendorIdentity/`
- `CONTEXTS.md` (section: `Vendor Identity`)
- `docs/decisions/0024-sha256-token-hashing-vendor-invitations.md`
- `docs/decisions/0028-jwt-for-vendor-identity.md`
- `docs/decisions/0032-multi-issuer-jwt-strategy.md`
- `docs/planning/vendor-portal-event-modeling.md`
