# Source-Coverage Spec: EVENTS, CARTON RECOVERY, STRUCTURED LOGGING, AUDIT TRAILS & SCANNER READ-QUALITY

**Cluster scope:** F15, F16, F22, F-LOG1, F20  
**Date:** 2026-08-12 | **Status:** Gap (nothing in this cluster is built in C# today)  
**Cross-ref:** decision-003, decision-004/AD-1, arch-log 006/007/009, backlog README §F15–F22, F-LOG1

---

## ⚠️ Pre-amble: SpaceScan is NOT in this cluster

`sdisp_TOOL_SpaceScan.sql` is a **DBA disk-space monitoring utility** — it queries `sys.tables`/`sys.partitions` and returns table-size metrics (KB/MB used/total). It has no relationship to read-quality or gap detection. Its name contains "Scan" only incidentally. **This file is excluded from the spec.** The actual gap/quality classification lives entirely in `sdisp_PA_LookupCarton.sql` lines 208–235.

---

## EVENT TAXONOMY (shared enum — all features depend on this)

The source uses **three distinct namespaces** that must each become C# types:

### 1. PLC Tracking Event Codes (`sdisp_BP2PA_Event.sql` lines 73–80)
Integer codes sent from the PLC conveyor layer. Currently handled by the Event SP:

| Code | Name | Description |
|------|------|-------------|
| `2012` | `CartonLost` | PLC lost track of a carton |
| `2015` | `LateCartonAssignment` | Assignment arrived after the carton passed |
| `2016` | `UnexpectedCarton` | Carton appeared with no assignment |
| `2017` | `LateTrackingError` | Encoder/tracking event — carton late |
| `2019` | `EarlyTrackingError` | Encoder/tracking event — carton early |
| `217` | *(excluded)* | Currently ignored by main logic (line 71) |
| `218` | *(excluded)* | Currently ignored by main logic (line 71) |

**Note:** The comparison at line 71 is `@EventID NOT IN ('217','218')` — these two codes are silently swallowed (no log, no recovery). Open question for the domain owner about what 217/218 mean.

### 2. Log Severity Levels (`sdisp_Log_Event.sql` lines 28–33, comment block)

| Value | Name | Usage |
|-------|------|-------|
| `30` | `Critical` | `CATCH` blocks only |
| `40` | `Error` | Needs human interaction |
| `50` | `Warning` | Does not need human interaction |
| `80` | `Information` | Change of state |
| `100` | `Verbose` | Detailed trace |
| `805` | *(anomaly)* | Used once in `sdisp_PA_Scan_Verify` line 149 — almost certainly a source typo for `80`; **flag for owner** |

### 3. VerifyPass / Reject Codes (`sdisp_CUSTOM_RejectHistory_Insert.sql` lines 44–65 and arch-log 006)
These are the **DCMS-facing reason codes** and are a cross-cutting contract. They appear in `RejectHistory.VerifyCode`, in `EventLog`, and will be used by the GUI reject screen. Preserve exact numbers.

| Code | Category | Description |
|------|----------|-------------|
| `0` | Generic | PandA Logic Error / generic fail |
| `1` | ✅ PASS | Verification passed |
| `3` | Bypass | Pass-through Carton (verify disabled + bypass) |
| `4` | Induct | Gap Error |
| `5` | Induct | No data from inbound scanner |
| `6` | Induct | Inbound Scanner Overflow |
| `7` | Induct | Inbound Scanner No Read |
| `8` | Induct | Inbound Scanner Label Conflict |
| `11` | Induct | No information for scanned carton (unknown LPN) |
| `12` | Verify | Verify scan error / no-data |
| `14` | Verify — BlindLabel | Blind Label not detected / missing |
| `15` | Verify — BlindLabel | Blind Label conflict |
| `20` | Verify — Shipping | Shipping label no-read / missing |
| `21` | Verify — Shipping | Incorrect shipping label applied |
| `22` | Verify — Content | Content label no-read / missing |
| `23` | Verify — Content | Incorrect content label applied |
| `27` | Verify — Parcel | Parcel label no-read / missing |
| `28` | Verify — Parcel | Parcel Label conflict |
| `29` | Verify — Parcel | Incorrect Parcel label applied |
| `33` | Verify — Shipping | Shipping Label conflict |
| `34` | Verify — Content | Content Label conflict |

**C# target:** `public enum VerifyReasonCode { … }` in `PandA.Core.Verification`, with XML-doc mapping each value to its source description. Current `VerifyOutcome` enum is an abstraction layer on top; keep both. `VerifyReasonCode` is what gets persisted in `RejectHistory` and exposed to the GUI.

### 4. CartonStatus Strings (`sdisp_PA_LookupCarton.sql` lines 189–200 via `sdiudf_PA_GetCartonStatus`)
These are string-keyed, read via a UDF. The full set seen across the induct flow:

`No Read` · `No Data` · `Label Conflict` · `Gap Error` · `Bypass` · `Verify Disabled` · `No Information` · `Duplicate` · `PrintHold` · `No Profile` · `No Reprint` · `Inactive` · `PrintReady` · `Verify - PASS` · `Verify - FAIL`

**C# target:** `public enum CartonStatus { … }` in `PandA.Core` — this drives lane routing, exception-label decisions, and GUI display. Not yet defined as a C# enum anywhere.

---

## F20 — Gap Error + Scanner Read-Quality Detection

### Source
- **`sdisp_PA_LookupCarton.sql` lines 208–235** — the complete classification logic  
- Called by `sdisp_PA_Scan_Induct` (line 74), which is the entry point for PLC message 281 (DeviceID=1)  
- Settings read: `MinGap` (default=20, line 169), `PrintExceptionLabels` (default=1, line 145)

### Behavior — Classification Rules (lines 208–235)

Read-quality and gap evaluation run on **`@Label1` (the blind label / best-read barcode) before any DB lookup**. The evaluation is a priority-ordered IF/ELSE chain — **first match wins**:

| Priority | Condition | Assigned CartonStatus | Notes |
|----------|-----------|----------------------|-------|
| 1 | `@BlindLabel LIKE '%?%' OR @BlindLabel = '-' OR @BlindLabel LIKE '<'` | `No Read` | `?` = scanner no-read sentinel; `-` = default/empty; `<` = likely framing artifact |
| 2 | `@BlindLabel LIKE '%!%' OR @BlindLabel = '0'` | `No Data` | `!` = no-data sentinel; `'0'` added 2024-09-03 |
| 3 | `@BlindLabel LIKE '%#%'` | `Label Conflict` | `#` = multi-read conflict sentinel |
| 4 | `@Gap < @MinGap` | `Gap Error` | Cartons too close (encoder units); `MinGap` default=20 |
| — | None of the above | Happy path → DB lookup | Falls through to xref + PandaData query |

**Label2–Label6 are NOT checked** for quality sentinels at this stage. Only Label1 (the blind label from the induct scanner) is evaluated. Labels 2–6 are passed through to `PandaCartonList` for audit regardless.

**Post-classification exception label behavior** (lines 468–558): if `PrintExceptionLabels=1` and `CartonStatus` is one of `No Read`, `No Data`, `Label Conflict`, `PrintHold`, `Duplicate`, `No Information` — AND carton is not in bypass — a new `PandaData` record is created with `LabelType1='Exception'` and ZPL from either a local template (`sdisp_TOOL_PA_BuildExceptionLabel`) or DCMS depending on `DCMSExceptions` setting.

**Gap Error does NOT trigger an exception label** (line 468: only NoRead/NoData/LabelConflict/PrintHold/Duplicate/NoInfo in the CASE). Gap errors are sent downstream without a label — operator or site policy handles them.

**Carton run history:** regardless of quality outcome, `sdisp_TOOL_PA_PandaCartonList_Insert` is called (line 412) — every induct scan, including all exception types, gets a `PandaCartonList` row.

**RejectHistory correlation:** induct-quality failures do **not** directly call `sdisp_CUSTOM_RejectHistory_Insert` — that is called from `sdisp_PA_Scan_Verify`. However, if a gap/no-read carton reaches verify, the verify codes 4, 5, 7, 8 (Table §3 above) capture the induct-origin failure reason.

### Already Built?
**Complete gap.** `InductService` in `PandA.Core` receives scanned label strings but does **no** sentinel-character inspection or MinGap comparison. From `current-architecture.md` and backlog item F20: `InductService` passes labels directly to lookup with no quality pre-filter. The `CartonStatus` enum does not exist yet.

### Target Module/Class

```
PandA.Core/
  InductQualityClassifier.cs   — static/pure: classify(Label1, gap, minGap) → CartonStatus
  CartonStatus.cs              — enum NoRead|NoData|LabelConflict|GapError|Bypass|NoInfo|...
  IMinGapProvider.cs           — int GetMinGap()  (settings port, defaulting to 20)
PandA.Sim/
  SimMinGapProvider.cs         — returns hardcoded / injected value for tests
```

`InductQualityClassifier` is a pure function — no I/O, no state. `InductService` calls it first; if the result is not `PrintReady`, it creates the exception path without touching the store lookup. This makes it trivially testable.

### Dependencies
- **F10 (Exception Labels)** consumes the `CartonStatus` output of this classifier — F20 must land first  
- **F22 (RejectHistory)** uses induct-quality verify codes 4–8 at the verify station downstream  
- **F-LOG1 (Carton Run History)** emits the `PandaCartonList` row with the classified `CartonStatus`  
- **F16 (Logging)** — `InductQualityClassifier` results should be emitted as log events (`IPandaEventSink`)  
- **Settings port** (not yet built) — `MinGap` must come from `ISettingsProvider`

### Acceptance Criteria
1. `InductQualityClassifier.Classify("0154?006001", gap: 25, minGap: 20)` → `CartonStatus.NoRead`
2. `Classify("0!", gap: 25, minGap: 20)` → `CartonStatus.NoData`
3. `Classify("0", gap: 25, minGap: 20)` → `CartonStatus.NoData` (literal "0" test)
4. `Classify("0154#006001", gap: 25, minGap: 20)` → `CartonStatus.LabelConflict`
5. `Classify("0154006001", gap: 10, minGap: 20)` → `CartonStatus.GapError` (gap 10 < minGap 20)
6. `Classify("0154006001", gap: 20, minGap: 20)` → `CartonStatus.PrintReady` (equal to minGap is **not** a gap error — `<` not `<=`)
7. `Classify("-", gap: 25, minGap: 20)` → `CartonStatus.NoRead` (literal `-` sentinel)
8. Priority order: `Classify("?#conflict", gap: 5, minGap: 20)` → `CartonStatus.NoRead` (? wins over # and gap)
9. `InductService` with a no-read carton does **not** call the `ITransportOrderStore` lookup  
10. Exception label path is triggered when `CartonStatus` is NoRead **and** `PrintExceptionLabels=true`

### Open Questions
- **OQ-F20-1:** For gap errors, is there any label printed at all, or does the carton flow unlabeled? The source does NOT include `Gap Error` in the exception-label case (line 468–479) — confirm this is intentional.  
- **OQ-F20-2:** The `<` sentinel in the no-read check (line 208) — is this a real STX character leaked from framing, or a legacy artifact? Should the port preserve this exact check?  
- **OQ-F20-3:** Labels 2–6 have quality sentinels too at the PLC level; should the port check all six for sentinels or only Label1?

---

## F15 — PLC Event / Carton Recovery

### Source
- **`sdisp_BP2PA_Event.sql` lines 59–133** — the complete proc  
- Entry: called from process layer on PLC message type (comment says "process_sp msg 1 for events like late assignments, carton lost, etc.")  
- Parameters: `@DBName VARCHAR(32)`, `@EventID VARCHAR(32)` (default `'-'`), `@CartonListID BIGINT` (default `0`), `@DeviceID INT` (default `0`)  
- Lookups: `sdivw_pandadefs` (line 63–69) → resolves `@PandaID` and `@VerifyDevice` (VerifyScannerDeviceID) from the DB name

### Behavior — Precise Rules

**Step 1 — Gate (line 71):** `IF @EventID NOT IN ('217','218')` — codes 217 and 218 are silently ignored (no log entry written, no recovery). All other codes, including unmapped ones, proceed.

**Step 2 — Error description (lines 73–82):** CASE on `@EventID`:
- `2012` → `'Carton Lost'`
- `2015` → `'Late Carton Assignment'`  
- `2016` → `'Unexpected Carton'`
- `2017` → `'Late Tracking Error'`
- `2019` → `'Early Tracking Error'`
- Anything else → `'Undefined [Event Code: X]'`

Formatted into: `'PLC Error Detected: <error> for CartonListID: <id> (Device <deviceId>)'`

**Step 3 — Log (lines 86–93):** `sdisp_Log_Event` called at `@LogLevel=50` (Warning).

**Step 4 — Recovery condition (line 95):**  
`IF @CartonListID > 0 AND (@VerifyDevice > 0 AND @DeviceID < @VerifyDevice)`

- `@CartonListID > 0`: a specific carton must be identified  
- `@VerifyDevice > 0`: there is a verify scanner configured  
- `@DeviceID < @VerifyDevice`: the event came from a device **before** the verify scanner (the carton has not yet been scanned by verify)

**Step 5 — Recovery action (lines 97–99):**  
`UPDATE PandaData SET Printed=0, ActiveRecord=1 WHERE CartonListID=@CartonListID`

This **re-arms** the carton: `Printed=0` clears the printed state (carton will be picked up by the next induct scan as un-printed), `ActiveRecord=1` marks it as active.

**Step 6 — Second log (lines 105–112):** second `sdisp_Log_Event` at `@LogLevel=50` documenting the recovery action.

**If recovery condition is false:** only the initial log is written; the carton is NOT re-armed (e.g., if the event came from the verify station or beyond, or if there is no CartonListID).

### Design Tension with decision-003

> **This is the central collision point for this cluster.**

Decision-003 explicitly states: *"No auto re-arm on verify fail."* The source's `sdisp_TOOL_PA_VerifyLabel` doing `Printed=0, ActiveRecord=1` on verify fail is the hole decision-003 corrects.

**However, F15's carton recovery is different:** it re-arms due to a **PLC tracking failure before verify** (e.g., the carton was lost in conveyor tracking, never actually reached verify). The intent is to give the carton another chance to print correctly — it is NOT a verify failure. The condition `@DeviceID < @VerifyDevice` is the gating rule.

**Port decision (to be confirmed with owner):** F15's recovery maps to a new `TransportOrder` method: `ResetForTrackingEvent(PlcEventCode code)` — distinct from `MarkVerifyFailed()`. It should:
1. Only trigger if the TO is in `Printed` status (it was already printed but tracking lost it before verify) — not on `Verified` or `HeldForIntervention`
2. Transition back to `Advised` (not `ReprintAuthorized` — this is a system-initiated recovery, not operator authorization)
3. Emit an event log entry at Warning level
4. Be subject to the reprint-rules setting: if `ReprintLabels=0`, recovery should NOT re-arm a carton that has already been verified

### Already Built?
**Complete gap.** No `ResetForTrackingEvent` method. No PLC event ingestion path. `TransportOrder` has no mechanism to transition from `Printed` back to `Advised`. Backlog item F15 explicitly notes "No `TransportOrder.ResetForTrackingEvent()` equivalent."

### Target Module/Class

```
PandA.Core/
  PlcEventCode.cs             — enum CartonLost=2012, LateAssignment=2015, UnexpectedCarton=2016,
                                         LateTrackingError=2017, EarlyTrackingError=2019
  IPlcEventHandler.cs         — Task HandleAsync(PlcEvent evt)  [port for PLC message 285-area]
  PlcEvent.cs                 — record(PlcEventCode Code, long? CartonListId, int DeviceId, string LineId)
  PlcEventHandlerService.cs   — resolves TO, calls ResetForTrackingEvent if pre-verify + prints warning event
PandA.Core/TransportOrder.cs  — add: ResetForTrackingEvent(PlcEventCode)
```

`PlcEventHandlerService` takes `ITransportOrderStore`, `IPandaEventSink`, `IVerifyDeviceProvider`. The `IVerifyDeviceProvider.GetVerifyDeviceId(lineId)` returns the configured verify scanner device ID (from `LineConfig`).

### Dependencies
- **F16 (Logging)** — must emit Warning-level events  
- **F-LOG1 (Run History)** — a recovery creates a "re-armed by tracking event" lifecycle entry  
- **decision-003 (reprint policy)** — recovery semantics must respect the reprint gate  
- **F-ADV1 (re-advice re-arm)** — both F15 and F-ADV1 are paths that re-arm a carton; they must be governed by the same reprint-rules policy consistently

### Acceptance Criteria
1. `HandleAsync(PlcEvent(CartonLost, CartonListId=42, DeviceId=1))` with `VerifyDeviceId=2` → TO transitions `Printed → Advised`
2. `HandleAsync` with `DeviceId=2` (at verify scanner) → TO stays `Printed` (no recovery)
3. `HandleAsync` with `DeviceId=3` (beyond verify) → no recovery
4. `HandleAsync` with `CartonListId=0` → no PandaData update, warning logged
5. `HandleAsync` on a `Verified` TO → no recovery (already finished)
6. `HandleAsync` on a `HeldForIntervention` TO → no recovery (different path: human intervention required)
7. `HandleAsync` with `VerifyDeviceId=0` (no verify scanner configured) → no recovery, warning logged
8. Event code `217` → silently ignored, no log, no recovery
9. Unmapped code (e.g., `9999`) → logs `'Undefined [Event Code: 9999]'` at Warning, no recovery
10. Two consecutive recovery events for the same carton → idempotent (still in `Advised` state, PrintCount unchanged)

### Open Questions
- **OQ-F15-1:** What do event codes 217 and 218 mean? They are explicitly excluded from processing (line 71). Are they non-carton zone events that should be handled differently, or deprecated?
- **OQ-F15-2:** Should `ResetForTrackingEvent` decrement `PrintCount`, reset it to 0, or leave it? (Source sets `Printed=0` which zeros the counter; decision-003 makes it monotonic — conflict.)
- **OQ-F15-3:** If `ReprintLabels=0`, should a tracking-event recovery still re-arm the carton? Source does not gate on this setting.
- **OQ-F15-4:** Is PLC message type for this event code-range 280–299? The arch-log 009 maps `sdisp_BP2PA_Event` to this range but the exact message number for events (vs. 281=induct, 283=printer-status, 284=zone) is not documented.

---

## F16 — Structured Event Logging

### Source
- **`sdisp_Log_Event.sql`** — the unified logging entry point, called by virtually every SP  
- **`sdisp_eLog_LogIt.sql`** — writes to the structured `uEventLog` table  
- **`sdisp_eLog_add.sql`** — lazily registers (SP, EventKey) → stable `EventDescriptionID` in `EventDescriptions`  
- **`sdisp_eLog_Harvest.sql`** — a DBA utility SP that scans all SP source text for `{eLog begin}`/`{eLog end}` markers to produce a catalog; this is NOT a runtime path

### Tables Written

**`EventLog`** (`5.0_CreateTables/EventLog.sql`):
```
RecID          BIGINT IDENTITY(1,1) PK
CreationTime   DATETIME             default GETDATE()
EventDescription VARCHAR(256)        default '-'   -- human-readable description
LogLevel       INT                  default 100
SourceProc     VARCHAR(64)          default '-'
PandaID        VARCHAR(32)          null
PrinterID      VARCHAR(32)          null
LPN            VARCHAR(max)         null            -- the blind label / carton barcode
CartonListID   BIGINT               null
PandaDataID    BIGINT               null
```

**`uEventLog`** (`5.0_CreateTables/uEventLog.sql`):
```
RecID        BIGINT IDENTITY(100,1) PK
CreationTime DATETIME
EventID      BIGINT NOT NULL        -- FK→EventDescriptions.EventDescriptionID
Uint01–06    BIGINT null            -- Uint01=CartonListID, Uint02=PandaDataID
Int01–10     INT null
Var01–20     VARCHAR(64) null       -- Var01=PandaID, Var02=PrinterID, Var03=LPN
```

**`EventDescriptions`** (`5.0_CreateTables/EventDescriptions.sql`):
```
EventDescriptionID  BIGINT      (not identity — computed by sdisp_eLog_add)
CreationTime        DATETIME
SourceSPName        VARCHAR(64)  PK component
EventKey            VARCHAR(512) PK component
StoredDescription   VARCHAR(512) null  — optional localized/overridden text
```

**`EventStoredProcedureList`** (`5.0_CreateTables/EventStoredProcedureList.sql`):
```
EventSPID    INT IDENTITY(100,100) PK  — auto-assigned in 100-step increments per SP
EventSPName  VARCHAR(64)
```

### Behavior — Two-Tier Pipeline

**Tier 1 — `sdisp_Log_Event` (human log)**  
- Signature: `@EventDescription VARCHAR(255)`, `@SourceProc VARCHAR(64)`, `@LogLevel INT`, `@PandaID`, `@PrinterID`, `@LPN`, `@CartonID BIGINT`, `@PandaDataID BIGINT`
- Writes **one row to `EventLog`** (line 49–72), then calls Tier 2 (`sdisp_eLog_LogIt`) with the same data mapped to positional slots (line 75–116): `CartonListID→Uint01`, `PandaDataID→Uint02`, `PandaID→Var01`, `PrinterID→Var02`, `LPN→Var03`

**Tier 2 — `sdisp_eLog_LogIt` + `sdisp_eLog_add` (structured log)**  
- `sdisp_eLog_LogIt` first calls `sdisp_eLog_add` to ensure the `(SourceSPName, EventKey)` pair is registered  
- `sdisp_eLog_add` ID computation (`sdisp_eLog_add.sql` lines 131–148):
  - Gets the SP's `EventSPID` (auto-creates if new SP, IDENTITY 100, 200, 300…)
  - Gets highest existing `EventDescriptionID` for that SP
  - New ID = `MAX(EventSPID, lastID) + 1` — IDs cluster around the SP's block, monotonically increasing within it
- `sdisp_eLog_LogIt` inserts one row into `uEventLog` with the resolved `EventID` plus all 6 BIGINT + 10 INT + 20 VARCHAR columns

**`sdisp_eLog_Harvest` (DBA catalog utility, not runtime):**  
Scans ALL SPs in the database for the string markers `{eLog begin}` and `{eLog end}` using a dynamic SQL `PATINDEX` loop; extracts the text and LogLevel between the markers. This is a documentation/introspection tool to inventory what events exist. **No C# equivalent needed** — in C#, the event catalog is inherent in the code.

### Known Source Anomaly
`sdisp_PA_Scan_Verify` line 149: `@LogLevel = 805` — all other calls use values from {30, 40, 50, 80, 100}. The value `805` is almost certainly a source typo for `80` (Information). The C# port should normalize this to `80`.

### Already Built?
**Complete gap.** No `IPandaEventSink`, no `PandaEvent`, no `PandaEventLevel`, no structured log abstraction anywhere in `PandA.Core` or `PandA.Sim`.

### Target Module/Class — Proposed Logging Abstraction

The DB is out of scope. The abstraction must allow:
1. `PandA.Core` to emit events without caring about the sink
2. `PandA.Sim` to capture events in-memory for test assertions
3. A future `PandA.EController` adapter to write to `EventLog` + `uEventLog`

```
PandA.Core/
  IPandaEventSink.cs          — void Emit(PandaEvent evt)
  PandaEvent.cs               — sealed record(PandaEventLevel Level, string Description,
                                               string SourceComponent, string? PandaId,
                                               string? PrinterId, string? Lpn,
                                               long? CartonListId, long? PandaDataId,
                                               DateTimeOffset OccurredAt)
  PandaEventLevel.cs          — enum Critical=30, Error=40, Warning=50, Information=80, Verbose=100
  NullPandaEventSink.cs       — no-op implementation (for callers that don't inject one)

PandA.Sim/
  InMemoryEventSink.cs        — List<PandaEvent>; thread-safe; used in tests for assertions
  InMemoryEventSinkExtensions — .GetEvents(level), .GetEvents(cartonListId), .GetWarningsAndAbove()
```

All existing services (`InductService`, `VerifyStationService`, `CartonAdviceService`, `LaneEvalService`) should receive `IPandaEventSink` via constructor injection and emit at appropriate levels on state changes.

**Structured log (Tier 2 / uEventLog equivalent):**  
The `EventDescriptionID` auto-registration scheme is a SQL-specific trick for generating stable numeric IDs across restarts. In C#, a future `IStructuredEventLog` could map `(componentName, eventKey) → stable Guid or int` but this is an observability concern, not a domain concern. **Defer to the EController adapter** — for now, `IPandaEventSink` with a human-readable `Description` is sufficient. Add a `Tags` dictionary for structured fields if needed.

### Dependencies
- **Every other feature in this cluster** — F15, F20, F22, F-LOG1 all call `sdisp_Log_Event`  
- **All existing services** — `InductService`, `VerifyStationService`, `CartonAdviceService` must be retrofitted to inject and use `IPandaEventSink`  
- **F17 (Purge)** — `EventLog` retention is part of purge (`PurgeSetting_*` settings govern how many days to keep)

### Acceptance Criteria
1. `NullPandaEventSink.Emit(any)` → no exception, no side effect
2. `InMemoryEventSink.Emit(evt)` stores event; `.GetEvents()` returns it in insertion order
3. `InductService` emits `Information` event on successful CartonList creation (source line 456: "Inbound CartonList record added")
4. `InductService` emits `Warning` event on NoReprint status (source line 319/340)
5. `VerifyStationService` emits `Information` event on pass (source verify DCMS confirm, normalized from `805→80`)
6. `VerifyStationService` emits `Critical` event (level 30) when its CATCH block fires
7. Two consecutive events from different services each appear as separate entries with correct `SourceComponent`
8. `PandaEventLevel` enum values exactly match source numeric values (30, 40, 50, 80, 100)
9. `InMemoryEventSink.GetWarningsAndAbove()` returns only events with `Level <= Warning (50)` (lower number = higher severity)

### Open Questions
- **OQ-F16-1:** The `uEventLog`/`EventDescriptions` Tier 2 generates stable numeric EventDescriptionIDs. Is there any downstream consumer (GUI, DCMS, BI/reporting) that queries by `EventID`? If yes, the EController adapter must replicate the ID computation exactly, not just insert text rows.
- **OQ-F16-2:** `sdisp_eLog_Harvest` implies there's a convention of marking SPs with `{eLog begin}`/`{eLog end}` comments. Should C# services have an equivalent metadata convention (e.g., `[EventSource("…")]` attributes), or is a documentation-only approach sufficient?
- **OQ-F16-3:** Should `IPandaEventSink` be synchronous (`void Emit`) or async (`Task EmitAsync`)? Async allows non-blocking DB writes in the adapter, but Core services are currently sync.

---

## F22 — Reject History Audit Trail

### Source
- **`sdisp_CUSTOM_RejectHistory_Insert.sql` lines 40–90** — the full insert proc  
- Called from **`sdisp_PA_Scan_Verify.sql` lines 117–131** — the only caller

### Table Written

**`RejectHistory`** (`5.0_CreateTables/RejectHistory.sql`):
```
RecID        BIGINT IDENTITY(1,1)  PK component
VerifyCode   VARCHAR(32)           null — the numeric verify/reject code (see taxonomy §3 above)
VerifyReason VARCHAR(64)           null — human-readable description from CASE
PandaDataID  BIGINT NOT NULL       PK component
CartonListID BIGINT                null
```

Note: PK is `(RecID, PandaDataID)` — PandaDataID is part of the key. The same PandaDataID could appear multiple times if the carton fails verify on multiple passes (e.g., reprint authorized, passes induct, fails verify again).

### Behavior — Precise Rules

**`sdisp_PA_Scan_Verify` line 117:** `IF @VerifyPass <> 1` — insert for every non-pass outcome.

**CartonListID resolution (lines 120–125):** The carton list ID used is **NOT** the induct CartonListID but the **most recent verify-scan CartonListID** for that SeqNum:
```sql
SELECT TOP 1 @NewCartonListID = CartonListID  
FROM dbo.PandaCartonList  
WHERE SeqNum = @SeqNum  
ORDER BY CartonListID DESC
```
If no match is found, falls back to `@CartonListID` (the induct one). This means the audit row always links to the **verify scan pass** of the carton, not the induct scan.

**`sdisp_CUSTOM_RejectHistory_Insert`:** maps `@VerifyCode → @VerifyReason` text (20 mappings, lines 44–65), then inserts the row. CATCH block logs at level 30 (Critical) but does NOT propagate — a reject-history failure is non-fatal to the verify flow.

### Already Built?
**Complete gap.** No `RejectHistory` model, no `IRejectHistoryRepository`, no reject-record emission from `VerifyStationService`. GUI reject screen (`sdisp_GUI_GetPandARejectCartons`) depends on this table.

### Target Module/Class

```
PandA.Core/
  RejectRecord.cs             — record(long PandaDataId, long CartonListId, VerifyReasonCode Code,
                                        string Reason, DateTimeOffset RejectedAt)
  IRejectHistoryRepository.cs — Task AddAsync(RejectRecord record)
  VerifyReasonCode.cs         — enum (see taxonomy §3 — the 20 codes)
  VerifyReasonCodeExtensions  — .ToDescription() → the CASE text strings (stable: drives GUI)

PandA.Sim/
  InMemoryRejectHistoryRepository.cs — stores in List<RejectRecord>
```

`VerifyStationService` injects `IRejectHistoryRepository` and calls `AddAsync` when `VerifyOutcome != Pass && != Ignore`. The `VerifyOutcome → VerifyReasonCode` mapping must incorporate the per-label-type logic from arch-log 006 §B and the verify-code taxonomy (§3 above).

**Outcome → Code mapping (from arch-log 006 + F22 source):**

| VerifyOutcome | LabelType | VerifyReasonCode |
|---------------|-----------|-----------------|
| `Ignore (bypass)` | any | `3` (not stored) |
| `Fail` (mismatch) | BlindLabel | `14` |
| `Fail` (mismatch) | Shipping | `21` |
| `Fail` (mismatch) | Content | `23` |
| `Fail` (mismatch) | Parcel | `29` |
| `NoRead` | BlindLabel | `14` |
| `NoRead` | Shipping | `20` |
| `NoRead` | Content | `22` |
| `NoRead` | Parcel | `27` |
| `Conflict` | BlindLabel | `15` |
| `Conflict` | Shipping | `33` |
| `Conflict` | Content | `34` |
| `Conflict` | Parcel | `28` |
| `NoData` | any | `12` |
| `Fail` (missing/leftover expected) | BlindLabel | `14` |
| `Fail` (missing/leftover expected) | Shipping | `20` |
| `Fail` (missing/leftover expected) | Content | `22` |
| `Fail` (missing/leftover expected) | Parcel | `27` |
| Error/catch | any | `0` |

### Dependencies
- **F16 (Logging)** — CATCH block must emit Critical-level event on insert failure  
- **F20 (Read Quality)** — induct-origin failures (NoRead/NoData/GapError) have corresponding verify codes (7,5,4) — the induct service may need to write pre-verify reject records too (source doesn't, but gap-error cartons are never verified, so their RejectHistory row would need to be written at induct)
- **GUI (`sdisp_GUI_GetPandARejectCartons`)** — depends on this table  
- **Wave reports** — wave completion reports reference `RejectHistory`  
- **`VerificationService`** — must produce `VerifyReasonCode` in per-label detail (or `VerifyStationService` derives it from `VerifyLabelDetail` after the fact)

### Acceptance Criteria
1. Verify fail (shipping mismatch) → `RejectRecord` inserted with `Code=21, Reason="Incorrect shipping label applied"`
2. `VerifyPass=1` (pass) → NO `RejectRecord` inserted
3. `VerifyOutcome.Ignore` (bypass) → NO `RejectRecord` inserted (code 3 is not stored in source either)
4. `VerifyReasonCode.ToDescription()` returns exact source strings (e.g., `12 → "Verify scan error"`)
5. `CartonListId` on the reject record is the **verify-scan** CartonListId (most recent for SeqNum), not the induct one
6. `RejectRecord.PandaDataId` matches the `TransportOrder`'s `PandaDataId`
7. `IRejectHistoryRepository.AddAsync` failure is non-fatal — `VerifyStationService` catches and logs at Critical but does not propagate
8. Same PandaDataId can appear in two reject records if a carton is reprinted and fails verify a second time

### Open Questions
- **OQ-F22-1:** Gap Error cartons (`CartonStatus='Gap Error'`) pass through the induct pipeline without a verify scan. They never produce a `RejectHistory` row in the source. Should the C# port write a `RejectRecord(Code=4, "Gap Error")` at induct time, so the operator's reject screen shows them?
- **OQ-F22-2:** `VerifyCode` is stored as `VARCHAR(32)` in the DB even though the values are all integers. Intentional for future string codes, or a type inconsistency?
- **OQ-F22-3:** Should `VerifyOutcome.NoData` at verify always map to code `12` regardless of label type? The source `sdisp_CUSTOM_RejectHistory_Insert` treats `12` as a universal no-data code; arch-log 006 confirms this.

---

## F-LOG1 — Carton Run-History Event Logging

### Source
- **`sdisp_TOOL_PA_PandaCartonList_Insert.sql`** — creates a run-history row on every induct scan  
- **`sdisp_TOOL_PA_PandaCartonList_Update.sql`** — updates the row (e.g., printer selection result)  
- Called from `sdisp_PA_LookupCarton.sql` line 412 (insert) and line 130 of `sdisp_PA_Scan_Induct.sql` (update with printer)
- Decision-004/AD-1 explicitly bookmarks this as the audit trail behind re-runs/reprints

### Table Written

**`PandaCartonList`** (`5.0_CreateTables/PandaCartonList.sql`):
```
CartonListID   BIGINT IDENTITY(1,1) PK        — the run's unique ID; FK target for EventLog, RejectHistory
CreationTime   DATETIME             default GETDATE()
CartonStatus   VARCHAR(32)          null       — status at induct (No Read, Gap Error, PrintReady, etc.)
PandaDataID    BIGINT NOT NULL                 — which PandaData record (the advised label set)
PandaID        VARCHAR(32) NOT NULL            — which PandA line/station
SorterNumber   INT                  null
SorterMode     INT                  null
DeviceID       INT                  null       — 1=induct scanner
SeqNum         INT                  null       — PLC carton-slot counter
LabelStatus    INT                  null       — 0=good, 1=lowboy, 2=no-data, 3=data-error
PandaScanLabel1..6  VARCHAR(max)    null       — raw scanned barcode strings at induct
CartonLength/Width/Height/Weight  INT  null   — physical dimensions from dimensioner
FrontGap       INT                  null       — measured gap to preceding carton
LaneNumber     INT                  null       — resolved destination lane
PrinterNumber  VARCHAR(32)          null       — assigned printer (filled in after printer selection)
```

### Behavior — Lifecycle of a PandaCartonList Row

**Insert (induct scan, `sdisp_PA_LookupCarton` line 412):**  
- Called for **every induct scan** regardless of status (no-read, gap error, happy path — all get a row)  
- `PrinterNumber` is `NULL` at insert time  
- Returns `@CartonListID` (SCOPE_IDENTITY) — this ID flows through the entire session

**Update (after printer selection, `sdisp_PA_Scan_Induct` line 130):**  
- `sdisp_TOOL_PA_PandaCartonList_Update` called with `@PrinterNumber` set  
- Also called when exception PandaData is created (line 540 in LookupCarton) to set `@PandaDataID`  
- Update uses `ISNULL(@param, [existingColumn])` pattern — **only non-null parameters update their column** (`sdisp_TOOL_PA_PandaCartonList_Update.sql` lines 61–84)

**Note on PrinterNumber trimming (line 83):** `LEFT(@PrinterNumber, LEN(@PrinterNumber)-1)` — strips the last character from `PrinterNumber` on update. This looks like a trailing delimiter artifact from how printer IDs are assembled in the PLC pipeline. **Flag for owner.**

**Run History Semantics:**  
One `PandaCartonList` row = one pass through the induct scanner. A carton that:
- Runs once and passes → 1 row in PandaCartonList  
- Fails verify → gets re-armed → runs again → 2 rows with same `PandaDataID`, different `CartonListID`  
- Gets a tracking event (F15 recovery) → may get another induct scan → 3rd row  

This is the "how many times it ran, each outcome" history the domain owner requires.

**The carton barcode's full history** is therefore:  
`SELECT * FROM PandaCartonList WHERE PandaDataID = x ORDER BY CartonListID ASC` — each row is a run attempt, with `CartonStatus`, `CreationTime`, `PrinterNumber`, and the physical measurements.

### Already Built?
**Complete gap.** `TransportOrder` has `PrintCount` (monotonic int) and per-label `LabelPrintState`, but no `CartonRunHistory` — no equivalent of the `PandaCartonList` row sequence. `InductService` does not persist any run-history record. This is **the audit trail cited in decision-004/AD-1**.

### Target Module/Class

```
PandA.Core/
  CartonRunRecord.cs            — record(long RunId, string TuId, long PandaDataId,
                                          string LineId, int SorterNumber, int SorterMode,
                                          int DeviceId, int SeqNum, int LabelStatus,
                                          string[] ScannedLabels,       // [0..5]
                                          int Length, int Width, int Height, int Weight,
                                          int FrontGap,
                                          CartonStatus StatusAtInduct,
                                          string? AssignedPrinter,
                                          int? DestinationLane,
                                          DateTimeOffset CreatedAt)
  ICartonRunRepository.cs       — Task<long> CreateRunAsync(CartonRunRecord record)
                                — Task UpdatePrinterAsync(long runId, string printerId)
                                — Task<IReadOnlyList<CartonRunRecord>> GetRunsForOrderAsync(long pandaDataId)

PandA.Sim/
  InMemoryCartonRunRepository.cs — Dictionary<long, CartonRunRecord>; RunId auto-increments
```

`InductService` calls `ICartonRunRepository.CreateRunAsync` immediately after quality classification. After printer selection succeeds, calls `UpdatePrinterAsync`. `GetRunsForOrderAsync` is used by the operator GUI and F-LOG1 query paths.

**Lifecycle event integration (F16 coupling):**  
When `InductService` creates a run record, it should also emit a `PandaEvent` (via `IPandaEventSink`) with:
- Level: `Information`  
- Description: `"Carton [TuId] induct run #{PrintCount} — status: [status]"`  
- Fields: `PandaId`, `Lpn=TuId`, `CartonListId=runId`, `PandaDataId`

This is the event the domain owner sees in the event log to trace a barcode's history.

### Dependencies
- **F20 (Read Quality)** — the `CartonStatus` on the run record comes directly from quality classification  
- **F15 (PLC Recovery)** — a recovery-triggered re-arm leads to a new run row on the next induct  
- **F16 (Logging)** — lifecycle events emit via `IPandaEventSink` alongside the run record  
- **F22 (RejectHistory)** — `RejectHistory.CartonListID` is the verify-scan run's `CartonListID`  
- **F-ADV1 (Re-advice re-arm)** — re-advice for a carton that has prior run records should be logged as a re-run event ("advised again after N prior runs")  
- **F23 (Wave auto-complete)** — wave completion checks reference `CartonListID`  
- **Operator GUI** — `sdisp_GUI_GetPandAWaveCartons` and the reject screen query by `PandaDataID`/`CartonListID`

### Acceptance Criteria
1. Every `InductService.InductAsync` call (even for no-read/gap-error cartons) produces one `CartonRunRecord` with the correct `StatusAtInduct`
2. `GetRunsForOrderAsync(pandaDataId)` for a carton that ran twice returns **two** records in `RunId` ascending order
3. First run record has `AssignedPrinter = null` at creation time; after `UpdatePrinterAsync` it is populated
4. A re-advised carton (same TuId, new advice) that runs again appears as a third run record; `PrintCount` on the TO matches the count of completed (non-exception) run records
5. `CartonRunRecord.ScannedLabels` contains exactly 6 slots (indices 0–5); empty/missing labels are `""` not null
6. `CartonRunRecord.FrontGap` correctly records the gap that caused `StatusAtInduct=GapError`
7. A `PandaEvent(Information)` is emitted by `InductService` with the run's `CartonListId` for every successful run creation
8. `InMemoryCartonRunRepository` is thread-safe for concurrent induct scans on different lines

### Open Questions
- **OQ-FLOG1-1:** `PrinterNumber` in source is trimmed by one character on update (line 83): `LEFT(@PrinterNumber, LEN(@PrinterNumber)-1)`. What trailing character is being stripped, and should the C# port replicate this?
- **OQ-FLOG1-2:** The `PandaCartonList` row is created even for bypassed/gap-error cartons (no PandaDataID match). In those cases `PandaDataID = 0`. Should the `CartonRunRecord` carry a nullable `PandaDataId`? The source has `PandaDataID NOT NULL` but defaults to `0`.
- **OQ-FLOG1-3:** How long should `CartonRunRecord` data be retained? Source `sdisp_PA_Purge` purges `PandaCartonList` with other tables but the exact retention setting is in `PurgeSetting_*` (F17). Does the domain owner want a minimum retention for the run-history audit trail?
- **OQ-FLOG1-4:** The `SeqNum` cycles 0–2000 (arch-log 009). Multiple `CartonRunRecord` rows for the same `SeqNum` value will exist over time. Is `(SeqNum, CreationTime)` or just `RunId` the right key for GUI queries?

---

## Cross-Cluster Dependency Map

The five features are tightly coupled. Here is the dependency graph in build order:

```
F20 (Quality Classifier) — no dependencies in this cluster; must land first
    ↓
F16 (IPandaEventSink) — depends on nothing; can land in parallel with F20
    ↓
F-LOG1 (ICartonRunRepository) — depends on F20 (status) + F16 (events)
    ↓
F15 (PlcEventHandlerService) — depends on F16 (logging) + F-LOG1 (re-arm creates audit entry)
    ↓
F22 (IRejectHistoryRepository) — depends on F16 (logging) + VerifyReasonCode taxonomy
```

Additionally, all **existing services** need retrofitting once F16 lands:
- `InductService` → add `IPandaEventSink` + `ICartonRunRepository`  
- `VerifyStationService` → add `IPandaEventSink` + `IRejectHistoryRepository`  
- `CartonAdviceService` → add `IPandaEventSink` (for re-advice logging, F-ADV1 bookmark)  
- `LaneEvalService` → add `IPandaEventSink` (for slow/shut line events)

---

## Summary: Already Built vs. Gap

| Feature | C# Status | Gap Description |
|---------|-----------|-----------------|
| **F15** PLC Event / Recovery | 🔴 Complete gap | No `PlcEventCode` enum, no `ResetForTrackingEvent`, no PLC event handler |
| **F16** Structured Event Logging | 🔴 Complete gap | No `IPandaEventSink`, no `PandaEvent`, no `PandaEventLevel`; zero event emission from any service |
| **F22** Reject History | 🔴 Complete gap | No `VerifyReasonCode`, no `IRejectHistoryRepository`, no emission from `VerifyStationService` |
| **F-LOG1** Carton Run History | 🔴 Complete gap | `PrintCount` is monotonic ✓; no `CartonRunRecord`, no `ICartonRunRepository`, no per-run physical data |
| **F20** Scanner Read Quality | 🔴 Complete gap | No sentinel detection, no `MinGap` check, no `CartonStatus` enum, no pre-lookup classification |
| **SpaceScan** | ➡️ Out of scope | DBA disk-space utility; no port required |

**Test delta on landing this cluster (estimated):** +40–60 new unit tests across 5 features; all existing 231 tests remain green with only additive changes (new constructor params with defaults or `NullPandaEventSink`).
