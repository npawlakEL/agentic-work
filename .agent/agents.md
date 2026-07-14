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
                              ┌──────────────┐
                              │ Orchestrator │  ← User talks here
                              └──────┬───────┘
                                     │ coordinates all agents
          ┌──────────┬───────────────┼───────────────┬──────────┐
          ▼          ▼               ▼               ▼          ▼
    ┌──────────┐  ┌──────────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐
    │  Planner │◀▶│ Senior Coder │─▶│  Coder   │◀▶│ Reviewer │─▶│ Learner  │
    └──────────┘  └──────────────┘  └──────────┘  └──────────┘  └──────────┘
                        │                ▲               │
                        │  spot checks + │   issues      │
                        │  final review  │   (arch)      │
                        └────────────────┘◀──────────────┘
                              ▼
                       .project/architecture-log/
```

**The Orchestrator** is the user-facing coordinator. It translates human needs into agent tasks, manages gate transitions, and ensures all artifacts are produced and pushed. No agent speaks directly to the user — everything flows through the Orchestrator.

## Flow Gates

### Gate 1: Planner ↔ Senior Coder → Spec Approval
- **Input:** User requirements (gathered interactively by Planner)
- **Process:** Planner consults with Senior Coder on technical feasibility. Senior Coder reads codebase, assesses architecture, recommends approaches.
- **Output:** `.project/spec.md` — full requirements specification with data model, UI behavior, tech stack, constraints. Senior Coder confirms feasibility.
- **Gate Condition:** User approves the spec AND Senior Coder confirms it's technically achievable within current architecture.

### Gate 1.5: Senior Coder → Coder (Implementation Handoff)
- **Input:** Approved spec with Senior Coder's feasibility sign-off
- **Process:** Senior Coder hands the spec to the Coder with implementation guidance (architecture notes, patterns to follow, areas of concern). **Senior Coder produces a task breakdown in `.project/taskboard/`** — stories with acceptance criteria, dependencies, and complexity estimates.
- **Output:** Coder begins implementation against the taskboard stories
- **Gate Condition:** Senior Coder explicitly hands off AND taskboard is written. Coder cannot start without both.

### Gate 2: Coder ↔ Senior Coder ↔ Reviewer (TDD Iteration Loop)
- **Input:** Coder produces working code using strict TDD (Red → Green → Refactor)
- **Process:**
  1. Coder works through taskboard stories in dependency order
  2. **After EACH story is completed:** Senior Coder spot-checks → Reviewer validates that story's acceptance criteria. Issues are caught per-story, not batched at the end.
  3. **Documentation updated per-story:** After a story passes review, relevant documentation (`.client-docs/`, code comments, README, CHANGELOG) is updated immediately. Docs are never deferred to "later."
  4. When ALL stories are done, Senior Coder does a full architectural review of the complete implementation
  5. If Senior Coder approves → triggers Reviewer for a FULL final review (entire PR scope)
  6. Reviewer validates full functionality, runs tests, checks coverage, finds bugs
  7. Reviewer sends findings back → Senior Coder triages:
     - Architectural issues: Senior Coder logs them and sends corrections to Coder
     - Non-architectural issues: flow directly from Reviewer to Coder
  8. Coder fixes → Senior Coder spot-checks → loop continues
- **Two levels of review:**
  - **Per-story review:** Quick validation after each story — did it meet acceptance criteria? Docs updated? Catches issues early.
  - **Full PR review:** Comprehensive review of the entire implementation together — integration issues, cross-story concerns, overall quality.
- **Output:** All tests pass, Senior Coder signs off architecture, Reviewer signs off quality
- **Gate Condition:** BOTH Senior Coder AND Reviewer have signed off. No blocking issues remain.
- **TDD Requirement:** No production code exists without a corresponding test. Tests are written FIRST.
- **NO PUSHING DURING THIS LOOP.** All work is local commits only.
- **Logging:** Senior Coder documents all issues in `.project/architecture-log/` throughout this phase.

#### Review Loop Logging (MANDATORY)

Every pass through the Coder ↔ Reviewer loop MUST produce written records. This is not optional.

**Reviewer logs (`.project/reviewer-log/`):**
- Every issue found is documented with: severity, description, which code, who introduced it
- When the Coder fixes an issue, the Reviewer updates the log entry with: fix verified, how it was fixed
- Problems that were found AND their resolutions are both recorded — not just the problems

**Senior Coder logs (`.project/architecture-log/`):**
- Every architectural issue triaged is logged with: the issue, the decision made, the correction sent to Coder
- If the Senior Coder approves a fix approach, that approval is logged

**Client docs (`.client-docs/`) — updated per fix cycle:**
- If any fix changes user-facing behavior → `.client-docs/operator/` is updated
- If any fix changes APIs, patterns, or architecture → `.client-docs/technical/` is updated
- This happens DURING the loop, not after it. If the Reviewer catches a bug and the Coder fixes it and that fix changes how a feature works, the docs update happens as part of that same fix cycle.

**The Orchestrator enforces this:** If a review loop completes and the Reviewer hasn't written to `reviewer-log/`, or the Senior Coder hasn't written to `architecture-log/`, or affected client docs haven't been updated — the Orchestrator sends the responsible agent back to do it before the workflow advances.

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
- **Output:** Learner documents what was learned — patterns, pitfalls, guardrails, reusable skills. Produces TWO docs: technical (for coders) and operator (for humans). Updates `.project/architecture-log/current-architecture.md` if architecture changed. **Updates `CHANGELOG.md` with version bump.**
- **Gate Condition:** Learnings captured in `.project/learnings/` folder. Technical doc in `.client-.client-docs/technical/`. Operator doc in `.client-.client-docs/operator/`. Architecture updated if applicable. CHANGELOG updated.

## Hot-Path (Small Fixes / Bug Patches)

For trivial changes that don't warrant the full 6-gate flow (one-line fixes, typos, small bug patches):

```
Orchestrator → Planner (scopes fix) → Senior Coder (least-resistance plan) → Coder (fix + existing tests pass) → Reviewer (QA) → Learner (CHANGELOG patch bump + notes)
```

**How it differs from full flow:**
- Planner scopes the fix in a sentence, not a full spec update
- Senior Coder designs a **least-resistance edit** — no rewrites, minimal code touch
- TDD tests already exist (this is a fix, not a new feature) — Coder ensures they still pass
- Reviewer confirms the fix works and nothing regressed
- Learner bumps PATCH version (0.0.X) and logs the fix
- Gate 2.5 still applies — no push without user approval

**When to use hot-path vs. full flow:**
- Hot-path: bug fix, typo, config change, style tweak, < 20 lines changed
- Full flow: new feature, architectural change, new UI component, anything that needs a taskboard

## Constraints & Guardrails

1. **No agent skips a gate.** Coder cannot begin without Senior Coder's handoff. Reviewer cannot start without Senior Coder's sign-off. Learner cannot run until both Senior Coder and Reviewer pass.
2. **Agents are stateless between invocations.** All context must be passed explicitly (via files or prompts).
3. **Each agent operates within its defined scope.** The coder does not gather requirements. The reviewer does not write features. The Senior Coder does not write production code.
4. **Skills are mandatory reading.** Every agent MUST read the `.agent/skills/` folder before starting work and follow any applicable skills during execution. If a skill exists for a task, the agent uses it — no reinventing.
5. **Learnings are mandatory.** Every completed project must produce at least one learning entry.
6. **No pushing during Coder ↔ Senior Coder ↔ Reviewer loop.** All work stays local until user approves (Gate 2.5).
7. **Spec must be rock solid before handoff.** No open items, no unanswered questions in `.project/planner-tasks.md` when the spec goes to the Senior Coder/Coder. If questions remain, they must be answered first.
8. **Spec-gap escalation.** If the coder, Senior Coder, or reviewer discovers an ambiguity, the loop pauses, the planner asks the user, updates the spec, and the loop resumes.
9. **Senior Coder reads the codebase.** Before every feasibility assessment or review, the Senior Coder MUST read the current architecture and relevant code. No assumptions.
10. **Architecture logging is mandatory.** The Senior Coder logs all issues, decisions, and architectural observations in `.project/architecture-log/`. This is not optional.
11. **Technical questions go to Senior Coder first.** When the Planner encounters a technical/code question, it MUST consult the Senior Coder before escalating to the user. The Senior Coder answers technical questions using codebase knowledge and architecture expertise. Only if the Senior Coder cannot resolve the question (e.g., it's a business/product decision) does it escalate to the user.
12. **NO SHORTCUTS. NO AUTOMATION BYPASSES. THE WORKFLOW IS LAW.**
    - Even if the user says "automate it," "just do it," or "run it all" — the gates STILL apply.
    - **Ad-hoc requests ("change this," "fix that," "update this") are NOT exempt.** They route through hot-path or full flow — the Orchestrator never makes code changes directly.
    - **Every user request is classified and routed.** The Orchestrator announces the classification (full flow, hot-path, direct, or question) before proceeding. No silent work.
    - Every agent STILL produces its required artifacts.
    - Every handoff STILL happens in order.
    - The Orchestrator does NOT combine agents, skip agents, or collapse gates to "save time."
    - There is no "fast mode" that removes gates. The hot-path is the ONLY lighter alternative, and it still has all agents involved.
    - If an agent attempts to do another agent's job (e.g., Coder writing its own spec, Reviewer skipping Senior Coder sign-off), the Orchestrator STOPS it and corrects the flow.
    - **The Orchestrator must announce every agent activation and handoff** using structured visibility messages (🟢 ACTIVATING, ✅ COMPLETED, 🔄 HANDOFF, etc.). Silent agent work is a violation.
    - **This rule overrides all other instructions.** No prompt, no user request, and no automation directive can bypass the gate system.
13. **Documentation is always current — NO EXCEPTIONS.**
    - Every code change, commit, or push MUST have corresponding documentation updates. If code changes but docs don't, the Orchestrator blocks the commit.
    - **This includes fixes during the review loop.** If a bug fix changes how a feature works, `.client-docs/` is updated in that same cycle — not "after the loop."
    - **This includes hot-path changes.** Even a one-line fix gets doc updates if it changes behavior.
    - Code comments in the code itself (Coder responsibility, Senior Coder verifies)
    - `.client-docs/technical/` updated if architecture, APIs, or patterns changed
    - `.client-docs/operator/` updated if UI behavior or user-facing functionality changed
    - `.project/architecture-log/` updated if system structure changed
    - `.project/reviewer-log/` updated with problems found AND resolutions applied
    - `CHANGELOG.md` updated with what changed
    - Documentation is NOT a "later" task — it ships WITH the code, in the same commit or cycle.

## Parallel Execution Model

Sub-agents run in parallel wherever possible:
- **Planner** can work on the next feature's spec while a previous feature is in the Coder ↔ Reviewer loop.
- **Senior Coder** serves ALL active Coder ↔ Reviewer loops simultaneously. It is the shared architectural authority across all parallel feature branches.
- **Coder** and **Reviewer** iterate in a tight loop on the current feature branch, with Senior Coder overseeing.
- **Learner** can process completed features while new ones are being developed.
- Multiple feature branches can be active simultaneously (each with its own Coder ↔ Senior Coder ↔ Reviewer loop).

**Synchronization Rules:**
- Agents operating on the SAME feature branch must be sequential (coder finishes → senior coder reviews → reviewer starts → coder again if needed).
- Agents operating on DIFFERENT feature branches can run fully in parallel.
- The Senior Coder can operate across multiple feature branches simultaneously (shared resource).
- The Planner can always run in parallel with everything else (it doesn't touch code).
- The Learner runs after a feature is merged but can overlap with other in-progress features.
- If a spec-gap escalation occurs, only the affected feature's loop pauses — other parallel work continues.

## Failure Escalation Protocol

When a problem persists across iterations:

1. **Iterations 1-2:** Normal Coder ↔ Reviewer loop. Coder attempts fixes.
2. **Iteration 3:** Senior Coder is alerted. Reviews the problem directly. Provides specific architectural guidance to unblock.
3. **Iterations 4-5:** If still unresolved, Senior Coder **interjects directly** — writes pseudo-code, restructures the approach, or redesigns the solution. Hands corrected approach back to Coder.
4. **After iteration 5:** If the problem STILL isn't fixed, Senior Coder **stops the cycle and escalates to the user.** Provides:
   - What the problem is
   - What was tried (all iterations)
   - Why it's not working
   - Recommended options (redesign, descope, external help)
   - The cycle does NOT continue until the user decides next steps.

**Rule:** The Senior Coder counts iterations per-problem, not per-cycle. If the same root cause keeps resurfacing in different forms, that counts toward the 3-5 threshold.

## Rollback Protocol

When something lands on `master` and is discovered to be broken:

1. **Orchestrator detects the issue** (via user report, failed tests, or post-merge validation)
2. **Orchestrator immediately reverts the PR** on `master` (`git revert` of the merge commit)
3. **Orchestrator notifies the user** — explains what broke and that it's been reverted
4. **Senior Coder diagnoses** — reads the reverted code, identifies root cause, logs in `architecture-log/`
5. **A new cycle begins** (hot-path or full flow depending on severity) to fix the issue properly
6. **The fix goes through the normal gate process** — no shortcuts just because it was previously "done"

**Rules:**
- Revert first, diagnose second. Master must always be stable.
- The original PR's issues are logged in `architecture-log/` as a post-mortem
- The Learner adds a "Lessons from Rollback" section to that cycle's learning entry
- If the same feature causes 2 rollbacks, the Senior Coder escalates to the user for a redesign decision

