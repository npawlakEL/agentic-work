# 011 — PandA Source Coverage Gap Analysis

**Date:** 2026-08-12  
**Author:** Senior Engineer (gap analysis pass)  
**Status:** Reference — do not edit; gaps fold into the backlog after review.

---

## 1. Introduction & Method

This document is a full diff of the PandA SQL source (322 objects) against what is already implemented in
`PandA.Core` + `PandA.Sim` and what is already recorded in the backlog. The goal is to surface every
meaningful piece of functionality that is **not yet implemented and not yet planned** (the "GAP" category).

**Method:**
1. Walked all 322 source objects across six subfolders: `2.1_CreateSystemMessages` (1), `3.0_CreateUDF` (6),
   `4.0_CreateSynonyms` (57), `5.0_CreateTables` (35), `6.0_PopulateTables` (22), `7.0_CreateViews` (31),
   `8.0_CreateSP` (170).
2. Opened every file (or representative sample for large families); all classifications are based on
   direct file reading, not inference from filenames.
3. Read all 12 backlog items from `.project/backlog/README.md` — every item there is PLANNED, not a gap.
4. Read key C# source files (`TransportOrder.cs`, `PrinterState.cs`, `LineConfig.cs`, `InductResult.cs`,
   `VerifyOutcome.cs`, `InductService.cs`, `VerifyStationService.cs`) to verify what is actually implemented.
5. Grouped all source objects into **43 functional families**, classified each, and produced 17 GAP entries
   ordered by priority.

**Scope note:** The user explicitly flagged three categories as out-of-scope from the start:
architecture-specific SQL Server adapter plumbing (synonyms, SynBuilder, DB2VLC/TCP transport views),
the `PandA.EController` physical telegram transport adapter, and multi-DB `SourceMode` routing. These are
classified OUT-OF-SCOPE below without further analysis.

---

## 2. Coverage Matrix

| # | Family | Key Source Objects | Classification | C# Equivalent / Notes |
|---|--------|--------------------|----------------|----------------------|
| F01 | Core Induct Pipeline (MP1+MP2) | `sdisp_BP2PA_Scan_Induct`, `sdisp_PA_Scan_Induct`, `sdisp_PA_LookupCarton`, `sdisp_PA_PickPrinter`, `sdisp_PA_Print` | **IMPLEMENTED** | `InductService`, `CartonAdviceService`, `PrinterSelectionService`, `IPrinterGateway` |
| F02 | Verify Pipeline (MP286) | `sdisp_BP2PA_Scan_Verify`, `sdisp_PA_Scan_Verify`, `sdisp_PA_VerifyCarton`, `sdisp_TOOL_PA_VerifyLabel` | **IMPLEMENTED** | `VerificationService`, `VerifyStationService`, `VerifyModels` |
| F03 | Verify Threshold Tracker | `sdisp_PA_VerifyThreshold_Update`, `_Add`, `_Refresh` | **IMPLEMENTED** | `VerifyThresholdTracker` |
| F04 | Scanner Buffer-Order | `Settings_LabelBufferOrder` seed, buffer-parsing block in `sdisp_TOOL_PA_VerifyLabel` | **IMPLEMENTED** | `LabelBufferOrder`; arch-log 009; done per backlog |
| F05 | Fire-Point Model + PLC Send | `PrinterFirePoints`, `LabelProfileHeader/Detail/Map`, `sdisp_PA2BP_SendPrinterFirePoints`, `sdisp_TOOL_PA_GetPrinterFirePoints` | **IMPLEMENTED (partial)** | `FirePoint`, `FirePointProfile`, `FirePointResolver` done (arch-log 010); lane dest, profile switching, DynamicApplyPoint deferred to backlog |
| F06 | Printer Selection + Load-Balancing | Selection logic in `sdisp_PA_PickPrinter`, `sdivw_PrinterStatus`, `LabelProfileMap`, `LabelDef` | **IMPLEMENTED** | `PrinterSelectionService`, `PrinterConfig`, `PrinterState` |
| F07 | Message Framing (281/282/283/284/285/286) | `sdisp_BP2PA_Scan_Induct`, `sdisp_BP2PA_Scan_Verify`, `PaMessages.cs` | **IMPLEMENTED (partial)** | 281+286 done; 282–285 deferred as sim expansion (backlog) |
| **F08** | **Lane Routing** | **`sdisp_TOOL_PA_GetFinalLaneFromStatus`, `LaneDef` table, `LastDiverted` column** | **GAP** | No `LaneDef`, no lane routing in C# |
| **F09** | **Lane Evaluation + Spare Management** | **`sdisp_PA_LaneEval`, `sdisp_PA_Status_Printer`, `sdisp_PA_Status_Zone`, `PandaState`, `PandADetails`** | **GAP** | `PrinterState.PlcOnline/EngineOnline` are static bools; no spare promotion algorithm |
| **F10** | **Exception Label Building** | **`sdisp_TOOL_PA_BuildExceptionLabel`, `LabelTemplates` table, exception block in `sdisp_PA_LookupCarton`** | **GAP** | 8 ZPL templates; `PrintExceptionLabels` default=0 in seed; no C# path |
| **F11** | **ZPL Vetting (VetLabel)** | **`sdisp_TOOL_PA_VetLabel`** | **GAP** | Strips 25+ commands; `FilterLabels` default=1 (ON); not in print pipeline |
| **F12** | **Printer Status Suffix (~HS)** | **`sdisp_TOOL_PA_AppendStatusSuffix`** | **GAP** | Appends `~HS` to request Zebra status reply; `PrinterStatusSuffix` default=0 |
| **F13** | **PrintEngine Status Ingestion** | **`sdisp_PA_Status_PrintEngine`, `PrintEngineStatus` table, `sdiudf_PA_GetPrinterRecIDFromConnections`** | **GAP** | Parses 3 Zebra TCP status messages (11/10/1 commas); 27 flag fields; no C# equivalent |
| **F14** | **MandA Manual Apply Stations** | **`sdisp_MA_Scan_Induct`, `sdisp_MA_Scan_Verify`, `sdisp_GUI_MandA_Scan`, `sdisp_GUI_MandA_Verify`, `sdisp_GUI_GetMandaList`** | **GAP** | Distinct operator workflow; PandaID prefix 'MANDA%'; no mode switch in C# |
| **F15** | **PLC Event / Carton Recovery** | **`sdisp_BP2PA_Event` (codes 2012, 2015, 2016, 2017, 2019)** | **GAP** | Resets `Printed=0`/`ActiveRecord=1` on tracking errors before verify scanner |
| **F16** | **Structured Event Logging** | **`sdisp_Log_Event`, `sdisp_eLog_LogIt`, `sdisp_eLog_add`, `EventLog`, `uEventLog`, `EventDescriptions`** | **GAP** | Called by every SP; two-tier log; no `IEventLog` in C# |
| **F17** | **Purge / Data Lifecycle** | **`sdisp_PA_Purge`** | **GAP** | Nightly cleanup of PandaData, EventLog, PrintEngineStatus etc.; no C# equivalent |
| **F18** | **XRef Multi-barcode Matching** | **`PandaDataXRef` table, `sdivw_PandaDataXRef`, xref joins in `sdisp_PA_LookupCarton` + `sdisp_TOOL_PA_VerifyLabel`** | **GAP** | C# lookup and verify only match on `TuId`/`Label.Lpn` |
| **F19** | **ProfileName Validation at Induct** | **`@IsValid` block in `sdisp_PA_LookupCarton`, `sdivw_LabelProfiles`** | **GAP** | Missing-profile→NoProfile status; no check in `InductService` |
| **F20** | **Gap Error + Scanner Read Quality Detection** | **Gap check (`@Gap < @MinGap`) and `?, !, #` character detection in `sdisp_PA_LookupCarton`** | **GAP** | `InductStatus.NoData` exists but character detection not in induct logic; `MinGap` not read |
| **F21** | **Carton Slot Number / PLC Index** | **`sdisp_TOOL_GetSlotNumber`, `CartonAssignSeq` SQL SEQUENCE (1–300 cycling)** | **GAP** | `PrintJob`/`InductResult` have no slot index; needed for `vPA.Assign[i]` PLC array |
| **F22** | **Reject History Audit Trail** | **`sdisp_CUSTOM_RejectHistory_Insert`, `RejectHistory` table** | **GAP** | Verify reject codes+reasons never persisted in C#; GUI reject screen depends on this |
| **F23** | **Wave Auto-complete (hot-path coupling)** | **`sdisp_TOOL_CUSTOM_CheckWaveCmp` (called inside `sdisp_PA_VerifyCarton`)** | **GAP** | Auto-completes wave on every verify-pass; must be wired into `VerificationService` |
| **F24** | **oLPN XRef Association** | **`sdisp_TOOL_CUSTOM_LPNxRef`, `sdisp_TOOL_CUSTOM_LPNxRef_Disassociate`** | **GAP** | ULW-specific: associates outer LPN post-advice; depends on XRef model (F18) |
| **F25** | **SiteBuilder Commissioning CRUD** | **`sdisp_TOOL_SiteBuilder_*` (26 procs: Create_*, Get_*, Remove_*, Update_*)** | **GAP (Low)** | GUI-driven config CRUD; C# uses JSON config; admin path not built |
| F26 | Carton Locking | `sdisp_PA_Lock`, `sdisp_PA_LockRemove` | **OUT-OF-SCOPE** | `sp_getapplock` = SQL-specific; C# uses `SemaphoreSlim`; architectural replacement |
| F27 | HI / DCMS Inbound Label Job | `sdisp_HI_DCMSCore_Inbound_PandALabels_Job`, `HI_Xfer_Inb_PandALabels`, `sdivw_HI_PandA_GetReadyPandALabels` | **OUT-OF-SCOPE** | Cross-DB DCMS polling adapter; C# equiv = `ITransportOrderStore` + `CartonAdviceService` |
| F28 | Wave Lifecycle | `Wave`, `WaveHistory`, `WaveRange`, `sdisp_TOOL_CUSTOM_Wave_*`, `sdisp_GUI_PandaWaveAction_*`, `sdisp_PA2DCMS_WaveStatus` | **PLANNED** | Backlog: "Wave / WaveRange data + wave lifecycle" |
| F29 | Operator GUI | ~30 `sdisp_GUI_*` procs | **PLANNED** | Backlog: "Operator GUI (Blazor reimplementation)" |
| F30 | DynamicApplyPoint | `sdisp_TOOL_CUSTOM_DynamicApplyPoint` | **PLANNED** | Backlog: "Fire-point profile switching + DynamicApplyPoint" |
| F31 | Duplicate-Advice Toggle | `OverwriteLabelData` setting, related block in `sdisp_PA_LookupCarton` | **PLANNED** | Backlog: "Duplicate-advice handling toggle" |
| F32 | BluePaw Stop/Slow Line | `sdisp_TOOL_PA_ShutLineDown`, `sdisp_TOOL_PA_ShutZoneDown`, `sdisp_TOOL_PA_SlowLineDown` | **PLANNED** | Backlog: "BluePaw stop-line / slow-line (codes TBD)" |
| F33 | Operator Reprint Auth | `sdisp_GUI_SetPrintedFlag` + `TransportOrder.AuthorizeReprint` | **PLANNED** | Backlog: "Operator reprint authorization" |
| F34 | Global Reprint Labels Setting | `Reprint Labels` setting (RecID 17) in `sdisp_PA_LookupCarton` | **PLANNED** | Backlog: "Global 'Reprint Labels' allow-all setting" |
| F35 | Fire-Point Profile Switching + ProfileName | `sdisp_PA2BP_SendPrinterFirePoints` ProfileName param, `sdivw_LabelProfiles` | **PLANNED** | Backlog: "Fire-point profile switching + host-driven ProfileName" |
| F36 | SynBuilder / Cross-DB Wiring | `sdisp_TOOL_SynBuilder_*`, all `sdisy_*` synonyms, `DB2VLC_*`/`MessageLog_*`/`TCP_TX_Send_*` | **OUT-OF-SCOPE** | SQL Server cross-DB synonym transport; out-of-scope per task brief |
| F37 | TCP Send (PA2TCP) | `sdisp_PA2TCP_SendTCPData` | **OUT-OF-SCOPE** | C# equiv = `IPrinterGateway`; adapter concern |
| F38 | Table Export | `sdisp_TOOL_Table2File`, `sdisp_TOOL_Table2File_JOB`, `AvailableTableExports`, `PendingTableExports` | **OUT-OF-SCOPE** | SQL BCP + `xp_cmdshell` export; no C# equivalent planned |
| F39 | ScratchPad Test Procs | `sdisp_ScratchPad_*` (15 procs) | **OUT-OF-SCOPE** | SQL-side test scaffold; Sim harness replaces in C# |
| F40 | Space / Diagnostic Tools | `sdisp_TOOL_SpaceScan`, `sdisp_TOOL_GetAllTableOldestRecord`, `sdisp_TOOL_Enable`, `sdisp_eLog_Harvest` | **OUT-OF-SCOPE** | DB management / dev tooling |
| F41 | Support Tools | `sdisp_Support_TOOL_*` (3 procs), `sdisp_TOOL_CUSTOM_CheckPandAInfo` | **OUT-OF-SCOPE** | Operational support / data migration |
| F42 | eLog Harvest | `sdisp_eLog_Harvest` | **OUT-OF-SCOPE** | Dev tool that extracts logging markers from SP source text |
| F43 | Sim Harness (SQL-side scaffold) | `sdisp_ScratchPad_Advice_*` (9 procs) | **OUT-OF-SCOPE** | Replaced by `PandA.Sim` + `PandA.Harness`; see backlog sim expansion |

**Totals:** 7 IMPLEMENTED (2 partial) · 8 PLANNED · **17 GAP** · 11 OUT-OF-SCOPE

---

## 3. Gaps — Detailed

### 3.1 HIGH PRIORITY

---

#### GAP-1 — Lane Routing Model
**Family:** F08  
**Source objects:** `sdisp_TOOL_PA_GetFinalLaneFromStatus` · `LaneDef` (table + seed in `6.0_PopulateTables/LaneDef.sql`) · `LastDiverted` column on `LaneDef` · `Settings_CartonStatuses` seed

**What it does:**  
`GetFinalLaneFromStatus(@PandaID, @CartonStatus, @FinalDestination)` maps a carton's status to a physical
divert lane number. The algorithm:
1. Find matching `LaneDef` rows for the line's `PandaRecID` using a three-rule match on `LaneID`:
   - Exact: `LaneID = @FinalDestination` (pass-dest from PandaData column `VerifyPassDestName` / `VerifyFailDestName`)
   - Suffix: `@CartonStatus LIKE '%' + LaneID` (e.g. 'Verify - FAIL: No Read' matches 'Verify - Fail')
   - Prefix: `@CartonStatus LIKE LaneID + '%'` (e.g. 'Verify - Pass' matches 'Verify - Pass')
2. Among matching lanes, pick the one with oldest `LastDiverted` (round-robin across dual-chute configs).
3. Fall back to the `REJECT` lane row if still NULL.

Seed data (`LaneDef.sql`): PandaRecID=1 → Verify-Pass=lane 12, Verify-Fail=lane 10, REJECT=lane 0;
PandaRecID=2 → Verify-Pass=lane 2, Verify-Fail=lane 1, REJECT=lane 0.

Called from `sdisp_PA_LookupCarton` (at induct — assigns default FAIL lane to `PandaCartonList.LaneNumber`)
and from `sdisp_PA_VerifyCarton` (at verify — updates `LaneNumber` to the actual divert lane, writes to
`PandaCartonList.LaneNumber` which the eController reads for the PLC Dest tag).

**Why it matters for the port:**  
The PLC fire-point bundle (`vPA.Assign[i].Dest`) must include the destination lane. Currently `InductResult`
and `VerifyOutcome` carry no lane field. Without this the eController adapter cannot fill the Dest tag —
fire-point integration (arch-log 010, partially done) is incomplete. Additionally, the C# `LineConfig` has
no `Lanes` collection and `LaneDef` has no equivalent model anywhere.

**Missing in C#:**
- `LaneDef` model class (LaneId string, LaneNumber int, LastDiverted DateTimeOffset?)
- `Lanes` collection on `LineConfig`
- `ILaneRoutingService` (or method on `LineConfig`) implementing the three-rule match + round-robin
- `DivertLane int?` field on `InductResult` and `VerifyOutcome`

**Priority:** High  
**Dependencies:** Feeds fire-point PLC tag bundle; needed before eController adapter; must come together
with slot number (GAP-7) so both Dest and Assign index are available at the same time.

---

#### GAP-2 — Lane Evaluation + Dynamic Spare Management
**Family:** F09  
**Source objects:** `sdisp_PA_LaneEval` · `sdisp_PA_Status_Printer` · `sdisp_PA_Status_Zone` · `PandaState` · `PandADetails` EAV · `PrinterState.PLCStatus` / `PrinterState.EngineStatus` (string columns in SQL)

**What it does:**  
`PA_Status_Printer` (triggered by msg 283 from the PLC) updates `PrinterState.PLCStatus = @Status` (string:
'Online', 'Offline', etc.), then calls `PA_LaneEval`. `PA_Status_Zone` (msg 284) updates
`PandaState.ZoneStatus = @Status` and calls `ShutZoneDown` when status=0.

`PA_LaneEval` algorithm per printer type (Side, Top — from `PandADetails` EAV key `onlineprintermin{Type}`):
1. COUNT online printers: `PLCStatus='Online' AND EngineStatus='Online'`.
2. COUNT spare printers: `IsSpare=1`.
3. If `(OnlineCount - SpareCount) < MinOnline`:
   - If a spare exists: `UPDATE PrinterState SET IsSpare=0` (promote spare) and loop (GOTO style).
   - If no spare: call `ShutLineDown` (or `SlowLineDown` if the "2 Printer Rule" setting=1 and TotalPrinters=2).
4. If surplus (`OnlineCount > MinOnline AND (OnlineCount - SpareCount) > MinOnline`): mark one spare.

The `PandADetails` EAV (PandaID + KeyName + KeyValue) also stores `VerifyScannerDeviceID` (used in
carton-recovery, GAP-4) and `PandaZone`.

**Why it matters for the port:**  
The C# `PrinterState` class does have `PlcOnline` and `EngineOnline` bool fields, but they are **static bools
set at startup** — nothing updates them from incoming PLC messages. The `IsSpare` field is also static config.
Without dynamic promotion/demotion:
- A printer that goes offline mid-run is never detected → selection keeps trying it.
- A spare is never activated → the line shuts down instead of using the spare.
- The "2 Printer Rule" slow-down is never triggered.

**Missing in C#:**
- Msg-283 handler that updates `PrinterState.PlcOnline` from PLC status strings (depends on sim expansion)
- `PA_LaneEval` equivalent logic in `PrinterSelectionService` or a new `LaneEvalService`
- `MinOnlineByType` config (per printer type minimum count) on `LineConfig`
- Spare promotion/demotion algorithm (guarded by the same lock as printer selection)

**Priority:** High  
**Dependencies:** Msg 283/284 framing (backlog sim expansion), `ILineControl` port for stop/slow signals
(backlog BluePaw item — the algorithm itself can be built independently of the signal implementation).

---

#### GAP-3 — Exception Label Building
**Family:** F10  
**Source objects:** `sdisp_TOOL_PA_BuildExceptionLabel` · `LabelTemplates` table (`6.0_PopulateTables/LabelTemplates.sql`) · exception-insert block in `sdisp_PA_LookupCarton` (lines ~220–290)

**What it does:**  
When `PrintExceptionLabels=1` (default 0) and the carton status is an exception type, `PA_LookupCarton`
resolves an exception type key, queries `LabelTemplates.ZPLData` by `LabelType`, substitutes `<CartonID>`
and `<LPN>` tokens, then **inserts a new `PandaData` row** with `LabelType1='Exception'` and the ZPL as
`LabelData1`. This new `PandaDataID` replaces the original in `PandaCartonList`. The normal `PickPrinter`
→ `Print` path then fires for this exception record.

Template types in seed (`LabelTemplates.sql`, 8 entries): `Not Received`, `ScanError_NoRead`,
`ScanError_NoData`, `DataError_NoInfo`, `DataError_Duplicate`, `DataError_LabelConflict`,
`ScanError_Conflict`, `DataMismatch`. All are ZPL fragments.

Status-to-template mapping (in `BuildExceptionLabel`):
- 'No Read' → `ScanError_NoRead`
- 'No Data' → `ScanError_NoData`
- 'No Information' → `DataError_NoInfo`
- 'Duplicate' → `DataError_Duplicate`
- 'Label Conflict' → `DataError_LabelConflict` / `ScanError_Conflict`
- 'PrintHold' → `Not Received`
- 'DataMismatch' → `DataMismatch`

**Why it matters:**  
Exception cartons are common. Without exception labels, a carton with no data produces no label at all —
it flows to the apply station unlabeled, gets sorted to reject, and the operator has no visual indication
of *why*. Currently `InductResult.NoActiveOrder` returns the status but prints nothing and creates no record.
The `PrintExceptionLabels` setting defaults to 0 — but many production sites enable it.

**Missing in C#:**
- `LabelTemplates` config (keyed by exception type — can be JSON-seeded)
- Token substitution (`<CartonID>`, `<LPN>`)
- `IExceptionLabelBuilder` service
- Pre-lookup induct filter: when lookup returns exception status AND PrintExceptionLabels ON, create an
  exception `TransportOrder` and route through normal print path (must not mutate the original `TransportOrder`)
- Scanner quality detection (GAP-8) must land in the same slice (it feeds the exception type)

**Priority:** High  
**Dependencies:** GAP-8 (scanner quality detection provides the exception type), `LabelTemplates` config
model, exception order identity strategy.

---

#### GAP-4 — PLC Event Handling / Carton Recovery
**Family:** F15  
**Source objects:** `sdisp_BP2PA_Event` (all event code branches)

**What it does:**  
Receives PLC events. For all codes except 217/218 (explicitly filtered out):
- Resolves `PandaID` from PLCDBName.
- Inserts an event log entry.
- **Critical recovery action:** When `@CartonListID > 0` AND `@DeviceID < VerifyScannerDeviceID`
  (event occurred before the verify scanner):
  - `UPDATE PandaData SET Printed=0, ActiveRecord=1 WHERE CartonListID = @CartonListID`
  - This re-arms the carton for re-induction.

Event codes handled: 2012 (Carton Lost), 2015 (Late Assignment), 2016 (Unexpected Carton),
2017 (Late Tracking Error), 2019 (Early Tracking Error).

`VerifyScannerDeviceID` is read from `PandADetails` EAV; without it, all events with a device ID
below the verify scanner would reset — the guard prevents over-resetting events that happen after verify.

**Why it matters:**  
Without this, a carton that is physically lost (label mis-fired, jam, conveyor fault) before reaching the
verify scanner is stuck in `Printed` / `HeldForIntervention` state. When the operator manually retrieves
and re-scans it, `TransportOrder.CanPrint` returns false (PrintCount=1, no authorization). The operator
must manually authorize every recovered carton — which is operationally disruptive and wrong; these events
are automatic machine faults, not labeling errors.

**Missing in C#:**
- PLC event message reception (msg 285 or a dedicated event message — depends on sim expansion backlog)
- `VerifyScannerDeviceId` config on `LineConfig`
- `TransportOrder.ResetForTrackingEvent()` method (sets Status→Advised, preserves PrintCount history
  but re-enables CanPrint — distinct from `AuthorizeReprint` which is operator-driven)

**Priority:** High  
**Dependencies:** Event message framing (sim expansion backlog item covers 285); `VerifyScannerDeviceId`
in `LineConfig`.

---

#### GAP-5 — Structured Event Logging (IEventLog port)
**Family:** F16  
**Source objects:** `sdisp_Log_Event` · `sdisp_eLog_LogIt` · `sdisp_eLog_add` · `EventLog` table · `uEventLog` table · `EventDescriptions` table · `sdisp_eLog_add`

**What it does:**  
Two-tier logging called by virtually every SP on every significant state transition:

**Tier 1 — Flat (`EventLog`):** per-event row with RecID, CreationTime, EventDescription, SourceProc,
LogLevel (30=Critical, 40=Error, 50=Warning, 80=Information, 100=Verbose), PandaID, PrinterID, LPN,
CartonListID, PandaDataID. `sdisp_GUI_GetPandaEvents` queries this directly.

**Tier 2 — Structured (`uEventLog`):** normalised record keyed by `EventDescriptionID` (FK to
`EventDescriptions` which deduplicates event text) with 6 `bigint` slots (Uint01–06), 10 `int` slots
(Int01–10), 20 `varchar(500)` slots (Var01–20). Enables analytics and cross-event joins without string
parsing. `sdisp_eLog_add` upserts into `EventDescriptions` (pattern-match on event text → EventID).

Every meaningful state change in the source has a `EXEC sdisp_eLog_add ...` call: induct scan,
printer selection, print success/failure, verify pass/fail, threshold breach, lane eval change, shutdown.

**Why it matters:**  
Without structured event logging, the system is operationally opaque. The operator GUI (`GUI_GetPandaEvents`)
depends on `EventLog`. Post-incident investigation has no trace. Currently the C# services have zero logging
abstraction — not even `ILogger<T>` is injected in `InductService`, `VerificationService`, or
`PrinterSelectionService`.

**Missing in C#:**
- `IEventLog` port (or `ILogger<T>` with structured properties: PandaId, CartonListId, PandaDataId,
  PrinterId, LogLevel enum)
- Wire into all existing services (InductService, CartonAdviceService, VerificationService,
  PrinterSelectionService, VerifyThresholdTracker)
- Persistent `IEventLogStore` for query by GUI (can be in-memory for Phase 1, persistent for GUI backlog)

**Priority:** High  
**Dependencies:** None — can start immediately. GUI queries come later (part of Blazor backlog).

---

#### GAP-6 — XRef Multi-barcode Matching (Induct + Verify)
**Family:** F18  
**Source objects:** `PandaDataXRef` table · `sdivw_PandaDataXRef` view · xref CTE join in `sdisp_PA_LookupCarton` · xref UNION in `sdisp_TOOL_PA_VerifyLabel`

**What it does:**  
`PandaDataXRef` links a `PandaDataID` to multiple barcodes with a typed `BarcodeDescription` tag:
- BL (Blind Label) — the primary scan ID
- UPC, GTIN, EAN — alternate retail barcodes on the carton
- ItemID — internal item identifier
- oLPN — outer LPN added post-advice by ItmSort/RF system (see GAP-10)

At **induct**, `PA_LookupCarton` uses a CTE joining `PandaDataXRef WHERE Barcode = @BlindLabel` —
any barcode type can be used as the induct scan, not only the primary blind label.

At **verify**, `TOOL_PA_VerifyLabel` builds `@VerifyLabels` from:
```sql
SELECT Barcode FROM PandaDataXRef WHERE PandaDataID = @PandaDataID AND LabelName = @ExpectedLabelName
UNION
SELECT LabelBarcode1 FROM PandaData WHERE PandaDataID = @PandaDataID
-- ... through LabelBarcode6
```
A scanned label matches if it equals ANY entry for the same label position — so a UPC barcode on a
shipping label counts as a verify pass if it's in XRef for that label position.

**Why it matters:**  
Real cartons carry multiple barcodes. Without XRef lookup:
- A carton scanned at induct with a UPC → `NoActiveOrder` (incorrectly rejected).
- A carton verified with a valid but non-primary barcode → verify failure (incorrectly rejected).
The C# `ITransportOrderStore.FindActiveByTuIdAsync(blindLabel)` matches only on `TransportOrder.TuId`.
The C# `VerificationService` compares scanned values directly against `Label.Lpn` — no XRef fan-out.

**Missing in C#:**
- `IReadOnlyList<(string Barcode, string BarcodeDescription)>` on `TransportOrder` (or `PandaLabelSet`)
- `FindActiveByTuIdAsync` updated to search all XRef barcodes (or a separate lookup method)
- `VerificationService` verify loop updated to check XRef barcodes per label position

**Priority:** High  
**Dependencies:** Must be in place before oLPN association (GAP-10) can be built.

---

### 3.2 MEDIUM PRIORITY

---

#### GAP-7 — Carton Slot Number / PLC Array Index
**Family:** F21  
**Source objects:** `sdisp_TOOL_GetSlotNumber` · SQL SEQUENCE `dbo.CartonAssignSeq` (INT, 1→300, CYCLE, CACHE 300)

**What it does:**  
`PA_PickPrinter` calls `GetSlotNumber(@SlotNumber OUTPUT)` as its first step. The SEQUENCE cycles 1–300
(matching the 300-slot `vPA.Assign[i]` PLC tag array). The assigned slot number is returned to
`BP2PA_Scan_Induct` as an output parameter and passed immediately to
`PA2BP_SendPrinterFirePoints(@Index=@SlotNumber)`, where it indexes the write target in the PLC array.

**Why it matters:**  
The PLC maintains a fixed-size array of carton tracking records keyed by this index. Without a slot, the
eController adapter has no array index to write to. The C# `PrintJob` and `InductResult` have no slot field.
When `FirePointResolver` resolves fire-point values (arch-log 010), it has no index — the resolved data
cannot be written to the PLC.

**Missing in C#:**
- `ISlotCounter` service (cyclic counter 1–300, thread-safe)
- `SlotIndex int` on `PrintJob` (populated during printer selection)
- `InductResult.LabelAssignment` updated to carry slot index
- `FirePointResolver` output updated to include slot index

**Priority:** Medium  
**Dependencies:** Needed before eController adapter integration; can be modeled now as a simple counter.

---

#### GAP-8 — Gap Error + Scanner Read Quality Detection
**Family:** F20  
**Source objects:** Gap check and `?, !, #` character detection block in `sdisp_PA_LookupCarton`

**What it does:**  
Before any database lookup, `PA_LookupCarton` applies induction-time guards:

**Dimensional:**
- `@Gap < @MinGap` (setting default=20 encoder units) → `Gap Error` status (cartons too close; second
  carton rejected, not the first)

**Scanner character quality:**
- `@BlindLabel LIKE '%?%'` or `= '-'` or `LIKE '<%'` → `No Read` (scanner didn't read the barcode)
- `@BlindLabel LIKE '%!%'` or `= '0'` → `No Data` (conveyor interface returned no data)
- `@BlindLabel LIKE '%#%'` → `Label Conflict` (multiple barcodes read simultaneously)
- `@BlindLabel LIKE '%*%'` → `Bypass` (conveyor signals no processing needed)

The `@Gap` and `@Length` values come from the 281 message's scanner measurement fields. Height
(`@Height`) is matched to `HeightCheckLabelField` setting (default=2) to know which label field carries
height for the DynamicApplyPoint calculation.

**Why it matters:**  
These checks are the primary mechanism for handling real-world scanner edge cases. Without them:
- A '?' blind label goes to `FindActiveByTuIdAsync` → returns `NoActiveOrder` (wrong: should be `NoRead`)
- A '#' label → `NoActiveOrder` instead of `LabelConflict`
- A gap-error carton → proceeds to PickPrinter and prints, colliding with the previous carton

The 281 message parsed by `PaMessages` does carry `Gap`, `Length`, `Height` fields — they are parsed but
never evaluated by `InductService`.

**Missing in C#:**
- Character-prefix detection in `InductService.InductAsync` before calling `FindActiveByTuIdAsync`
- Gap measurement evaluation (compare parsed `Gap` to `MinGap` config on `LineConfig`)
- New `InductStatus` cases: `GapError`, `NoRead`, `LabelConflict`, `Bypass`
- `MinGap` setting on `LineConfig`

**Priority:** Medium  
**Dependencies:** Should land in the same slice as exception label building (GAP-3) since the scanner status
drives the exception type selection.

---

#### GAP-9 — ZPL Vetting (VetLabel)
**Family:** F11  
**Source objects:** `sdisp_TOOL_PA_VetLabel` · `FilterLabels` setting (default=1=ON)

**What it does:**  
`PA_Print` calls `VetLabel(@ZPLData)` when `FilterLabels=1` (the default). The proc strips 25+ ZPL commands
that are valid for direct-print but wrong for a Print-and-Apply applicator:
- Print rate commands: `^PRA` through `^PRE`, `^PR2` through `^PR9` (applicator has fixed belt speed)
- Map clear: `^XA^MCY^XZ` sequence (would clear the print buffer mid-job)
- Media darkness: `^MD*` (applicator calibrates its own darkness)
- Print orientation: `^PON`, `^POI^FS` (applicator orientation is mechanical, not ZPL)
- Media/sensor: `^MTT`, `^MTD`, `^JSN`, `~JSN`, `^MNY`
- Print mode: `^MMT`, `^MM*`
- Tear-off position: `^TA000`, `~TA000`
- Print quantity: `^PQ1,0,0,N` (applicator manages print count separately)
- Mirroring, encoding, reverse: `^PMN`, `^CI0`, `^LRN`, `^SZ2`

Also rewrites every `^XA` to `^XA^LH13,0` to shift the label home 13 dots — the applicator head-to-media
offset specific to this model of tamp head.

**Why it matters:**  
Host WMS systems generate ZPL for desktop printers. A `^PR9` (high speed) command sent to an applicator
printer set for belt-speed synchronization causes mis-registration. The `^LH13,0` offset is physically
required for the tamp head geometry. `FilterLabels` defaults to 1 (ON) — so this runs on every production
label. The C# `IPrinterGateway` currently sends raw ZPL with no filtering.

**Missing in C#:**
- `IZplFilter` interface with a `Filter(string zpl) → string` method
- `ZplVetFilter` implementation (strip list + LH rewrite)
- Injection into `InductService` print pipeline, controllable by `FilterLabels` config
- Bypass in `PandA.Sim` (sim should send known-clean ZPL without filtering)

**Priority:** Medium  
**Dependencies:** None; self-contained string processing.

---

#### GAP-10 — PrintEngine Status Ingestion (Zebra TCP)
**Family:** F13  
**Source objects:** `sdisp_PA_Status_PrintEngine` · `PrintEngineStatus` table (27 columns) · `sdiudf_PA_GetPrinterRecIDFromConnections` UDF

**What it does:**  
Receives comma-delimited status strings from Zebra printers (triggered by `~HS` status-request suffix —
see GAP-12). Parses by comma count:
- **11 commas (msg-01, 12 fields):** CommSettings, FlagPaperOut, FlagPause, LabelLength, NumFormats,
  FlagBufferFull, FlagCommDiag, FlagPartFormat, FlagBadRAM, TempRangeLo, TempRangeHi. Derives
  `FlagLidOpen = HeadUp AND PaperOut AND RibbonOut AND Pause`. Inserts a new row, deactivates previous.
- **10 commas (msg-02, 11 fields):** FunctSettings, FlagHeadUp, FlagRibbonOut, FlagThermTransMode,
  PrintMode, PrintWidthMode, FlagLabelWait, RemainingLabels, FlagFormatOnFly, NumGraphicsInMem.
  Updates the active row.
- **1 comma (msg-03, 2 fields):** Pswd, StaticRAM. Updates the active row.

Looks up printer by IP+Port using `sdiudf_PA_GetPrinterRecIDFromConnections`.

**Why it matters:**  
The operator GUI (`sdisp_GUI_GetPrintEngineStatus`) queries `PrintEngineStatus` to show paper-out,
ribbon-out, head-up, buffer-full, and remaining-label-count per printer. Without this, printer health is
invisible to operators. Also: `FlagLidOpen` (derived from the combination of flags) is the most important
single operational alert — it indicates a jam or open printer cover.

**Missing in C#:**
- `PrintEngineStatus` model (27 fields)
- TCP receive channel from Zebra printers (separate from send; part of printer gateway)
- Three-message parser (comma count dispatch)
- IP+Port → `PrinterConfig` lookup (can use `LineConfig.Printers` collection)
- `IPrintEngineStatusStore` for persistence + GUI query

**Priority:** Medium  
**Dependencies:** Depends on `~HS` suffix append (GAP-12) to trigger printer responses; TCP receive path
(separate concern from `IPrinterGateway` which is send-only).

---

#### GAP-11 — MandA Manual Apply Stations
**Family:** F14  
**Source objects:** `sdisp_MA_Scan_Induct` · `sdisp_MA_Scan_Verify` · `sdisp_GUI_MandA_Scan` · `sdisp_GUI_MandA_Verify` · `sdisp_GUI_MandA_Screen_Update` · `sdisp_GUI_GetMandaList`

**What it does:**  
A MandA (Manual Apply) station is an operator-staffed workstation — no conveyor, no automatic applicator.

**Induct path:** `MA_Scan_Induct` calls `PA_LookupCarton` (same logic; `SorterNumber=1`) → `PA_PickPrinter`
which detects `PandaID LIKE 'MANDA%'` and fast-exits by selecting the first printer for that line (no
load-balancing) → immediately calls `PA_Print`. No induct-to-verify gap management needed (no conveyor).

**Verify path:** `MA_Scan_Verify` calls `PA_VerifyCarton` (same logic) then
`sdisp_WMS_PandAVerify_Insert` (DCMS/WMS acknowledgment — adapter concern).

`sdisp_GUI_MandA_Scan` and `sdisp_GUI_MandA_Verify` are operator-facing: display a list of pending cartons,
accept scan, call the underlying proc, display result. `GUI_GetMandaList` returns all `PrintReady` cartons
for a given PandaID for the pick list.

**Why it matters:**  
Some SKUs cannot be labeled by an automated applicator (oversize, fragile, orientation-sensitive). MandA
stations provide a manual path sharing the same data model. Without MandA support, these cartons have no
print path in the C# port. The PandaID prefix detection is a key mode discriminator — without it, a
'MANDA%' PandaID falls into the normal load-balanced picker, which is wrong behavior.

**Missing in C#:**
- PandaID prefix detection in `PrinterSelectionService` (fast-exit for MandA lines)
- `IMandAService` (wraps lookup → print in one operator scan) or mode parameter on `InductService`
- Operator UI (part of Blazor GUI backlog; the backend service is the gap here)

**Priority:** Medium  
**Dependencies:** Operator GUI (PLANNED backlog) for the screen; the backend service can be built without UI.

---

#### GAP-12 — Wave Auto-Complete (Verify Hot-Path Coupling)
**Family:** F23  
**Source objects:** `sdisp_TOOL_CUSTOM_CheckWaveCmp` called inside `sdisp_PA_VerifyCarton` · `sdisp_TOOL_CUSTOM_PandAWaveComplete`

**What it does:**  
Inside `PA_VerifyCarton`, immediately after setting `@VerifyPass=1` (verify success), the code executes:
```sql
EXEC sdisp_TOOL_CUSTOM_CheckWaveCmp @WaveID = @WaveID, @UserID = 'SDI'
```

`CheckWaveCmp` queries:
1. Total cartons in the wave for this PandaID.
2. Verified cartons: `COUNT(*) WHERE ActiveRecord=0 AND Printed>0 AND WaveStatus='ACTIVE'`.
3. If total == verified: calls `PandAWaveComplete(@WaveID)` → sets `WaveHistory.Status='COMPLETED'`,
   fires `PA2DCMS_WaveStatus` → `HI_Ins_OrderWaveStatus` (DCMS synonym, adapter concern).

**Why it matters:**  
This is **not just a "Wave lifecycle" concern** — it is embedded in the verify hot path. The PLANNED backlog
item for Wave lifecycle focuses on wave create/delete/range management. The auto-complete is a separate concern:
every successful verify must trigger a wave-completion check. If the Wave lifecycle is built without wiring
a callback/event into `VerificationService.VerifyAsync`, wave auto-completion will be silently broken at integration.

**Missing in C#:**
- Domain event `VerifyPassedEvent` (or direct call) emitted by `VerificationService.VerifyAsync` on pass
- `IWaveCompletionChecker` service that handles the count comparison + wave-complete promotion
- Wire-up between verify service and wave checker (DI, event bus, or direct call with null-check guard)

**Priority:** Medium  
**Dependencies:** Wave lifecycle (PLANNED backlog) must be built first; this is a coupling addition to that work.

---

#### GAP-13 — Reject History Audit Trail
**Family:** F22  
**Source objects:** `sdisp_CUSTOM_RejectHistory_Insert` · `RejectHistory` table

**What it does:**  
On every verify failure, inserts one row into `RejectHistory` with:
- `VerifyCode` (numeric): 0, 3, 4, 5, 6, 7, 8, 11, 12, 14, 15, 20, 21, 22, 23, 27, 28, 29, 33, 34
- `VerifyReason` (human-readable string mapped from code)
- `PandaDataID`, `CartonListID` (correlation keys)

`sdisp_GUI_GetPandARejectCartons` queries this table for the reject-carton operator screen.
`sdisp_TOOL_Table2File` exports it for wave reporting.

**Why it matters:**  
Without reject history, operators cannot produce reject reports or investigate systemic mis-label causes.
The C# `VerifyOutcome` enum captures the reason in-process but never persists it. The Blazor GUI reject
screen (PLANNED backlog) will query this.

**Missing in C#:**
- `RejectHistory` entity (VerifyCode, VerifyReason, PandaDataId, CartonListId, FailedAt)
- Extension to `ITransportOrderStore` (or a separate `IRejectHistoryStore`) to record rejection events
- `VerificationService` must call this on `HeldForIntervention` transition

**Priority:** Medium  
**Dependencies:** `ITransportOrderStore` extension; part of the operator GUI data model.

---

#### GAP-14 — ProfileName Validation Gate at Induct
**Family:** F19  
**Source objects:** `@IsValid` block in `sdisp_PA_LookupCarton` (lines ~290–370) · `sdivw_LabelProfiles` view

**What it does:**  
After finding a `PandaData` record, `PA_LookupCarton` evaluates the `ProfileName` field:
- `-1` (no data found at all / ProfileName was never populated) → pass through (allow no-data carton)
- `0` (ProfileName present but `NOT EXISTS` in `sdivw_LabelProfiles`) → `CartonStatus='No Profile'`, PandaDataID=0
- `1` (ProfileName exists in fire-point profiles) → proceed normally

If `IsValid=0` and `PandaDataID > 0` (data existed but profile is missing): sets `CartonStatus='No Profile'`,
clears `PandaDataID`, and does not print. This is a distinct status (`Settings_CartonStatuses` RecID 18).

**Why it matters:**  
Without this check, a carton with valid advice but a ProfileName referencing a missing fire-point profile
proceeds to printer selection and sends fire-point data with null apply points to the PLC. The source
guards against this with a pre-check; the C# `InductService` has no equivalent profile-existence check.

**Missing in C#:**
- Profile existence check in `InductService.InductAsync` before calling `PrinterSelectionService.Select`
- `InductStatus.NoProfile` case (or reuse `NoData`)
- `LineConfig` or `ILineProvider` must expose a method to check if a named profile exists

**Priority:** Medium  
**Dependencies:** `FirePointProfile` model (IMPLEMENTED); `LineConfig.ActiveProfile` already exists but a
name-based lookup is not.

---

#### GAP-15 — oLPN Cross-Reference Association
**Family:** F24  
**Source objects:** `sdisp_TOOL_CUSTOM_LPNxRef` · `sdisp_TOOL_CUSTOM_LPNxRef_Disassociate`

**What it does:**  
Called by an external RF/ItmSort system to associate an outer LPN with the `PandaData` record for a
specific `@OrderID` within `@WaveID`. The SP:
1. Looks up `PandaDataID` via `PandaDataXRef WHERE Barcode = @OrderID` where wave is ACTIVE.
2. Guards: already-associated oLPN, wave not active, wave completed.
3. Inserts `PandaDataXRef(PandaDataID, @oLPN, BarcodeDescription='oLPN', Priority=1)`.

`Disassociate` deletes by `Barcode = @oLPN AND BarcodeDescription = 'oLPN'`.

**Why it matters:**  
Sites using ItmSort/RF for cartonization assign an outer LPN at pack time — after label advice was sent.
The outer LPN must be in XRef so it can be scanned at verify. Without this association endpoint, the
physical outer carton LPN cannot be tied to the label record, causing verify failures for all such cartons.

**Missing in C#:**
- `ITransportOrderStore.AddXRefAsync(tuId, barcode, description)` and `RemoveXRefAsync`
- A service endpoint (HTTP or internal) accepting the external RF call
- Wave-active guard on the association (wave lifecycle dependency)

**Priority:** Medium  
**Dependencies:** XRef model must be in place (GAP-6 first), then wave lifecycle (PLANNED backlog).

---

#### GAP-16 — Purge / Data Lifecycle
**Family:** F17  
**Source objects:** `sdisp_PA_Purge`

**What it does:**  
Scheduled nightly cleanup (SQL Agent job). Deletes records older than configurable retention windows
(all in `Settings` table):
- `PandaData` where `BlindLabel IN ('-','?')` older than `PurgeSetting_ExceptionData` (default 7 days)
- `PandaData` where `ActiveRecord=0` and `VerifyTime` older than `PurgeSetting_InactiveData` (default 21d)
- `PandaData` orphaned from deleted waves
- Completed `Wave`/`WaveHistory` older than `PurgeSetting_UsedData` (default 21d)
- `PandaCartonList` older than `PurgeSetting_UsedData`
- `PrintEngineStatus` older than `PurgeSetting_UnUsedData` (default 14d)
- `EventLog`, `uEventLog`, `EventDescriptions` older than `PurgeSetting_UnUsedData`
- Orphaned `PandaDataXRef` rows (no parent PandaData)

**Why it matters:**  
Without retention policies, stores grow unboundedly. At 50,000 cartons/day with 21-day retention, the
order store has ~1M rows. The `InMemoryTransportOrderStore` in the sim has no eviction. A production-grade
persistent store (needed before or alongside the Blazor GUI) will need scheduled cleanup.

**Missing in C#:**
- `IHostedService` background purge scheduler
- Retention settings on `LineConfig` or global config
- `ITransportOrderStore` retention API (e.g. `PurgeOlderThan(DataCategory, TimeSpan)`)

**Priority:** Medium  
**Dependencies:** Persistent store (needed before purge is meaningful); can be built as a no-op for in-memory.

---

### 3.3 LOW PRIORITY

---

#### GAP-17 — Printer Status Suffix (~HS)
**Family:** F12  
**Source objects:** `sdisp_TOOL_PA_AppendStatusSuffix` · `PrinterStatusSuffix` setting (default=0=OFF)

**What it does:**  
`PA_Print` calls `AppendStatusSuffix` when `PrinterStatusSuffix=1`. Appends `~HS` to the ZPL string
before sending to the printer. `~HS` is the Zebra Host Status command — the printer responds with its
current status fields on the TCP socket, which `sdisp_PA_Status_PrintEngine` then parses (GAP-10).

**Why it matters:**  
Without `~HS` appended, the printer never sends status responses. The PrintEngine status ingestion (GAP-10)
will never receive data regardless of how well it is implemented. However, `PrinterStatusSuffix` defaults
to OFF — this is an elective feature. Enable only at sites that want Zebra status polling.

**Missing in C#:**
- `PrinterStatusSuffix` setting on `LineConfig` (bool)
- In ZPL filter pipeline: when setting is ON, append `~HS` before handing to gateway
- (Works in conjunction with GAP-10 and GAP-9)

**Priority:** Low  
**Dependencies:** GAP-10 (PrintEngine ingestion must exist to use the response); GAP-9 (ZPL pipeline).

---

#### GAP-18 — SiteBuilder Commissioning CRUD
**Family:** F25  
**Source objects:** `sdisp_TOOL_SiteBuilder_*` (26 procedures): Create/Get/Remove/Update for PandAs, Printers, FirePoints, Labels, Lanes, plus `Remove_AllData`

**What it does:**  
A GUI-driven commissioning wizard. Operators (or admins) create and edit:
- PandAs (line definitions: PandaID, ZoneName, SorterNumber, PandaZone, DB connection info)
- Printers (PrinterID, IP, Port, PrinterType, DefaultApplyDistance, FirePointDeviceID)
- FirePoint headers, labels (LabelType, DefaultLabelName), lanes (LaneDef rows)
- DB connections for cross-system linking

`Remove_AllData` performs a destructive reset of all commissioning tables.

**Why it matters:**  
The C# port uses JSON config files. For sites with many lines and printers, manual JSON editing is
error-prone and gates onboarding speed. An admin API or CLI for commissioning is needed eventually.
The CRUD procedures are the authoritative reference for all editable fields per entity — they document
the full schema of the commissioning data model.

**Missing in C#:**
- Admin API endpoints (or CLI commands) for entity CRUD
- Field-level reference: use the SiteBuilder procs as the spec for which fields each entity exposes

**Priority:** Low  
**Dependencies:** Full entity models must be stable first; naturally belongs with the admin/GUI phase.

---

## 4. Coupling / Risks

### CR-1: Lane number is required in both induct and verify outputs — and feeds fire-point transport

`GetFinalLaneFromStatus` is called at induct (default FAIL lane for `PandaCartonList.LaneNumber`) **and**
at verify (actual divert lane). The C# `InductResult` and `VerifyOutcome` carry no `DivertLane` field.
The fire-point bundle (`vPA.Assign[i].Dest`) is already partially implemented (arch-log 010) but will
be incomplete without the lane. **Risk:** If GAP-1 is added as a standalone lane-routing service without
simultaneously updating `InductResult.LabelAssignment` and `VerifyOutcome`, the gap reopens at the adapter
boundary and is hard to find.

---

### CR-2: `LaneEval` is triggered on every msg-283 and mutates `PrinterState.IsSpare`

Every printer status message (msg 283) → `LaneEval` → may flip `PrinterState.IsSpare`. The
`PrinterSelectionService` reads `IsSpare` during selection. These run concurrently:
**Risk:** Without a shared lock, a spare promotion that happens mid-selection will be invisible to the
selection in progress — it may select a printer still marked spare, or skip one that was just promoted.
**Required:** The spare-management algorithm (GAP-2) must acquire the same synchronization primitive as
`PrinterSelectionService.Select`. Design: a per-line `SemaphoreSlim` or channel (actor-per-line).

---

### CR-3: Wave auto-complete is embedded in the verify hot path — the backlog item doesn't say so

`CheckWaveCmp` is called inside `PA_VerifyCarton` on every verify pass. The PLANNED backlog item
"Wave / WaveRange lifecycle" focuses on create/delete/range management. **Risk:** If wave lifecycle is
implemented without explicitly adding a wave-completion callback to `VerificationService.VerifyAsync`,
wave auto-completion will be permanently broken in the C# port. A design decision is needed: direct service
call (tight coupling), domain event, or an `IWaveCompletionObserver` injected into `VerificationService`.

---

### CR-4: Exception labels create a **new** `PandaData` record — they must not mutate the original `TransportOrder`

In `PA_LookupCarton`, an exception label causes a brand-new `PandaData` insertion with `LabelType1='Exception'`.
The original advice record (if any) is untouched. **Risk:** If the C# exception label path (GAP-3) naively
overwrites `TransportOrder.Labels`, the original advice data is lost. A recovered carton (after the exception
is resolved) would have no label set to print. The correct model is a separate ephemeral
`ExceptionTransportOrder` that does not disturb the parent `TransportOrder`.

---

### CR-5: PLC tracking events reset print state — bypassing `TransportOrder.CanPrint`

`BP2PA_Event` sets `Printed=0, ActiveRecord=1` directly on `PandaData`, bypassing all reprint-gating.
In C#, `TransportOrder.CanPrint` blocks reprinting after `CompletePrintRun`. **Risk:** A tracking-event
recovery (GAP-4) must call `TransportOrder.ResetForTrackingEvent()` — a new method that re-enables `CanPrint`
without requiring operator `AuthorizeReprint`. This is distinct from the operator path: machine faults are
automatic recoveries, not authorization requests. Without this distinction, recovered cartons will present
as `NoReprint` at re-induct. The `TransportOrderStatus` enum may need a new state (`LostInTransit`?) to
distinguish machine-recovery from operator-intervention.

---

### CR-6: `PandaCartonList.CartonListID` is the PLC correlation key between induct and verify

At induct, `PA_LookupCarton` creates a `PandaCartonList` row and returns `@CartonListID`. The eController
writes this into the PLC's tracking memory. The PLC sends it back in the 286 verify message. The SQL verify
path finds the record via `WHERE CartonListID = @CartonListID`. The C# `VerifyStationService` finds the
`TransportOrder` by scanned label, not by a numeric ID. **Risk:** In real integration, the 286 message
carries `CartonListID` as the correlation key — if the C# port only matches on barcode, it will fail to
correlate under any mis-scan or multi-barcode scenario. `TransportOrder` needs a `CartonListId` field
(or equivalent slot/index surrogate — see GAP-7) that the eController can echo.

---

### CR-7: Verify threshold breach should pause the printer — not just raise an event

`VerifyThresholdTracker` is IMPLEMENTED in C# and raises `VerifyThresholdExceeded`. In the source,
`PA_VerifyThreshold_Update` on threshold breach sends a `~PP` (printer pause) ZPL command via
`PA2TCP_SendTCPData`. **Risk:** The C# threshold tracker raises the event but does not emit a printer-pause
command. The `VerifyThresholdExceeded` event needs a handler that calls `IPrinterGateway.SendAsync(~PP)`
(or equivalent) to the appropriate printer. Without this, a consecutive-fail threshold breach is logged
but the printer continues printing mis-labeled cartons.

---

### CR-8: `VerifyContentLabel` setting controls verify scope — C# always verifies all labels

Setting `VerifyContentLabel` (default=1 from seed) controls whether content/parcel label positions are
verified in addition to the shipping label. In `TOOL_PA_VerifyLabel`, when `VerifyContentLabel=0`, only
the shipping label position is compared; other positions are skipped. **Risk:** The C# `VerificationService`
always validates all positions in `PandaLabelSet`. For sites where `VerifyContentLabel=0`, every carton
without a content-label scan will report `Fail` instead of `Pass`. This setting must be added to
`LineConfig` and evaluated in `VerificationService`.

---

## 5. Recommended Next Slices

Ordered by implementation dependency and correctness impact:

---

### Slice A — Lane Routing Model + Slot Number (GAP-1 + GAP-7)

**Rationale:** These two gaps share a single scope: the induct-side PLC tag bundle. Together they complete
the fire-point integration that is already half-built (arch-log 010).

**Work:**
1. Add `LaneConfig` record (`LaneId: string`, `LaneNumber: int`, `LastDiverted: DateTimeOffset?`) and
   `Lanes: IReadOnlyList<LaneConfig>` to `LineConfig`.
2. Implement `LaneRoutingService` (or `LineConfig.GetDivertLane(status, finalDest)`) with the three-rule
   match + round-robin least-recently-diverted.
3. Add `DivertLane int?` to `InductResult.LabelAssignment` and to `VerifyOutcome`.
4. Add `ISlotCounter` (thread-safe cyclic 1–300) and `SlotIndex int` to `PrintJob` / `LabelAssignment`.
5. Add `VerifyScannerDeviceId` to `LineConfig` (needed for GAP-4 in the next slice).

**Test coverage:** Unit tests for three-rule match, round-robin tie-break, REJECT fallback, slot wrap-around.

---

### Slice B — XRef Multi-barcode + Scanner Quality Detection (GAP-6 + GAP-8)

**Rationale:** These fix silent correctness breaks on every production site (wrong-barcode induct → NoActiveOrder;
wrong-barcode verify → false Fail). Scanner quality detection feeds exception label building (Slice C).

**Work:**
1. Add `IReadOnlyList<(string Barcode, string Description)> XRefBarcodes` to `TransportOrder` (carried alongside `Labels`).
2. Update `InMemoryTransportOrderStore.FindActiveByTuIdAsync` to search all `XRefBarcodes`.
3. Update `VerificationService` verify loop to check XRef barcodes per label position before reporting Fail.
4. Add character-prefix detection to `InductService.InductAsync` (before `FindActiveByTuIdAsync`): `?, -, <, !, 0, #, *` → appropriate `InductStatus`.
5. Add `Gap` and `MinGap` evaluation (gap measurement from 281 message vs `LineConfig.MinGap`).
6. Add `InductStatus.GapError`, `NoRead`, `LabelConflict`, `Bypass` enum values.

**Test coverage:** XRef lookup by UPC/GTIN/EAN, XRef verify match, ? / ! / # / * character detection,
gap threshold boundary tests.

---

### Slice C — Exception Label Building + Event Logging (GAP-3 + GAP-5)

**Rationale:** Exception labels require scanner quality detection (Slice B provides it). Event logging is
a cross-cutting concern that should be added before more services are built — retrofitting later is expensive.

**Work:**
1. **Exception labels:**
   - Add `LabelTemplate` config record (`ExceptionType: string`, `ZplTemplate: string`, token substitution for `{CartonId}` / `{Lpn}`).
   - Add `ExceptionLabelTemplates: IReadOnlyList<LabelTemplate>` to `LineConfig`.
   - Implement `IExceptionLabelBuilder.Build(exceptionType, cartonId, lpn) → Label`.
   - In `InductService`: when scanner-quality status detected AND `PrintExceptionLabels=true`, build an
     exception `TransportOrder` (separate object, does not mutate the original) and send to `IPrinterGateway`.
2. **Event logging:**
   - Add `IEventLog` port: `LogAsync(LogLevel, string message, PandaEventContext ctx)` where
     `PandaEventContext` carries `LineId`, `PrinterId?`, `TuId?`, `CartonListId?`, `LabelId?`.
   - Wire `IEventLog` into `InductService`, `VerificationService`, `CartonAdviceService`,
     `PrinterSelectionService`, `VerifyThresholdTracker`.
   - Default implementation: `ILogger<T>` with structured properties + in-memory ring buffer (for GUI query).

**Test coverage:** Exception label ZPL generation per exception type, token substitution, event context
propagation through induct and verify.

---

## Appendix A: Settings Reference

| Setting | Seed Default | Used In | C# Gap |
|---------|-------------|---------|--------|
| `FilterLabels` | 1 (ON) | `sdisp_PA_Print` | GAP-9 (ZPL Vetting) |
| `PrintExceptionLabels` | 0 (OFF) | `sdisp_PA_LookupCarton` | GAP-3 |
| `MinGap` | 20 | `sdisp_PA_LookupCarton` | GAP-8 |
| `VerifyContentLabel` | 1 | `sdisp_TOOL_PA_VerifyLabel` | CR-8 (verify scope) |
| `2 Printer Rule` | 0 (OFF) | `sdisp_PA_LaneEval` | GAP-2 |
| `PrinterStatusSuffix` | 0 (OFF) | `sdisp_PA_Print` | GAP-17 |
| `DynamicPrintPoint` | 1 (ON) | `sdisp_PA_PickPrinter` | PLANNED (DynamicApplyPoint) |
| `EncoderResolution` | 0.2 steps/inch | `sdisp_TOOL_CUSTOM_DynamicApplyPoint` | PLANNED |
| `DefaultDimension` | 20 | `sdisp_PA_LookupCarton` | Not modeled |
| `DefaultHeight` | 10 | Height-check logic | Not modeled |
| `OverwriteLabelData` | 0 | `sdisp_PA_LookupCarton` | PLANNED |
| `Reprint Labels` | 0 | `sdisp_PA_LookupCarton` | PLANNED |
| `PurgeSetting_UsedData` | 21 days | `sdisp_PA_Purge` | GAP-16 |
| `PurgeSetting_InactiveData` | 21 days | `sdisp_PA_Purge` | GAP-16 |
| `PurgeSetting_ExceptionData` | 7 days | `sdisp_PA_Purge` | GAP-16 |
| `PurgeSetting_UnUsedData` | 14 days | `sdisp_PA_Purge` | GAP-16 |
| `HeightCheckEnabled` | 1 | `sdisp_PA_LookupCarton` | Partially provisioned |
| `HeightCheckLabelField` | 2 | `sdisp_PA_LookupCarton` | Not mapped |
| `ForcedReplen` | 0 | `sdisp_PA_LookupCarton` | Not modeled |
| `DCMSExceptions` | 0 | `sdisp_PA_LookupCarton` | OUT-OF-SCOPE |

---

## Appendix B: CartonStatus Taxonomy

All statuses from `6.0_PopulateTables/Settings_CartonStatuses.sql`:

| RecID | StatusName | Notes |
|-------|-----------|-------|
| 5 | No Information | No PandaData found for TuId |
| 6 | Duplicate | Already verified carton |
| 7 | No Read | Scanner didn't read barcode (`?`) |
| 8 | No Data | Scanner returned no data (`!`) |
| 9 | Label Conflict | Multiple barcodes read (`#`) |
| 10 | PrintHold | Not properly received from host |
| 11 | Gap Error | Cartons too close (gap < MinGap) |
| 12 | Bypass | No processing required (`*`) |
| 13 | Verify Disabled | Don't verify this carton type |
| 14 | PrintReady | Happy path — data found, proceed to print |
| 15 | PID Error | Code/system exception |
| 16 | Verify - PASS | Happy path verify |
| 17 | Verify - FAIL | Verify failed |
| 18 | No Profile | ProfileName not in fire-point tables |
| 19 | No Reprint | Reprint not allowed |
| 20 | Inactive | Inactive record, reprint denied |
| 21 | Tracking Error | PLC tracking error (pre-verify) |
| 22 | Tracking Error | PLC tracking error (post-verify) |

Statuses 7, 8, 9, 11, 12 relate to GAP-8 (scanner quality detection). Status 18 relates to GAP-14
(ProfileName validation). Statuses 21/22 relate to GAP-4 (PLC event recovery).
