# ADR 0005: Reqnroll for BDD Testing

**Status:** ✅ Accepted

**Date:** 2026-02-05

## Context

CritterSupply is adopting BDD (Behavior-Driven Development) practices to improve communication between technical and non-technical stakeholders, create living documentation, and ensure user-facing features are tested from a user perspective.

We've already created Gherkin `.feature` files in `docs/features/` for the Customer Experience bounded context. Now we need to select a .NET BDD framework to execute these specifications as automated tests.

**Requirements:**
- Full Gherkin support (Given/When/Then syntax)
- Integration with xUnit (our existing test framework)
- Modern .NET 10+ support
- Active maintenance and community
- Open source with no licensing restrictions
- Works with Alba integration testing patterns
- Compatible with TestContainers infrastructure

**Client requirement:** One client is already requesting Reqnroll usage, providing external validation of this choice.

## Decision

We will use **Reqnroll** as our BDD testing framework.

## Rationale

### Why Reqnroll?

1. **Open Source, No License Restrictions**
   - Fully open source under BSD-3-Clause license
   - No paid tiers or feature paywalls (unlike SpecFlow+)
   - Community-driven development model

2. **SpecFlow-Compatible**
   - Drop-in replacement for SpecFlow
   - Created by original SpecFlow maintainers after Tricentis acquisition
   - 100% Gherkin-compatible
   - Familiar patterns for developers with SpecFlow experience

3. **Active Development**
   - Monthly releases since fork in 2023
   - Modern .NET 8+ support (works with .NET 10)
   - Responsive community on GitHub

4. **xUnit Integration**
   - First-class support for xUnit via `Reqnroll.xUnit` package
   - Works seamlessly with existing test infrastructure

5. **Living Documentation**
   - Generates human-readable reports from `.feature` files
   - Keeps specifications in sync with tests
   - Non-technical stakeholders can read/validate behavior

6. **Integration with CritterSupply Patterns**
   - Works with Alba for HTTP integration testing
   - Compatible with TestContainers fixtures
   - Supports ScenarioContext for sharing state between steps
   - Can inject TestFixture into step definitions

### Why Not Alternatives?

**SpecFlow:**
- Paid features (SpecFlow+ Runner, Living Doc)
- Commercial licensing concerns for some features
- Otherwise excellent, but Reqnroll provides same benefits without cost

**LightBDD:**
- No Gherkin support (uses C# fluent syntax)
- Defeats purpose of `.feature` files for non-technical stakeholders
- More developer-focused, less true BDD

**Xunit.Gherkin.Quick:**
- Limited features compared to Reqnroll
- Less mature tooling and community
- Attribute-based approach less flexible

## Consequences

### Positive

1. **BDD Workflow Enabled**
   - Write `.feature` files during planning phase
   - Implement step definitions during development
   - Tests verify behavior from user perspective

2. **Living Documentation**
   - `.feature` files serve as up-to-date specification
   - Generated reports show which scenarios pass/fail
   - Non-technical stakeholders can validate requirements

3. **Better Communication**
   - Product owners can write/review Gherkin scenarios
   - Reduces ambiguity in requirements
   - Shared understanding between business and development

4. **Client Alignment**
   - Client is already requesting Reqnroll usage
   - Builds expertise transferable to client projects

5. **No Licensing Costs**
   - Open source with no restrictions
   - No budget concerns for scaling usage

### Negative

1. **Learning Curve**
   - Team needs to learn Gherkin syntax
   - Step definition patterns require training
   - Additional abstraction layer over integration tests

2. **Tooling Maturity**
   - IDE support not as robust as SpecFlow (yet)
   - Fewer third-party extensions
   - Community smaller than SpecFlow (but growing)

3. **Test Maintenance**
   - Step definitions need to stay in sync with `.feature` files
   - Refactoring requires updating both specs and steps
   - Risk of brittle tests if not well-designed

4. **Overhead for Simple Tests**
   - Not all tests need Gherkin (unit tests remain pure C#)
   - BDD best for user-facing integration tests
   - May be overkill for internal APIs

### Mitigation Strategies

1. **Start Small**
   - Begin with Customer Experience BC (user-facing)
   - Proof-of-concept with existing `.feature` files
   - Evaluate before expanding to all BCs

2. **Focus on High-Value Scenarios**
   - Use Reqnroll for complex user flows (checkout, order placement)
   - Keep simple CRUD tests as Alba-only integration tests
   - Reserve BDD for behaviors that benefit from Gherkin clarity

3. **Document Patterns**
   - Create skill document for Reqnroll usage
   - Provide step definition examples
   - Show integration with TestFixture and Alba

4. **IDE Setup**
   - Install Reqnroll Visual Studio extension (when available)
   - Configure syntax highlighting for `.feature` files
   - Use Reqnroll CLI for scaffolding

## Implementation Plan

1. **Spike (Proof-of-Concept)**
   - Add Reqnroll packages to one test project (e.g., Shopping.Api.IntegrationTests)
   - Implement step definitions for one existing `.feature` file
   - Verify integration with Alba and TestContainers

2. **Documentation**
   - Create `skills/reqnroll-bdd-testing.md` skill document
   - Update CLAUDE.md to reference BDD workflow
   - Add examples to `docs/features/README.md`

3. **Customer Experience BC**
   - Implement step definitions for `cart-real-time-updates.feature`, `checkout-flow.feature`, `product-browsing.feature`
   - Use BDD as primary testing approach for user-facing scenarios
   - Generate living documentation reports

4. **Expand to Other BCs (Optional)**
   - Evaluate success in Customer Experience
   - Consider adoption for Orders, Shopping, Product Catalog
   - Keep BDD focused on user-facing behaviors

## Alternatives Considered

| Framework | Pros | Cons | Verdict |
|-----------|------|------|---------|
| **SpecFlow** | Mature, excellent tooling, large community | Paid features, licensing concerns | Good, but Reqnroll provides same benefits for free |
| **Reqnroll** | Open source, SpecFlow-compatible, actively maintained | Smaller ecosystem, newer tooling | ✅ **Selected** |
| **LightBDD** | Lightweight, type-safe C# syntax | No Gherkin, not true BDD | Rejected - defeats purpose of `.feature` files |
| **Xunit.Gherkin.Quick** | Simple, xUnit-native | Limited features, less mature | Rejected - insufficient capabilities |

## References

- [Reqnroll Official Website](https://reqnroll.net/)
- [Reqnroll GitHub Repository](https://github.com/reqnroll/Reqnroll)
- [Gherkin Reference](https://cucumber.io/docs/gherkin/reference/)
- [CritterSupply Feature Files](../features/)
- [CLAUDE.md - BDD Feature Specifications](../../CLAUDE.md#bdd-feature-specifications-gherkin)
