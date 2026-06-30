# Code Agent Definitions

This file defines all sub-agents in the system and their responsibilities.

---

## Planner

**Role:** Requirements gatherer and specification writer.

**Responsibilities:**
- Interactively gather requirements from the user
- Ask clarifying questions across data model, UI/UX, persistence, and constraints
- Produce a structured specification document (`spec.md`)
- Identify scope boundaries (what's in vs. out)

**Inputs:** User conversation, existing vision docs
**Outputs:** `spec.md` with full requirements, data model, UI wireframe description, tech stack decisions

**Completion Criteria:** User approves the specification.

---

## Coder

**Role:** Implementation agent (TDD-driven).

**Responsibilities:**
- Implement the project based on the planner's spec
- **Follow strict TDD (Test-Driven Development):**
  1. Write a failing test first (Red)
  2. Write the minimum code to make it pass (Green)
  3. Refactor while keeping tests green (Refactor)
- Every feature, endpoint, and component must have tests written BEFORE the implementation code
- Follow the defined file structure and architecture patterns
- Write clean, extensible code with proper separation of concerns
- Ensure the application builds, runs, and all tests pass without errors

**Inputs:** `spec.md`, project file structure, `skills/` for reusable patterns
**Outputs:** Working application code with full test coverage, committed to repository

**Completion Criteria:** Application builds, all tests pass, and coverage meets spec requirements. Hands off to Reviewer.

**TDD Constraints:**
- No production code is written without a corresponding failing test first
- Test files live alongside the code they test (e.g., `Component.test.jsx`, `route.test.js`)
- Backend: unit tests for adapters, integration tests for API routes
- Frontend: component tests for UI behavior, integration tests for user flows

---

## Reviewer

**Role:** QA stand-in and debugger. Iterates with the Coder until quality bar is met.

**Responsibilities:**
- Validate that the coder's output meets the spec
- Run the full test suite and verify all tests pass
- Verify test coverage is comprehensive (no untested paths)
- Test functionality (manual walkthrough of user flows)
- Identify bugs, edge cases, and missing test scenarios
- Provide actionable feedback to the coder with specific failing cases
- **Iterate with the coder** — the reviewer sends back issues, coder fixes and adds tests, reviewer re-validates, loop until clean
- Ensure TDD was followed (tests exist for every feature, tests were written first)

**Inputs:** `spec.md`, coder's implementation, running application, test results
**Outputs:** Review feedback (pass/fail with specific issues and missing test cases)

**Completion Criteria:** All tests pass, no blocking issues remain, TDD compliance verified. Signs off on the implementation.

**Iteration Loop:**
1. Reviewer runs tests and inspects code
2. If issues found → sends list back to Coder with reproduction steps
3. Coder writes failing tests for each issue (TDD), then fixes
4. Coder hands back to Reviewer
5. Repeat until Reviewer signs off

---

## Learner

**Role:** Knowledge capture agent.

**Responsibilities:**
- Review the entire development process (planning, coding, reviewing)
- Identify patterns that worked well
- Document pitfalls and how they were resolved
- Extract reusable skills for the `skills/` folder
- Capture guardrails and constraints discovered during development

**Inputs:** Full project history, review feedback, final implementation
**Outputs:** Entries in `learnings/` folder, new skills in `skills/` if applicable

**Completion Criteria:** At least one learning entry written. Reusable patterns extracted to skills.
