---
name: changelog-and-learn
description: >
  Use whenever a change is about to land (a commit, a push, or closing any
  cycle) to run the Gate 3 Learner duties — bump CHANGELOG.md and write a
  learnings entry. Applies to EVERY change that lands, including
  Orchestrator-direct harness/workflow edits and hot-path fixes, not just full
  feature cycles.
load_when: >
  About to commit/push, closing a cycle or hot-path, or landing a harness/
  workflow/doc change. Match keywords: commit, push, changelog, version bump,
  close the cycle, Gate 3, learner, learnings, "did you update the changelog".
upstream: true
---
# Skill: Changelog-and-Learn (Gate 3 never gets skipped)

## Trigger
- ANY change is about to land: a commit, a push, or the close of a cycle/hot-path.
- **Especially** for Orchestrator-direct changes (harness/workflow/doc edits) — the
  ones that don't go through the full Planner→Coder flow and therefore quietly
  skip the Learner.

## Why this exists
The routing table sends "harness/workflow change → Orchestrator direct." That is
about *who does the work*, NOT about skipping the close-out. Gate 3 (Learner →
CHANGELOG + learnings) still applies to every change that lands. The failure mode:
the Orchestrator commits harness edits directly for a whole session and the
CHANGELOG never moves. If the user ever has to ask "have you been updating the
changelog?", this gate was skipped. (See Constraint #24.)

## Steps (Learner runs these before the change is considered done)
1. **Bump `CHANGELOG.md`** under `[Unreleased]` (or open it if empty):
   - PATCH (0.0.X): bug fix, hot-patch, small doc/workflow tweak.
   - MINOR (0.X.0): a new mode, agent capability, skill, or significant behavior.
   - MAJOR (X.0.0): milestone/baseline release.
   The Orchestrator approves the version number; the Learner writes the entry.
2. **Write a learnings entry** in `.project/learnings/` when the change taught
   something (a pattern, a pitfall, a corrected process). Not every trivial typo
   needs one, but every behavior/process change does.
3. **Group by theme** in the changelog (Added / Changed / Fixed / Removed) so the
   history stays readable, not a flat commit dump.
4. **Commit the CHANGELOG + learnings in the SAME commit as the change** (or the
   immediately following commit) — never let them drift a session behind.

## Notes
- This is the close-out half of every cycle — pair it with `commit-and-push`
  (that skill governs *when* a push is allowed; this one governs *what must be
  written before* the change is done).
- "Orchestrator-direct" is not an exemption. Direct = the Orchestrator may do the
  edit itself, but it still engages the Learner to close Gate 3.
- Backfill is allowed: if the log fell behind, reconstruct it from `git log` and
  land one catch-up entry rather than leaving it blank.

## Evidence
User correction, 2026-08-17: an entire session of harness changes was committed
Orchestrator-direct and the CHANGELOG was never updated. User: "this must be a
skill; you didn't follow the workflow of spinning up a Learner after each commit."
Promoted immediately per Constraint #23 (corrections are training data).
