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

_(Next cycle's changes will be logged here by the Learner.)_

---

## [0.5.0] — 2026-08-12 — Lane-eval + verify-semantics cycle

First tagged version. Captures the accumulated PandA.Core / PandA.Sim / PandA.Harness engine built to date
plus this cycle's lane-eval slice and the retro-review verify/advice adjudications (decision-004).

### Added
- **Lane eval / spare / dynamic printer state** (arch-log 012, gap F09): dynamic `PrinterState`
  (`LastStatusUpdate`, `VerifyFailCount`, `IsOnline`), per-orientation `PrinterGroupPolicy` with a generalized
  degraded/slow-line policy (supersedes the source 2-Printer Rule), `ZoneState`, and `LaneEvalService`
  (promote/demote spare → `SlowLine`/`ShutLine`/`ShutZone`). Wired into `SimHost` + harness
  (`p <id> up|down`, `z up|down`, `s`).
- Verify bypass safety (decision-004 VF-3): a bypass that reads no-read/no-data now fails, holds the carton,
  and counts toward the pause threshold (a clean bypass still proceeds without counting).
- Configurable post-trip threshold reset (decision-004 VF-4): `VerifyThresholdTracker(resetOnTrip)`.
- Fire-point resolve-on-print integration coverage; `PrinterSelectionService.IsAvailable` invariant doc.

### Changed
- `VerifyThresholdTracker` constructor now takes `resetOnTrip` (default `false` = prior behavior).

### Decisions
- **decision-004** — verify-core & re-advice semantics: VF-1 (unexpected/duplicate scan ⇒ FAIL, kept strict),
  VF-2 (same-type expected = ordered slots, kept), VF-3 (bypass glitch fails+counts, implemented),
  VF-4 (configurable post-trip reset, implemented), AD-1 (reprint-rules-gated re-advice → backlog F-ADV1).

### Baseline (pre-0.5.0, previously untracked)
- Phase-1 print path: carton advice (MP1), induct/print (MP2), printer selection + load-balancing/collision
  rules, per-line label buffer order, fire-point profiles (inch/edge `1T/1L/0M`).
- Phase-2 verify: classification/toggles, per-line consecutive fail-threshold, verify-station lifecycle.
- Reprint lifecycle correction (decision-003): monotonic `PrintCount`, hold-for-intervention, operator-gated
  reprint.
- Message sim harness over real 281/286 wire frames; PLC↔PandA protocol doc (arch-log 009).

### Tests
- 231 passing.