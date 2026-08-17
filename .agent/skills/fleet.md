---
name: fleet
description: >
  Use to decide whether to run a FLEET of parallel agent loops and, if so, how to
  shard the work safely. The Orchestrator engages this AUTOMATICALLY for large,
  broad, shardable work (codebase deep-dive, big Finalize, broad refactor/
  migration, test backfill, multi-repo propagation) and stays single-track for
  small or tightly-coupled work. Covers the auto-scaling decision, ownership
  sharding to prevent conflicts, concurrency sizing, and the draft-PR-only rails.
load_when: >
  A large/broad or deep-dive request, a Finalize with many independent findings,
  a wide refactor/migration/test-backfill, or multi-repo work — anything where
  parallelizing across agents might help. Match keywords: fleet, parallel, deep
  dive, large refactor, migration, backfill, multi-repo, many findings, scale out.
upstream: true
---
# Skill: Fleet (Auto-Scaled Parallel Execution)

## Trigger
- The Orchestrator runs this decision on every substantial request — **it is
  automatic**, not user-invoked. The user never has to say "use a fleet."
- It fires when work *might* be worth parallelizing; the checklist below decides
  yes/no and how many.

## The decision (run it, then act — don't ask)
Engage a fleet only when **BOTH** are true:
1. **Large / broad** — many independent units of work (findings, files, modules,
   repos), not a single change.
2. **Shardable** — the units are independent and can be given **non-overlapping
   file/module ownership**, each with clear acceptance criteria.

**Auto-engage a fleet for:** codebase deep-dive; large Finalize with many
independent fixes; broad refactor/migration across many call-sites; test/coverage
backfill; lint/dependency sweep; multi-repo harness propagation; Nightwatch across
repos.

**Stay single-track (the default) for:** small changes, single-file/hot-path
fixes; tightly-coupled feature work (parallel agents would collide); ambiguous
specs (tighten first — a fleet multiplies misunderstanding); anything that can't
be partitioned without file overlap.

> **Coupling — not size — is the deciding factor.** Big but tangled = single
> track. Wide but independent = fleet. When in doubt, single-track: an unnecessary
> fleet costs more (conflicts, review flood) than sequential work.

## Sharding (Senior Coder owns the partition)
1. The Senior Coder divides the work into independent units and assigns **exclusive
   file/module ownership** — no two loops ever write the same file.
2. **Shared/core files** (touched by many units) are pulled out and handled
   single-track or serialized FIRST, then the independent units fan out.
3. Ownership is blast-radius aware: a unit "owns" not just its files but the
   coupled tests it must keep green (Constraint #21).

## Concurrency sizing
- Pick **N proportional to independent units AND the user's review bandwidth** —
  not "as many as possible." A fleet that outruns the human merge gate just builds
  a backlog.
- Cap N to what the Orchestrator can coordinate without drift (Constraint #22).

## Running it
- Each loop is a **normal gated Coder ↔ Reviewer loop** on its own branch; the
  Senior Coder is the shared architect across all of them.
- Each loop runs its coupled regression suites (and `harness-check.mjs` for
  harness changes) and opens a **draft PR only** — **the fleet never merges**;
  the user is always the merge gate.
- Consolidate results into ONE prioritized, deduplicated review queue + a roll-up
  digest; keep `.project/STATE.md` current with fleet status.

## Announce (visible, every time)
```
🚁 FLEET auto-engaged — [N] loops (large + shardable)   [or]   single-track (no fleet needed)
   Loop k → [unit] · owns [files]
   Shared/core [files] → single-track
```

## Notes
- Fleet multiplies your inputs: good specs → more good work; fuzzy specs → more
  mess, faster. The spec-solid gate (Constraint #7) matters MORE at fleet scale.
- Execution agents (Coder/Reviewer) scale horizontally on Sonnet-tier; the shared
  Senior Coder stays Opus-tier. See `.agent/model-config.md`.

## Evidence
User request, 2026-08-17: add fleet deployment but have the Orchestrator direct it
automatically by work size/coupling — "if it's a large project or code deep-dive,
do it auto; if it's small or a change, I don't see a fleet being necessary."
