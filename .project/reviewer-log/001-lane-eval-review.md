# 001 — Lane evaluation / spare / dynamic printer state review

**Feature slice:** Lane evaluation, spare management, dynamic printer state (gap F09)
**Commit reviewed:** `684b239` (branch `npawlakel-print-and-apply`)
**Found by:** Reviewer (code-review agent), Gate 2 full review
**Iteration:** 1
**Date:** 2026-08-12
**Design doc:** `.project/architecture-log/012-lane-eval-spare-state.md`
**Source ported:** `sdisp_PA_LaneEval` (+ `sdisp_PA_Status_Printer/Zone`, `PrinterState`/`PandAState`/`PandADetails`)

## Verdict: **No blocking bugs.** Tests 218/218 green.

Reviewer verified the risky mechanics by hand-tracing 1-spare, 2-spare-cascade, and mixed-orientation
cases: promote re-eval loop **terminates** and does **not** double-count; demote condition
(`online > min AND usable > min`) matches source; offline-spare-clear and per-orientation grouping
correct; `LineControl` precedence correct; selection excludes spares via `IsAvailable`. The two
sanctioned deviations (degraded generalization; demote-non-spare) are implemented correctly and
preserve the invariant.

## Findings & resolutions

| # | Sev | Finding | Resolution | Verified |
|---|-----|---------|------------|----------|
| 1 | Low (doc) | Offline-spare-clear fires on either signal down (`!IsOnline`), vs source `PLCStatus=0 AND EngineStatus=0`. Correct & intended, but not listed as a deviation in doc 012 §4. | Added as deviation #3 in doc 012 §4 (more correct; engine-only-down spare would otherwise depress `usable`). | yes |
| 2 | Medium (test gap) | Multi-pass promote (GOTO TOPCURS cascade) never exercised — every promote test promotes exactly one spare. | Added `MultipleSpares_PromotedAcrossPasses_UntilBalanced` (min=3, 1 active + 3 online spares + 1 offline → 2 promotes, Balanced, oldest spare retained). | yes |
| 3 | Medium (test gap) | Promote newest-by-`LastStatusUpdate` tie-break untested (single-spare only). | Added `PromotesNewestSpare_ByLastStatusUpdate` (two spares, newer promoted, older stays spare). | yes |
| 4 | Low (test gap) | "One shed per signal" for surplus > 1 (demote has no re-eval) not pinned. | Added `SurplusOfTwo_ShedsOnlyOneSparePerEvaluation` (usable=min+2 → exactly one demote). | yes |
| 5 | Low (test gap) | Zone-down short-circuit not asserted to leave spare flags/`LastStatusUpdate` untouched. | Added `ZoneDown_ShortCircuits_LeavesSpareFlagsUntouched` (offline stale spare preserved). | yes |
| 6 | Low (concurrency) | `LaneEvalService` mutates shared `PrinterState` unsynchronized; fine single-threaded, races when F13 ingestion lands. | Documented the per-line serialization requirement in the F13 backlog entry (the in-process guard doc 012 §9 promises). No code change now. | yes |
| 7 | Low (correctness) | Offline-clear iterated **all** `states.Values`, not just `line.Printers`; a future shared/global states dict could let one line clear another's spares. | Scoped the clear to `line.Printers` in `LaneEvalService.cs` (matches source per-line `PrinterRecID` scoping). | yes |

## Post-fix state
- Code changes: `src/PandA.Core/LaneEvalService.cs` (F7 scoping).
- Doc: `.project/architecture-log/012` §4 (F1); `.project/backlog/README.md` F13 (F6).
- Tests: +4 in `tests/PandA.Tests/LaneEvalServiceTests.cs` (F2–F5).
- Full suite re-run after fixes: **222/222 green.**
