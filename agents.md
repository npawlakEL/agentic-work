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
┌──────────┐     ┌──────────────┐     ┌──────────┐     ┌──────────┐     ┌──────────┐
│  Planner │◀───▶│ Senior Coder │────▶│  Coder   │◀───▶│ Reviewer │────▶│ Learner  │
└──────────┘     └──────────────┘     └──────────┘     └──────────┘     └──────────┘
                       │                    ▲                │
                       │   spot checks +    │   issues       │
                       │   final review     │   (arch)       │
                       └────────────────────┘◀───────────────┘
                              ▼
                       architecture-log/
```

**Key change:** The Senior Coder sits between Planner and Coder as the architectural authority. It consults during planning, oversees implementation, and triages review findings.

## Flow Gates

### Gate 1: Planner ↔ Senior Coder → Spec Approval
- **Input:** User requirements (gathered interactively by Planner)
- **Process:** Planner consults with Senior Coder on technical feasibility. Senior Coder reads codebase, assesses architecture, recommends approaches.
- **Output:** `spec.md` — full requirements specification with data model, UI behavior, tech stack, constraints. Senior Coder confirms feasibility.
- **Gate Condition:** User approves the spec AND Senior Coder confirms it's technically achievable within current architecture.

### Gate 1.5: Senior Coder → Coder (Implementation Handoff)
- **Input:** Approved spec with Senior Coder's feasibility sign-off
- **Process:** Senior Coder hands the spec to the Coder with implementation guidance (architecture notes, patterns to follow, areas of concern)
- **Output:** Coder begins implementation
- **Gate Condition:** Senior Coder explicitly hands off. Coder cannot start without Senior Coder's go-ahead.

### Gate 2: Coder ↔ Senior Coder ↔ Reviewer (TDD Iteration Loop)
- **Input:** Coder produces working code using strict TDD (Red → Green → Refactor)
- **Process:**
  1. Senior Coder performs spot-checks during implementation (course-corrects if needed)
  2. When Coder reports "done," Senior Coder does a full architectural review
  3. If Senior Coder approves → triggers Reviewer
  4. Reviewer validates functionality, runs tests, checks coverage, finds bugs
  5. Reviewer sends findings back → Senior Coder triages:
     - Architectural issues: Senior Coder logs them and sends corrections to Coder
     - Non-architectural issues: flow directly from Reviewer to Coder
  6. Coder fixes → Senior Coder spot-checks → loop continues
- **Output:** All tests pass, Senior Coder signs off architecture, Reviewer signs off quality
- **Gate Condition:** BOTH Senior Coder AND Reviewer have signed off. No blocking issues remain.
- **TDD Requirement:** No production code exists without a corresponding test. Tests are written FIRST.
- **NO PUSHING DURING THIS LOOP.** All work is local commits only.
- **Logging:** Senior Coder documents all issues in `architecture-log/` throughout this phase.

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

1. **No agent skips a gate.** Coder cannot begin without Senior Coder's handoff. Reviewer cannot start without Senior Coder's sign-off. Learner cannot run until both Senior Coder and Reviewer pass.
2. **Agents are stateless between invocations.** All context must be passed explicitly (via files or prompts).
3. **Each agent operates within its defined scope.** The coder does not gather requirements. The reviewer does not write features. The Senior Coder does not write production code.
4. **Skills are mandatory reading.** Every agent MUST read the `skills/` folder before starting work and follow any applicable skills during execution. If a skill exists for a task, the agent uses it — no reinventing.
5. **Learnings are mandatory.** Every completed project must produce at least one learning entry.
6. **No pushing during Coder ↔ Senior Coder ↔ Reviewer loop.** All work stays local until user approves (Gate 2.5).
7. **Spec must be rock solid before handoff.** No open items, no unanswered questions in `planner-tasks.md` when the spec goes to the Senior Coder/Coder. If questions remain, they must be answered first.
8. **Spec-gap escalation.** If the coder, Senior Coder, or reviewer discovers an ambiguity, the loop pauses, the planner asks the user, updates the spec, and the loop resumes.
9. **Senior Coder reads the codebase.** Before every feasibility assessment or review, the Senior Coder MUST read the current architecture and relevant code. No assumptions.
10. **Architecture logging is mandatory.** The Senior Coder logs all issues, decisions, and architectural observations in `architecture-log/`. This is not optional.
11. **Technical questions go to Senior Coder first.** When the Planner encounters a technical/code question, it MUST consult the Senior Coder before escalating to the user. The Senior Coder answers technical questions using codebase knowledge and architecture expertise. Only if the Senior Coder cannot resolve the question (e.g., it's a business/product decision) does it escalate to the user.

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
