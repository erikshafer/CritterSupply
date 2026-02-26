# Contributing to CritterSupply

CritterSupply is a reference architecture demonstrating the Critter Stack (Wolverine + Marten) in a realistic e-commerce domain.

This guide covers development workflows, Git conventions, and contribution patterns.

---

## Getting Started

### Prerequisites

- .NET 10 SDK
- Docker Desktop (for Postgres + RabbitMQ)
- JetBrains Rider or Visual Studio 2022+ (recommended for `.http` file testing)
- Git

### First-Time Setup

```bash
# Clone the repository
git clone https://github.com/erikshafer/CritterSupply.git
cd CritterSupply

# Start infrastructure
docker-compose --profile infrastructure up -d

# Build the solution
dotnet build

# Run tests
dotnet test
```

---

## Development Workflow

### 1. Check the Current Cycle

See [docs/planning/CURRENT-CYCLE.md](./docs/planning/CURRENT-CYCLE.md) for active work.

**GitHub-first tracking:**
- Issues: [GitHub Issues](https://github.com/erikshafer/CritterSupply/issues)
- Project Board: [CritterSupply Development](https://github.com/users/erikshafer/projects/9)
- Milestones: [GitHub Milestones](https://github.com/erikshafer/CritterSupply/milestones)

### 2. Pick an Issue

Browse open issues and look for:
- `status:ready` — Ready to start
- `good-first-issue` — Beginner-friendly
- `help-wanted` — Community contributions welcome

**Comment on the issue** before starting work to avoid duplication.

### 3. Create a Branch

Use the pattern: `issue-<number>-<short-description>`

**Examples:**
```bash
git checkout main
git pull origin main
git checkout -b issue-57-auth-integration
git checkout -b issue-120-fix-cart-bug
git checkout -b issue-99-refactor-handlers
```

**Why this pattern?**
- ✅ GitHub auto-links issue `#57` when you reference it in commits
- ✅ Grep-friendly: `git branch --list 'issue-57*'`
- ✅ Short but descriptive
- ✅ Sortable numerically

### 4. Make Changes

**Follow CritterSupply conventions:**
- Read relevant skills from [docs/skills/](./docs/skills/) before implementing
- Consult [CONTEXTS.md](./CONTEXTS.md) for bounded context integration contracts
- Check [CLAUDE.md](./CLAUDE.md) for coding standards and architecture decisions
- Write integration tests (Alba + TestContainers)
- Update `.http` files for manual API testing

**Commit frequently with clear messages:**

```bash
# Good commit messages
git commit -m "feat: add login endpoint (#57)"
git commit -m "fix: handle invalid email in login (#57)"
git commit -m "docs: update ADR 0012 with test results (#57)"
git commit -m "test: add integration test for logout flow (#57)"
```

**Commit message format:**
```
<type>: <subject> (#<issue-number>)

[optional body]

[optional footer: Fixes #123]
```

**Types:**
- `feat:` — New feature
- `fix:` — Bug fix
- `docs:` — Documentation only
- `test:` — Adding or updating tests
- `refactor:` — Code restructuring (no behavior change)
- `chore:` — Tooling, dependencies, CI/CD
- `style:` — Formatting, whitespace (no code change)

### 5. Push and Create Pull Request

```bash
# Push branch to remote
git push -u origin issue-57-auth-integration

# Create PR (via GitHub UI or CLI)
gh pr create --title "[#57] Add authentication integration" --body "Fixes #57

## Summary
- Added cookie-based authentication to Customer Identity API
- Seeded 3 test users (Alice, Bob, Charlie)
- Tested login/logout/whoami endpoints

## Testing
- Manual testing with `.http` file
- Integration tests passing

## References
- [ADR 0012](./docs/decisions/0012-simple-session-based-authentication.md)"
```

**PR Title Pattern:**
```
[#<issue-number>] <Brief description>
```

**PR Body Must Include:**
- `Fixes #<issue-number>` — Auto-closes issue when merged
- Summary of changes
- Testing performed
- References to ADRs, skills, or CONTEXTS.md

### 6. Code Review

- Maintainers will review your PR
- Address feedback by pushing new commits to the same branch
- Once approved, maintainer will merge (usually squash merge)

---

## Git Conventions

### Branch Naming

| Pattern | Example | Use Case |
|---------|---------|----------|
| `issue-<number>-<description>` | `issue-57-auth-integration` | Feature work (recommended) |
| `issue-<number>-fix-<description>` | `issue-120-fix-cart-bug` | Bug fixes (optional, redundant with labels) |

**Avoid:**
- ❌ `feature/auth` — No issue reference
- ❌ `erik-working-on-stuff` — Not descriptive
- ❌ `57` — Too terse (what is 57?)

### Commit Messages

**Always reference the issue number:**

```bash
# Good
git commit -m "feat: add Password column to Customer table (#57)"
git commit -m "feat: implement login/logout endpoints (#57)"

# Better (closes issue automatically when merged)
git commit -m "feat: complete authentication integration

- Add cookie-based auth to Customer Identity API
- Seed 3 test users (Alice, Bob, Charlie)
- Add Login.razor page to Storefront.Web
- Replace stub customerId in Cart/Checkout

Fixes #57"
```

**Magic keywords that auto-close issues:**
- `Fixes #57`
- `Closes #57`
- `Resolves #57`

(Use in commit message body or PR description)

### When to Branch vs. Work on `main`

**Create a branch for:**
- Multi-session work (most cycle tasks)
- Experimental changes
- Work requiring review before merging

**Direct commits to `main` (rare):**
- Quick documentation fixes (< 5 minutes)
- Typo corrections
- Non-code changes (if repo owner)

**For CritterSupply:**
- Default to branching for all cycle work
- Keeps `main` stable for demos and forks
- Demonstrates real-world Git workflows

---

## Code Standards

### C# Style

See [docs/skills/modern-csharp-coding-standards.md](./docs/skills/modern-csharp-coding-standards.md) for complete guide.

**Key rules:**
- `sealed` by default for commands, queries, events, models
- Immutability: use `record`, `with` expressions, `IReadOnlyList<T>`
- Pure functions for business logic (side effects at edges)
- FluentValidation for all commands/queries
- Nullable reference types enabled (`<Nullable>enable</Nullable>`)

### Testing

- **Integration tests over unit tests** — Test complete vertical slices with Alba
- Use TestContainers for Postgres/RabbitMQ
- Manual testing via `.http` files (JetBrains IDEs)
- BDD scenarios in `docs/features/` when appropriate

See [docs/skills/critterstack-testing-patterns.md](./docs/skills/critterstack-testing-patterns.md).

### Project Structure

```
src/
  <Bounded Context>/
    <ProjectName>/          # Domain (regular SDK)
    <ProjectName>.Api/      # API (Web SDK)

tests/
  <Bounded Context>/
    <ProjectName>.IntegrationTests/
```

See [docs/skills/vertical-slice-organization.md](./docs/skills/vertical-slice-organization.md).

### Documentation

**Always update when adding features:**
- [CONTEXTS.md](./CONTEXTS.md) — Integration contracts between BCs
- [docs/decisions/](./docs/decisions/) — Create ADR for architectural decisions
- [docs/features/](./docs/features/) — Gherkin scenarios for user-facing features
- [docs/planning/CURRENT-CYCLE.md](./docs/planning/CURRENT-CYCLE.md) — Active cycle summary

**Never create unless requested:**
- README files (prefer inline code comments)
- Markdown docs for trivial changes

---

## Issue and PR Workflow

### Creating Issues

**Use labels to categorize:**

| Label | Meaning |
|-------|---------|
| `bc:*` | Bounded context (e.g., `bc:orders`, `bc:shopping`) |
| `type:*` | Issue type (`type:feature`, `type:bug`, `type:testing`, `type:adr`) |
| `status:*` | Current state (`status:backlog`, `status:planned`, `status:in-progress`) |
| `priority:*` | Importance (`priority:high`, `priority:medium`, `priority:low`) |
| `value:*` | Business value (`value:high`, `value:medium`, `value:low`) |
| `urgency:*` | Time sensitivity (`urgency:high`, `urgency:medium`, `urgency:low`) |

**Issue title format:**
```
[<Scope>] <Brief description>
```

**Examples:**
- `[Auth] Replace stub customerId with Customer Identity BC authentication`
- `[Testing] Automated browser tests for Customer Experience Blazor UI`
- `[Infrastructure] Replace docker-compose with .NET Aspire`

### Linking Issues to Milestones

All issues should be linked to a milestone (cycle):

```bash
gh issue create --title "[Auth] Add login page" --milestone "Cycle 19: Authentication & Authorization" --label "bc:customer-experience,type:feature,status:planned"
```

### Closing Issues

Issues auto-close when PR merges if commit/PR body includes:
- `Fixes #57`
- `Closes #57`
- `Resolves #57`

**Manual close only if:**
- Duplicate issue
- Won't fix / out of scope
- Already fixed by other work

---

## Running the System Locally

### Infrastructure Only (Recommended for Development)

```bash
docker-compose --profile infrastructure up -d

# Run APIs natively
cd "src/Orders/Orders.Api"
dotnet run

cd "src/Customer Identity/CustomerIdentity.Api"
dotnet run

# etc.
```

### Full System (Containerized)

```bash
docker-compose --profile all up --build
```

### Selective Services

```bash
docker-compose --profile infrastructure --profile orders --profile shopping up
```

See [CLAUDE.md Docker Development section](./CLAUDE.md#docker-development) for details.

---

## Testing Locally

### Run All Tests

```bash
dotnet test
```

### Run Specific Test Project

```bash
dotnet test tests/Orders/Orders.IntegrationTests/
```

### Manual API Testing

Use `.http` files in each API project:

```
src/Orders/Orders.Api/Orders.Api.http
src/Shopping/Shopping.Api/Shopping.Api.http
src/Customer Identity/CustomerIdentity.Api/CustomerIdentity.Api.http
```

See [docs/HTTP-FILES-GUIDE.md](./docs/HTTP-FILES-GUIDE.md) for usage.

---

## Architectural Decisions

**Before making significant changes:**

1. **Read [CONTEXTS.md](./CONTEXTS.md)** — Source of truth for BC boundaries and integration contracts
2. **Check existing ADRs** in [docs/decisions/](./docs/decisions/)
3. **Consult relevant skill** in [docs/skills/](./docs/skills/)

**When to create an ADR:**
- Technology selection (e.g., EF Core vs Marten for a BC)
- Pattern/approach decision (e.g., orchestration vs choreography)
- Bounded context boundary changes
- Deviation from established patterns

**ADR naming:** `<NNNN>-<title>.md` (e.g., `0012-simple-session-based-authentication.md`)

See [CLAUDE.md Architectural Decision Records section](./CLAUDE.md#architectural-decision-records-adrs).

---

## Getting Help

- **GitHub Discussions** — Ask questions, propose ideas
- **GitHub Issues** — Report bugs, request features
- **Discord/Slack** — (if community channels exist)

**Before asking:**
- Check [CLAUDE.md](./CLAUDE.md) for development guidelines
- Search existing issues
- Read relevant skills in [docs/skills/](./docs/skills/)

---

## Project Principles

CritterSupply is a **reference architecture**, not a production-ready e-commerce platform. This means:

- **Developer experience > Production hardening** — Frictionless local dev is prioritized
- **Clarity > Cleverness** — Patterns should be obvious and teachable
- **Pragmatism > Dogma** — Best practices balanced with simplicity
- **Completeness > Perfection** — Show full vertical slices, not polished fragments

**Design decisions favor:**
- ✅ Clone → Run → Explore (no external service setup)
- ✅ Demonstrating Critter Stack idioms
- ✅ Realistic domain (e-commerce) with non-trivial complexity
- ✅ Testing patterns (Alba, TestContainers, BDD)

**Out of scope:**
- OAuth providers (Auth0, Google, etc.) — Adds external dependencies
- Production-grade security (secrets management, rate limiting) — Reference architecture focus
- Multi-tenancy — Adds complexity without demonstrating core patterns
- Mobile apps — Focus on backend + BFF + Blazor

---

## License

See [LICENSE](./LICENSE) for details.

---

## Acknowledgments

CritterSupply demonstrates patterns from:
- [Wolverine](https://github.com/JasperFx/wolverine) — Message handling and HTTP endpoints
- [Marten](https://github.com/JasperFx/marten) — Event sourcing and document database
- Domain-Driven Design (DDD) and CQRS principles
- Vertical Slice Architecture

Built and maintained by [Erik Shafer](https://github.com/erikshafer).
