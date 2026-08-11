# 005 — Printer selection & load balancing (authoritative algorithm)

**Author:** Senior Coder
**Date:** 2026-08-11
**Status:** Authoritative. Verified against source `sdisp_PA_PickPrinter` + `sdisp_PA_LaneEval` +
`PrinterState`/`PrinterDetails`/`LabelProfileMap`/`LabelDef`. User-confirmed 2026-08-11.
**Why its own doc:** printer selection is a **complex, multi-label routing query** — the crux of "which
printer gets which label." It must be ported faithfully; a naive "first available" or global round-robin is
wrong.

> Companion: 004 (component review), decision-002 (architecture), spec.md §5.2.

## Terminology
- **Printer type** — `PrinterDetails.AttributeName='PrinterType'` value (e.g. `TOP`, `SIDE`). Drives
  apply-point geometry and is the grouping unit for spare/min logic.
- **Label type** — `LabelDef.LabelName` (e.g. `Shipping`, `Content`). What kind of label a printer is
  configured to emit. Not the same axis as printer type.
- **`LabelProfileMap`** — maps `PrinterRecID → LabelDefRecID` (→ `LabelDef.LabelName`). This is the
  **label-type → printer** capability map. One printer may map to **multiple** label types; one label type
  may be served by **multiple** printers.
- **`PrinterState`** — per printer: `PLCStatus`, `EngineStatus`, `IsSpare`, `VerifyFailCount`,
  `LastPrinted`, `LastStatusUpdate`.
- **`OnlinePrinterTypeMin`** — per printer-type minimum active count (PandA attribute
  `onlineprintermin<type>`).

## Part 1 — Selection & load balancing (`sdisp_PA_PickPrinter`, per carton)

### 1a. Eligible pool (`avlPrinters` CTE)
A row per **(printer, label-type-it-can-print, LastPrinted)** where, for the carton's PandA line:
- `PLCStatus = 'Online'` AND `EngineStatus = 'Online'` AND `IsSpare = 0`, and
- the printer is mapped to that label type via `LabelProfileMap ⋈ LabelDef`.
A printer mapped to N label types contributes N rows.

### 1b. Per-label-type round robin (`tablename` CTE)
For each **label type the carton actually needs** (`avl.LabelName IN (SELECT DISTINCT LabelType FROM
@labels)`):
- **PID1 (primary)** = `TOP 1 PrinterRecID … WHERE LabelName = <type> ORDER BY LastPrinted ASC` — the
  **least-recently-printed** eligible printer **for that label type**.
- **PID2 (backup)** = same query excluding PID1 — the **next** least-recently-printed for that type
  (may be NULL if only one printer serves the type).

Round-robin is therefore **scoped within each label type**, keyed on `LastPrinted` (ascending = staleest
first). After a label prints, `PrinterState.LastPrinted` is stamped to now, so the next carton rotates.
**Tie-break (port decision):** when candidates share an equal `LastPrinted` (incl. cold start / never
printed = min), **configured order wins** — the first printer listed for that label type in `PandaLine.json`.
This makes the otherwise non-deterministic SQL `ORDER BY` deterministic and testable.

### 1c. Same-carton collision avoidance (the critical, easy-to-miss rule)
A carton often needs several label types. If **one printer is chosen as PID1 for more than one of the
carton's label types**, it cannot physically print two different labels for the same carton at once. The
`MERGE` resolves this:
```
count = number of the carton's label types for which this printer is PID1
final printer for a label type = (count > 1 AND PID2 IS NOT NULL) ? PID2 : PID1
```
i.e. when a printer collides across two of the carton's label types, the colliding label type **falls back
to its backup (PID2)** so the carton's labels are **spread across distinct printers** whenever an
alternative exists. The chosen printer is written back onto each label (`@Labels.PrinterID`).

### 1d. After selection (per label)
Read `PrinterType` (TOP/SIDE) for the chosen printer → apply-point math (TOP: `height/25.4 /
EncoderResolution` → pulses) → send fire points (`PA2BP_SendPrinterFirePoints`; bad fire point `ErrorCode=2`
→ skip **that** label, continue) → print (`PA_Print`) → stamp `LastPrinted`.

### Notes / provisioned seams
- **Orientation is a provisioned dimension (deferred).** Beyond label type, a label carries an **orientation**
  (host sends it; defaults to `Side` when absent) and printers have a `PrinterType` (`TOP`/`SIDE`). A
  customer rule (seen as custom `sdisp_TOOL_CUSTOM_DynamicApplyPoint` / `DynamicPrintPoint` setting)
  **dynamically transfers a label from Side to Top when the carton height is below a threshold** — e.g.
  `Shipping` maps to both a side and a top printer, prints Side by default, Top when height < cutover.
  **Design implication:** `IPrinterSelectionService` must treat candidate resolution as
  **(label type + required orientation)** — resolve orientation first (default/host/height-rule), then run
  the per-label-type least-recently-printed round robin **within the printers of that orientation**. Model
  `PrinterType` on the printer config and pass carton height into selection now, even though the
  height-threshold transfer itself is deferred (Phase 1 uses orientation = default/host value only).
- `MANDA%` (manual apply) PandAs short-circuit to a directly chosen printer.
- The orientation label (`LabelType='Orientation'`) is extracted from the label set and removed before
  selection; in this core proc version it only defaults to `Side` and the apply-point math uses the chosen
  printer's `PrinterType`.

## Part 2 — Spare designation & pool health (`sdisp_PA_LaneEval`, on status change, under lock)

Runs **per printer type**. Counts: `OnlineCount` (PLC+Engine Online), `SpareCount` (IsSpare=1),
`TotalCount`. First clears spare on fully-offline printers (`PLCStatus=0 AND EngineStatus=0 → IsSpare=0`).
Then, with **effective active = OnlineCount − SpareCount** vs **Min = OnlinePrinterTypeMin**:
- **active < Min (short):**
  - `TotalCount=2 AND OnlineCount=1 AND "2 Printer Rule"=1` → **slow line** (`SlowLineDown`), don't shut.
  - else if `SpareCount > 0` → **activate a spare** (pick spare of type `ORDER BY LastStatusUpdate DESC`,
    set `IsSpare=0`); re-evaluate.
  - else → **shut the line down** (log threshold-exceeded).
- **OnlineCount > Min (surplus)** and `(active) > Min` → **park one online non-spare printer as spare**
  (`IsSpare=1`).
- **active == Min** → balanced, no change.

**Interaction with Part 1:** spares (`IsSpare=1`) are excluded from `avlPrinters`, so parking a printer
removes it from rotation; activating a spare returns it. A just-activated spare has an old/stale
`LastPrinted`, so it is naturally picked first by the round robin.

## Port mapping (C#)
- `LabelProfileMap` → per-line config: each printer's `LabelMap` = the label types it serves (decision-002).
- `IPrinterSelectionService` implements 1a–1c exactly: filter eligible (online + not spare + LabelMap
  contains type), per-label-type least-recently-used pick, **and the collision→backup fallback across the
  carton's label types**. Track `LastPrinted` per printer in the selection state (runtime, not config).
- `PrinterType`, apply-point math, fire points → later phase (host sends ready ZPL in Phase 1).
- `LaneEval` spare/min/2-printer-rule/shutdown → later phase (health), but modeled now so `IPrinter` state
  carries `IsSpare`, `PLCStatus`, `EngineStatus`, `LastPrinted`, `OnlinePrinterTypeMin`.

### Phase-1 relevance
Phase 1 (advice → induct → pick printer → send ZPL) **must** implement 1a–1c including the collision→PID2
routing (it is *the* printer-routing behavior). Spare **activation/parking dynamics** (Part 2) can be
deferred, but the eligibility filter must already honor `IsSpare=0` and online status.
