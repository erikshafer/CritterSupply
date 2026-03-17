# AI-Assisted Development in CritterSupply

This document details how AI tools are used in CritterSupply development and how to leverage them effectively in your contributions.

## Overview

CritterSupply is built with Claude as a collaborative coding partner. Beyond just generating code, it's an exercise in teaching AI tools to think in event-driven patterns and leverage the Critter Stack idiomatically—helping to improve the guidance these tools can offer the broader community.

The more these tools see well-structured examples, the better guidance they can offer developers exploring these approaches for the first time.

## Development Guidelines

See [CLAUDE.md](../CLAUDE.md) for comprehensive AI development guidelines, including:
- Documentation hierarchy and structure
- Skill invocation guide
- Testing strategy
- Integration patterns
- Common mistakes & anti-patterns

See [docs/README.md](./README.md) for the complete documentation structure.

## Architectural Review

See [docs/ARCHITECTURAL-REVIEW.md](./ARCHITECTURAL-REVIEW.md) for an independent review of bounded context design, service communication patterns, and recommendations from an experienced software architect perspective.

---

## Custom GitHub Copilot Agents

CritterSupply includes specialized GitHub Copilot agents with domain expertise to assist with development. These agents provide focused feedback from different perspectives to improve code quality, architecture, and business alignment.

### Available Agents

#### 👨‍💼 Principal Software Architect

**File:** [`.github/agents/principal-architect.md`](../.github/agents/principal-architect.md)

**Expertise:** .NET, event-driven systems, distributed architecture, and the Critter Stack (Wolverine + Marten)

**Focus Areas:**
- Code quality and maintainability
- System design and architecture decisions
- Bounded context boundaries and BC interactions
- Event sourcing and CQRS patterns
- Integration patterns (orchestration vs choreography)
- Technical debt and refactoring opportunities
- Project trajectory and long-term sustainability

**Experience:** 15+ years of production experience with distributed systems

---

#### 🏪 Product Owner

**File:** [`.github/agents/product-owner.md`](../.github/agents/product-owner.md)

**Expertise:** E-commerce domain, vendor relations, product/inventory management, marketplace channels

**Focus Areas:**
- Business-focused feedback on workflows
- Bounded context alignment with real-world business processes
- Event-driven workflow design from business perspective
- Domain language and ubiquitous language validation
- Feature prioritization and business value
- User story and acceptance criteria review
- Real-world e-commerce policy alignment

**Experience:** 10+ years in e-commerce operations and vendor management

---

#### 🚀 DevOps Engineer

**File:** [`.github/agents/devops-engineer.md`](../.github/agents/devops-engineer.md)

**Expertise:** CI/CD orchestration, Infrastructure as Code (IaC), deployment strategies, GitHub Actions, Docker/Kubernetes, observability

**Focus Areas:**
- Deployment pipeline design and optimization
- Infrastructure automation and IaC patterns
- Deployment strategies (blue/green, canary, rollback)
- GitHub Actions workflow optimization
- Container orchestration and Docker Compose patterns
- Observability and OpenTelemetry instrumentation
- Risk analysis and environment-aware strategies
- Autonomous deployment pipeline design

---

#### 🧪 QA Engineer

**File:** [`.github/agents/qa-engineer.md`](../.github/agents/qa-engineer.md)

**Expertise:** Manual and automated testing, BDD, full-stack quality strategy, event-driven system testing

**Focus Areas:**
- Test coverage analysis and strategy
- BDD scenario design (Gherkin)
- Integration test patterns (Alba, TestContainers)
- E2E testing (Playwright)
- Component testing (bUnit)
- Test infrastructure and fixtures
- Quality metrics and reporting
- Cross-BC integration testing
- Event-driven system verification

---

#### 🎨 UX Engineer

**File:** [`.github/agents/ux-engineer.md`](../.github/agents/ux-engineer.md)

**Expertise:** WCAG 2.1/2.2 accessibility, responsive design, e-commerce interaction patterns, Blazor applications

**Focus Areas:**
- Accessibility compliance (WCAG 2.1/2.2)
- Responsive design and mobile-first approaches
- E-commerce UX patterns and best practices
- Blazor component design and usability
- Read model and projection design from user perspective
- Event Storming and Event Modeling for UI design
- Domain-Driven Design in the UI (ubiquitous language, bounded context seams)
- Team Topologies and Conway's Law considerations
- Dashboard and data visualization design
- Cognitive load optimization

---

## How to Use Custom Agents

### In Pull Requests

Tag the agent in a PR comment to get specialized feedback:

```
@principal-architect Can you review the event sourcing implementation in this PR?

@product-owner Does this order cancellation flow match real-world e-commerce policies?

@devops-engineer How should we deploy this Orders BC refactor with zero downtime?

@qa-engineer What integration tests should we add to cover the new checkout flow?

@ux-engineer Does this checkout page layout follow good UX principles?
```

### In Issues

Tag agents when planning new features or discussing architectural decisions:

```
@principal-architect Is this bounded context boundary properly defined?

@product-owner Should "BackorderRequested" be a separate event or extend "ReservationFailed"?

@qa-engineer Is the BDD coverage sufficient for the Order saga happy path?

@ux-engineer How should we handle mobile responsive design for the vendor dashboard?
```

### When to Use Which Agent

**Architecture & Design Decisions:**
- Start with `@principal-architect` for technical architecture
- Follow up with `@product-owner` for business alignment
- Consult `@devops-engineer` for deployment implications

**Feature Development:**
- Begin with `@product-owner` for business requirements
- Engage `@principal-architect` for technical design
- Involve `@qa-engineer` for test strategy
- Consult `@ux-engineer` for UI/UX design

**Quality & Testing:**
- Primary: `@qa-engineer` for test coverage and strategy
- Secondary: `@principal-architect` for test architecture
- Tertiary: `@ux-engineer` for E2E user journey tests

**Deployment & Operations:**
- Primary: `@devops-engineer` for CI/CD and infrastructure
- Secondary: `@principal-architect` for deployment architecture
- Consider `@qa-engineer` for smoke testing strategy

---

## Best Practices

1. **Be Specific:** Provide context about what you're trying to achieve
2. **Ask Follow-ups:** Agents can provide deeper insights with clarifying questions
3. **Tag Multiple Agents:** Different perspectives often reveal blind spots
4. **Reference Code:** Link to specific files or line numbers when asking for reviews
5. **Apply Feedback Iteratively:** Not all feedback needs to be addressed immediately—prioritize based on impact

---

## Contributing

When contributing to CritterSupply, consider leveraging these agents to:
- Validate your approach before implementing
- Get feedback on PRs before requesting human review
- Explore alternative solutions to problems
- Understand the reasoning behind existing patterns

The agents are designed to help maintain consistency with established patterns while encouraging thoughtful innovation where it adds value.

---

**Last Updated:** 2026-03-17
