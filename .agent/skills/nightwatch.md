---
name: nightwatch
description: >
  Use to run or set up the scheduled trunk guardian: overnight it runs the full
  slow suite + mutation testing on unchanged master, drafts fixes for REAL
  regressions through the normal gates (never merges, never weakens a test),
  and emits a morning digest. Load when running "nightwatch", triaging its
  results, or wiring up the nightly schedule for a project.
load_when: >
  A scheduled/nightly trunk check, a "run nightwatch" request, triaging
  overnight red, or setting up the nightly workflow.
  Match keywords: nightwatch, nightly, scheduled, trunk guardian, overnight
  suite, morning digest, flaky, quarantine.
upstream: true
---
# Skill: Nightwatch (Scheduled Trunk Guardian)

## Trigger
- A scheduled (nightly) run fires against the main branch.
- The user says "run nightwatch" for an on-demand trunk health pass.
- You're triaging the results of an overnight run, or setting up the schedule
  for a project.

See the full protocol in `agents.md` → "Nightwatch Mode." This skill covers the
mechanics and the per-project setup.

## The three phases
1. **Overnight — re-check & hunt:** on the latest, *unchanged* `master`, run the
   FULL suite (unit + driver/integration/e2e) plus targeted mutation testing on
   critical modules. This is the slow signal a per-PR loop can't afford.
2. **If red — open a fix:** triage each failure (flaky vs. real). Real
   regressions go through the normal async fix loop and land as a **draft PR**.
   Never merge. Never weaken a test.
3. **Morning — digest:** summarize green/red, real vs. flaky, draft PRs opened,
   and any tooling that needed correcting. Log to `architecture-log/`.

## Triage: flaky vs. real (do this BEFORE any fix)
A failure is likely **flaky/environmental** if:
- It passes on re-run without any code change (re-run the specific test 2–3×).
- It depends on timing, ordering, randomness, network, or an external service.
- It fails intermittently across unrelated runs.
→ **Quarantine per the project's policy, log it, report it. Do NOT touch product
code.** Chasing a phantom regression is worse than leaving it flagged.

A failure is a **real regression** if it reproduces deterministically on the
unchanged trunk. → Run the fix loop.

## Fixing a real regression (async, gated)
1. Branch from `master` (e.g. `nightwatch/<date>-<short-desc>`).
2. Senior Coder scopes the fix and the blast radius (which suites must go green).
3. Coder implements the minimal correct fix. **Forbidden:** deleting assertions,
   loosening thresholds, adding `[Skip]`/`.skip`/`xit`, or otherwise weakening a
   test to force green. Green is earned by fixing the CODE.
4. Reviewer re-runs the relevant suites (and mutation testing if the module is
   critical) and signs off — same bar as any human PR.
5. Open a **DRAFT PR** into the user's queue. **Never merge** — the user is
   always Gate 2.75.
6. If the fix can't be made cleanly within the normal escalation limits, STOP,
   leave it red, and report it in the digest. No hacky patches.

## Morning digest format
```
🌙 NIGHTWATCH — <date>
✅ Green: <suites/modules that passed>
🔴 Real regressions: <n>  → draft PRs: #.. , #..
🌀 Flaky/quarantined: <n>  → <tests + why>
🧬 Mutation: <module> score <x%> (was <y%>) → survivors: ..
🛠️ Tooling corrections: <bad auto-fix reverted / limits hit>
❓ For you: <decisions needed>
```
Write it to `.project/architecture-log/` (dated); push open questions to
`.project/planner-tasks.md`, deferred items to `.project/backlog/`.

## Per-project setup (schedule + commands)
Nightwatch runs as a **scheduled workflow** (nightly cron). Because the actual
commands depend on the stack, wire these per project:
- **Test command(s):** the full suite, e.g. `dotnet test` (xUnit) and any driver/
  e2e runner; frontend `npm test -- --run`.
- **Mutation command(s):** e.g. `dotnet stryker --mutate "<critical globs>"` /
  `npx stryker run`, scoped to critical modules only.
- **Schedule:** nightly, off-hours (mutation runs are slow).
- **Kickoff prompt** for the scheduled session, e.g.:
  > "Run Nightwatch on `master`. Follow `.agent/skills/nightwatch.md`: run the
  > full suite + targeted mutation testing, triage red flaky-vs-real, open DRAFT
  > PRs for real regressions (never merge, never weaken a test), and post the
  > morning digest to `.project/architecture-log/`."

## Notes
- Nightwatch is the natural home for slow checks (mutation testing, full e2e)
  that don't belong in the per-save loop.
- It is a guardian, not an autopilot: its ceiling is "draft a fix + tell the
  user." Merging and test-changes-of-record always stay with the human.
