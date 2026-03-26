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

> **Considering additional agents?** See [AGENT-EXPANSION-PROPOSAL.md](./AGENT-EXPANSION-PROPOSAL.md) for a gap analysis of the current roster plus six draft agent documents written in GitHub's custom-agent markdown style.

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

#### 🔐 Application Security & Identity Engineer

**File:** [`.github/agents/application-security-identity-engineer.md`](../.github/agents/application-security-identity-engineer.md)

**Expertise:** Authentication, authorization, tenant isolation, session management, JWT flows, and secure application design

**Focus Areas:**
- Authentication and authorization design
- Session cookie vs JWT tradeoffs
- Role and permission modeling
- Tenant isolation and boundary enforcement
- SignalR authentication propagation and connection safety
- Secure token handling in Blazor applications
- Sensitive data exposure and least-privilege design
- Threat modeling for admin, operator, and self-service flows

---

#### 🧭 Event Modeling Facilitator

**File:** [`.github/agents/event-modeling-facilitator.md`](../.github/agents/event-modeling-facilitator.md)

**Expertise:** Event Modeling workshop facilitation, event-driven domain discovery, slice definition, and converting workshop output into delivery artifacts

**Focus Areas:**
- Brain-dump and storytelling facilitation
- Event naming and command-intent clarity
- Timeline construction and missing-event discovery
- Vertical slice identification and refinement
- Given/When/Then scenario derivation
- Distinguishing aggregates, projections, policies, and sagas
- Turning workshop output into issues, feature files, and implementation-ready slices

---

#### 🖥️ Frontend Platform Engineer

**File:** [`.github/agents/frontend-platform-engineer.md`](../.github/agents/frontend-platform-engineer.md)

**Expertise:** Blazor Server and WebAssembly architecture, component systems, shared UI patterns, BFF-facing view models, and frontend testability

**Focus Areas:**
- Blazor component architecture and page composition
- Shared UI patterns across multiple web applications
- BFF contract shape and view-model ergonomics
- Real-time UI updates with SignalR and Wolverine
- Authentication-aware UI flows for session- and JWT-based applications
- MudBlazor usage patterns, consistency, and maintainability
- Frontend testability with bUnit and Playwright
- Hosting-model tradeoffs between Blazor Server and Blazor WebAssembly

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

@application-security-identity-engineer Does this vendor approval flow create any tenant-isolation or privilege-escalation risks?

@event-modeling-facilitator Can you help us slice this returns workflow into Event Modeling artifacts and Given/When/Then scenarios?

@frontend-platform-engineer Should this dashboard composition live in the Backoffice BFF or in the Blazor app?
```

### In Issues

Tag agents when planning new features or discussing architectural decisions:

```
@principal-architect Is this bounded context boundary properly defined?

@product-owner Should "BackorderRequested" be a separate event or extend "ReservationFailed"?

@qa-engineer Is the BDD coverage sufficient for the Order saga happy path?

@ux-engineer How should we handle mobile responsive design for the vendor dashboard?

@application-security-identity-engineer Are these new backoffice role boundaries too broad for the actions exposed by this screen?

@event-modeling-facilitator Can you facilitate the first pass of Event Modeling for this pricing workflow?

@frontend-platform-engineer What frontend architecture patterns should we standardize before we expand this UI module?
```

### When to Use Which Agent

**Architecture & Design Decisions:**
- Start with `@principal-architect` for technical architecture
- Follow up with `@product-owner` for business alignment
- Involve `@event-modeling-facilitator` when the work starts with domain discovery or workshop output
- Bring in `@application-security-identity-engineer` for auth- or identity-sensitive design
- Consult `@frontend-platform-engineer` for multi-UI or Blazor architecture concerns
- Consult `@devops-engineer` for deployment implications

**Feature Development:**
- Begin with `@product-owner` for business requirements
- Use `@event-modeling-facilitator` when shaping slices, scenarios, or workshop outputs
- Engage `@principal-architect` for technical design
- Engage `@frontend-platform-engineer` for Blazor, BFF, component, or shared UI architecture
- Engage `@application-security-identity-engineer` when the feature touches auth, permissions, sessions, or tenancy
- Involve `@qa-engineer` for test strategy
- Consult `@ux-engineer` for UI/UX design
- End implementation sessions with `@qa-engineer` + `@ux-engineer` sign-off using `docs/skills/final-qa-ux-review.md`

**Quality & Testing:**
- Primary: `@qa-engineer` for test coverage and strategy
- Secondary: `@principal-architect` for test architecture
- Tertiary: `@ux-engineer` for E2E user journey tests

**End-of-Session Review:**
- For any implementation session — including planning + implementation sessions that resulted in repository changes — run a final combined QA/UX review
- Use `docs/skills/final-qa-ux-review.md` for the standard review flow and output format

**Deployment & Operations:**
- Primary: `@devops-engineer` for CI/CD and infrastructure
- Secondary: `@principal-architect` for deployment architecture
- Consider `@qa-engineer` for smoke testing strategy

**Specialized Reviews:**
- Primary: `@application-security-identity-engineer` for authentication, authorization, and tenant isolation
- Primary: `@event-modeling-facilitator` for Event Modeling workshops, slices, and scenario derivation
- Primary: `@frontend-platform-engineer` for Blazor architecture, shared UI patterns, and BFF-facing frontend composition

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

**Last Updated:** 2026-03-26
