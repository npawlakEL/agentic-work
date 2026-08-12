# Cycle 012 — Lane evaluation, spare management, dynamic printer state (gap F09)

Owned by: Senior Coder. Retroactively recorded — this cycle was implemented before the taskboard
discipline was applied; captured here for traceability. Design: `.project/architecture-log/012`.

## Context

Port `sdisp_PA_LaneEval` + the `PrinterState`/`PandAState` runtime model into `PandA.Core`. Confirmed
from seed data that source `PrinterType` == apply orientation (Side/Top), so min-count/spare grouping is
per-orientation. Generalize the source "2 Printer Rule" into a degraded (slow-line) policy.

## Stories

### S1 — Dynamic PrinterState — ✅ done
Add `LastStatusUpdate`, `VerifyFailCount`, `IsOnline` (PLC && Engine) to `PrinterState`; keep `IsAvailable`.
**AC:** state is mutable at runtime; `IsOnline` requires both signals; selection contract unchanged.

### S2 — PrinterGroupPolicy + LineConfig wiring — ✅ done
Per-orientation `{ OnlineMin, PrinterCount, SlowLineFloor, AllowDegraded }`; `LineConfig.PrinterPolicies`.
**AC:** legacy 2-printer rule reproducible as `count=2, min=2, floor=1, degraded=true`.

### S3 — ZoneState — ✅ done
Per-line `ZoneOnline` + `ActiveTrainId`.
**AC:** zone-down short-circuits lane-eval to ShutZone.

### S4 — LaneEvalService + LaneEvalResult — ✅ done
Port the algorithm: clear spares on offline; per-orientation promote/demote; Balanced/SlowLine/ShutLine/
ShutZone; re-eval loop on promote; return applied changes. Deviations: degraded generalization,
demote-non-spare, offline-clear-on-either-down (all logged in doc 012 §4/§5).
**AC:** invariant "exactly OnlineMin usable per orientation" maintained; all four LineControl outcomes reachable.

### S5 — SimHost + harness wiring — ✅ done
Seed a Side group policy + zone; `p <id> up|down`, `z up|down`, `s` commands; log line-control + changes;
stub BluePaw egress.
**AC:** all four outcomes demonstrable live (verified: demote → promote → slow → shut → zone-shut).

### S6 — Tests — ✅ done (+ Reviewer gap fills)
Initial 15 tests; Reviewer (reviewer-log 001) added 4 more (multi-pass promote, promote-newest,
surplus-of-two single shed, zone-down preserves spares). **222 tests green.**

## Bookmarked (out of this cycle)
Engine-status message ingestion (F13, incl. per-line lane-eval serialization), real BluePaw slow/shut/zone
egress tags + codes, cross-process status lock.
