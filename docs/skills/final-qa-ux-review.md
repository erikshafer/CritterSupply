# Final QA/UX Review

Use this skill to standardize the final review phase for any implementation session in CritterSupply.

## When to Use

Run this skill at the **end** of any session that includes implementation work, including:

- Pure implementation sessions
- Planning + implementation sessions that move from design into repository changes
- Sessions focused on code, tests, workflows, configuration, or implementation-facing documentation
- Sessions where the agent needs to decide whether the work is production-ready, MVP-ready, or still blocked

Do **not** treat a session as exempt simply because it started with planning. If the session produced implementation artifacts or implementation-ready changes, this review applies.

This skill is optional for strictly planning-only or analysis-only sessions that do not modify the repository.

## Core Concepts

### 1) Two complementary reviewers

CritterSupply uses two existing custom agents for the final implementation review:

- `@qa-engineer` — evaluates test coverage, regression risk, negative paths, integration gaps, environment coverage, and release readiness from a quality perspective
- `@ux-engineer` — evaluates workflow clarity, accessibility, feedback states, interaction consistency, cognitive load, mobile/responsive concerns, and overall usability

These reviews should complement each other. The goal is not duplicate feedback; it is to surface both technical quality gaps and user-facing risks before the session closes.

### 2) Review the real changes, not just a summary

Point the reviewers at:

- The actual files changed in the session
- Any validation already run (build, tests, lint, manual verification)
- Any known constraints or deferred items
- Relevant feature files, integration tests, UI pages, workflows, or planning documents

Reviewers should inspect the implementation as it exists, not infer it from a generic description alone.

### 3) Distinguish severity clearly

Every synthesized review should classify findings into:

- **Blocking / must-fix before merge** — issues serious enough that the session should not be considered ready
- **Should-fix soon / next session** — meaningful gaps that can be deferred deliberately
- **Deferred / polish** — non-blocking enhancements, usability polish, or follow-up improvements

This keeps the final handoff actionable and prevents important gaps from being buried in long prose.

### 4) Planning + implementation sessions still require sign-off

If a session included both planning and implementation, the final QA/UX review still runs. Planning context often improves the review because it clarifies intended scope, success criteria, and what was deliberately deferred.

## Review Workflow

1. **Confirm applicability**
   - Did the session modify implementation artifacts?
   - If yes, run the final QA/UX review

2. **Gather inputs**
   - Changed files
   - Validation results already run
   - Scope/goal of the session
   - Any known risks, constraints, or deferred work

3. **Invoke both reviewers**
   - Ask `@qa-engineer` for coverage, risk, and readiness analysis
   - Ask `@ux-engineer` for usability, accessibility, and interaction analysis

4. **Resolve obvious localized fixes**
   - If either reviewer surfaces a small, high-confidence issue that belongs in the current session, fix it before finalizing

5. **Synthesize one final review**
   - Produce a concise combined summary using the standard output template below

6. **Record follow-up work explicitly**
   - Convert deferred findings into next-session actions, issue ideas, or checklist items where appropriate

## Expected Output Format

Use this exact section structure in the final session response:

```markdown
## Final QA/UX Review

### QA Verdict
- Overall quality assessment
- Key test coverage concerns or confirmation of sufficiency

### UX Verdict
- Overall usability/accessibility assessment
- Key workflow or interaction concerns

### Blocking Issues
- None, or a short bullet list of must-fix items

### Should-Fix Soon
- Short bullet list of important follow-up items for the next session

### Deferred / Polish
- Short bullet list of acceptable non-blocking enhancements

### Recommended Next Actions
1. Concrete next action
2. Concrete next action
3. Concrete next action

### Release Readiness
- `Production-ready`
- `Acceptable for MVP with follow-ups`
- `Not ready / blocked`
```

## Recommended Reviewer Prompts

Keep prompts short and grounded in the changed files.

### QA Engineer

Ask for:

- Test coverage sufficiency
- Negative path gaps
- Integration/E2E/unit test recommendations
- Release-readiness concerns
- Clear must-fix vs defer guidance

### UX Engineer

Ask for:

- Workflow clarity
- Feedback-state completeness
- Accessibility and consistency concerns
- User confusion or dead-end risks
- Clear must-fix vs defer guidance

## Common Pitfalls

- Treating planning + implementation sessions as exempt from review
- Asking only one reviewer instead of both QA and UX
- Producing vague “looks good” feedback without file-level grounding
- Mixing blocking issues with optional polish
- Leaving deferred issues implicit instead of naming them explicitly
- Repeating the entire implementation summary instead of producing a focused final review

## Testing

This skill does not replace the repository's required validation steps.

Before running the final QA/UX review, complete the appropriate build/test/lint/manual verification work for the changes already required by `CLAUDE.md`. The review should interpret those results, identify gaps, and decide whether additional coverage or UX fixes are still needed.

## See Also

- `CLAUDE.md` — Skill Invocation Guide and session workflow
- `docs/AI-ASSISTED-DEVELOPMENT.md` — Custom agent descriptions and usage guidance
- `docs/skills/critterstack-testing-patterns.md` — Integration and unit testing patterns
- `docs/skills/e2e-playwright-testing.md` — Browser E2E testing patterns
- `docs/skills/bunit-component-testing.md` — Blazor component test guidance
