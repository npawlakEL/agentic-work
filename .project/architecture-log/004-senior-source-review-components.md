# 004 — Senior Source Review: PandA functional components (lookup, printer selection, config, verify, gaps)

**Author:** Senior Coder
**Date:** 2026-08-11
**Purpose:** In-depth review of how the PandA SQL source actually works across lookup, printer selection,
config, and verification — and an explicit checklist of components a first C# port could miss. Grounds the
phased backlog so nothing is lost. Source: `files/panda-src/.../8.0_CreateSP`, `5.0_CreateTables`, seed.

> Companion to 003 (concept map). 003 = *where things map*; 004 = *what the logic actually does* + gaps.

## A. Config (how PandA is parameterized)
- **Settings** key/value table, read via `sdisp_TOOL_GetSetting` (auto-creates a row with a default if the
  key is missing — so defaults are embedded at call sites). A cached settings provider replaces this.
- **Behavior settings that drive the engine** (defaults): `HeightCheckLabelField=2`, `DefaultDimension=20`,
  `DefaultHeight=10`, `DefaultLength=40`, `MinGap=20`, `EncoderResolution=0.2` (inch/pulse),
  `PrintExceptionLabels=0`, `DCMSExceptions=0`, `ForcedReplen=0`, `FilterLabels=1`,
  `PrinterStatusSuffix=0`, `Reprint Labels=0`, `OverwriteLabelData=0`, `DynamicPrintPoint=0`,
  `LoadBalanceEnabled=0`, `VerifyContentLabel=1`, `2 Printer Rule=0`,
  `PurgeSetting_UnUsedData=14 / UsedData=7 / InactiveData=21 / ExceptionData=7` (days).
- **Commissioning config tables:** `PandAs`/`PandADetails`, `Printers`/`PrinterDetails`,
  `PrinterFirePoints`, `LaneDef`, `LabelDef`/`LabelType`, `LabelProfileHeader`/`Detail`/`Map`,
  `Settings_CartonStatuses` (21 statuses), `PandAState`/`PrinterState`. → JSON config (decision-002).
- **Views** used by the engine: `sdivw_PrinterStatus`, `sdivw_PrinterDefs`, `sdivw_PrinterFirePoints`,
  `sdivw_LabelProfiles` (Active=1), `sdivw_PandAWaves`.

## B. Lookup (`sdisp_PA_LookupCarton`) — the induction brain
Inputs: PandaID, sorter/device/seq, up to 6 scanned labels, weight/gap/length/height, scanner id.
1. **Pre-checks on the blind label / gap** produce early statuses before any DB read:
   `No Read` (`?`,`-`,`<`), `No Data` (`!`,`0`), `Label Conflict` (`#`), `Gap Error` (`Gap < MinGap`).
2. **Active-record election** (many PandaData rows can share a blind label): `SELECT TOP 1 ... ORDER BY
   Printed ASC, ActiveRecord DESC, wave StatusTime ASC, CreationTime (oldest if append / newest if
   overwrite per `OverwriteLabelData`), PandaDataID ASC`; excludes `LabelType1='Exception'`; wave-gated to
   `WaveStatus='ACTIVE'`. (Filtered index `BlindLabel+ActiveRecord WHERE ActiveRecord=1`.)
3. **Profile validation:** `ProfileName` must exist in `sdivw_LabelProfiles` → sets validity (`No Profile`).
4. **Reprint/inactive rules:** if the elected record is inactive/already-printed, `Reprint Labels` decides
   `No Reprint` vs. reprint; logs "Record no longer active. Reprinting."
5. **Exception cartons:** when enabled (`PrintExceptionLabels`), builds an exception label
   (`sdisp_TOOL_PA_BuildExceptionLabel`) and INSERTs a PandaData record.
6. **History:** INSERTs a `PandaCartonList` row (the per-pass carton instance) → returns CartonListID,
   PandaDataID, CartonStatus, BlindLabel, FinalLaneID.

Status vocabulary (`Settings_CartonStatuses`, ~21): No Read, No Data, Label Conflict, Code Error, Gap
Error, No Profile, No Reprint, Inactive, Duplicate, PrintHold, Bypass, Verify Disabled, No Information,
Verify Pass/Fail, etc.

## C. Printer selection (`sdisp_PA_PickPrinter` + fire points)
1. Extracts carton length/height and labels 1–6; **orientation from label #2** (metadata, not printed).
2. **Eligible-printer CTE** filters: PLC status On, engine status OK, **not spare**, and **joins
   LabelProfileMap so the printer handles the label's type** (this is the `LabelMap` mapping).
3. **Load balancing** by `LastPrinted` (round-robin) **scoped per label type**, when enabled; spare
   printers are failover. **Collision rule:** if one printer is the primary pick for two of the carton's
   label types, the second falls back to its backup printer so labels spread across distinct printers.
   **Full algorithm: architecture-log 005.**
4. **Apply-point math for TOP printers:** height → inches → **encoder pulses** (`EncoderResolution`);
   supports `DynamicPrintPoint` for variable carton sizes; top-vs-side apply chosen per label/geometry.
5. **Per label:** `sdisp_PA2BP_SendPrinterFirePoints` looks up fire points
   (`sdisp_TOOL_PA_GetPrinterFirePoints`), validates devices, computes dynamic apply point, writes PLC tags
   (`DB2VLC`). `ErrorCode=2` (invalid fire point) → **skip that label, continue** (not fatal).
6. Then `sdisp_PA_Print` per label.

## D. Print (`sdisp_PA_Print`)
- **Vets/filters the ZPL** (`sdisp_TOOL_PA_VetLabel`/`VerifyLabel`, `FilterLabels`) — strips/adjusts before
  send; optional **status suffix** on the label (`PrinterStatusSuffix`).
- Sends via TCP (`sdisp_PA2TCP_SendTCPData`) to the printer IP:port; logs the event; sets the **Printed**
  flag. Reprint governed by `Reprint Labels` + `ActiveRecord`.
- `sdisp_TOOL_PA_BuildExceptionLabel` builds a fallback label when no data/profile.

## E. Verify (`sdisp_PA_VerifyCarton` → `sdisp_TOOL_PA_VerifyLabel`)
- Looks up the pass instance by CartonListID; pre-verify short-circuits: No Read/Data/Conflict → fail codes
  7/5/8; Gap Error → 4; `SeqNum > 2000` → Tracking Error.
- **Happy path:** scanned barcode compared against `LabelBarcode1..6`; match → **VerifyPass=1**; mismatch →
  a code from a **(label type × failure reason) matrix**. `VerifyContentLabel` toggles content-label check.
  > The exact code matrix and rules are now in **architecture-log 006 (verify model)** — treat 006 as
  > authoritative; the earlier "code 2–12" shorthand here was incomplete.
- **Thresholds:** `sdisp_PA_VerifyThreshold_Update`/`_Add`/`_Refresh` track consecutive fail counts
  (health/alarming).
- Result → routing via `sdisp_TOOL_PA_GetFinalLaneFromStatus`.

## F. Lane routing (`sdisp_PA_LaneEval`, `sdisp_TOOL_PA_GetFinalLaneFromStatus`)
- Final lane chosen by **matching carton status to LaneDef**, picking the **least-diverted** eligible lane,
  **fallback REJECT**. Note: **substring match** on status text (e.g. "REJECT" within
  "Verify - FAIL: Gap Error").
- `sdisp_PA_LaneEval` runs under `sdisp_PA_Lock` serialization; enforces `OnlinePrinterMin` and the
  **"2 Printer Rule"**, marking idle printers **spare** to keep a balanced pool.

## G. Status / health
- `sdisp_PA_Status_Printer` / `sdisp_BP2PA_Status_Printer` update `PrinterState` (PLCStatus,
  LastStatusUpdate); `sdisp_PA_Status_PrintEngine` tracks print-engine health. Printer online/engine status
  **gates** eligibility in PickPrinter.

## H. Eventing (→ native)
- `sdisp_Log_Event` writes structured events (PandaID, LPN, CartonListID, PandaDataID, LogLevel) inside
  every `BEGIN TRY/CATCH`. `sdisp_eLog_*` is a newer system. **Replaced by native logging** (not ported as
  data) — but the *traceability fields* (PandaID/LPN/carton ids) must be preserved in log scopes.

## I. Entry points (message points)
| Proc | PLC msg | Purpose | Critical |
|---|---|---|---|
| `sdisp_BP2PA_Scan_Induct` | 281 | induct from sorter | induction |
| `sdisp_BP2PA_Scan_Verify` | 286 | verify scan | verify |
| `sdisp_BP2PA_Status_Printer` | – | printer status | health |
| `sdisp_BP2PA_Print` | – | manual print submit | optional |
| `sdisp_MA_Scan_Induct` / `_Verify` | – | manual/operator override | optional |

## J. MISSED-COMPONENTS CHECKLIST (what a naive "advice→induct→pick→ZPL" slice overlooks)
Marked **[P1]** if relevant to our Phase-1 pass-through slice, **[later]** otherwise.
- **[later]** Gap / height / dimension checks (Gap<MinGap is a hard blocker → Gap Error, not warning).
- **[later]** Height→inches→pulses apply-point math + top/side apply + `DynamicPrintPoint`.
- **[P1]** Profile-exists validation *(we skip templates, but printer/type mapping validity still applies)*.
- **[P1]** Reprint interaction (`Printed` + active + `Reprint Labels`) — our don't-reprint-if-printed rule.
- **[P1]** Orientation/label metadata is **not** a printable label (don't treat every slot as ZPL).
- **[later]** Wave gating (ACTIVE-only) in lookup.
- **[P1]** **Load-balanced** printer selection (LastPrinted round-robin, **per label type**) vs
  first-available, **including the same-carton collision→backup-printer routing** (architecture-log 005).
- **[P1]** Printer **online/spare** gating before selection (offline/spare excluded).
- **[P1]** Fire-point error = **skip that label**, not fail whole carton (per-label resilience).
- **[later]** Bypass override (skips all checks) and One-Time-Use.
- **[later]** Duplicate detection; PrintHold; Forced Replen; No-Read/No-Data/Conflict statuses.
- **[later]** Exception-label auto-generation (`BuildExceptionLabel`) when no data/profile.
- **[later]** Status→lane **substring** routing + least-diverted + REJECT fallback.
- **[later]** Verify barcode-vs-LabelBarcode1-6 + VerifyPass codes 0–12 + threshold tracking.
- **[later]** Lane-eval spare assignment + "2 Printer Rule" + `OnlinePrinterMin`.
- **[later]** Purge jobs (Unused/Used/Inactive/Exception day-thresholds) — needed so `TuId` reuse can't
  match stale TOs (ties to decision-002 D9 active-record filtering).
- **[P1]** Carton-status vocabulary (`Settings_CartonStatuses`) — port as an enum/const set even for P1
  statuses (ADVISED/PRINTED/NO_DATA/NO_PRINTER).
- **[cross]** Traceability fields (PandaID, LPN, CartonListID, PandaDataID) in every log scope.
- **[cross]** `PandaCartonList` = per-pass **history**; in the TO model this is the TO lifecycle + status
  history, not a separate table.

## K. Coverage note
This review touches the core-path procs; the ~45 `sdisp_TOOL_SiteBuilder_*` (commissioning CRUD) collapse
to **authoring JSON config**, the ~30 `sdisp_GUI_*` to the **operator GUI** (backlog, essential-deferred),
and `sdisp_ScratchPad_*` to **tests**. Full object accounting remains in `panda-object-inventory.csv`.
