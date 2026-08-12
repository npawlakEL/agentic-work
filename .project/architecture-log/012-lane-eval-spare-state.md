# 012 — Lane evaluation, spare management, and printer state

Status: **accepted (design)** · Ports `sdisp_PA_LaneEval` + the `PrinterState` / `PandAState`
runtime model. Resolves gap **F09**. Related: 005 (printer selection), 011 (gap analysis).

## 1. Purpose

A PandA line keeps a target number of printers **online and in rotation** per apply
orientation. When printers drop or recover, the line must automatically:

- **park surplus printers as spares** (functional reserves, excluded from selection), and
- **pull a spare back into rotation** when an active printer is lost, and
- when it can no longer meet the minimum and has no spares left, decide to **slow** the
  line (degraded operation) or **shut** it down.

This is the "lane evaluation" loop. It runs every time a printer- or zone-status signal
arrives. This doc defines the C# model and the decision algorithm, ported from
`sdisp_PA_LaneEval`, plus one deliberate generalization (degraded-mode, §5).

## 2. The `PrinterType` dimension — grounded in source data

**`PrinterType` IS the apply orientation (`Side` / `Top`).** Confirmed from seed data:

- `PrinterDetails.AttributeName='PrinterType'` values are literally `Top` / `Side`.
- Per-line thresholds are keyed on those same values:
  `PandADetails`: `OnlinePrinterMin_Side`, `PrinterCount_Side`, `OnlinePrinterMin_Top`,
  `PrinterCount_Top` (e.g. panda_01 → Side 2/2, Top 1/1).
- `sdisp_PA_LaneEval` groups by `PrinterType` and looks up the min via
  `AttributeName LIKE 'onlineprintermin%' AND LIKE '%' + PrinterType`.
- `sdisp_PA_PickPrinter` selects by that same orientation.

**Consequence:** the min-count + spare check is done **per orientation group**,
independently. Losing a `Side` printer is evaluated within the `Side` group only; `Top`
is untouched. Our `PrinterConfig.PrinterType` (`ApplyOrientation`) already carries this
dimension — we only add the per-orientation thresholds and the dynamic state.

## 3. Runtime state model

### `PrinterState` (source `PrinterState` table) — now fully dynamic

| Field | Source | Meaning |
|---|---|---|
| `PlcStatus` (bool) | `PLCStatus` 0/1 | PLC/applicator health (set by printer-status signal) |
| `EngineStatus` (bool) | `EngineStatus` 0/1 | print-engine health (set by engine-status ingestion, F13) |
| `IsSpare` (bool) | `IsSpare` 0/1 | parked as a functional reserve (excluded from selection) |
| `LastStatusUpdate` | `LastStatusUpdate` | when status last changed; tie-break for promote/demote |
| `LastPrinted` | `LastPrinted` | load-balance key (least-recently-printed first) |
| `VerifyFailCount` | `VerifyFailCount` | consecutive verify failures (existing threshold feature) |

- `Online => PlcStatus && EngineStatus` — **both** required (source checks both).
- `IsAvailable => Online && !IsSpare` — eligibility for selection (unchanged contract).

Both PLC and engine signals are modeled and required now. The **message format / parsing**
for the engine-status wire is bookmarked (F13); this slice drives the fields directly.

### `ZoneState` (source `PandAState` table) — per line

| Field | Source | Meaning |
|---|---|---|
| `ZoneOnline` (bool) | `ZoneStatus` 0/1 | conveyor zone up/down |
| `ActiveTrainId` | `ActiveTrainID` | current train on the line (carried for parity; not used by lane-eval) |

### `PrinterGroupPolicy` (source `PandADetails` per-orientation attributes) — per line, per orientation

| Field | Source | Meaning |
|---|---|---|
| `OnlineMin` | `OnlinePrinterMin_<T>` | usable printers required for **full-speed** running |
| `PrinterCount` | `PrinterCount_<T>` | installed printers of this orientation |
| `SlowLineFloor` | *new (see §5)* | min usable to keep running **degraded**; default 1 |
| `AllowDegraded` | *generalizes `2 Printer Rule`* | customer opts into slow-line operation |

`LineConfig` gains `IReadOnlyDictionary<ApplyOrientation, PrinterGroupPolicy> PrinterPolicies`.

## 4. The algorithm (ported from `sdisp_PA_LaneEval`)

Per line, under a status lock (`sdisp_PA_Lock 'PA_Status'` — modeled as a per-line guard),
**for each orientation group present**, with re-evaluation after any promotion:

```
clear IsSpare on every offline printer            (a down printer can't be a reserve)

for each orientation group G with policy P:
    online  = count(G where Online)
    spare   = count(G where IsSpare)
    total   = count(G)
    usable  = online - spare

    if usable < P.OnlineMin:
        if spare > 0:
            promote newest spare (IsSpare=0, order LastStatusUpdate DESC); RE-EVAL ALL
        elif P.AllowDegraded and usable >= P.SlowLineFloor and usable >= 1:
            SlowLine
        else:
            ShutLine  (source logs at level 50)
    elif online > P.OnlineMin and usable > P.OnlineMin:
        demote newest active non-spare (IsSpare=1, order LastStatusUpdate DESC)
    else:
        Balanced
```

**Invariant:** keep exactly `OnlineMin` usable per orientation; park surplus as spares; on
loss pull from spares; when none left, slow (if degraded allowed) or shut.

**Source-faithfulness deviations (deliberate):**
1. **Degraded generalization** — see §5. The source's `2 Printer Rule` is the
   `PrinterCount==2, online==1` case of this.
2. **Demote picks a non-spare** — the source demote query orders online printers by
   `LastStatusUpdate DESC` *without* excluding already-spare printers, so it can pick an
   already-spare printer and no-op (a latent source bug; harmless because it runs once).
   We demote a **non-spare** online printer (the correct intent). Documented here.
3. **Offline-spare-clear on either signal down** — the source clears `IsSpare` only when
   `PLCStatus=0 AND EngineStatus=0` (both down); we clear when `!IsOnline` (either down).
   This is more correct: an engine-only-down spare would otherwise stay counted in
   `SpareCount` and depress `usable` below reality. The clear is scoped to the line's own
   printers (matching the source's per-line `PrinterRecID` scoping).

## 5. Generalized degraded-mode policy (supersedes the source `2 Printer Rule`)

The source hard-codes one degraded case: `PrinterCount=2, OnlineCount=1, 2 Printer Rule=1`
→ SlowLine instead of ShutLine. The intent is broader: *when a group loses redundancy but
still has ≥1 working printer, some customers want the line to keep running slow rather than
stop.* We model that intent directly:

- `AllowDegraded` — the customer opts a group into slow-line operation.
- `SlowLineFloor` — the minimum usable printers to keep running degraded (default `1`).

When a group can't meet `OnlineMin` and has no spare to promote:
`AllowDegraded && usable >= SlowLineFloor` → **SlowLine**, else **ShutLine**.

**Strict superset of the source:** a group with `PrinterCount=2, OnlineMin=2,
SlowLineFloor=1, AllowDegraded=true` reproduces the exact `2 Printer Rule` behavior (lose
one → usable 1 ≥ floor → SlowLine; lose both → usable 0 → ShutLine). It now also covers
over-provisioned groups (e.g. limp along on 1–2 of 4) and hard-stop groups
(`AllowDegraded=false`).

The **meaning** of SlowLine (fewer printers can't service cartons at full conveyor speed,
so the line is slowed so survivors service every carton) is an egress concern: the actual
slow tag / rate is the BluePaw egress and stays **stubbed** here. We model the *decision*.

## 6. Result contract

`LaneEvalService.Evaluate(line, states, zone)` **mutates** `PrinterState.IsSpare` (and
`LastStatusUpdate`) directly, and **returns**:

```
LaneEvalResult {
    LineControl Control          // Balanced | SlowLine | ShutLine | ShutZone
    IReadOnlyList<PrinterChange> // { PrinterId, PromotedFromSpare | DemotedToSpare }
    string Reason                // human-readable, per orientation
}
```

- If `ZoneOnline == false` → short-circuit to `ShutZone` (no spare math; the whole line is
  down regardless of printers). Mirrors `sdisp_PA_Status_Zone` setting `ZoneStatus=0`.
- SimHost / harness **log** `Control` + `Changes`; real BluePaw stop/slow egress is stubbed.

## 7. Triggers (functionality now; message parse bookmarked)

| Signal | Source SP | Effect |
|---|---|---|
| Printer status | `sdisp_PA_Status_Printer` | set `PlcStatus` → run lane-eval |
| Engine status | (F13 ingestion) | set `EngineStatus` → run lane-eval |
| Zone status | `sdisp_PA_Status_Zone` | set `ZoneOnline` for all lines in zone → `ShutZone` when down |

The inbound message codes (283 printer / 284 zone) and the engine-status parse are
**bookmarked** (F13 + BluePaw ingestion). This slice exposes the functional entry points;
SimHost drives them directly.

## 8. Selection integration

`PrinterSelectionService` already excludes unavailable printers via
`PrinterState.IsAvailable` (`Online && !IsSpare`). Once lane-eval maintains `IsSpare`
dynamically, selection automatically honors spares with no change. Added test coverage
confirms a demoted (spare) printer is skipped and a promoted one re-enters rotation.

## 9. Out of scope / bookmarked

- BluePaw stop/slow/zone egress tags + codes (existing backlog).
- Engine-status message ingestion + codes (F13).
- The `sdisp_PA_Lock` distributed mutex — modeled as an in-process per-line guard; real
  cross-process locking is an integration concern.
- `ActiveTrainId` train tracking (carried on `ZoneState` for parity; unused here).
