# ADR 0005: Use MudBlazor for Customer Experience UI

**Status:** ✅ Accepted

**Date:** 2026-02-05

**Context:**

Customer Experience BC (Cycle 16) requires a Blazor Server frontend with polished UI components for:
- Product catalog browsing with cards and grids
- Shopping cart with badges and line item management
- Checkout wizard with multi-step navigation
- Order history with data tables

Options for UI framework:
1. **Bootstrap** - Popular CSS framework with basic components
2. **MudBlazor** - Modern Blazor component library with Material Design
3. **Radzen Blazor** - Alternative Blazor component library
4. **Custom CSS** - Build from scratch

---

## Decision

**Use MudBlazor** for all Customer Experience UI components.

Install `MudBlazor` NuGet package and use Material Design components throughout.

---

## Rationale

### Why MudBlazor?

**1. Blazor-Native Components**
- Built specifically for Blazor (not CSS-only like Bootstrap)
- No jQuery dependencies
- Full C# API (no JavaScript interop needed)

**2. Material Design**
- Modern, consistent design language
- Polished out-of-the-box appearance
- Professional look without custom CSS

**3. Rich Component Library**
- `MudCard` / `MudCardMedia` - Product cards
- `MudBadge` - Cart badge indicator
- `MudStepper` - Checkout wizard steps
- `MudSelect<T>` - Address selection dropdown
- `MudTable<T>` - Order history table with pagination
- `MudTextField` / `MudNumericField` - Form inputs
- `MudButton` / `MudIconButton` - Actions
- `MudAppBar` / `MudDrawer` / `MudLayout` - Navigation

**4. Active Community & Documentation**
- Well-documented API
- Active development (regular updates)
- Large community (Stack Overflow, GitHub discussions)

**5. Aligns with Future Client Work**
- Developer (Erik) will use MudBlazor with another client
- Getting familiar with MudBlazor provides transferable experience
- Reference architecture demonstrates real-world UI patterns

---

## Why Not Bootstrap?

Bootstrap is a great CSS framework but has limitations for Blazor:

| Feature | Bootstrap | MudBlazor | Our Need |
|---------|-----------|-----------|----------|
| Blazor Components | ❌ CSS only | ✅ Native Blazor | ✅ Required |
| Material Design | ❌ Custom theming | ✅ Built-in | ✅ Preferred |
| Data Table | ❌ HTML table | ✅ `MudTable<T>` with pagination | ✅ Order history |
| Stepper | ❌ Custom build | ✅ `MudStepper` | ✅ Checkout wizard |
| Badge | ✅ CSS badge | ✅ `MudBadge` component | ✅ Cart icon |
| Select | ✅ HTML select | ✅ `MudSelect<T>` with binding | ✅ Address dropdown |

**Bootstrap Drawbacks:**
- Requires Bootstrap CSS + JavaScript
- Not Blazor-native (HTML/CSS classes only)
- No built-in components (stepper, data table, etc.)
- Would need custom components for complex UI

---

## Consequences

### Positive

✅ **Faster Development**
- Pre-built components reduce custom CSS/JavaScript
- Consistent Material Design reduces design decisions

✅ **Better UX**
- Polished animations and transitions
- Responsive by default
- Accessibility built-in (ARIA labels, keyboard navigation)

✅ **Type-Safe Binding**
- `MudSelect<AddressSummary>` with full C# type safety
- `MudTable<Order>` with lambda expressions for columns
- No string-based CSS class manipulation

✅ **Maintainability**
- Well-documented API
- Active community for support
- Regular updates and bug fixes

✅ **Reference Architecture Value**
- Shows modern Blazor UI patterns
- Demonstrates Material Design in .NET
- Aligns with future client work (transferable skills)

### Negative

⚠️ **Learning Curve**
- New component library (vs. familiar Bootstrap)
- **Mitigation:** Excellent documentation, plenty of examples

⚠️ **NuGet Dependency**
- Adds external dependency to project
- **Mitigation:** MudBlazor is actively maintained, stable API

⚠️ **Material Design Only**
- Locked into Material Design aesthetic
- **Mitigation:** MudBlazor themes are customizable (colors, typography, spacing)

### Trade-Offs Accepted

We accept the following in exchange for MudBlazor's benefits:
1. New library to learn (vs. Bootstrap familiarity)
2. Material Design aesthetic (vs. custom design)
3. External NuGet dependency (vs. vanilla Blazor)

---

## Alternatives Considered

### Alternative 1: Bootstrap

**Pros:**
- Familiar CSS framework
- Large ecosystem of themes and plugins
- Works with any web framework

**Cons:**
- Not Blazor-native (CSS classes only)
- Requires custom components for complex UI (stepper, data table)
- Older design patterns (pre-Material Design)

**Verdict:** ❌ Rejected - Too much custom work for complex components

---

### Alternative 2: Radzen Blazor

**Pros:**
- Similar to MudBlazor (Blazor-native components)
- Good component library
- Active development

**Cons:**
- Less polished than MudBlazor (community feedback)
- Smaller community than MudBlazor
- Not aligned with future client work

**Verdict:** ❌ Rejected - MudBlazor has larger community and aligns with future client work

---

### Alternative 3: Custom CSS

**Pros:**
- Full control over design
- No external dependencies

**Cons:**
- Significant development time (build stepper, data table, etc.)
- Requires CSS expertise
- Maintenance burden (animations, responsiveness, accessibility)

**Verdict:** ❌ Rejected - Too much effort for reference architecture

---

## Implementation Notes

### MudBlazor Setup

**1. Install NuGet Package:**
```bash
dotnet add package MudBlazor
```

**2. Add to `Program.cs`:**
```csharp
builder.Services.AddMudServices();
```

**3. Add to `_Host.cshtml` or `App.razor`:**
```html
<link href="https://fonts.googleapis.com/css?family=Roboto:300,400,500,700&display=swap" rel="stylesheet" />
<link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
<script src="_content/MudBlazor/MudBlazor.min.js"></script>
```

**4. Wrap App in `MudThemeProvider` (in `MainLayout.razor`):**
```razor
<MudThemeProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<MudLayout>
    <MudAppBar>
        <MudIconButton Icon="@Icons.Material.Filled.Menu" Color="Color.Inherit" Edge="Edge.Start" />
        <MudText Typo="Typo.h6">CritterSupply</MudText>
        <MudSpacer />
        <MudBadge Content="@cartItemCount" Color="Color.Secondary">
            <MudIconButton Icon="@Icons.Material.Filled.ShoppingCart" Color="Color.Inherit" />
        </MudBadge>
    </MudAppBar>

    <MudMainContent>
        @Body
    </MudMainContent>
</MudLayout>
```

---

### Component Examples

**Product Card:**
```razor
<MudCard>
    <MudCardMedia Image="@product.ImageUrl" Height="200" />
    <MudCardContent>
        <MudText Typo="Typo.h6">@product.Name</MudText>
        <MudText Typo="Typo.body2">@product.Price.ToString("C")</MudText>
    </MudCardContent>
    <MudCardActions>
        <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="@AddToCart">
            Add to Cart
        </MudButton>
    </MudCardActions>
</MudCard>
```

**Checkout Stepper:**
```razor
<MudStepper @ref="stepper" ActiveStepIndex="@currentStep">
    <MudStep Title="Shipping Address">
        <MudSelect T="AddressSummary" @bind-Value="selectedAddress" Label="Select Address">
            @foreach (var addr in savedAddresses)
            {
                <MudSelectItem Value="@addr">@addr.DisplayLine</MudSelectItem>
            }
        </MudSelect>
    </MudStep>

    <MudStep Title="Shipping Method">
        <MudRadioGroup @bind-SelectedOption="selectedShippingMethod">
            <MudRadio Option="@("Standard")" Color="Color.Primary">Standard Ground ($5.99)</MudRadio>
            <MudRadio Option="@("Express")" Color="Color.Primary">Express ($12.99)</MudRadio>
        </MudRadioGroup>
    </MudStep>

    <MudStep Title="Payment">
        <MudTextField Label="Card Number" @bind-Value="cardNumber" />
    </MudStep>

    <MudStep Title="Review">
        <MudText Typo="Typo.h6">Order Summary</MudText>
        <!-- Order details -->
    </MudStep>
</MudStepper>
```

**Order History Table:**
```razor
<MudTable Items="@orders" Hover="true" Striped="true">
    <HeaderContent>
        <MudTh>Order ID</MudTh>
        <MudTh>Date</MudTh>
        <MudTh>Status</MudTh>
        <MudTh>Total</MudTh>
    </HeaderContent>
    <RowTemplate>
        <MudTd>@context.OrderId</MudTd>
        <MudTd>@context.PlacedAt.ToString("d")</MudTd>
        <MudTd>@context.Status</MudTd>
        <MudTd>@context.Total.ToString("C")</MudTd>
    </RowTemplate>
</MudTable>
```

---

## Success Criteria

✅ MudBlazor installed and configured in Storefront.Web project
✅ All pages use MudBlazor components (no Bootstrap)
✅ Cart badge updates with MudBadge
✅ Checkout wizard uses MudStepper
✅ Order history uses MudTable with pagination

---

## References

- [MudBlazor Documentation](https://mudblazor.com/)
- [MudBlazor GitHub](https://github.com/MudBlazor/MudBlazor)
- [MudBlazor Component Gallery](https://mudblazor.com/components)
- [Cycle 16 Plan](../planning/cycles/cycle-16-customer-experience.md)
- [ADR 0004: SSE over SignalR](./0004-sse-over-signalr.md)

---

**Decision Made By:** Erik Shafer / Claude AI Assistant
**Approved By:** Erik Shafer (2026-02-05)
