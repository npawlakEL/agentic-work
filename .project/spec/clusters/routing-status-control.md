# PandA Port — Spec-Ready Detail: Lane Routing · Line/Zone Control Egress · Status Ingestion

> **Conventions used below:**  
> `SQL:<file>:L<n>` — source SQL line reference.  
> `CS:<file>:L<n>` — existing C# line reference.  
> All SQL paths are relative to `C:\Users\NoahPawlak\.copilot\…\8.0_CreateSP\` unless prefixed with `5.0_` or `6.0_`.

---

## F08 — Lane Routing (Carton Status → Divert Lane Number)

### Source
- Primary: `sdisp_TOOL_PA_GetFinalLaneFromStatus.sql`
- Table DDL: `5.0_CreateTables/LaneDef.sql` (L9–19) — `(RecID, PandaRecID, LaneID VARCHAR(32), LaneNumber INT, LastDiverted DATETIME)`
- Seed data: `6.0_PopulateTables/LaneDef.sql` (L3–7) — four rows: PANDA 1 → Verify-Pass=lane 12, Verify-Fail=lane 10; PANDA 2 → Verify-Pass=lane 2, Verify-Fail=lane 1. **Note: no REJECT row in the seed.**
- Referenced by: `sdisp_PA_LookupCarton` (induct — assigns a default FAIL lane) and `sdisp_PA_VerifyCarton` (verify — updates to the actual divert lane). Also used to populate `vPA.Assign[i].Dest` PLC tag (arch-log 009 §3A).

### Behavior

**Inputs:** `@PandaID VARCHAR(64)`, `@CartonStatus VARCHAR(64)`, `@FinalDestination VARCHAR(64)`  
**Output:** `@FinalLane VARCHAR(64)` (holds an integer lane number as a string; may be NULL if fallback also misses)

**Algorithm (SQL:GetFinalLaneFromStatus.sql:L53–101):**

1. **Round-robin CTE** (`LastDivertedLanes`, L63–82): For every `LaneDef` row matching `PandaRecID`, group by `LaneID` and select the row with `MIN(ISNULL(LastDiverted, GETDATE()))`. This picks the least-recently-used physical chute for each lane label. The `ISNULL(…, GETDATE())` means a NULL `LastDiverted` is treated as *right now* — so a never-diverted lane has the **highest** effective timestamp and therefore loses the `MIN`, making it the **last** preferred. In practice with seeded equal timestamps all lanes tie and SQL's arbitrary TOP 1 breaks the tie; in production, the last-diverted timestamp continuously rotates priority.

2. **Three-rule match** (`SELECT TOP 1 … WHERE`, L83–90): Among the round-robin set, find a row where ANY of:
   - `LaneID = @FinalDestination` — exact match against the carton's stored pass/fail destination name (from `PandaData.VerifyPassDestName` or `VerifyFailDestName`)
   - `@CartonStatus LIKE '%' + LaneID` — the carton status *ends with* the LaneID (suffix match). Example: status `'Verify - FAIL: No Read'` matches lane `'Verify - FAIL'`.
   - `@CartonStatus LIKE LaneID + '%'` — the carton status *starts with* the LaneID (prefix match). Example: status `'Verify - Pass'` matches lane `'Verify - Pass'`.

   `TOP 1` without `ORDER BY` after the CTE is non-deterministic if multiple distinct LaneIDs match all three criteria simultaneously. In the seeded config, each carton normally matches exactly one LaneID so this is not a problem in practice.

3. **REJECT fallback** (L92–101): If `@FinalLane` is still NULL after step 2, query `LaneDef WHERE LaneID = 'REJECT'` for this `PandaRecID`. **The seeded data has no REJECT row** — so the fallback returns NULL in a fresh install. The procedure returns NULL `@FinalLane` without error in this case.

4. **`LastDiverted` update**: The source proc itself does **not** update `LastDiverted`. The round-robin only works correctly if the caller (or another proc such as `sdisp_PA_VerifyCarton`) writes back the current timestamp to the diverted row. This update is **not visible in the files provided** and is an open question (see below).

**Output type quirk:** `@FinalLane` is declared `VARCHAR(64)` but contains the integer `LaneNumber` value as a string. The C# model should use `int?`.

### Already Built in C#?

**Not built.** `LineConfig` (`CS:LineConfig.cs`) has no `Lanes` property. `InductResult` and `VerifyOutcome` carry no `DivertLane`. `LaneDef` has no C# model anywhere. Arch-log 011 §3.1 GAP-1 and backlog "GAP F08" both confirm this is an unbuilt gap. The fire-point/PLC bundle `vPA.Assign[i].Dest` (arch-log 009 §3A) cannot be populated without this.

### Target Module/Class

| Concern | C# Home |
|---------|---------|
| `LaneDef` value object | `PandA.Core.LaneDef` — `record LaneDef(string LaneId, int LaneNumber, DateTimeOffset? LastDiverted)` |
| Lane collection on config | `LineConfig.Lanes: IReadOnlyList<LaneDef>` (new parameter on existing `LineConfig`) |
| Routing pure logic | `PandA.Core.LaneRoutingService.Resolve(IReadOnlyList<LaneDef> lanes, string cartonStatus, string finalDestination) : int?` — pure static/injectable, no I/O, fully unit-testable |
| `LastDiverted` mutation | Expose `LaneDef.WithLastDiverted(DateTimeOffset)` returning a new record (immutable lanes) **or** make `LastDiverted` mutable (matching `PrinterState` precedent). The mutation must be called by `InductService`/`VerifyStationService` after routing is resolved. |
| DivertLane on results | Add `int? DivertLane` to `InductResult` and `VerifyOutcome` |

Namespace: `PandA.Core`. No I/O, no adapter. `PandA.Sim` seeded lanes go into `InMemoryLineProvider` alongside printers.

### Dependencies
- Requires `LineConfig` update (new `Lanes` parameter) — non-breaking if `IEnumerable<LaneDef>? lanes = null` defaulting to empty.
- Feeds the fire-point PLC write bundle (arch-log 009 §3A, `vPA.Assign[i].Dest`); needed before eController adapter can write the Dest tag.
- Must coordinate with F21 (slot number) — both are PLC tag writes needed at the same time.
- `InductResult`/`VerifyOutcome` changes will touch `InductService` and `VerifyStationService`.

### Acceptance Criteria

1. **Pass-exact**: Given a lane `{ LaneId="Verify - Pass", LaneNumber=12 }` and inputs `cartonStatus="anything"`, `finalDestination="Verify - Pass"`, `Resolve` returns `12`.
2. **Status suffix match**: Given lane `{ LaneId="Verify - FAIL", LaneNumber=10 }` and `cartonStatus="Verify - FAIL: No Read"`, `finalDestination="other"`, `Resolve` returns `10` (suffix rule, `cartonStatus LIKE '%' + LaneId`).
3. **Status prefix match**: Given lane `{ LaneId="Verify - Pass", LaneNumber=12 }` and `cartonStatus="Verify - Pass"`, `finalDestination="other"`, `Resolve` returns `12` (prefix rule).
4. **REJECT fallback**: Given no lane matches status or destination but a lane `{ LaneId="REJECT", LaneNumber=0 }` is configured, `Resolve` returns `0`.
5. **No-match null return**: Given no lane matches any rule and no REJECT lane is configured, `Resolve` returns `null`.
6. **Round-robin (oldest-first)**: Given two lanes with the same `LaneId="Verify - Pass"` — one with `LastDiverted` = 5 minutes ago, one with 10 minutes ago — `Resolve` returns the LaneNumber of the one diverted 10 minutes ago (oldest, i.e. minimum timestamp wins).
7. **NULL `LastDiverted` loses round-robin** (source behavior, not intuitive): A lane with `LastDiverted=null` is treated as `GETDATE()` (most recent), so it loses MIN selection against any historically-diverted lane. Document this explicitly; a test asserts a never-diverted lane is NOT selected over one diverted yesterday.
8. **`InductService` and `VerifyStationService` populate `DivertLane`**: After a successful induct on a line with lanes configured, `InductResult.DivertLane` is non-null.
9. **Mutation propagates**: After `Resolve` is called, the winning lane's `LastDiverted` is updated to `now`; calling `Resolve` again with the same inputs picks the other lane (round-robin).
10. **Multi-PandA scoping**: A line with `LineId="L2"` using different lane numbers does not affect the routing result for `LineId="L1"`.

### Open Questions for Domain Owner

1. **Who updates `LastDiverted`?** The source proc only reads — it never writes the timestamp back. Which proc writes it, and when (at induct assignment time, or at physical divert confirmation from the PLC)? Without this the round-robin degrades to an arbitrary fixed pick.
2. **`NULL LastDiverted` intent**: The `ISNULL(LastDiverted, GETDATE())` makes unused lanes compete *last* in the MIN selection. Is this intentional? Should a never-diverted lane be preferred (oldest) or is the current behavior correct?
3. **REJECT lane absence from seed**: The production seed has no REJECT row. Is REJECT always provisioned at install time? What should the C# behavior be when REJECT is also absent — return `null` or throw a configuration exception?
4. **Multiple `LaneId` matches simultaneously**: What is the intended priority if `@FinalDestination` matches *and* the status prefix/suffix also matches a different LaneId? The `TOP 1` on the CTE is non-deterministic. Should a priority order be established (`FinalDestination > suffix > prefix`)?

---

## LINECTRL — Line/Zone Control Egress (Shut/Slow the Conveyor)

### Source

- `sdisp_TOOL_PA_ShutLineDown.sql` (L8–95) — sends `vConv.ZoneAr[n].RemoteStop = 1` to PLC
- `sdisp_TOOL_PA_SlowLineDown.sql` (L8–95) — sends `vPanda.SlowPanda[n].SlowFlag = 1` to PLC
- `sdisp_TOOL_PA_ShutZoneDown.sql` (L8–65) — iterates all pandas in a zone, calls ShutLineDown for each
- Called from: `sdisp_PA_Status_Zone.sql:L65–79` (ShutZoneDown when ZoneStatus=0); `sdisp_PA_LaneEval` (ShutLineDown/SlowLineDown on lane-eval decision — not in available files but inferred from LaneEvalService docs)
- Config source: `PandAs` table (`5.0_CreateTables/PandAs.sql`): `PLCZone INT`, `SorterPLCRecID BIGINT`, `PLCDBName VARCHAR(64)`, `PandaID VARCHAR(32)`

### Behavior

**ShutLineDown** (`sdisp_TOOL_PA_ShutLineDown.sql:L46–76`):
1. Reads `PLCZone` (the zone array index) and `PLCDBName` from `PandAs WHERE PandaID = @PandaID` (L47–54).
2. Builds tag name: `'vConv.ZoneAr[' + PLCZone + '].RemoteStop'` (L57).
3. Writes `DB2VLC` row to the PLC cross-DB table (L61–74):
   ```
   SorterID = 'CSS'
   TagName  = 'vConv.ZoneAr[{PLCZone}].RemoteStop'
   TagIndex = 0
   TagValueNum = 1     ← write value 1 (stop)
   ProcessFlag = 1
   BitSize = 16
   IsNum = 1
   TagValueString = NULL
   ```
   The target database is `@PLCDBName.dbo.DB2VLC` (resolved dynamically via `sp_executesql`).

**SlowLineDown** (`sdisp_TOOL_PA_SlowLineDown.sql:L47–74`):
1. Reads **`SorterPLCRecID`** (not `PLCZone`) and `PLCDBName` from `PandAs` (L49–54).
2. Builds tag name: `'vPanda.SlowPanda[' + SorterPLCRecID + '].SlowFlag'` (L58).
3. Writes the identical DB2VLC row format with TagValueNum=1.

**Critical distinction:** ShutLineDown indexes into `vConv.ZoneAr` using `PLCZone`; SlowLineDown indexes into `vPanda.SlowPanda` using `SorterPLCRecID`. These are different PLC tag namespaces, different array indices, and use different columns from `PandAs`. Both exist on the `PandAs` table simultaneously.

**ShutZoneDown** (`sdisp_TOOL_PA_ShutZoneDown.sql:L38–63`):
1. Counts pandas in the zone via `PandaDetails WHERE AttributeName='PandaZone' AND AttributeValue=CAST(@PandaZone AS VARCHAR)` (L39–46).
2. Iterates with a WHILE loop. **There is a bug at L52–54**: `SELECT PandaID = @PandaID FROM PandAs WHERE RecID = @inc` sets the column *alias* — `@PandaID` is never actually assigned. The correct T-SQL is `SELECT @PandaID = PandaID`. In the SQL version, `@PandaID` stays blank/empty on every iteration, meaning `ShutLineDown` is called with an empty PandaID. The C# port should fix this.
3. Correct intent: for each PandaRecID in the zone (ascending from 1 to count), call ShutLineDown with the corresponding `PandaID`.

**DB2VLC write framing (from arch-log 009 §3):** `sdisp_DB2VLC_Send` (`SHA b84e8b5a`) attempts `CAST(TagValue AS INT)` — if numeric, writes `TagValueNum`; otherwise writes `TagValueString`. Both ShutLine and SlowLine pass numeric `1`, so `IsNum=1` and `TagValueNum=1` are correct. The OPC bridge polls `DB2VLC_CE[1–6]` tables and writes to Allen-Bradley PLC tag memory. In `sdisp_PA2BP_SendPrinterFirePoints` (L242–393) the static if-else dispatches PandaRecID 1–6 to separate tables; the same pattern applies for line control.

**What triggers egress?** `LaneEvalService.Evaluate` already returns `LaneEvalResult.Control` = `SlowLine`, `ShutLine`, or `ShutZone`. The egress contract — calling `ShutLineDown` or `SlowLineDown` — is the **missing downstream step** after the lane-eval decision is produced. Currently `SimHost.RunLaneEval` logs the control decision but takes no action beyond that (CS:SimHost.cs:L183–188 — loop only calls `_laneEval.Evaluate`).

### Already Built in C#?

**Decision side: built.** `LaneEvalService` in `PandA.Core` returns `LineControl.ShutLine/SlowLine/ShutZone` (CS:LaneEvalResult.cs:L4–17). `SimHost` exposes `SetPrinterStatus`/`SetZoneStatus` and logs the result.

**Egress side: NOT built.** There is no `ILinePlcGateway` (or equivalent) in `PandA.Core`. The backlog item "BluePaw stop-line / slow-line tag (codes TBD)" was marked "blocked on codes." **The codes are now known from the source SQL.** This spec unblocks that backlog item.

`PrinterGroupPolicy` (`AllowDegraded`, `SlowLineFloor`) already exists in `PandA.Core`. `LineConfig.PrinterPolicies` carries the policies. `ZoneState` on `SimHost` drives `ShutZone`.

### Target Module/Class

| Concern | C# Home |
|---------|---------|
| Egress port (pure abstraction) | `PandA.Core.ILinePlcGateway` — single method `ValueTask SendLineControlAsync(LineControlCommand cmd, CancellationToken ct = default)` |
| `LineControlCommand` value object | `PandA.Core.LineControlCommand(LineControl Control, string LineId, int PlcArrayIndex, string PlcTagNamespace)` — carries the resolved tag params |
| Line PLC config | Add `int PlcZone` + `int SorterPlcRecId` + `string PlcDbName` to `LineConfig` (or a new `LinePlcConfig` sub-record) — maps from `PandAs.PLCZone`, `.SorterPLCRecID`, `.PLCDBName` |
| Sim stub | `PandA.Sim.CapturingLinePlcGateway : ILinePlcGateway` — records commands, no I/O |
| Wire into `SimHost` | Call `ILinePlcGateway.SendLineControlAsync` immediately after `LaneEvalService.Evaluate` returns non-`Balanced` |
| Egress adapter (deferred) | `PandA.EController.LinePlcGateway` — writes `DB2VLC_CE[n]` row; OPC bridge concern; deferred |

**Tag-name resolution** belongs in `PandA.Core` as a pure function:
```
ShutLine  → "vConv.ZoneAr[{LineConfig.PlcZone}].RemoteStop"
SlowLine  → "vPanda.SlowPanda[{LineConfig.SorterPlcRecId}].SlowFlag"
ShutZone  → iterate all lines in the zone, emit ShutLine per line
```

Namespace: `PandA.Core` for port + command; `PandA.Sim` for the capturing stub; `PandA.EController` for the real DB write (deferred).

### Dependencies
- Requires `LaneEvalService` (already built, `PandA.Core`).
- Requires `LineConfig` update: add `PlcZone int`, `SorterPlcRecId int`, `PlcDbName string` (new optional ctor params, defaulting to 0/"" for tests that don't exercise egress).
- `SimHost` update: inject `ILinePlcGateway` and call it after lane-eval.
- F13 (status ingestion) triggers lane-eval which triggers egress — must be built together or in sequence.

### Acceptance Criteria

1. **ShutLine tag name**: For a line with `PlcZone=3`, `Evaluate(…) → ShutLine` causes `ILinePlcGateway` to receive a command whose `TagName == "vConv.ZoneAr[3].RemoteStop"` and `TagValue == 1`.
2. **SlowLine tag name**: For a line with `SorterPlcRecId=2`, `Evaluate(…) → SlowLine` causes `TagName == "vPanda.SlowPanda[2].SlowFlag"` and `TagValue == 1`.
3. **ShutZone fans out**: For a zone containing lines L1 (PlcZone=1) and L2 (PlcZone=2), `Evaluate(…) → ShutZone` emits exactly two `RemoteStop` commands — one per line — not a `SlowFlag`.
4. **Balanced emits nothing**: `Evaluate(…) → Balanced` does not call `ILinePlcGateway` at all.
5. **ShutZone does not invoke SlowLine**: A `ShutZone` result never emits a `SlowFlag` command; it only emits `RemoteStop`.
6. **ShutLine does not use `SorterPlcRecId`**: Verify `ShutLine` uses `PlcZone`, not `SorterPlcRecId`, as the array index (these are different columns from `PandAs`; a mis-wiring is the most likely source-fidelity bug).
7. **SlowLine does not use `PlcZone`**: Verify `SlowLine` uses `SorterPlcRecId`, not `PlcZone`.
8. **Capturing stub records commands in order**: A `SlowLine` followed by a subsequent `ShutLine` appears in the stub's captured list in that order.
9. **ShutZone source-bug fixed**: The C# ShutZone implementation correctly iterates over actual PandaIDs for the zone (not empty string); assert each emitted `RemoteStop` command carries a non-empty `LineId`.
10. **Gateway called exactly once per orientation group decision, not once per printer**: The egress is a line-level action; verify only one command is emitted even when a line has multiple printers in the failing orientation group.

### Open Questions for Domain Owner

1. **Integer values for Shut/Slow tags**: The source always writes `TagValueNum = 1`. Is the stop/slow signal cleared by writing `0`? What is the expected clear sequence (does the PLC self-clear, or must PandA explicitly clear after the line recovers)?
2. **ShutZone vs ShutLine scope**: `ShutZoneDown` iterates pandas ordered by `RecID` from 1 to `PandaCount`. But the WHERE clause queries `PandaDetails WHERE PandaZone = n` for the *count*, then selects from `PandAs WHERE RecID = @inc` for the actual PandaID. This means it picks pandas by RecID (1, 2, 3…) regardless of which zone they belong to. Is this correct behavior or is it a source bug?
3. **OPC tag ownership**: The write to `DB2VLC_CE[n]` is the BluePaw/OPC path. The C# adapter will not write SQL tables directly — it will use the ADS BluePaw connector NuGet. Confirm the tag names `vConv.ZoneAr[n].RemoteStop` and `vPanda.SlowPanda[n].SlowFlag` are the actual PLC tag path strings accepted by the connector.

---

## F13 — PrintEngine Status Ingestion (Zebra TCP → `EngineOnline`)

### Source

**Wire entry points (BP2PA tier — PLC-side handlers):**
- `sdisp_BP2PA_Status_Printer.sql` — msg 283, PLC→PandA printer status (L1–140)
- `sdisp_BP2PA_Status_Zone.sql` — msg 284, PLC→PandA zone status (L1–87)

**Domain handlers (PA tier):**
- `sdisp_PA_Status_Printer.sql` (L1–98) — updates `PrinterState.PLCStatus` and calls LaneEval
- `sdisp_PA_Status_Zone.sql` (L1–97) — updates `PandaState.ZoneStatus` and calls ShutZoneDown

**Print-engine status (separate TCP path — not from PLC):**
- `sdisp_PA_Status_PrintEngine.sql` (L1–491) — parses 3-message Zebra status reply, stores in `PrintEngineStatus`
- Table DDL: `5.0_CreateTables/PrintEngineStatus.sql` (27 columns, L1–39)
- **The mapping from `PrintEngineStatus` → `PrinterState.EngineStatus` is not present in the available source.**

### Behavior

#### Sub-feature F13a — PLC Printer Status (msg 283)

**Call chain (msg 283):**
```
PLC wire <283,SourceMode,SorterNumber,PrinterNumber,PrinterStatus>
  → sdisp_BP2PA_Status_Printer(@DBName, @PrinterID, @PrinterStatus INT, @SorterNumber)
      → resolve PandaID from PandAs WHERE PLCDBName=@DBName AND SorterPLCRecID=@SorterNumber (L51–57)
      → fallback: try SorterPLCRecID=1 if not found (L62–70)
      → bail with error if still not found (L71–88)
      → EXEC sdisp_PA_Status_Printer(@PandaID, @PrinterID, @PrinterStatus) (L102–107)
          → resolve PrinterRecID: SELECT RecID FROM Printers WHERE PandaRecID AND PrinterID (L50–51)
          → UPDATE PrinterState SET PLCStatus=@PrinterStatus, LastStatusUpdate=NOW WHERE PrinterRecID (L53–57)
          → EXEC sdisp_PA_LaneEval (L60–62)
```

**`@PrinterStatus` semantics:** The SQL column `PrinterState.PLCStatus` is `INT NULL` (`5.0_CreateTables/PrinterState.sql:L11`). The wire field is also INT. **The mapping from integer to online/offline is not defined in the available files** — it is an open question (see below). The C# `PrinterState.PlcOnline` is `bool`, so the adapter must map `int → bool`.

**`@ZoneStatus` semantics** (msg 284, `sdisp_PA_Status_Zone.sql:L64`): `IF @ZoneStatus = 0 BEGIN EXEC ShutZoneDown END`. **0 = zone offline, non-zero = zone online.** This is explicit. `ZoneState.ZoneOnline` in C# is a `bool`, so map: `ZoneOnline = (ZoneStatus != 0)`.

#### Sub-feature F13b — PLC Zone Status (msg 284)

**Call chain (msg 284):**
```
PLC wire <284,SourceMode,ZoneNumber,ZoneStatus>
  → sdisp_BP2PA_Status_Zone(@DBName, @ZoneNumber, @ZoneStatus) 
      → EXEC sdisp_PA_Status_Zone(@ZoneID=@ZoneNumber, @ZoneStatus)
          → UPDATE PandaState SET ZoneStatus=@ZoneStatus WHERE PandaRecID IN (pandas in zone) (L38–50)
          → IF @ZoneStatus = 0: EXEC ShutZoneDown (L64–78) — triggers LINECTRL egress
          → LOG event (L52–79)
```

Note: `sdisp_PA_Status_Zone` calls `ShutZoneDown` directly. In the C# port, the equivalent flow is: receive zone status → update `ZoneState.ZoneOnline` → call `LaneEvalService.Evaluate` → if `ShutZone`, call `ILinePlcGateway`. The direct `ShutZoneDown` call in the SQL source is subsumed by the LaneEval → egress chain already designed.

#### Sub-feature F13c — Zebra PrintEngine Status (TCP path, 3-message protocol)

**Context:** After a label is printed (and optionally when `~HS` is appended per F12), the Zebra printer sends back 3 status messages over the TCP connection. These are received by the platform's TCP listener and routed to `sdisp_PA_Status_PrintEngine(@IPAddress, @PortNumber, @DataMessage, @ResponseID)`.

**Message discrimination** (`sdisp_PA_Status_PrintEngine.sql:L97–195`): Comma count in `@DataMessage` determines which of the 3 messages is being parsed.

| CommaCount | Variable | Message | Fields | Parse style |
|-----------|----------|---------|--------|------------|
| 11 (`@Message01Length`) | `@CommaNum == 11` | **Message 01** | `CommSettings(3), FlagPaperOut(1), FlagPause(1), LabelLength(4), NumFormats(3), FlagBufferFull(1), FlagCommDiag(1), FlagPartFormat(1), ConstUnused_1(3), FlagBadRAM(1), TempRangeLo(1), TempRangeHi(1)` | SUBSTRING positional parse (L101–149) |
| 10 (`@Message02Length`) | `@CommaNum == 10` | **Message 02** | `FunctSettings(3), ConstUnused_2(1), FlagHeadUp(1), FlagRibbonOut(1), FlagThermTransMode(1), PrintMode(1), PrintWidthMode(1), FlagLabelWait(1), RemainingLabels(8), FlagFormatOnFly(1), NumGraphicsInMem(3)` | SUBSTRING positional (L150–195) |
| 1 (`@Message03Length`) | `@CommaNum == 1` | **Message 03** | `Pswd(4), StaticRAM(1)` | SUBSTRING positional (L196–205) |

Example messages from the source header (L27–42):
```
Message 01: '014,0,1,1237,000,0,0,0,000,1,0,0'   (12 values, 11 commas)
Message 02: '001,0,1,0,1,4,6,0,00100000,1,000'   (11 values, 10 commas)
Message 03: '1234,0'                               (2 values, 1 comma)
```

**`FlagLidOpen` derived field** (`sdisp_PA_Status_PrintEngine.sql:L207–223`):
```
FlagLidOpen = (FlagPause > 0) AND NOT (FlagHeadUp > 0 AND FlagPaperOut > 0 AND FlagRibbonOut > 0)
```
That is: if the printer is paused *and* it's NOT the combination of all three physical fault flags simultaneously, the lid is considered open. Stored only on Message 01 insert.

**DB persistence protocol** (`sdisp_PA_Status_PrintEngine.sql:L387–490`):
- **Message 01**: Deactivate all existing active rows for the printer (`UPDATE SET Active=0`), then INSERT a new active row. All Message 01 fields are stored; Message 02 and 03 fields default NULL.
- **Message 02**: UPDATE the existing active row (`WHERE Active=1 AND PrinterRecID=…`) with the Message 02 fields. The row must already exist from Message 01.
- **Message 03**: UPDATE the existing active row with Pswd and StaticRAM.

**`PrinterRecID` lookup**: `SET @PrinterRecID = dbo.sdiudf_PA_GetPrinterRecIDFromConnections(@IPAddress, @PortNumber)` (L385). The UDF resolves printer by IP+port. This is how the Zebra status is correlated to the correct printer record.

**`EngineOnline` derivation — THE GAP**: `sdisp_PA_Status_PrintEngine` **only writes `PrintEngineStatus`**. It does NOT update `PrinterState.EngineStatus`. There is no visible proc in the provided source that reads `PrintEngineStatus` and writes back to `PrinterState.EngineStatus`. The C# port must define this mapping. The recommended fault-based derivation (domain owner confirmation needed):

```
EngineOnline = !(FlagPaperOut > 0 || FlagHeadUp > 0 || FlagRibbonOut > 0)
```

`FlagPause` alone (operator-set pause) is ambiguous — it may represent deliberate operator intervention, not a physical fault. The C# port should expose a configurable policy for this. `FlagBadRAM`, `TempRangeLo`, `TempRangeHi` are errors but arguably non-fatal for routing purposes. Treating the three physical-fault flags as the `EngineOnline=false` gates is the most defensible baseline.

**`PrintMode` codes** (Message 02, `sdisp_PA_Status_PrintEngine.sql:L308–355`):
`0`=Rewind, `1`=Peel Off, `2`=Tear Off, `3`=Cutter, `4`=**Applicator** (expected for PandA), `5`=Delayed Cut, `6`=Linerless Peel, `7`=Linerless Rewind, `8`=Partial Cutter, `9`=RFID, `K`=Kiosk, `S`/`A`=Kiosk Cutstream.

### Already Built in C#?

**Partially.**

| Component | Built? |
|-----------|--------|
| `PrinterState.PlcOnline` / `EngineOnline` bool fields | Yes — CS:PrinterState.cs:L8–27 |
| Dynamic update from runtime signal | **Not built**: `PlcOnline`/`EngineOnline` are set at startup only |
| `ZoneState.ZoneOnline` | Yes — CS:ZoneState.cs:L8–20 |
| Dynamic zone-status update | **Stubbed**: `SimHost.SetZoneStatus` sets `_zone.ZoneOnline` directly (CS:SimHost.cs:L191–200) — no wire parse |
| PLC printer status handler (msg 283 wire parse) | **Not built** — SimHost stubs directly |
| PLC zone status handler (msg 284 wire parse) | **Not built** — SimHost stubs directly |
| `PrintEngineStatus` C# model | **Not built** |
| Zebra TCP status message parser | **Not built** |
| `EngineOnline` derivation from parsed status | **Not built** |
| Lane-eval trigger after status update | Yes — `SimHost.SetPrinterStatus` calls `RunLaneEval` (CS:SimHost.cs:L183–188) |

### Target Module/Class

| Concern | C# Home |
|---------|---------|
| PLC msg 283/284 frame records | `PandA.Sim.Messaging.PrinterStatusMessage` + `ZoneStatusMessage` (alongside existing `InductScanMessage`/`VerifyScanMessage` in `PaMessages.cs`) |
| Msg 283 handler (pure logic) | `PandA.Core.PrinterStatusHandler.Handle(string pandaId, int printerId, int plcStatus) → PlcOnline: bool` — maps int → bool |
| Msg 284 handler (pure logic) | `PandA.Core.ZoneStatusHandler.Handle(int zoneId, int zoneStatus) → ZoneOnline: bool` — maps int → bool |
| Zebra 3-message parser | `PandA.Core.PrintEngineStatusParser.Parse(string dataMessage) : PrintEngineStatusRecord` — pure, no I/O |
| `PrintEngineStatusRecord` value object | `PandA.Core.PrintEngineStatusRecord` — 27 parsed flag fields matching `PrintEngineStatus` table columns, plus derived `FlagLidOpen` |
| `EngineOnline` derivation | `PrintEngineStatusRecord.IsEngineOnline` computed property: `!(FlagPaperOut || FlagHeadUp || FlagRibbonOut)` — domain owner must confirm |
| `PrintEngineStatusStore` | `PandA.Core.IPrintEngineStatusStore` port — write (upsert) + read by PrinterRecID; `PandA.Sim.InMemoryPrintEngineStatusStore` |
| Wire into `SimHost` | Add `HandlePrinterStatusAsync(string pandaId, int printerId, int plcStatus)` and `HandlePrintEngineStatusAsync(string ip, int port, string raw)` — drives the core handlers then lane-eval |

Namespace: `PandA.Core` for pure logic; `PandA.Sim` for wire parsing + in-memory store.

### Dependencies
- F13c `PrintEngineStatusParser` is self-contained — can be built and unit-tested independently.
- F13a/F13b handlers call `LaneEvalService` (already built) and then `ILinePlcGateway` (LINECTRL — must be built in the same or prior slice).
- The printer IP+port → PrinterRecID lookup (`sdiudf_PA_GetPrinterRecIDFromConnections`) must be modeled in the C# printer-config lookup (already partially present via `PrinterConfig.Ip`/`.Port`).

### Acceptance Criteria

1. **`PrintEngineStatusParser` — Message 01 round-trip**: Given `"014,0,1,1237,000,0,0,0,000,1,0,0"`, parse returns `CommSettings="014"`, `FlagPaperOut="0"`, `FlagPause="1"`, `LabelLength="1237"`, `FlagBadRAM="1"`. (Source example, L27–29.)
2. **`PrintEngineStatusParser` — Message 02 round-trip**: Given `"001,0,1,0,1,4,6,0,00100000,1,000"`, parse returns `FunctSettings="001"`, `FlagHeadUp="1"`, `PrintMode="4"` (Applicator), `RemainingLabels="00100000"`.
3. **`PrintEngineStatusParser` — Message 03 round-trip**: Given `"1234,0"`, parse returns `Pswd="1234"`, `StaticRAM="0"`.
4. **Message discrimination by comma count**: `"a,b"` (1 comma) → Message 03; `"a,b,c,d,e,f,g,h,i,j,k"` (10 commas) → Message 02; wrong comma count → `FormatException` or `MessageTypeUnknown` discriminated case.
5. **`FlagLidOpen` derivation**: `FlagPause=1, FlagHeadUp=0, FlagPaperOut=0, FlagRibbonOut=0` → `FlagLidOpen=true`. `FlagPause=1, FlagHeadUp=1, FlagPaperOut=1, FlagRibbonOut=1` → `FlagLidOpen=false`. `FlagPause=0` → `FlagLidOpen=false` regardless of other flags.
6. **`IsEngineOnline` fault logic**: `FlagPaperOut=1` alone → `EngineOnline=false`. `FlagHeadUp=1` alone → `EngineOnline=false`. All flags `0` → `EngineOnline=true`.
7. **Zone status `0` → zone offline**: `ZoneStatusHandler.Handle(zoneId=1, zoneStatus=0)` returns `ZoneOnline=false`; any non-zero returns `ZoneOnline=true`.
8. **Printer status update triggers lane-eval**: Calling `HandlePrinterStatusAsync` with a value that drives `PlcOnline=false` on the only non-spare active printer results in a `LaneEvalResult` with `Control=ShutLine` (given a non-degraded policy) and a `LineControlCommand` being sent to `ILinePlcGateway`.
9. **`PrintEngineStatus` multi-message accumulation**: After a Message 01 insert followed by a Message 02 update, the stored record has Message 01 fields populated and Message 02 fields populated; the `Active` flag is `true` on exactly one record per printer.
10. **Unknown printer IP/port**: `HandlePrintEngineStatusAsync` with an IP+port matching no configured printer logs a fault but does not throw; `EngineOnline` state is not changed.

### Open Questions for Domain Owner

1. **`PLCStatus` integer values**: What int values does msg 283 carry? Is it `0=offline, 1=online`? Are there intermediate states (fault, warning)? The SQL stores the raw int to `PrinterState.PLCStatus` without any integer→string mapping in the available source.
2. **`EngineStatus` integer values and update mechanism**: The `PrinterState.EngineStatus` column exists (`INT NULL`) but nothing in the available source writes it from `PrintEngineStatus`. What process (trigger, scheduled proc, or the TCP handler itself) was supposed to derive `EngineStatus` from `PrintEngineStatus`? Is the rule `!(PaperOut || HeadUp || RibbonOut)` correct?
3. **`FlagPause` and `EngineOnline`**: A paused printer can't print — should operator-set `FlagPause` set `EngineOnline=false` or is it treated as a temporary known-offline state that should NOT trigger spare promotion?
4. **Engine status + PLC status independence**: If `PlcOnline=true` but `EngineOnline=false` (Zebra fault, PLC thinks printer is fine), lane-eval fires. Is this the intended behavior? The source checks both columns (`PLCStatus AND EngineStatus` per arch-log 012 §3), confirming yes.
5. **Three-message ordering guarantee**: Does the platform guarantee Message 01 always arrives before 02 before 03, or can they arrive out of order (requiring the `Active` row upsert to be more defensive)?

---

## F12 — Printer Status Suffix (~HS ZPL Command)

### Source
- `sdisp_TOOL_PA_AppendStatusSuffix.sql` (L8–49) — full proc
- Key line: L30 `DECLARE @StatusSuffix VARCHAR(32) = '~HS'`; L33 `SET @LabelString = @LabelString + @StatusSuffix`
- Setting: `6.0_PopulateTables/Settings.sql:L18` — `PrinterStatusSuffix`, `RecID=11`, default `'0'` (OFF). Description: `'Determines if we will attach the ~HS command onto the label for printer status'`.
- Called from: not explicitly visible in the files reviewed — implicitly called from the print path before TCP send, gated by the setting.

### Behavior

**Pure transformation:** Appends the 3-character string `~HS` to the ZPL payload string. No parameters other than the string (passed as `OUTPUT`). No side effects.

**`~HS` semantics (ZPL):** The `~HS` command instructs the Zebra printer to immediately transmit its hardware status back to the host over the TCP connection. The platform's TCP listener receives the response and routes it to `sdisp_PA_Status_PrintEngine` (F13c). This creates the feedback loop:
```
Print ZPL + ~HS → Zebra printer → TCP status reply (3 messages) → PrintEngine parser → EngineOnline update → LaneEval
```

**Gating:** The setting `PrinterStatusSuffix=0` (default OFF) means in a fresh install, `~HS` is NOT appended and no status feedback is requested. When `PrinterStatusSuffix=1`, every print job requests a status reply. This is significant: F12 is the *trigger* for F13c in the production data path. Without F12 enabled, `PrintEngineStatus` is never populated from live prints.

**Where in the print pipeline:** The suffix is appended to the complete ZPL string just before the TCP send. In the SQL source this is just before `EXEC sdisp_PA2TCP_SendTCPData` (the TCP send proc, `sdisp_PA2TCP_SendTCPData.sql` L76–89 shows it inserts `@LabelData` directly to the TCP queue). Therefore F12 must execute after ZPL assembly but before `IPrinterGateway.SendAsync`.

### Already Built in C#?

**Not built.** No equivalent of `AppendStatusSuffix` exists anywhere in `PandA.Core` or `PandA.Sim`. `IPrinterGateway.SendAsync` receives a `PrintJob` with a `Zpl` string — there is no hook to append `~HS` before that string is sent. The setting `PrinterStatusSuffix` is not modeled in `LineConfig` or anywhere in C#.

### Target Module/Class

| Concern | C# Home |
|---------|---------|
| Pure append function | `PandA.Core.ZplStatusSuffix` — static `string Append(string zpl) => zpl + "~HS"`. Possibly folded into `ZplBuilder` if one exists. |
| Setting flag | Add `bool PrinterStatusSuffix` to `LineConfig` (or a `PrinterSettings` sub-record alongside existing settings) |
| Call site | `InductService.InductAsync`, after `PrintJob` is assembled and before `IPrinterGateway.SendAsync` — if `line.PrinterStatusSuffix`, append to `job.Zpl` |
| Integration note | `~HS` appended to the string that `IPrinterGateway.SendAsync` receives. The `CapturingPrinterGateway` in Sim will record the suffix; tests can assert its presence/absence. |

Namespace: `PandA.Core` entirely. No adapter concern — the suffix is part of the ZPL payload.

### Dependencies
- Requires `LineConfig` update: `bool PrinterStatusSuffix` (default `false`).
- F12 produces the status request; F13c (PrintEngine parser) is the response handler. They are independent code paths but operationally linked — both should be delivered together or F12 should be gated until F13c is ready to process the replies.
- `InductService` is the call site; no other services need changes.

### Acceptance Criteria

1. **Append function purity**: `ZplStatusSuffix.Append("^XA^FO50,50^ADN,36,20^FDHELLO^FS^XZ")` returns `"^XA^FO50,50^ADN,36,20^FDHELLO^FS^XZ~HS"`.
2. **Append is idempotent-not**: Calling `Append` twice produces `"…~HS~HS"` — the proc does not guard against double-append; this is source-faithful (F12 must only be called once per print).
3. **Setting=false → no suffix**: `InductService.InductAsync` on a line where `PrinterStatusSuffix=false` dispatches a `PrintJob` whose `Zpl` does NOT end with `~HS`.
4. **Setting=true → suffix appended**: `InductService.InductAsync` on a line where `PrinterStatusSuffix=true` dispatches a `PrintJob` whose `Zpl` ends with `~HS`.
5. **Multi-label print**: When an induct dispatches 3 `PrintJob`s (one per label), all 3 `Zpl` strings end with `~HS` when the setting is on — each job requests its own status reply.
6. **Suffix position**: `~HS` is appended at the very end of the ZPL string, after `^XZ` (the ZPL end-of-format command). Appending before `^XZ` would cause a ZPL parse error on some firmware; this must be tested by asserting the suffix is the last 3 characters.
7. **`PrintEngineStatusParser` handles the returned messages**: A Message 01 string with 11 commas correctly identifies all fields (integration-readiness test for F12→F13c chain).

### Open Questions for Domain Owner

1. **`~HS` placement within ZPL**: The source simply concatenates. Should `~HS` appear before or after the final `^XZ`? Some Zebra documentation recommends `~HS` before `^XZ`; the source appends unconditionally.
2. **Per-printer vs per-line setting**: The `PrinterStatusSuffix` setting in the source is a global setting (`Settings` table — not per-printer or per-line). Should the C# model be global, per-line, or per-printer?
3. **Status polling standalone**: Can `~HS` be sent as a standalone command (empty ZPL body — just `~HS`) for a status poll without a print job? The source only appends it to label data; confirm whether standalone polling is needed.

---

## Cross-Cutting Notes

### Call Chain Summary (for Coder agent sequencing)

```
[Msg 283 / SetPrinterStatus]
  → PrinterState.PlcOnline = (status != 0)       ← F13a
  → LaneEvalService.Evaluate(line, states, zone)  ← already built
  → if Control != Balanced:
      ILinePlcGateway.SendLineControlAsync(…)     ← LINECTRL

[Msg 284 / SetZoneStatus]
  → ZoneState.ZoneOnline = (status != 0)          ← F13b
  → LaneEvalService.Evaluate(…) → ShutZone        ← already built
  → ILinePlcGateway.SendLineControlAsync(…)        ← LINECTRL

[Zebra TCP Status Reply]
  → PrintEngineStatusParser.Parse(raw)             ← F13c
  → PrintEngineStatusStore.Upsert(record)
  → if Message01: derive EngineOnline from flags
  → PrinterState.EngineOnline = …
  → LaneEvalService.Evaluate(…)                    ← already built
  → if Control != Balanced: ILinePlcGateway        ← LINECTRL

[Print Job Dispatch]
  → ZplAssembly + (if PrinterStatusSuffix) Append "~HS"   ← F12
  → IPrinterGateway.SendAsync(job)                         ← already built
  → [Zebra sends TCP reply → F13c path above]

[InductService / VerifyStationService]
  → LaneRoutingService.Resolve(lanes, status, dest)  ← F08
  → InductResult.DivertLane / VerifyOutcome.DivertLane set
  → lanes[winner].LastDiverted = now (round-robin mutation)
```

### Recommended Build Order

| Slice | Rationale |
|-------|-----------|
| 1. F12 (status suffix) | Self-contained, 1 file, 1 test file. Unblocks F13c integration. |
| 2. F13c (PrintEngine parser) | Pure SUBSTRING parse, no dependencies. 27 fields; most testable in isolation. |
| 3. F13a/b (PLC printer + zone status handlers) + LINECTRL egress | Must be a single slice: the wire handlers trigger lane-eval which triggers egress. |
| 4. F08 (lane routing) | Completes the `InductResult`/`VerifyOutcome` divert-lane field needed by the eController adapter. |

### `LineConfig` Changes Summary (across all four features)

All four features require additions to `LineConfig`. A single PR touching `LineConfig`'s constructor signature is cleaner than four separate PRs. Proposed additions (all optional/defaulted to minimize breakage):
```csharp
bool PrinterStatusSuffix = false,               // F12
IEnumerable<LaneDef>? lanes = null,             // F08
int plcZone = 0,                                // LINECTRL ShutLine
int sorterPlcRecId = 0,                         // LINECTRL SlowLine
string plcDbName = ""                           // LINECTRL (deferred to EController adapter)
```
