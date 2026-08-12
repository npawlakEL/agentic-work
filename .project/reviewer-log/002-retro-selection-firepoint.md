# 002 — Retro-review: printer selection + load balancing (005) & fire-point profiles (010)

**Slices:** Printer selection/load-balancing (arch-log 005) + fire-point profiles (arch-log 010)
**Found by:** Reviewer (code-review agent), retroactive Gate 2 review
**Iteration:** 1 (retro) · **Date:** 2026-08-12
**Source ported:** `sdisp_PA_PickPrinter`, `sdisp_GUI_LabelProfile_Insert`, `PrinterFirePoints`.

## Verdict: **Both slices clean — no Critical/High correctness bugs.** 222/222 green.

Reviewer verified line-by-line against source SQL: selection eligibility predicate
(online + not spare + CanPrint + orientation), PID1/PID2 least-recently-printed ranking, the
collision→backup fallback (matches source MERGE incl. "no second pass"), ConfigOrder tie-break,
and case-insensitive label matching — all faithful. Fire-point: ApplyPoint sign/edge guard matches
the insert-time SQL guard, Parse/ToString round-trips all seed shapes, FirePoint tracking-device +
neglect-print guards match doc 010, profile duplicate-slot detection + resolver miss-behavior correct.

## Findings & resolutions

| # | Sev | Finding | Resolution | Verified |
|---|-----|---------|------------|----------|
| 1 | Medium (coverage) | Fire-point "resolve on print" in `InductService` (resolves `ActiveProfile` → attaches `PrintJob.FirePoint`) has **no test** — every induct/reprint test uses printers with no profile, so that code path is uncovered. It is in-scope shipped behavior (doc 010 §4). | Added induct integration tests: one with an `ActiveProfile` slot present → asserts the resolved `FirePoint` reaches the dispatched `PrintJob`; one with the slot absent → `FirePoint` null. | yes |
| 2 | Low | `PrinterSelectionService.IsAvailable` treats a **missing** `PrinterState` as available (SQL INNER JOIN would exclude it). Deliberate cold-start/testability convenience; only bites if state population is incomplete in production. | Documented the invariant in code ("provider MUST supply a state for every configured printer") on the `IsAvailable` helper; no behavior change (many tests rely on empty States()). | yes |
| 3 | Low (coverage) | 008 §B1 "two types with distinct rankings converging on one backup printer" not a named test row (functionally subsumed by existing collision tests). | Added one theory row for completeness. | yes |

## Post-fix state
- Tests added: `InductServiceTests` fire-point coverage (F1), `PrinterSelectionMatrixTests` row (F3).
- Code: doc comment on `PrinterSelectionService.IsAvailable` (F2).
- No core-logic changes — both slices were already correct.
