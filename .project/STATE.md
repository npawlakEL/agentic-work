# Project State

> **The "where are we right now" file.** The Orchestrator keeps this current so any
> agent — or a fresh session, or a `boot` — can instantly recover context without
> reconstructing it from git log and checkpoints. Update it whenever the workflow
> state changes (gate transition, story start/finish, branch switch, milestone).
>
> This is a live status snapshot, NOT a log. Overwrite fields in place; history
> lives in `architecture-log/`, `reviewer-log/`, and `learnings/`.

| Field | Value |
|-------|-------|
| **Current phase / gate** | Harness self-maintenance — idle / awaiting direction |
| **Active branch** | `agent-harness` (source of authority; mirrored to `master`) |
| **In-flight** | Nothing active |
| **Last milestone** | 6 harness updates: STATE.md, integrity checker, README quickstart, Finalize security, skill-scan fix, constraints index (CHANGELOG `[Unreleased]`) |
| **Blocked on** | Nothing |
| **Next up** | Await user direction (optional: instantiate real Nightwatch cron in a repo with tests) |
| **Last updated** | 2026-08-17 — Orchestrator |

## Notes
- The Orchestrator owns this file. If it's stale, resumption and `boot` degrade —
  keep it honest.
- For a **harness-authoring repo**, this tracks harness work; for a **downstream
  product repo**, it tracks the product's current cycle.
