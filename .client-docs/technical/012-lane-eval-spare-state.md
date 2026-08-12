# 012 — Lane evaluation, spare management & dynamic printer state (technical)

**Audience:** Coders / Senior Coders working on `PandA.Core` and the econtroller adapter.
**Cycle:** 012 · **Gap closed:** F09 · **Design:** `.project/architecture-log/012-lane-eval-spare-state.md`
**Source ported:** `sdisp_PA_LaneEval` (+ `sdisp_PA_Status_Printer/Zone`, `PrinterState`/`PandAState`/`PandADetails`).

## What was built

The line now automatically manages how many printers are actively printing per **apply
orientation** (Side/Top), keeping the rest in reserve, and decides when the line must slow or
stop. Previously `PrinterState` was static (never updated at runtime); it is now the live health
object that drives selection, verify thresholds, and lane control.

## New / changed types (`PandA.Core`)

| Type | Kind | Role |
|---|---|---|
| `PrinterState` | changed | Now dynamic: `PlcOnline`, `EngineOnline`, `IsSpare`, `LastStatusUpdate`, `VerifyFailCount`, `LastPrinted`; derived `IsOnline` (PLC && Engine) and `IsAvailable` (online && !spare). |
| `PrinterGroupPolicy` | new | Per-orientation thresholds: `OnlineMin`, `PrinterCount`, `SlowLineFloor`, `AllowDegraded`. |
| `ZoneState` | new | Per-line conveyor state: `ZoneOnline`, `ActiveTrainId`. |
| `LaneEvalService` | new | The ported algorithm. `Evaluate(line, states, zone, now) → LaneEvalResult`. |
| `LaneEvalResult` | new | `LineControl` (`Balanced`/`SlowLine`/`ShutLine`/`ShutZone`), `IReadOnlyList<PrinterChange>`, `Reason`. |
| `PrinterChange` | new | `{ PrinterId, SpareChange (PromotedFromSpare/DemotedToSpare) }`. |
| `LineConfig` | changed | Added `IReadOnlyDictionary<ApplyOrientation, PrinterGroupPolicy> PrinterPolicies`. |

## Key model fact (grounded in source seed data)

`PrinterType` in the source **is** the apply orientation (`Side`/`Top`) — confirmed from
`PrinterDetails.AttributeName='PrinterType'` and the per-orientation thresholds
`OnlinePrinterMin_Side/_Top`, `PrinterCount_Side/_Top` in `PandADetails`. So **the min-count and
spare checks are done per orientation group, independently.** Our `PrinterConfig.PrinterType`
(`ApplyOrientation`) already carries this dimension.

## Algorithm (how it works internally)

`LaneEvalService.Evaluate` runs per line. If the zone is down it short-circuits to `ShutZone`
(no spare math, no mutation). Otherwise, for each orientation group present on the line, with a
re-evaluation loop after any promotion:

1. **Clear spares on offline printers** (scoped to the line's own printers).
2. `usable = onlineCount − spareCount`.
3. `usable < OnlineMin`:
   - spare available → **promote** newest (`LastStatusUpdate DESC`), then re-evaluate all groups;
   - else if `AllowDegraded && usable >= SlowLineFloor && usable >= 1` → **SlowLine**;
   - else → **ShutLine**.
4. `onlineCount > OnlineMin && usable > OnlineMin` → **demote** newest active to spare (one shed per call).
5. otherwise → **Balanced**.

`LineControl` across groups aggregates by **max severity** (`Balanced < SlowLine < ShutLine < ShutZone`).
The service **mutates** the shared `PrinterState.IsSpare`/`LastStatusUpdate` and **returns** the
changes; it never throws on a miss.

## Deliberate deviations from source (traceable in doc 012 §4/§5)

1. **Degraded generalization** — the source's hard-coded "2 Printer Rule" is generalized to
   `AllowDegraded` + `SlowLineFloor`. A group with `count=2, min=2, floor=1, degraded=true`
   reproduces the exact source behavior; it now also covers over-provisioned groups.
2. **Demote picks a non-spare** — the source demote query can pick an already-spare printer and
   no-op (latent bug); we select a non-spare, matching intent.
3. **Offline-clear on either signal down** — source clears spare only when both PLC and engine are
   down; we clear when either is down (an engine-only-down spare would otherwise depress `usable`).

## Integration

- **Selection** (`PrinterSelectionService`) already filters on `PrinterState.IsAvailable`
  (`online && !spare`), so once lane-eval maintains `IsSpare`, spares are excluded automatically —
  no selection change was needed.
- **Triggers** (functionality wired; message parsing bookmarked): printer-status sets `PlcOnline`;
  engine-status sets `EngineOnline` (real ingestion is gap **F13**); zone-status sets `ZoneOnline`.
- **SimHost** seeds a Side policy (`min 2, count 3, degraded`) + zone, exposes `SetPrinterStatus`,
  `SetZoneStatus`, `PrinterStatusReport`, and logs the line-control decision; BluePaw slow/shut/zone
  egress is a stubbed log line.

## Testing patterns

`tests/PandA.Tests/LaneEvalServiceTests.cs` (19 tests). Pattern: build a `LineConfig` via a small
`Line(...)` helper with per-orientation policies, a `States(...)` dictionary of `PrinterState`, and
assert `(Control, Changes, mutated IsSpare)`. Covers balanced/demote/promote/slow/shut/zone,
per-orientation independence, multi-pass promotion cascade, newest-by-`LastStatusUpdate` selection,
and zone-down leaving spare flags untouched.

## Known limitations / tech debt (bookmarked)

- **F13** — engine-status message ingestion; **must serialize lane-eval per line** when it lands
  (`LaneEvalService` mutates shared state without locking — safe only under single-threaded SimHost).
- Real **BluePaw** slow/shut/zone egress tags + codes are stubbed.
- The source `sdisp_PA_Lock 'PA_Status'` distributed mutex is modeled as an intended in-process
  per-line guard, not yet implemented.
- `ActiveTrainId` is carried for parity but unused by lane-eval.
