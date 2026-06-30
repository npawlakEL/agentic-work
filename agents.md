# Agents Flow & Control

## Agent Lifecycle

The agentic workflow follows a gated flow. Each agent must complete its phase before the next begins.

```
┌──────────┐     ┌──────────┐     ┌──────────┐     ┌──────────┐
│  Planner │────▶│  Coder   │◀───▶│ Reviewer │────▶│ Learner  │
└──────────┘     └──────────┘     └──────────┘     └──────────┘
     │                │                │                │
     ▼                ▼                ▼                ▼
  spec.md          code/           feedback          learnings/
```

## Flow Gates

### Gate 1: Planner → Coder
- **Input:** User requirements (gathered interactively)
- **Output:** `spec.md` — full requirements specification with data model, UI behavior, tech stack, constraints
- **Gate Condition:** User approves the spec before coder begins

### Gate 2: Coder ↔ Reviewer (TDD Iteration Loop)
- **Input:** Coder produces working code using strict TDD (Red → Green → Refactor)
- **Output:** Reviewer validates functionality, runs tests, checks coverage, finds bugs
- **Gate Condition:** Reviewer signs off — all tests pass, no blocking issues, TDD compliance verified
- **Loop:** Reviewer sends issues → Coder writes failing tests for each issue → Coder fixes → Reviewer re-validates. Repeat until clean.
- **TDD Requirement:** No production code exists without a corresponding test. Tests are written FIRST.
- **NO PUSHING DURING THIS LOOP.** All work is local commits only. No git push until Gate 2 is fully passed.

### Gate 2.5: User Approval (Push Gate)
- **Input:** Reviewer has signed off. All tests pass. Code is complete.
- **Gate Condition:** **The user must explicitly approve the push.** The system checks in with the user, presents a summary of what was done, and waits for confirmation before pushing to remote.
- **Output:** Single push of all commits to the branch.
- **Rule:** There is exactly ONE push per coder/reviewer cycle. No incremental pushes of fixes. No auto-push. The agent must ask the user and receive explicit approval.

### Gate 3: Reviewer → Learner
- **Input:** Completed, reviewed code
- **Output:** Learner documents what was learned — patterns, pitfalls, guardrails, reusable skills
- **Gate Condition:** Learnings captured in `learnings/` folder

## Constraints & Guardrails

1. **No agent skips a gate.** Coder cannot begin without an approved spec. Learner cannot run until reviewer passes.
2. **Agents are stateless between invocations.** All context must be passed explicitly (via files or prompts).
3. **Each agent operates within its defined scope.** The coder does not gather requirements. The reviewer does not write features.
4. **Skills are mandatory reading.** Every agent MUST read the `skills/` folder before starting work and follow any applicable skills during execution. If a skill exists for a task, the agent uses it — no reinventing.
5. **Learnings are mandatory.** Every completed project must produce at least one learning entry.
6. **No pushing during Coder ↔ Reviewer loop.** All work stays local until user approves (Gate 2.5).
