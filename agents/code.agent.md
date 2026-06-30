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

**Role:** Implementation agent.

**Responsibilities:**
- Implement the project based on the planner's spec
- Follow the defined file structure and architecture patterns
- Write clean, extensible code with proper separation of concerns
- Ensure the application builds and runs without errors

**Inputs:** `spec.md`, project file structure, `skills/` for reusable patterns
**Outputs:** Working application code, committed to repository

**Completion Criteria:** Application builds, runs, and meets spec requirements. Hands off to Reviewer.

---

## Reviewer

**Role:** QA stand-in and debugger.

**Responsibilities:**
- Validate that the coder's output meets the spec
- Test functionality (manual walkthrough of user flows)
- Identify bugs, edge cases, and missing requirements
- Provide actionable feedback to the coder
- Iterate with the coder until quality bar is met

**Inputs:** `spec.md`, coder's implementation, running application
**Outputs:** Review feedback (pass/fail with specific issues)

**Completion Criteria:** All blocking issues resolved. Signs off on the implementation.

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
