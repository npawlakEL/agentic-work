# Learning: Gate 3 was skipped for Orchestrator-direct changes

**Date:** 2026-08-17
**Cycle:** Harness self-maintenance (Boot/Retro/learning-loop session)
**Surfaced by:** User correction

## What happened
An entire session of harness/workflow changes (~10+ commits: finalize, auto-engage
Senior Coder, regression guardrail, master promotion, skill auto-loading, mutation
testing, Nightwatch, Boot, Retro, learning loop, vision flatten) was committed
**Orchestrator-direct** and pushed to `agent-harness`/`master` without ever running
Gate 3. `CHANGELOG.md` sat at the untouched template the whole time. The user
caught it: *"have you been updating the change log?"* — then: *"this must be a
skill; you didn't follow the workflow of spinning up a Learner after each commit."*

## Root cause
The routing table classifies "harness/workflow change → Orchestrator direct." The
Orchestrator wrongly read **"direct"** (who does the edit) as **"skip the
close-out"** (no Learner, no CHANGELOG). Gate 3 is owned by the Learner and applies
to every change that lands — it was silently dropped for the whole class of
Orchestrator-direct changes.

## Fix applied
1. **New skill** `.agent/skills/changelog-and-learn.md` (upstream) — fires on any
   commit/push/cycle-close and runs the Learner's Gate 3 duties, explicitly
   including Orchestrator-direct and hot-path changes. Allows backfill.
2. **Constraint #24** in `agents.md` — Gate 3 is never skipped; "Orchestrator
   direct" means the Orchestrator may do the edit, not skip the Learner.
3. **Routing table** row updated: "Harness/workflow change → Orchestrator direct →
   **then Learner closes Gate 3**."
4. **Backfilled** `CHANGELOG.md` as the `0.1.0` baseline from `git log`.

## Guardrail going forward
If the user ever again has to ask whether the changelog is current, this gate was
skipped and the Orchestrator failed. The `changelog-and-learn` skill auto-loads on
every commit to prevent recurrence.
