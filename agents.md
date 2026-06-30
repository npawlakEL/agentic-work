# Agents Flow & Control

## Git Flow

All development follows git flow branching:

1. **Branch from `master`** for every new feature/functionality.
2. All development for that feature happens on the feature branch.
3. Coder ↔ Reviewer loop happens entirely on the feature branch (local commits only until Gate 2.5).
4. After Gate 2.5 (user approves push), the feature branch is pushed to remote.
5. **A Pull Request is created** from the feature branch → `master`.
6. **The user reviews the PR.** This is the final gate — no merge without user approval.
7. After user approves the PR, the agent merges it into `master`.

**Rules:**
- Never commit directly to `master`.
- One feature branch per functionality/task.
- Branch naming: `feature/<short-description>` (e.g., `feature/lane-config-backend`).
- The PR is the final checkpoint — the user has full control over what lands in `master`.

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
- **Output:** Single push of all commits to the feature branch. PR created from feature branch → `master`.
- **Rule:** There is exactly ONE push per coder/reviewer cycle. No incremental pushes of fixes. No auto-push. The agent must ask the user and receive explicit approval.

### Gate 2.75: PR Review (User Merge Gate)
- **Input:** PR is open from feature branch → `master`.
- **Gate Condition:** **The user reviews and approves the PR.** No merge without user sign-off.
- **Output:** Agent merges the PR into `master` only after user approval.
- **Rule:** The agent cannot merge on its own. The user has final say over what lands in `master`.

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
7. **Spec must be rock solid before handoff.** No open items, no unanswered questions in `planner-tasks.md` when the spec goes to the coder. If questions remain, they must be answered first.
8. **Spec-gap escalation.** If the coder or reviewer discovers an ambiguity, the loop pauses, the planner asks the user, updates the spec, and the loop resumes.

## Parallel Execution Model

Sub-agents run in parallel wherever possible:
- **Planner** can work on the next feature's spec while a previous feature is in the Coder ↔ Reviewer loop.
- **Coder** and **Reviewer** iterate in a tight loop on the current feature branch.
- **Learner** can process completed features while new ones are being developed.
- Multiple feature branches can be active simultaneously (each with its own Coder ↔ Reviewer loop).

**Synchronization Rules:**
- Agents operating on the SAME feature branch must be sequential (coder finishes → reviewer starts → coder again if needed).
- Agents operating on DIFFERENT feature branches can run fully in parallel.
- The Planner can always run in parallel with everything else (it doesn't touch code).
- The Learner runs after a feature is merged but can overlap with other in-progress features.
- If a spec-gap escalation occurs, only the affected feature's loop pauses — other parallel work continues.
