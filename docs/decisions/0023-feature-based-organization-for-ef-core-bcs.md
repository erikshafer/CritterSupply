# ADR 0023: Feature-Based Organization for EF Core Bounded Contexts

**Status:** ✅ Accepted

**Date:** 2026-03-09

**Context:**

During Cycle 22 (VendorIdentity BC), we initially organized the codebase using a traditional layered architecture with technical folders:

```
src/Vendor Identity/VendorIdentity/
├── Commands/
│   ├── CreateVendorTenant.cs
│   ├── InviteVendorUser.cs
│   └── ...
├── Data/
│   └── VendorIdentityDbContext.cs
└── Entities/
    ├── VendorTenant.cs
    ├── VendorUser.cs
    └── VendorUserInvitation.cs
```

This structure worked but had several drawbacks:
1. **Scattered features** - Related files (command, handler, validator) spread across multiple folders
2. **Cognitive overhead** - Developers had to navigate 3-4 folders to understand a single feature
3. **Inconsistent with CritterSupply patterns** - All Marten-based BCs use vertical slice/feature-based organization

**Decision:**

VendorIdentity BC (and all future EF Core BCs) will use **feature-based organization**, grouping files by business capability rather than technical layer:

```
src/Vendor Identity/VendorIdentity/
├── TenantManagement/
│   ├── CreateVendorTenant.cs              # Command
│   ├── CreateVendorTenantValidator.cs     # FluentValidation
│   ├── CreateVendorTenantHandler.cs       # Wolverine handler
│   ├── GetVendorTenant.cs                 # Query
│   ├── UpdateVendorTenant.cs              # Command
│   └── ...
├── UserInvitations/
│   ├── InviteVendorUser.cs
│   ├── InviteVendorUserValidator.cs
│   ├── InviteVendorUserHandler.cs
│   ├── AcceptInvitation.cs
│   └── ...
└── Identity/
    ├── VendorIdentityDbContext.cs         # EF Core DbContext
    ├── VendorTenant.cs                    # Entity
    ├── VendorUser.cs                      # Entity
    ├── VendorUserInvitation.cs            # Entity
    ├── VendorUserStatus.cs                # Enum
    ├── VendorRole.cs                      # Enum
    └── InvitationStatus.cs                # Enum
```

**Rationale:**

1. **Consistency across BCs** - EF Core BCs now match Marten-based BC organization patterns
2. **Developer productivity** - All files for a feature (command, validator, handler) are colocated
3. **Easier navigation** - IDE file trees group related files together
4. **Clearer intent** - Folder names describe business capabilities, not technical layers
5. **Better for AI-assisted development** - AI tools can see feature scope without scanning multiple folders

**Feature Folder Guidelines:**

- **Feature folders** contain commands, queries, handlers, and validators for a specific business capability
- **Identity folder** contains infrastructure (DbContext, entities, enums) shared across features
- **No circular dependencies** - Feature folders should not reference each other
- **Shared models** - If multiple features need the same entity, it lives in `Identity/`

**Consequences:**

**Positive:**
- ✅ Faster feature development - all related files in one place
- ✅ Easier code reviews - feature changes are contained
- ✅ Better alignment with vertical slice architecture
- ✅ Consistent with existing Marten-based BCs (Shopping, Orders, Payments, etc.)
- ✅ AI tools can reason about features holistically

**Negative:**
- ❌ Requires refactoring existing BCs organized by technical layers (if any exist)
- ❌ Slight learning curve for developers accustomed to traditional layered architecture
- ❌ Entity classes in `Identity/` are slightly farther from their feature handlers (acceptable trade-off)

**Alternatives Considered:**

1. **Keep technical folders** - Easier for developers familiar with traditional .NET architecture, but inconsistent with CritterSupply patterns
2. **Hybrid approach** - Feature folders for commands, but separate `Data/` folder - adds complexity without clear benefit
3. **Entity classes in feature folders** - Could work for single-feature entities, but breaks down with shared entities like `VendorUser`

**Migration Checklist** (when refactoring existing EF Core BCs):

1. Identify business capabilities (e.g., TenantManagement, UserInvitations)
2. Create feature folders for each capability
3. Move commands, validators, and handlers to feature folders
4. Create `Identity/` folder for DbContext, entities, and enums
5. Update namespaces:
   - Feature folders: `<BcName>.<FeatureName>`
   - Identity folder: `<BcName>.Identity`
6. Run tests to verify no breaking changes

**Examples in CritterSupply:**

**Marten-based BC (Shopping):**
```
Shopping/
├── AddToCart/
│   ├── AddToCart.cs
│   └── AddToCartHandler.cs
├── Checkout/
│   ├── InitiateCheckout.cs
│   └── InitiateCheckoutHandler.cs
└── Cart.cs                      # Aggregate root
```

**EF Core BC (VendorIdentity):**
```
VendorIdentity/
├── TenantManagement/
│   ├── CreateVendorTenant.cs
│   └── CreateVendorTenantHandler.cs
├── UserInvitations/
│   ├── InviteVendorUser.cs
│   └── InviteVendorUserHandler.cs
└── Identity/
    ├── VendorIdentityDbContext.cs
    └── VendorTenant.cs          # Entity
```

**References:**

- Cycle 22 Phase 1: VendorIdentity BC refactoring
- `docs/skills/vertical-slice-organization.md`
- `docs/skills/efcore-wolverine-integration.md`
