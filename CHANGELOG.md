# Changelog

## Versioning Scheme

**Format:** `MAJOR.MINOR.PATCH` (e.g., `1.2.3`)

| Position | When to increment | Example |
|----------|-------------------|---------|
| **PATCH** (0.0.X) | Minor revision, bug fix, hot-patch, small change | `0.0.1` → `0.0.2` |
| **MINOR** (0.X.0) | Major feature addition, significant new functionality | `0.1.0` → `0.2.0` |
| **MAJOR** (X.0.0) | Full version release — a component of all accumulated changes, milestone delivery | `0.2.3` → `1.0.0` |

## Ownership

- The **Learner** updates this file after every cycle (Gate 3)
- The **Orchestrator** approves version number increments
- Version bumps are committed alongside learnings and docs

---

## [Unreleased]

### Added
- **Fleet mode (auto-scaled parallel execution)** — the Orchestrator now automatically decides whether to run a fleet of parallel Coder ↔ Reviewer loops based on the work: engaged for large *and* shardable tasks (codebase deep-dive, large Finalize, broad refactor/migration, test backfill, multi-repo propagation), single-track for small or tightly-coupled work. Uses exclusive per-loop file ownership (conflict prevention by partition), proportional concurrency, and draft-PR-only rails (never merges). New `.agent/skills/fleet.md`, "Fleet Mode" section in `agents.md`, Constraint #25, Orchestrator routing row + "Automatic Fleet Scaling" section, README "Automatic Behaviors", and a fleet-economics note in `model-config.md`.
- `.project/STATE.md` — a live "where are we" snapshot (current gate, in-flight story, branch, last milestone) the Orchestrator maintains and `boot` reads first, so context recovery is instant instead of reconstructed from git log.
- `.agent/tools/harness-check.mjs` — a zero-dependency, cross-platform integrity checker (mode↔routing parity, skill frontmatter, referenced-path existence, contiguous constraint numbering). Wired into Boot and Nightwatch.
- README "Adopt this harness" quickstart — explicit *copy in → run `boot` first → vision → work the flow* onboarding.
- Categorized index over the Constraints list (by theme) without renumbering, so references stay stable as the list grows.

### Changed
- Finalize now treats **security** as a first-class audit dimension (injection, authz, secrets, unsafe deserialization, SSRF/path-traversal, supply-chain, sensitive-data exposure) in both `agents.md` and the Senior Coder role — was a single thin bullet.
- Skill auto-load scan now explicitly **excludes `README.md`** (it documents the format; it is not a loadable skill) in Constraint #4 and the skills README.
- Fixed the stale README structure diagram (removed a dead `.agent/vision/` path; added `.project/vision.md`, `STATE.md`, `backlog/`, and `.agent/tools/`).

---

## [0.1.0] — 2026-08-17

First versioned baseline of the Agent Harness. Backfilled from 40 prior commits
(2026-07-06 → 2026-08-17) that were never logged — see
`.project/learnings/2026-08-17-changelog-gate-skipped.md`.

### Added — Agents & core workflow
- Six-agent gated workflow: Orchestrator, Planner, Senior Coder, Coder, Reviewer, Learner.
- Six flow gates (1 → 1.5 → 2 → 2.5 → 2.75 → 3) with the user as the merge gate (Gates 2.5 & 2.75).
- Per-agent model recommendations tuned for Opus 4.8 (`.agent/model-config.md`).
- Orchestrator personality (chill surf-bro) and the Agent Visibility Protocol (announce every handoff).

### Added — User-invoked modes
- **Boot** — run-first ingestion ritual: deep-read the harness + project, repo-type detection, harness integrity check, self-verifying Boot Report, commit to the workflow.
- **Finalize** — multi-Senior-Coder read-only codebase audit → one prioritized report.
- **Nightwatch** — scheduled trunk guardian: full suite + mutation on unchanged trunk, drafts fixes (never merges, never weakens a test), morning digest.
- **Retro** — process retrospective that mines logs + corrections and curates skills.
- **Grill Me** — Planner deep requirement interrogation.
- **Regroup** — Planner + Senior Coder joint review.
- **Hot-path** — lightweight route for small fixes.

### Added — Enforcement (Constraints #1–#24)
- Auto-engage Senior Coder on any code-related request — the user never prompts for it (#20).
- Regression guardrail — coupled unit + driver suites run green on every code change; opt-in mutation testing for high-risk modules (#21).
- No Orchestrator drift — delegation does not decay over long sessions (#22).
- Corrections are training data — captured automatically and promoted to skills (#23).
- Gate 3 never skipped — every landed change (incl. Orchestrator-direct harness edits) closes with the Learner: CHANGELOG + learnings (#24).
- Documentation-as-blocking-gate, mandatory review-loop logging, Planner proactive questioning, universal request routing, state tracking, "no shortcuts / workflow is law."

### Added — Skills system
- Skills auto-load by frontmatter (`name`/`description`/`load_when`/`upstream`) description match.
- Universal skills upstream to the `agent-harness` source-of-authority branch.
- Skills: `commit-and-push`, `senior-coder-checklist`, `harness-doc-merge`, `mutation-testing`, `nightwatch`, `boot`, `retro`, `changelog-and-learn`.

### Added — Structure
- `.agent/` (agents.md, roles, skills, model-config), `.project/` (vision, spec, planner-tasks, taskboard + architecture-log / reviewer-log / learnings / backlog / planning-sessions), `.client-docs/` (operator + technical).

### Changed
- Flattened `.project/vision/vision.md` → `.project/vision.md` so the three singular planning docs sit flat while folders stay reserved for multi-entry logs.
- Promotion model: `agent-harness` is the source of authority; `master` mirrors it (harness/workflow-only, identical tree).

### Removed
- Stripped the original lane-config application code to make the repo a generic, reusable harness; scrubbed stale untracked build artifacts from the worktree.