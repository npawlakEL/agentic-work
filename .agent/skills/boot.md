---
name: boot
description: >
  Use to onboard the harness into a project: the Orchestrator does a deep read
  of all harness docs (agents.md, roles, skills, model-config) and project state
  (vision, spec, tasks, logs, codebase), then self-verifies and commits to the
  workflow. Run this FIRST after cloning the harness, or anytime the workflow
  seems to be slipping and the Orchestrator needs to re-anchor.
load_when: >
  Onboarding/ingesting the harness into a repo, a "boot"/"boot up"/"ingest the
  workflow" request, or re-anchoring when the workflow is being ignored.
  Match keywords: boot, ingest, onboard, load the workflow, start here, re-anchor.
upstream: true
---
# Skill: Boot (Ingest & Internalize the Harness)

## Trigger
- **First thing** after copying/cloning this harness into a new project.
- The user says "boot," "boot up," or "ingest the workflow/agents."
- Anytime the Orchestrator senses drift or the user feels the workflow slipping —
  Boot re-anchors it.

Boot exists because dropping the harness into a repo does NOT guarantee it gets
followed. Boot makes ingestion explicit, deep, and verifiable.

## Steps
1. **Detect the repo type FIRST** (this sets expectations for everything below):
   - **Harness-authoring repo** — the repo IS the harness itself (its purpose is
     to curate `.agent/`; `.project/vision.md`, `spec`, `taskboard` are intentionally
     blank templates; little/no application code or tests). Empty planning docs
     here are BY DESIGN, not gaps — do not flag them as missing.
   - **Downstream product repo** — the harness was cloned INTO a real project
     (application code + tests exist; planning docs are expected to be filled in
     as work proceeds). Here, empty vision/spec after work has started IS a gap.
   Announce which type you detected; the Boot Report's "gaps" section is judged
   against that type.
2. **Read the whole harness (bodies, not skims):**
   - `.agent/agents.md` — every gate, every constraint, every mode.
   - every file in `.agent/roles/` — know each agent's job and boundaries.
   - `.agent/model-config.md` — recommended models per agent.
   - every file in `.agent/skills/` — read frontmatter AND bodies so you know
     what will auto-load and when.
3. **Run a harness integrity check** (cheap, catches doc drift):
   - Every mode that has a section in `agents.md` also has a routing-table entry
     in `orchestrator.agent.md` (and vice-versa) — no orphaned/missing modes.
   - Every skill has complete frontmatter (`name`, `description`, `load_when`,
     `upstream`) and `name` matches its filename — no dupes or name collisions.
   - Every `.project/`-path and `.client-docs/`-path referenced in the docs
     actually exists.
   - Constraint numbering in `agents.md` is contiguous (no gaps/repeats).
   Flag any mismatch as a gap in the report instead of silently proceeding.
4. **Read the project state:**
   - `.project/vision.md` (the whiteboard — user preferences live here).
   - `.project/spec.md`, `.project/planner-tasks.md`, `.project/taskboard/`.
   - latest entries in `architecture-log/`, `reviewer-log/`, `learnings/`,
     `backlog/` — know what happened and what's in flight.
5. **Survey the actual codebase (read-only):** top-level layout, stack/build
   files, test setup, how modules are organized. Enough to steward it — do NOT
   change anything.
6. **Self-verify with a Boot Report** (proves ingestion happened):
   ```
   🚀 BOOT COMPLETE — harness ingested
   Repo type: [harness-authoring | downstream product]
   Who I am: [Orchestrator identity + personality, one line]
   Workflow: 1 → 1.5 → 2 → 2.5 → 2.75 → 3 [one-line gate summary]
   Non-negotiables: no gate skips · user is the merge gate · auto-engage Senior
     Coder on anything code · no Orchestrator drift · never weaken a test
   Integrity: [modes↔routing OK · skills frontmatter OK · paths OK — or list gaps]
   Modes available: hot-path, finalize, nightwatch, retro, grill me, regroup
   Skills loaded: [count + which are most likely to fire in this project]
   Project state: [what this project is, current phase, what's in flight]
   Gaps/risks: [missing/stale/misconfigured files — judged vs. repo type — or "none"]
   Ready. What are we building?
   ```
7. **Commit to enforcement:** explicitly affirm you will run the gates, delegate
   (not freelance), and keep agent activity visible.

## Notes
- Boot is **read-only** — it ingests and reports; it never edits code.
- If required harness files are missing or malformed, flag them as gaps in the
  report rather than silently proceeding.
- Re-runnable: Boot is both first-run init AND a re-anchor when drift creeps in.
- Boot pairs with Constraint #22 (no drift) and the Long-Session Discipline in
  `orchestrator.agent.md` — same intent, run at the start.

## Evidence
Motivated by user report: cloning the harness into a project did not reliably
make the workflow get followed; an explicit ingestion ritual was needed
(user request, 2026-08-17).
