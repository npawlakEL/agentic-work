# PandA Source-Coverage Spec Pass — Cluster 2

**Source commit-date snapshot:** `127.0.0.1_SDI_PandA_20260811`
**C# baseline:** `agentic-work` @ v0.5.0 / 231 tests green
**Scope:** SETTINGS/ENABLE · REPRINT RULES · XREF/oLPN · SLOT INDEX · PURGE · LOCKS/CONCURRENCY · WAVE AUTO-COMPLETE · MANDA MANUAL STATIONS · HOST ADVICE INGESTION

---

## SETTINGS-1 — Runtime Settings Infrastructure (`sdisp_TOOL_GetSetting` / `sdisp_TOOL_Enable`)

### Source
| File | Key lines | Target table/object |
|---|---|---|
| `8.0_CreateSP/sdisp_TOOL_GetSetting.sql` | 7–71 | `dbo.Settings` |
| `8.0_CreateSP/sdisp_TOOL_Enable.sql` | 11–37 | `DATABASE` DDL |
| `5.0_CreateTables/Settings.sql` | 9–22 | Schema |
| `6.0_PopulateTables/Settings.sql` | 1–28 | Seed values |

**Settings table schema:**
```sql
Settings(
  RecID        BIGINT IDENTITY(1,1),
  VariableName VARCHAR(32) NOT NULL PK CLUSTERED,
  VariableValue VARCHAR(32) NULL,
  CreationTime  DATETIME    DEFAULT GETDATE(),
  SettingDesc   VARCHAR(MAX),
  Display       BIT
)
```

### Behavior
`sdisp_TOOL_GetSetting` is a **get-or-create** operation: read `VariableValue` from `dbo.settings WHERE VariableName = @VariableName`; if absent (NULL or empty string), **DELETE any stale row then INSERT** with `@VariableValueDefault` as the value (`GetSetting.sql:43–65`). The row is created with the caller-supplied `@SettingDesc` and `@VisableOnWebScreen` flag. Any caller can therefore cause auto-creation of a setting just by calling `GetSetting` with a default. The NOLOCK hint (`GetSetting.sql:40`) means the read is dirty but the write is guarded by the `ISNULL → ''` check.

`sdisp_TOOL_Enable` is a **one-time database bootstrap** proc (`Enable.sql:17–36`): it puts the database into `SINGLE_USER`, re-creates the Service Broker endpoint (`NEW_BROKER`), sets `TRUSTWORTHY ON`, enables `ALLOW_SNAPSHOT_ISOLATION`, and enables `READ_COMMITTED_SNAPSHOT`, then returns to `MULTI_USER`. This is executed once during installation/migration; it is not a runtime call.

**Seed values (authoritative at fresh install):**

| RecID | VariableName | Seed Value | Proc-default if auto-created | Notes |
|---|---|---|---|---|
| 17 | `Reprint Labels` | **0** | 1 (in LookupCarton call) | Seed wins; default-deny reprints |
| 27 | `OverwriteLabelData` | 0 | 0 | Append/oldest-wins by default |
| 12 | `PrintExceptionLabels` | 0 | 1 | Seed overrides; no exception labels default |
| 1 | `DCMSExceptions` | 0 | 0 | Local exception labels |
| 9 | `MinGap` | 20 | 20 | Carton-gap threshold (units: PLC distance ticks) |
| 4 | `ForcedReplen` | 0 | 0 | Bypass all lookup logic |
| 5 | `HeightCheckLabelField` | 2 | 2 | Which Label slot carries height measurement |
| 6 | `HeightCheckEnabled` | 1 | – | Height sensor present |
| 8 | `LoadBalanceEnabled` | 0 | – | Multi-printer round-robin |
| 20 | `2 Printer Rule` | 0 | – | Remove spare state on 2-printer systems |
| 3 | `FilterLabels` | 1 | – | Strip ZPL config commands |
| 11 | `PrinterStatusSuffix` | 0 | – | Append `~HS` to ZPL for Zebra status reply |
| 13 | `PurgeSetting_ExceptionData` | 7 | 7 | Days to keep exception PandaData |
| 14 | `PurgeSetting_InactiveData` | **21** | 14 | Seed wins (21 days for inactive records) |
| 15 | `PurgeSetting_UnUsedData` | 14 | 14 | EventLog/PrintEngineStatus retention |
| 16 | `PurgeSetting_UsedData` | **7** | 21 | Seed wins (7 days for used/wave data) |
| 23 | `DynamicPrintPoint` | 1 | – | Height-driven apply-point selection |
| 24 | `DefaultHeight` | 10 | – | Default height when no sensor |
| 25 | `DefaultDimension` | 20 | 20 | Default box dimension |
| 26 | `EncoderResolution` | .2 | – | Inches/step-pulse |

> ⚠️ **Seed vs proc-default discrepancy:** `Reprint Labels` seed=0 but the `sdisp_PA_LookupCarton` auto-create default is '1'. `PurgeSetting_UsedData` seed=7 but purge proc default='21'. `PurgeSetting_InactiveData` seed=21 but purge proc default='14'. The **seed always wins** on a properly deployed database — the auto-create path is only a safety net on environments missing the seed.

### Already Built?
**Nothing.** No `ISettingsProvider` abstraction exists. All downstream logic (reprint gate, purge, gap check, exception labels, ZPL filter) is blocked on this infrastructure. The architecture stores static commissioning config in JSON (`PandaLine.json`/`PandaLabeling.json`) per decision-002 D1, but the Settings table is fundamentally different: it holds **runtime-mutable operator-adjustable operational parameters** that can be changed without redeployment. These are two orthogonal concerns.

### Target Module/Class
```
PandA.Core/Settings/ISettingsProvider.cs          — interface
PandA.Core/Settings/SettingsDescriptor.cs         — typed descriptor (name, default, description)
PandA.Core/Settings/KnownSettings.cs              — static registry of all known setting names/defaults
PandA.Sim/Settings/InMemorySettingsProvider.cs    — dictionary-backed, mutable (sim + test)
(deferred) PandA.EController/Settings/DbSettingsProvider.cs — reads dbo.Settings via EF/Dapper
```

`sdisp_TOOL_Enable` → no C# port needed; translate to an EF migration script or a `DatabaseInitializer` service in `PandA.EController` that is called once during startup.

### Dependencies
- Blocks: REPRINT RULES (F-ADV1), PURGE (F17), GAP-ERROR detection (F20), exception labels (F10), `FilterLabels` (F11), `PrinterStatusSuffix` (F12), `DynamicPrintPoint`, `MinGap`, `OverwriteLabelData`, `F-VF4CFG`
- `InductService` needs `ISettingsProvider` to read `MinGap`, `ReprintLabels`, `OverwriteLabelData`, `ForcedReplen`, `PrintExceptionLabels`
- `CartonAdviceService` needs `ISettingsProvider` to read `OverwriteLabelData` and `ReprintLabels` (F-ADV1)

### Acceptance Criteria
1. `ISettingsProvider.GetAsync<T>("Reprint Labels", defaultValue: false)` returns the seeded value `false` from an `InMemorySettingsProvider` seeded from the known-settings registry.
2. `InMemorySettingsProvider.SetAsync("Reprint Labels", true)` persists; a subsequent `GetAsync` returns `true`.
3. A setting absent from the provider returns the declared default value — no exception.
4. `KnownSettings` static registry contains all 20+ settings listed above with correct types and defaults.
5. `ISettingsProvider` is injected into `InductService` and `CartonAdviceService`; both compile and existing tests still pass.
6. `DbSettingsProvider` round-trips: write `VariableValue = '1'` to the in-memory SQL, `GetAsync` returns `true`.
7. Auto-create semantics: if the setting row doesn't exist in the DB, `DbSettingsProvider.GetAsync` inserts the default row and returns the default (mirrors `GetSetting.sql:43–65`).
8. `sdisp_TOOL_Enable` is represented as an idempotent EF migration script; running it twice does not throw.

### Open Questions
- Should the `Display=1` flag surface settings to an operator UI? If yes, `ISettingsProvider` needs a `ListVisibleAsync()` query.
- Are settings **per-line** or global? The source has a single settings table (global). Confirm with domain owner whether per-line overrides are ever needed (e.g., different `MinGap` per line).
- `VariableValue` is `VARCHAR(32)` — max 32 characters. Is this sufficient for all future settings? (Current values are small integers/booleans.)

---

## SETTINGS-2 — Reprint Rules Setting & Operator Reprint Authorization (`sdisp_PA_LookupCarton` + `sdisp_GUI_SetPrintedFlag`)

### Source
| File | Key lines | Target table/column |
|---|---|---|
| `8.0_CreateSP/sdisp_PA_LookupCarton.sql` | 105, 165–185 | `dbo.settings` → `@ReprintLabels` |
| `8.0_CreateSP/sdisp_PA_LookupCarton.sql` | 263, 308–453 | `PandaData.Printed`, `PandaData.ActiveRecord` |
| `8.0_CreateSP/sdisp_GUI_SetPrintedFlag.sql` | 7–41 | `PandaData.Printed`, `PandaData.ActiveRecord` |
| `6.0_PopulateTables/Settings.sql` | Line for RecID=17 | `Settings.VariableValue = '0'` |

### Behavior

**The global reprint-rules setting** (`'Reprint Labels'`, RecID 17, seed default `'0'`) is read on **every induct scan** via `sdisp_TOOL_GetSetting` (`LookupCarton.sql:173–178`). The proc-call default of `'1'` is a safety net only; the seeded value is `'0'` (deny).

**Gating logic in `sdisp_PA_LookupCarton` — the full decision tree** (`LookupCarton.sql:308–453`):

```
Printed = @PrintedFlag (from SELECT TOP 1 PandaData)
ActiveRecord = @ActiveRecord

Case A: ReprintLabels=0 AND PrintedFlag>0 (setting OFF, carton printed)
  - ActiveRecord=0 (printed+verified): CartonStatus = 'No Reprint'
    → "Label has already been printed & verified. Cancelling reprint." (L317)
  - ActiveRecord=1 (printed, still in flight): CartonStatus = 'No Reprint'
    → "Last Label has already been printed and is on the line. Cancelling print." (L337)

Case B: IsValid=0 (ProfileName absent or not in sdivw_LabelProfiles): CartonStatus = 'No Profile' (L349–367)

Case C: ReprintLabels=0, ActiveRecord=0, PandaDataID>0, PrintedFlag=0
  → CartonStatus = 'Inactive', "Record for carton is no longer active. Cancelling print." (L373)

Case D: ReprintLabels=1, ActiveRecord=0, PandaDataID>0 (setting ON, carton verified)
  → proceed to print, log "Record for carton is no longer active. Reprinting" (L440–453)
```

When `ReprintLabels=1`, ALL previously-verified cartons become eligible for reprint without any per-carton operator action — it is a global allow-all override.

**`sdisp_GUI_SetPrintedFlag`** (`SetPrintedFlag.sql:37–41`) — operator per-carton authorization mechanism:
```sql
UPDATE PandaData SET
    printed      = @PrintFlag,
    ActiveRecord = CASE WHEN @PrintFlag = 0 THEN 1 ELSE 0 END
WHERE PandaDataID = @RecID
```
- `@PrintFlag=0`: clears `Printed` (re-arms) AND sets `ActiveRecord=1` (marks active again) → enables one reprint
- `@PrintFlag=1`: sets `Printed` AND sets `ActiveRecord=0` (deactivates) → locks out reprinting
- No guards: any RecID can be toggled regardless of carton state or wave status

### Already Built?
**Partially.** `TransportOrder.AuthorizeReprint(reason)` + `CanPrint` gate covers the **per-carton** operator authorization path (decision-003). The **global `ReprintLabels` setting** is not wired in anywhere. `CartonAdviceService.AdviseAsync` calls `OverwriteAdvice` unconditionally (F-ADV1 gap per decision-004 AD-1).

**Exact gap:** `InductService` enforces `CanPrint` but does not check `ISettingsProvider.Get("Reprint Labels")`. When `ReprintLabels=1`, the induct gate should allow `Verified`/`HeldForIntervention` cartons to print without `AuthorizeReprint`. When `ReprintLabels=0` (default), the current behavior is correct: only `ReprintAuthorized` status passes.

**`SetPrintedFlag` gap:** The operator action exists in source as an explicit SQL mutation of `Printed`+`ActiveRecord`. In C# the `AuthorizeReprint(reason)` method on `TransportOrder` covers the "authorize" direction. The web-screen action that calls it is not built (backlog: Operator GUI item). Critically, `SetPrintedFlag @PrintFlag=1` (the "mark as done / lock out reprint" direction) has no C# equivalent yet.

### Target Module/Class
```
PandA.Core/Services/InductService.cs       — add ISettingsProvider injection; check ReprintLabels setting
PandA.Core/TransportOrder.cs              — add ForceMarkPrinted() or MarkAsNonReprintable() for the @PrintFlag=1 direction
PandA.Core/Services/CartonAdviceService.cs — gate OverwriteAdvice on IsReprintable (F-ADV1)
(deferred) PandA.EController/Actions/SetPrintedFlagAction.cs — web screen operator action
```

### Dependencies
- **Blocks F-ADV1**: requires `ISettingsProvider` (SETTINGS-1) to read `ReprintLabels`.
- Depends on `ISettingsProvider` being available at `InductService` construction.
- `SetPrintedFlag` direction (lock out) touches `TransportOrder` lifecycle — coordinate with decision-003.

### Acceptance Criteria
1. `InductService` with `ReprintLabels=true` setting allows a `Verified` carton to print without `AuthorizeReprint`.
2. `InductService` with `ReprintLabels=false` (default) blocks a `Verified` carton unless `AuthorizeReprint` was called — existing behavior unchanged.
3. `CartonAdviceService.AdviseAsync` with `ReprintLabels=false` and an already-`Verified` order does NOT call `OverwriteAdvice`; returns the existing order and sets `ErrorCode=1` or a `ReprintDenied` result.
4. `CartonAdviceService.AdviseAsync` with `ReprintLabels=true` and an already-`Verified` order calls `OverwriteAdvice` and re-arms the carton.
5. `TransportOrder.ForceMarkPrinted()` (or equivalent) transitions status to `Printed`, `ActiveRecord` semantics preserved, so a subsequent `CanPrint` check returns false.
6. The seed default (`ReprintLabels=0`) is correctly resolved from `KnownSettings`; no auto-create fires on a seeded DB.
7. `sdisp_GUI_SetPrintedFlag @PrintFlag=0` maps to `AuthorizeReprint(reason)` in all operator-GUI integration tests.
8. A `HeldForIntervention` carton with `ReprintLabels=1` can be inducted and printed (no `AuthorizeReprint` needed).

### Open Questions
- `SetPrintedFlag @PrintFlag=1` direction: domain owner confirmation needed — is "lock a carton out of reprinting via GUI" a valid operator action in the new system, or is it superseded by the wave/lifecycle model?
- `ReprintLabels` applies globally or per-line? Source is global (single settings table). If per-line is ever needed, the `ISettingsProvider` interface needs a line-scoped overload.
- F-ADV1 outcome type: should re-advice of a non-reprintable carton return a domain error, silently no-op, or raise an alert? Confirm with domain owner.

---

## F18/F24 — XRef Multi-Barcode + oLPN Association (`sdisp_TOOL_CUSTOM_LPNxRef` / `sdisp_TOOL_CUSTOM_LPNxRef_Disassociate`)

### Source
| File | Key lines | Target table |
|---|---|---|
| `8.0_CreateSP/sdisp_TOOL_CUSTOM_LPNxRef.sql` | 8–241 | `PandaDataXRef` |
| `8.0_CreateSP/sdisp_TOOL_CUSTOM_LPNxRef_Disassociate.sql` | 8–70 | `PandaDataXRef` |
| `5.0_CreateTables/PandaDataXRef.sql` | 9–39 | Schema + indexes |
| `8.0_CreateSP/sdisp_HI_DCMSCore_Inbound_PandALabels_Job.sql` | 323–383 | Initial xref population |
| `8.0_CreateSP/sdisp_PA_LookupCarton.sql` | 243–295 | Xref-based lookup at induct |

**PandaDataXRef schema:**
```sql
PandaDataXRef(
  RecID              BIGINT IDENTITY(1,1),
  PandaDataID        BIGINT NOT NULL,       -- FK → PandaData.PandaDataID
  Barcode            VARCHAR(64),
  BarcodeDescription VARCHAR(MAX),          -- 'BL' | 'UPC' | 'GTIN' | 'EAN' | 'ItemID' | 'oLPN'
  Priority           INT,
  PK(RecID, PandaDataID)
  IX_PandaDataXRef_Barcode (Barcode ASC)
  IX_PandaDataXRef_PandaDataID includes (Barcode, BarcodeDescription)
)
```

### Behavior

**XRef population during host ingest** (one record per PandaData row, `HI_Job.sql:323–383`):
- `('BL', BlindLPN, Priority=1)` — always inserted
- `('UPC', NonUniqueLPN1, 1)` — if NonUniqueLPN1 IS NOT NULL
- `('GTIN', NonUniqueLPN2, 1)` — if NonUniqueLPN2 IS NOT NULL
- `('EAN', NonUniqueLPN3, 1)` — if NonUniqueLPN3 IS NOT NULL
- `('ItemID', NonUniqueLPN4, 1)` — if NonUniqueLPN4 IS NOT NULL

Non-unique barcodes (UPC/GTIN/EAN/ItemID) are barcodes that may appear on multiple cartons; they can narrow the lookup to a specific PandaData only when combined with the WaveID. The BL xref is the primary unique key.

**`sdisp_TOOL_CUSTOM_LPNxRef` — oLPN association** (called by ItmSort/RF **after** host advice, before induct):
- Params: `@oLPN VARCHAR(64)`, `@OrderID VARCHAR(64)` (=blind label), `@WaveID VARCHAR(64)`
- Step 1: Validate all 3 params non-null/non-'-' (`LPNxRef.sql:53–103`)
- Step 2: Find the `PandaDataID` via xref join:
  ```sql
  SELECT DISTINCT PDXR.PandaDataID
  FROM pandadataxref PDXR
  INNER JOIN PandaData PD ON PD.PandaDataID = PDXR.PandaDataID
  INNER JOIN sdivw_PandAWaves PW ON PW.WaveID = PD.WaveID
  WHERE Barcode = @OrderID AND PW.WaveStatus <> 'COMPLETED' AND PW.WaveID = @WaveID
  ```
  Guard: wave must be non-COMPLETED AND must match the supplied `@WaveID` (`LPNxRef.sql:105–120`).
- Step 3: If no match → `ErrorCode=1`, "doesn't exist in any valid xRef values in PandA" (`LPNxRef.sql:157–170`)
- Step 4: **One-oLPN-per-carton guard** — if ANY existing xref row for this PandaDataID has a non-null `oLPN` column → `ErrorCode=1`, "already has an xRef for an oLPN" (`LPNxRef.sql:173–195`). Note: the check predicate is `(oLPN = @oLPN OR oLPN IS NOT NULL)`, which effectively means "any non-null oLPN exists on this PandaDataID" — a strict single-association policy.
- Step 5: `SELECT TOP 1 @PandaDataID FROM @t` (takes first match if multiple PandaDataIDs share the OrderID in the wave — shouldn't happen with unique BL, but a safety net for non-unique barcodes)
- Step 6: `INSERT INTO PandaDataXRef (PandaDataID, @oLPN, 'oLPN', Priority=1)` (`LPNxRef.sql:204–216`)
- Step 7: Log at LogLevel=80

**`sdisp_TOOL_CUSTOM_LPNxRef_Disassociate`** — unconditional oLPN removal:
- `DELETE FROM PandaDataXref WHERE barcodedescription = 'oLPN' AND Barcode = @oLPN` (`Disassociate.sql:40–43`)
- Reports rowcount; deletes ALL rows for that oLPN (could span multiple PandaDataIDs if somehow duplicated — shouldn't happen due to the association guard, but defensively correct)
- **No wave/status guard** — disassociate works even for completed/active waves

**Lookup at induct** (`LookupCarton.sql:243–295`):
```sql
WITH ApplicableLabels AS (
    SELECT Barcode, Priority, PandaDataID
    FROM PandaDataXRef WHERE Barcode = @BlindLabel
)
SELECT TOP 1 pd.CartonStatus, pd.PandaDataID, ...
FROM PandaData pd
CROSS APPLY (
    SELECT Barcode, Priority FROM ApplicableLabels
    WHERE pd.PandaDataID = PandaDataID AND Barcode = @blindlabel
) xref
INNER JOIN sdivw_PandAWaves PDW ON PDW.WaveID = pd.WaveID AND WaveStatus='ACTIVE' AND CHARINDEX(@PandaID, PandaID)<>0
...
ORDER BY pd.Printed ASC, pd.ActiveRecord DESC, PDW.StatusTime ASC, ...
```
The xref CTE means the induct scan's barcode (`@BlindLabel`) is resolved through `PandaDataXRef.Barcode` — an oLPN or UPC/GTIN will match if it has a xref row pointing to a PandaData entry. After the 2025-02-11 change (`LookupCarton.sql:54`: "Removed PandaDataXref from lookup"), the note suggests a simplification was made — the xref CTE is still present in the code, but the comment refers to removing it from the verify path, not induct.

### Already Built?
**Not built.** `TransportOrder` has no xref concept. Induct matches only by `TuId == blindLabel`. `CartonAdviceService` does not create xref entries. This is a silent correctness gap for multi-barcode sites (any site that scans by UPC/GTIN/EAN/ItemID at induct, or uses oLPN association from RF scanners).

### Target Module/Class
```
PandA.Core/Domain/XRef.cs                         — value type: (Barcode, BarcodeType, Priority)
PandA.Core/Domain/BarcodeType.cs                  — enum: BL | UPC | GTIN | EAN | ItemID | oLPN
PandA.Core/Services/XRefService.cs                — Associate(tuId, oLPN, waveId) / Disassociate(oLPN)
PandA.Core/Ports/IXRefStore.cs                    — FindByBarcode(barcode) → TransportOrder?
PandA.Core/TransportOrder.cs                      — AddXRef(XRef), XRefs collection
PandA.Sim/Stores/InMemoryXRefStore.cs             — dictionary-backed
```

`ITransportOrderStore.FindActiveByTuIdAsync` must be extended or complemented with `FindActiveByBarcodeAsync(string barcode)` to support non-BL induct scans.

### Dependencies
- Depends on SETTINGS-1 (`ISettingsProvider`) for `OverwriteLabelData` (controls whether existing xrefs are cleared on re-advice)
- HOST ADVICE INGESTION (INBOUND-1) populates the initial xref rows — the two must use the same `IXRefStore`
- F24 (oLPN) depends on F18 (xref model existing)
- Blocks F19 (ProfileName) — the profile lookup in `LookupCarton` also uses the xref path for matching
- Wave model (WaveID on `TransportOrder`) is needed for the wave-guard in `XRefService.AssociateAsync`

### Acceptance Criteria
1. `XRefService.AssociateAsync(tuId, oLPN, waveId)` succeeds when the carton exists in an active (non-COMPLETED) wave with the given `waveId`.
2. `XRefService.AssociateAsync` returns `ErrorCode=AlreadyAssociated` when the carton already has an oLPN xref row.
3. `XRefService.AssociateAsync` returns `ErrorCode=NotFound` when no xref matches `(OrderID, WaveID)` in an active wave.
4. `XRefService.DisassociateAsync(oLPN)` deletes the xref row and returns the count deleted.
5. After `AssociateAsync`, `IXRefStore.FindActiveByBarcodeAsync(oLPN)` returns the associated `TransportOrder`.
6. `InductService` with a scan of an oLPN barcode (no BL xref) resolves to the correct `TransportOrder` via `FindActiveByBarcodeAsync`.
7. `CartonAdviceService.AdviseAsync` inserts xref rows for BL/UPC/GTIN/EAN/ItemID from the `PandaLabelSet` when provided.
8. `XRefService.DisassociateAsync` on a non-existent oLPN returns count=0 and no error.
9. Two cartons in different waves can share the same UPC barcode; `FindActiveByBarcodeAsync(UPC)` returns both, and the induct election ORDER BY (PrintCount ASC → ActiveRecord DESC → wave StatusTime ASC) picks the correct one.
10. Round-trip: advise with NonUniqueLPN1 → induct by NonUniqueLPN1 → correct carton dispatched.

### Open Questions
- Should `AssociateAsync` allow association with a SUSPENDED wave (WaveStatus not in ACTIVE/COMPLETED set)? Source guard is only `<> 'COMPLETED'`.
- The `CROSS APPLY` predicate in `LookupCarton` (`Barcode = @blindlabel`) means the xref lookup only matches the **scanned value** to `PandaDataXRef.Barcode` — so a carton scanned by oLPN will resolve if the oLPN is in the xref, but it will set `@BlindLabel` to the scanned value (the oLPN), not the actual blind label. Is this the intended behavior for MandA `PrintedBy` audit (where `@Label1` is the oLPN)?

---

## F21 — Carton Slot Number / PLC Index (`sdisp_TOOL_GetSlotNumber`)

### Source
| File | Key lines | Target object |
|---|---|---|
| `8.0_CreateSP/sdisp_TOOL_GetSlotNumber.sql` | 7–54 | `dbo.CartonAssignSeq` (SQL SEQUENCE) |

### Behavior
```sql
CREATE SEQUENCE dbo.CartonAssignSeq AS INT
  START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 300 CYCLE CACHE 300
SET @SlotNumber = NEXT VALUE FOR dbo.CartonAssignSeq
```
- The sequence is created on first call if absent (`GetSlotNumber.sql:36–52`)
- Returns next integer in range **[1, 300]**, then **cycles back to 1** — a monotonic cycling index
- `MAXVALUE 300` matches the PLC array `vPA.Assign[i]` (300 slots), which carries the fire-point bundle per carton slot
- `CACHE 300` means the full range is cached in memory — after a SQL Server restart, up to 300 numbers could be skipped (gap behavior), but since the window is always ≤300 cartons in flight, this is safe
- Output only: `@SlotNumber INT OUTPUT`

**Semantics in the PLC bundle:** The slot number is written as `vPA.Assign[SlotNumber].RecID`, `.Seq`, `.Dest`, `.PrintPoint`, `.ApplyPoint`, `.MsgRdy` by `sdisp_PA2BP_SendPrinterFirePoints`. Two cartons must not have the same slot number simultaneously, but since the conveyor carries at most 300 cartons in flight, a 300-element cycling counter is safe.

### Already Built?
**Not built.** `InductResult` and `PrintJob` have no slot index. Backlog GAP F21 confirms: "pairs with F08 (lane routing) as part of the fire-point/PLC outbound bundle."

### Target Module/Class
```
PandA.Core/Services/SlotIndexService.cs    — cycling counter logic
PandA.Core/Ports/ISlotIndexProvider.cs    — interface: NextSlot() → int
PandA.Sim/Sim/InMemorySlotIndexProvider.cs — Interlocked cycling counter (thread-safe)
PandA.Core/Domain/InductResult.cs         — add SlotIndex property
```

**C# implementation note:** The SQL SEQUENCE is a DB-level auto-restart-safe counter. In C#, a per-instance `Interlocked.Increment` with a `% 300 + 1` (Interlocked mod pattern) is safe for single-process deployments. For multi-process (multiple PandA instances sharing one DB), a shared counter is needed — either the DB sequence (read via one call), a Redis incr, or a distributed lock around a persisted counter. Confirm deployment topology with domain owner.

### Dependencies
- Depends on `InductService` (slot assigned at induct time, stored on `InductResult`)
- Feeds fire-point PLC outbound bundle (F08 lane routing + arch-log 010 fire points)
- No settings dependency

### Acceptance Criteria
1. `ISlotIndexProvider.NextSlot()` returns values in [1, 300].
2. After 300 calls, the next call returns 1 (cycles correctly).
3. Concurrent calls from multiple threads never return the same slot number within the same cycle pass (`Interlocked` or equivalent).
4. `InductResult.SlotIndex` is populated by `InductService`.
5. `SimHost` logs the slot index with each induct event.
6. An `InMemorySlotIndexProvider` initialized at 299 returns 299, 300, 1, 2 in sequence.

### Open Questions
- Is the PLC array strictly 300 slots, or is this site-specific? If configurable, expose `MaxSlots` as a `LineConfig` property sourced from JSON.
- Should the slot index be per-line (independent cycling counters per PandA line) or shared across lines? Source has one DB sequence per database (not per-line).

---

## F17 — Purge / Data Lifecycle (`sdisp_PA_Purge` + `sdisp_TOOL_GetAllTableOldestRecord`)

### Source
| File | Key lines | Target tables |
|---|---|---|
| `8.0_CreateSP/sdisp_PA_Purge.sql` | 8–188 | `PandaData`, `Wave`, `WaveHistory`, `PandaDataXRef`, `PandaCartonList`, `PrintEngineStatus`, `EventLog`, `uEventLog`, `EventDescriptions` |
| `8.0_CreateSP/sdisp_TOOL_GetAllTableOldestRecord.sql` | 8–110 | All datetime-bearing tables (diagnostic) |
| `6.0_PopulateTables/Settings.sql` | RecIDs 13–16 | Retention settings |

### Behavior

`sdisp_PA_Purge` runs as a nightly job (SQL Agent). All operations are in a single TRY block; a failure on any step logs the error and exits without retrying subsequent steps. No transactions — each DELETE commits immediately (AUTOCOMMIT).

**Purge sequence** (effective retention from seed values):

| Step | Table | Condition | Retention |
|---|---|---|---|
| 1 | `PandaData` | `(BlindLabel IN ('-','?') OR CartonStatus IN ('No Information','No Read')) AND DATEDIFF(day, CreationTime, NOW) > 7` | 7 days (ExceptionData) |
| 2 | `PandaData` | `ActiveRecord=0 AND DATEDIFF(day, VerifyTime, NOW) > 21` | 21 days after verify (InactiveData — seed=21) |
| 3 | `Wave` | `WaveID IN (sdivw_PandAWaves WHERE WaveStatus='COMPLETED' AND DATEDIFF(day, StatusTime, NOW) > 7)` | 7 days after completion (UsedData — seed=7) |
| 4 | `WaveHistory` | `WaveRecID NOT IN Wave.RecID` | Orphan cleanup (cascades from step 3) |
| 5 | `PandaData` | `WaveID IS NULL AND DATEDIFF(day, CreationTime, NOW) > 7` | 7 days for wave-less records |
| 6 | `PandaData` | `WaveID NOT IN Wave.WaveID` | Orphan cleanup (cascades from step 3) |
| 7 | `PandaDataXRef` | `PandaDataID NOT IN PandaData.PandaDataID` | Orphan cleanup (cascades from steps 1/2/5/6) |
| 8 | `PandaCartonList` | `DATEDIFF(day, CreationTime, NOW) > 7` | 7 days (UsedData — seed=7) |
| 9 | `PrintEngineStatus` | `DATEDIFF(day, CreationTime, NOW) > 14` | 14 days (UnUsedData) |
| 10 | `EventLog` | `DATEDIFF(day, CreationTime, NOW) > 14` | 14 days |
| 11 | `uEventLog` | `DATEDIFF(day, CreationTime, NOW) > 14` | 14 days |
| 12 | `EventDescriptions` | `DATEDIFF(day, CreationTime, NOW) > 14` | 14 days |

Note: Steps 1, 2, 5, 6 all target `PandaData` by different criteria. Step 6 is a cascade guard — after step 3 deletes completed waves, step 6 removes any PandaData whose WaveID no longer exists. Step 7 then cleans xrefs for any removed PandaData.

**Logging:** Purge logs "Purge Starting" and "Purge Complete" at LogLevel=100. Any CATCH logs the error at LogLevel=30.

**`sdisp_TOOL_GetAllTableOldestRecord`** is a diagnostic-only tool (`GetAllTableOldestRecord.sql:8–110`): it iterates all user tables via cursor, executing dynamic SQL to compute total row count, oldest record, and count of records older than 30/90 days per datetime-bearing column. Used by support to spot data buildup. This is not called during normal operations and has no C# runtime equivalent needed — it is an **operator/DBA tool**.

### Already Built?
**Not built.** No retention policy, no scheduled purge service.

### Target Module/Class
```
PandA.Core/Services/PurgeService.cs          — orchestrates retention policy, reads settings
PandA.Core/Ports/IPurgeStore.cs              — DeleteExpiredAsync(PurgePolicy policy) per entity type
PandA.Core/Domain/PurgePolicy.cs             — holds all 4 retention values (int days each)
PandA.Sim/Services/InMemoryPurgeStore.cs     — manipulates in-memory collections
(deferred) PandA.EController/Jobs/PurgeJob.cs — SQL Agent wrapper / hosted-service timer
```

The **ordering of deletions matters** (XRef before PandaData would orphan nothing; PandaData before Wave means Wave is left with no data → covered by step 3+4). In C# the same sequence must be preserved. Since PandaData XRef is FK-child of PandaData, XRef must be cleaned after PandaData.

### Dependencies
- Depends on SETTINGS-1 (`ISettingsProvider`) to read all 4 `PurgeSetting_*` values
- Depends on F16 (`IEventLog`) for the "Purge Starting/Complete" log entries
- Wave model (F23) defines `WaveStatus.COMPLETED` — purge must reference the same enum
- `sdisp_TOOL_GetAllTableOldestRecord` → no C# port needed; expose a diagnostic endpoint if an operator UI is built

### Acceptance Criteria
1. `PurgeService.RunAsync()` with `PurgePolicy(unusedData=14, usedData=7, inactiveData=21, exceptionData=7)` deletes `PandaData` records matching the exception-data criteria older than 7 days.
2. Verified inactive records (`ActiveRecord=0`) are deleted only after `VerifyTime + 21 days`, not `CreationTime`.
3. Completed waves older than 7 days from `StatusTime` are deleted; their `WaveHistory` rows are cascade-deleted.
4. `PandaData` referencing a deleted wave (orphaned after wave purge) is also deleted.
5. `PandaDataXRef` orphan cleanup fires AFTER `PandaData` deletes — no FK violations.
6. `EventLog` records older than 14 days are removed; records within 14 days are not.
7. A fresh `InMemoryPurgeStore` with 5 cartons (2 expired, 3 not) after `RunAsync` contains exactly 3.
8. Purge emits "Purge Starting" and "Purge Complete" log events via `IEventLog`.
9. Any exception in one step is caught/logged; remaining steps are attempted (or documented that they are not — clarify with domain owner whether partial purge is acceptable).
10. `PurgeSetting_UsedData` default in `KnownSettings` = 7 (matches seed, not proc-default of 21).

### Open Questions
- Step 9 behavior: the source uses a single TRY/CATCH that aborts all remaining steps on failure. Should C# replicate (all-or-nothing log then stop) or attempt each step independently? Confirm with domain owner for operational resilience.
- `PurgeService` — triggered how in C#? SQL Agent → Hosted Service with `IHostedService` + `PeriodicTimer`? Or a scheduled Azure Function/Worker? Confirm deployment context.
- Is `sdisp_TOOL_GetAllTableOldestRecord` needed as a runtime API (for a UI "DB health" page) or only as an ad-hoc DBA query?

---

## LOCK-1 — Concurrency Locks (`sdisp_PA_Lock` / `sdisp_PA_LockRemove`)

### Source
| File | Key lines | Target object |
|---|---|---|
| `8.0_CreateSP/sdisp_PA_Lock.sql` | 7–67 | `sys.sp_getapplock` |
| `8.0_CreateSP/sdisp_PA_LockRemove.sql` | 7–24 | `sys.sp_releaseapplock` |

### Behavior

**`sdisp_PA_Lock`** (`Lock.sql:44–67`):
```sql
EXEC @RC = sys.sp_getapplock
    @Resource    = @ResourceName,
    @LockMode    = 'Exclusive',
    @LockOwner   = 'Session',
    @LockTimeout = 60000            -- 60 seconds
```
- `@LockMode='Exclusive'` — no other session can hold any lock on this resource simultaneously
- `@LockOwner='Session'` — lock is held for the lifetime of the DB session (connection); released automatically if the connection drops
- `@LockTimeout=60000` — if the lock cannot be acquired within 60 seconds, `@RC` is returned as negative (`-1=timeout`, `-3=deadlock`, `-999=other error`)
- On `@RC < 0`: logs the failure at LogLevel=50 but **does NOT RETURN or raise an error** (`Lock.sql:51–67`) — the caller proceeds unlocked. This is a soft failure with a warning, not a hard guard.

**`sdisp_PA_LockRemove`** (`LockRemove.sql:22–24`):
```sql
EXEC sp_releaseapplock @Resource=@ResourceName, @LockOwner='Session'
```
- No error handling — if called on a resource not held, SQL Server silently ignores
- Must be called in the same session as `PA_Lock`

**Known resource names (inferred from architecture-log 011 §F13):**
- `'PA_Status'` — guards printer/zone/lane-eval status updates (referenced in arch-log doc 012 §9)

### Already Built?
**No C# equivalent.** Architecture-log 011 gap F13 note: "when engine-status ingestion lands, it must serialize lane evaluation per line — `LaneEvalService` mutates shared `PrinterState` objects without locking (fine under single-threaded SimHost today). Concurrent printer/engine/zone signals on the same line will race the spare-flag mutations." The current `SimHost` is single-threaded; this is safe today but will break when real concurrent messages arrive.

The source's SQL app-lock approach (DB-level cross-session mutex) is overkill for single-process C# but would be needed for multi-process deployments.

### Target Module/Class
```
PandA.Core/Ports/ILockProvider.cs             — AcquireAsync(name, timeout) → IAsyncDisposable
PandA.Sim/Concurrency/InMemoryLockProvider.cs — SemaphoreSlim per resource name (dictionary)
(deferred) PandA.EController/Concurrency/AppLockProvider.cs — sp_getapplock wrapper for cross-process
```

**In-process design:** `InMemoryLockProvider` holds a `ConcurrentDictionary<string, SemaphoreSlim>` and returns an `IAsyncDisposable` handle that releases the semaphore on `DisposeAsync()`. The `AcquireAsync` method passes a `TimeSpan` (default 60 seconds to match the source).

**Usage in `LaneEvalService`:** Wrap `LaneEvalService.EvaluateAsync()` calls with `ILockProvider.AcquireAsync("LaneEval_{lineId}")` so that concurrent printer/zone status messages for the same line serialize the `PrinterState` mutations.

### Dependencies
- Depends on F13 (PrintEngine status ingestion) to make the concurrency problem real
- `LaneEvalService` injection needs `ILockProvider`
- No settings dependency

### Acceptance Criteria
1. `InMemoryLockProvider.AcquireAsync("PA_Status")` returns a handle; a second concurrent call waits until the first is disposed.
2. Disposing the handle releases the lock; the waiting caller proceeds.
3. `AcquireAsync` with a 60-second timeout cancels and throws `TimeoutException` (or `OperationCanceledException`) if the lock is not acquired within the timeout.
4. An invalid/empty resource name returns a failed result (mirrors `Lock.sql:23–42` guard).
5. `LaneEvalService` acquires `ILockProvider.AcquireAsync("LaneEval_{lineId}")` before mutating `PrinterState`; two concurrent `EvaluateAsync` calls on the same line serialize correctly.
6. `LaneEvalService` calls for different lineIds do NOT block each other.
7. Disposing a handle for a resource not held does not throw.

### Open Questions
- Is PandA ever deployed multi-process (multiple C# hosts, same DB)? If yes, `AppLockProvider` (using `sp_getapplock`) must be the production implementation and `InMemoryLockProvider` only for tests/Sim.
- `@RC < 0` in the source is a soft failure. Should C# also be soft (log + proceed) or should it throw/propagate? Confirm with domain owner whether a 60-second lock timeout is a critical error or a recoverable warning.

---

## F23 — Wave Auto-Complete Hot-Path (`sdisp_TOOL_CUSTOM_CheckWaveCmp` / `sdisp_TOOL_CUSTOM_PandAWaveComplete` / `sdisp_PA2DCMS_WaveStatus`)

### Source
| File | Key lines | Target tables |
|---|---|---|
| `8.0_CreateSP/sdisp_TOOL_CUSTOM_CheckWaveCmp.sql` | 8–73 | `PandaData`, `sdivw_PandAWaves` |
| `8.0_CreateSP/sdisp_TOOL_CUSTOM_PandAWaveComplete.sql` | 8–107 | `WaveHistory`, `WaveRange` |
| `8.0_CreateSP/sdisp_PA2DCMS_WaveStatus.sql` | 8–92 | `sdisp_HI_Ins_OrderWaveStatus` (DCMS outbound) |
| `5.0_CreateTables/Wave.sql` | 9–20 | `Wave(RecID, CreationTime, WaveID)` |
| `5.0_CreateTables/WaveHistory.sql` | 9–62 | `WaveHistory(RecID, CreationTime, Active, WaveRecID, WaveStatus, UserID)` |
| `5.0_CreateTables/WaveRange.sql` | 9–22 | `WaveRange(RecID, AddedTime, WaveRecID, PandARecID)` |

### Behavior

**`sdisp_TOOL_CUSTOM_CheckWaveCmp`** is called inside `sdisp_PA_VerifyCarton` on every verify-pass. It is the **hot-path auto-complete trigger**.

Completion check (`CheckWaveCmp.sql:39–47`):
```sql
SELECT @CMP = CASE WHEN Val.Cartons = Val.Verified THEN 1 ELSE 0 END
FROM (
    SELECT
        COUNT(*) AS Cartons,
        SUM(CASE WHEN PD.ActiveRecord = 0 AND PD.Printed > 0 THEN 1 ELSE 0 END) AS Verified
    FROM sdivw_PandAWaves VPW
    INNER JOIN PandaData PD ON PD.WaveID = VPW.WaveID
    WHERE VPW.WaveID = @WaveID
      AND VPW.WaveStatus = 'ACTIVE'    -- wave must be ACTIVE to check
    GROUP BY VPW.waveid, VPW.WaveStatus, VPW.PandaID
) AS Val
```

**"Verified" definition:** `PD.ActiveRecord = 0 AND PD.Printed > 0` — the carton record has been **deactivated (verified)** AND has a non-zero print count. This is a stricter condition than just `ActiveRecord=0` (a record could be inactive without having been printed if it was purged or errored).

**Completion trigger:** If `Cartons == Verified` (ALL cartons in the active wave are verified+printed), calls `sdisp_TOOL_CUSTOM_PandAWaveComplete @WaveID, @UserID='SDI'` — system-initiated.

**`sdisp_TOOL_CUSTOM_PandAWaveComplete`** (`WaveComplete.sql:38–89`):
1. `sdisp_TOOL_CUSTOM_WaveHistory_Insert(@WaveID, @WaveStatus='COMPLETED', @UserID)` — appends a `WaveHistory` row marking the wave complete
2. `sdisp_TOOL_CUSTOM_WaveRange_Delete(@WaveID)` — removes the `WaveRange` rows (PandA↔Wave associations)
3. Logs at LogLevel=80: "Wave was completed with WaveID = X by UserID = Y"

**`sdisp_PA2DCMS_WaveStatus`** — DCMS outbound notification (`WaveStatus.sql:42–59`):
- Called separately (not from CheckWaveCmp directly — called by the wave action handlers). Sends an `OrderWaveStatus` message to DCMS:
  - `UserID` empty or `'SDI'` (system): sends without `OperatorID`
  - `UserID` set (operator-initiated): sends with `@OperatorID = @UserID`
- Logs the DCMS record ID at LogLevel=50

**Wave data model:**
- `Wave(RecID, CreationTime, WaveID)` — minimal; WaveID is a VARCHAR(32) business key
- `WaveHistory(RecID, CreationTime, Active INT, WaveRecID FK, WaveStatus, UserID)` — event-log pattern; every status change is a new row. `Active` flag marks the current status row.
- `WaveRange(RecID, AddedTime, WaveRecID FK, PandARecID FK)` — which PandA lines participate in this wave. Deleted on wave completion.
- `sdivw_PandAWaves` — view that derives current `WaveStatus` and `PandaID` from `WaveHistory` (latest active row per WaveRecID)

### Already Built?
**Not built.** `VerifyStationService` calls `VerificationService` and `VerifyThresholdTracker` but has no wave reference. No `IWaveRepository`, no `Wave` domain object, no completion event. Backlog GAP F23 says: "Must wire into `VerificationService.VerifyAsync`, not a later wave phase."

### Target Module/Class
```
PandA.Core/Domain/Wave.cs              — entity: WaveId, Status (READY|ACTIVE|SUSPENDED|COMPLETED), CartonCount, VerifiedCount
PandA.Core/Domain/WaveStatus.cs        — enum
PandA.Core/Services/WaveService.cs     — CheckAndAutoCompleteAsync(waveId), CompleteAsync(waveId, userId)
PandA.Core/Ports/IWaveStore.cs         — GetAsync(waveId), UpdateAsync, GetVerificationCountsAsync
PandA.Core/Ports/IDcmsWaveNotifier.cs  — NotifyWaveStatusAsync(waveId, status, pandaId, userId?)
PandA.Sim/Stores/InMemoryWaveStore.cs
PandA.Core/TransportOrder.cs           — add WaveId property (needed for grouping)
PandA.Core/Services/VerifyStationService.cs — after successful verify: call WaveService.CheckAndAutoCompleteAsync
```

**Hot-path coupling:** `VerifyStationService.VerifyAsync` must call `WaveService.CheckAndAutoCompleteAsync(order.WaveId)` **after** marking the order verified. This must be in the same logical unit (or same transaction scope if using DB) to avoid a partial-complete race where the last carton is verified twice concurrently.

### Dependencies
- Depends on `TransportOrder.WaveId` (not yet on the model) — HOST ADVICE INGESTION (INBOUND-1) provides this
- Depends on LOCK-1 (`ILockProvider`) to guard the `CheckAndAutoComplete` check against concurrent verify-passes on the same wave
- Feeds `IDcmsWaveNotifier` (DCMS outbound — requires the DCMS integration adapter in `PandA.EController`)
- Wave creation/loading is part of HOST ADVICE INGESTION (INBOUND-1)

### Acceptance Criteria
1. After the last carton in a wave is verified, `WaveService.CheckAndAutoCompleteAsync` transitions the wave to `COMPLETED`.
2. A wave with 5 cartons — 4 verified+printed, 1 not — remains `ACTIVE` after the 4th verify-pass.
3. A wave in `COMPLETED` or `READY` status is not eligible for `CheckAndAutoComplete` (`WaveStatus='ACTIVE'` guard).
4. On auto-complete: `WaveHistory` gains a new row `{WaveStatus='COMPLETED', UserID='SDI', Active=1}`.
5. On auto-complete: `WaveRange` rows for the wave are deleted.
6. `IDcmsWaveNotifier.NotifyWaveStatusAsync` is called with `userId=null/'SDI'` for auto-complete, and `userId=operatorId` for operator-initiated complete.
7. Concurrent verify-passes on the last two cartons of a wave (both arrive simultaneously) auto-complete exactly once — not zero or twice (requires LOCK-1).
8. A carton with `ActiveRecord=0, Printed=0` (inactive but not printed — e.g., exception or error) does **not** count toward the Verified count, and the wave does not auto-complete.
9. `VerifyStationService` test: after verifying the last carton, `IWaveStore` is called with `COMPLETED` status.

### Open Questions
- Is `WaveStatus='ACTIVE'` the only state that can auto-complete? What about `SUSPENDED`? Source guard is only `WaveStatus='ACTIVE'`.
- `WaveRange` is deleted on completion — does the C# model need to retain this association for auditing, or is it safe to discard as in the source?
- `IDcmsWaveNotifier` — is DCMS notification synchronous (blocking verify until ACK) or fire-and-forget? Source calls `sdisp_HI_Ins_OrderWaveStatus` inline (synchronous); a failure is caught but doesn't block the wave completion.

---

## F14 — MandA Manual Apply Stations (`sdisp_MA_Scan_Induct` / `sdisp_MA_Scan_Verify`)

### Source
| File | Key lines | Key calls |
|---|---|---|
| `8.0_CreateSP/sdisp_MA_Scan_Induct.sql` | 8–126 | `sdisp_PA_LookupCarton`, `sdisp_PA_PickPrinter`, `sdisp_TOOL_PA_PandaCartonList_Update` |
| `8.0_CreateSP/sdisp_MA_Scan_Verify.sql` | 8–110 | `sdisp_PA_VerifyCarton`, `sdisp_WMS_PandAVerify_Insert` |

### Behavior

**MandA ("Manual Apply") stations** are operator-staffed workstations. An operator physically picks up a carton, scans it to identify it (induct), receives back the label to print, prints it manually, applies it to the carton, then scans the applied label to verify correct application.

**`sdisp_MA_Scan_Induct`** — operator scans the carton barcode:
- Params: `@MandaID VARCHAR(64)` (station ID, e.g. `'manda_03'`), `@Label1 VARCHAR(64)` (scanned barcode)
- Delegates to `sdisp_PA_LookupCarton` with **fixed dummy values** (`MandaScanInduct.sql:45–68`):
  ```
  @SorterNumber=1, @SorterMode=1, @DeviceID=1, @SeqNum=1, @LabelStatus=0
  @Weight=999, @Gap=999, @Length=999, @ScannerID='MANDA_SCAN'
  ```
  - `@Gap=999` → always ≥ MinGap (20), so **gap errors never fire at MandA stations**
  - `@Weight/Length=999` → sentinel values; dimension-based logic ignored
- Only `CartonStatus='PrintReady'` passes (`MandaScanInduct.sql:76–90`):
  - `'Bypass'` → `ErrorCode=1`, "Carton is Bypass carton, no label required" — Bypass cartons need no label, operator should divert them directly
  - Anything else → `ErrorCode=1`, "Carton not found"
- On `PrintReady`: reads `LabelData1` from `PandaData` and returns it as `@LabelData` (`MandaScanInduct.sql:84–89`) — the caller (RF terminal or GUI) uses this ZPL to drive a local printer
- Calls `sdisp_PA_PickPrinter(@MandaID, @CartonListID, @PandaDataID, @BlindLabel='', @CartonStatus='')` — selects the printer configured for this MandA station (`MandaScanInduct.sql:92–99`)
- Calls `sdisp_TOOL_PA_PandaCartonList_Update(@CartonListID, @PrinterNumber)` — records which printer was assigned (`MandaScanInduct.sql:101–104`)
- **Returns `@CartonListID` to the caller** — this must be stored by the calling session and re-submitted at verify

**`sdisp_MA_Scan_Verify`** — operator scans the label after applying it:
- Params: `@MandaID VARCHAR(64)`, `@Label1 VARCHAR(64)` (applied label barcode), `@CartonListID BIGINT` (**must come from the induct call**)
- Delegates to `sdisp_PA_VerifyCarton` with fixed params (`MandaScanVerify.sql:48–62`): same `@SorterNumber=1, @SorterMode=1, @DeviceID=1, @SeqNum=1`
- Only `CartonStatus='Verify - Pass'` succeeds (`MandaScanVerify.sql:64–69`); anything else → `ErrorCode=1`, "Carton wasn't verified correctly"
- On pass: calls `sdisp_WMS_PandAVerify_Insert` (`MandaScanVerify.sql:77–86`):
  ```
  @CartonRecID = @CartonListID
  @iLPN = @BlindLabel          (inner LPN = the blind label)
  @oLPN = @Label1              (outer LPN = what the operator scanned)
  @VerifyTime = GETDATE()
  @PrintedBy = 'M'             (Manual — audit distinguisher)
  ```
  - `@PrintedBy='M'` distinguishes MandA verification from automatic line (`'A'` or similar) in the WMS audit record

**Key MandA vs. automatic-line differences:**

| Aspect | Automatic Line | MandA |
|---|---|---|
| Gap check | `@Gap` from PLC, checked vs. MinGap | Hardcoded 999 — always passes |
| Weight/dimensions | Real PLC sensor values | 999 sentinel |
| ScannerID | PLC device ID | `'MANDA_SCAN'` fixed string |
| Label data return | Not returned to caller (printed immediately) | `@LabelData = LabelData1` returned to caller |
| Printer selection | Full fire-point routing | First available printer for `@MandaID` |
| `PrintedBy` | Not `'M'` | `'M'` — manual |
| Session state | Stateless per-scan | `@CartonListID` must persist across induct+verify |
| Bypass handling | Bypass cartons get lane routing | "No label required" error — operator handles physically |

### Already Built?
**Not built.** No `MandaService`. `InductService` has no mode flag. Backlog GAP F14: "No mode switch in C#."

### Target Module/Class
```
PandA.Core/Services/MandaInductService.cs   — MandA induct: lookup + return label data + pick printer
PandA.Core/Services/MandaVerifyService.cs   — MandA verify: verify carton + WMS insert
PandA.Core/Domain/MandaInductResult.cs      — {CartonListId, LabelData, PrinterId, CartonStatus}
PandA.Core/Ports/IWmsVerifyNotifier.cs      — NotifyVerifyAsync(cartonListId, iLPN, oLPN, verifyTime, printedBy)
```

`MandaInductService` wraps `ICartonLookup` with the fixed MandA parameters (gap=999, weight/length=999, scannerSource="MANDA_SCAN") and returns label data. It must share the same `ITransportOrderStore` and `IPrinterSelectionService` as `InductService`.

`MandaVerifyService` wraps `VerifyStationService` with fixed parameters and calls `IWmsVerifyNotifier` with `PrintedBy='M'`.

**Session state (`CartonListId`):** The `CartonListId` must be threaded from induct response to verify request. In the RF-terminal flow this is stored in the device session. In C# the API must expose this as a return value from `MandaInductService.InductAsync`.

### Dependencies
- Depends on `ICartonLookup` (which calls `InductService` / LookupCarton logic) — MandA overrides only the physical parameters
- Depends on `IPrinterSelectionService` (first-printer-for-station logic)
- Depends on `VerifyStationService`
- Depends on `IWmsVerifyNotifier` (WMS integration — eController adapter)
- **Does NOT depend on wave auto-complete** for the verify path at MandA? (Check: `sdisp_MA_Scan_Verify` calls `sdisp_PA_VerifyCarton` which calls `sdisp_TOOL_CUSTOM_CheckWaveCmp` — yes, wave auto-complete DOES fire on MandA verify. F23 must exist first.)

### Acceptance Criteria
1. `MandaInductService.InductAsync("manda_03", label)` returns `CartonStatus='PrintReady'` and `LabelData` when the carton exists in an active wave.
2. A Bypass carton returns `ErrorCode=1`, "Carton is Bypass carton, no label required."
3. An unknown barcode returns `ErrorCode=1`, "Carton not found."
4. The gap-check path is never entered (gap sentinel 999 ≥ MinGap 20).
5. `MandaVerifyService.VerifyAsync("manda_03", label, cartonListId)` passes when the scanned label matches the expected verify barcode.
6. On MandA verify-pass, `IWmsVerifyNotifier` is called with `PrintedBy='M'`.
7. `PrintedBy='M'` is **not** used on automatic-line verify (`PrintedBy` is absent or uses a different sentinel).
8. `MandaInductResult.CartonListId` is non-zero; it is accepted by `MandaVerifyService.VerifyAsync` and resolves to the same carton.
9. Consecutive MandA induct+verify on the same carton with the correct `CartonListId` leaves the carton in `Verified` state.
10. Wave auto-complete fires after the last MandA verify-pass in a wave (F23 integration).

### Open Questions
- MandA stations' `@MandaID` is a string (e.g. `'manda_03'`). Is the convention prefix-based (`MANDA%`)? Should `ILineProvider` treat MandA IDs differently from PandA line IDs, or are they the same type with a mode flag?
- `LabelData1` only is returned — what if a carton has multiple label types? Does a MandA station print all typed labels, or only the first? Source only returns `LabelData1`.
- `sdisp_WMS_PandAVerify_Insert` is a DCMS/WMS integration call not in scope. What is the C# equivalent? A simple `IDcmsVerifyNotifier` outbound port? Confirm with domain owner whether the WMS integration is in-scope for MandA or deferred.

---

## INBOUND-1 — Host Label Advice Ingestion (`sdisp_HI_DCMSCore_Inbound_PandALabels_Job`)

### Source
| File | Key lines | Target tables |
|---|---|---|
| `8.0_CreateSP/sdisp_HI_DCMSCore_Inbound_PandALabels_Job.sql` | 8–514 | `PandaData`, `PandaDataXRef`, `Wave`, via `sdisp_HI_ACK_ReadyPandALabel` |

**Source feed:** `sdivw_HI_PandA_GetReadyPandALabels` — a DCMS-side view over `HI_Xfer_Inb_PandALabels` that exposes only `PandAReady=1` unprocessed records. The proc is run as a SQL Agent job (polling).

### Behavior — full field mapping

**Record election** (`HI_Job.sql:102–115`):
```sql
INSERT INTO @CompleteList (RecID, BlindLabel, WaveID)
SELECT RecID, BlindLPN,
    SUBSTRING(InboundFilename, CHARINDEX('LD', InboundFilename)+2,
              CHARINDEX('_', InboundFilename)-(CHARINDEX('LD', InboundFilename)+2))
FROM sdivw_HI_PandA_GetReadyPandALabels
ORDER BY OrderPriority ASC, RecID ASC
```
WaveID is **extracted from the filename** using a brittle string operation: `InboundFilename` expected pattern is `...LD{WaveID}_...`. This is a custom ULW implementation detail.

**Wave filtering** (`HI_Job.sql:119–133`): Only waves with status `'READY'` or `'COMPLETED'`, OR new WaveIDs not yet in `dbo.Wave`, are eligible. Active waves (`ACTIVE`/`SUSPENDED`) are **excluded** — their data cannot be replaced mid-wave.

**Good-list filter** (`HI_Job.sql:167–183`): Records must have `LEN(BlindLabel) > 2` AND be in an eligible wave.

**`OverwriteLabelData` handling** (`HI_Job.sql:219–231`): If setting=1, marks existing PandaData rows inactive before inserting new ones. Default is append (setting=0).

**Wave delete → data insert → wave create sequence** (`HI_Job.sql:186–499`):
1. Delete old wave data (for READY/COMPLETED waves being replaced)
2. Insert all PandaData records (one per eligible @GoodList entry)
3. Insert all Wave records (after data, so partial batches don't create orphaned waves)

**PandaData INSERT fields** (`HI_Job.sql:249–319`):
| Source column | PandaData column | Type |
|---|---|---|
| `BlindLPN` | `BlindLabel` | VARCHAR(64) |
| `LabelData1..6` | `LabelData1..6` | VARCHAR(MAX) — ZPL strings |
| `VerifyLPN1..6` | `LabelBarcode1..6` | VARCHAR(32) — expected verify barcodes |
| `LabelType1..6` | `LabelType1..6` | VARCHAR(32) |
| `VerifyPassDest` | `VerifyPassDestName` | VARCHAR(32) |
| `VerifyFailDest` | `VerifyFailDestName` | VARCHAR(32) |
| `Verify` | `VerifyEnabled` | INT (0/1) |
| `Bypass` | `Bypass` | INT (0/1) |
| `ProfileName` | `ProfileName` | VARCHAR(64) |
| `SUBSTRING(InboundFilename...)` | `WaveID` | VARCHAR(32) |
| fixed `'PrintReady'` | `CartonStatus` | |
| fixed `GETDATE()` | `CreationTime` | |
| fixed `0` | `CartonListID` | BIGINT (assigned at induct) |
| fixed `1` | `ActiveRecord` | |
| fixed `0` | `Printed` | |
| fixed `20` | `CartonSize` | |
| fixed `NULL` | `VerifyTime` | |

**XRef INSERT per record** (`HI_Job.sql:323–383`) — UNION of up to 5 rows:
```
('BL',     BlindLPN,    Priority=1)
('UPC',    NonUniqueLPN1, 1)  IF NOT NULL
('GTIN',   NonUniqueLPN2, 1)  IF NOT NULL
('EAN',    NonUniqueLPN3, 1)  IF NOT NULL
('ItemID', NonUniqueLPN4, 1)  IF NOT NULL
```

**ACK** (`HI_Job.sql:386–424`): calls `sdisp_HI_ACK_ReadyPandALabel(@RecID, @BlindLPN, @ErrorCode=0)` on success, `@ErrorCode=1` on reject (records outside good list). Inner try/catch per record — an ACK failure is logged but does NOT stop the batch.

### Already Built?
**Partially.** `CartonAdviceService.AdviseAsync(lineId, blindLabel, labels)` creates or overwrites a `TransportOrder`. `PandaLabelSet` holds N typed `Label(LabelType, Lpn, Zpl)` entries.

**Concrete gaps:**

| SQL field | C# gap |
|---|---|
| `ProfileName` | Not on `TransportOrder` or `PandaLabelSet`; needed for F19 (profile validation at induct) and fire-point profile switching |
| `WaveID` | Not on `TransportOrder`; needed for F23 (wave auto-complete), F18 (xref wave guard), F24 (oLPN wave guard) |
| `VerifyEnabled` | Not on `TransportOrder`; needed to skip verify for individual cartons |
| `Bypass` | Not on `TransportOrder`; needed in induct to return `Bypass` status |
| `VerifyPassDestName` / `VerifyFailDestName` | Not on `TransportOrder`; needed for lane routing (F08) |
| `OrderPriority` | Not in `AdviseAsync` signature; needed for correct insertion order when multiple records arrive for the same wave |
| `NonUniqueLPN1..4` (BarcodeType) | Not in `PandaLabelSet`; xref rows not created by `AdviseAsync` |
| Wave create/delete logic | No `IWaveStore` or `WaveService` |
| `OverwriteLabelData` setting | Not read by `CartonAdviceService` |
| ACK mechanism | No `IHostAdviceAcknowledger` port |
| Wave eligibility guard (ACTIVE waves excluded) | Not checked |
| `LEN(BlindLabel) > 2` filter | Not applied |

### Target Module/Class
```
PandA.Core/Domain/AdviceMessage.cs       — full host-advice payload (all fields above)
PandA.Core/TransportOrder.cs            — add: WaveId, ProfileName, VerifyEnabled, Bypass, VerifyPassDest, VerifyFailDest
PandA.Core/Services/CartonAdviceService.cs — extend AdviseAsync to accept AdviceMessage; call IXRefStore, IWaveStore
PandA.Core/Ports/IHostAdviceAcknowledger.cs — AckAsync(recId, blindLabel, success)
PandA.Core/Ports/IWaveStore.cs          — (see F23)
(deferred) PandA.EController/Jobs/HostAdviceIngestionJob.cs — polls sdivw_HI + calls AdviseAsync + ACK
```

**Wave ID extraction note:** The brittle `SUBSTRING(InboundFilename...)` pattern for WaveID extraction is a SQL-side concern. The C# `AdviceMessage` should receive `WaveId` as a **first-class field** parsed by the host-integration layer (either the DCMS adapter or a dedicated filename parser). The extraction logic `CHARINDEX('LD', InboundFilename)+2` through the next `_` should be isolated in a `WaveIdParser.ParseFromFilename(string filename)` helper with tests, rather than embedded in the service.

### Dependencies
- Blocks F19 (`ProfileName` needed on `TransportOrder` for profile validation at induct)
- Blocks F18/F24 (xref population needed for multi-barcode induct)
- Blocks F23 (WaveID needed on `TransportOrder` for auto-complete)
- Blocks `sdisp_PA_LookupCarton` wave filter (wave guard at induct needs WaveID)
- Depends on SETTINGS-1 (`ISettingsProvider`) for `OverwriteLabelData`
- Depends on SETTINGS-2 (reprint-rules gated re-advice, F-ADV1)

### Acceptance Criteria
1. `CartonAdviceService.AdviseAsync` with a full `AdviceMessage` creates a `TransportOrder` with `WaveId`, `ProfileName`, `Bypass`, and `VerifyEnabled` correctly populated.
2. `AdviceMessage.WaveId` parsed from a filename `...LD12345_...` by `WaveIdParser.ParseFromFilename` returns `'12345'`.
3. `AdviseAsync` for a wave in `ACTIVE` status returns `WaveNotEligible` result — does NOT insert records (mirrors the active-wave exclusion guard at `HI_Job.sql:119–133`).
4. `AdviseAsync` with `OverwriteLabelData=true` marks the existing active record inactive before inserting the new one.
5. `AdviseAsync` with `OverwriteLabelData=false` (default) appends; both old and new records coexist; induct disambiguation by ORDER BY rules elects the correct one.
6. After `AdviseAsync`, `IXRefStore.FindActiveByBarcodeAsync(NonUniqueLPN1)` resolves to the new `TransportOrder`.
7. A record with `LEN(BlindLabel) <= 2` is rejected; `IHostAdviceAcknowledger.AckAsync(recId, blindLabel, success=false)` is called.
8. A valid record results in `IHostAdviceAcknowledger.AckAsync(recId, blindLabel, success=true)` called exactly once.
9. An ACK failure (port throws) is caught and logged; the record remains processed; the next record continues.
10. After advising a batch for waveId `'W1'`, `IWaveStore.GetAsync('W1')` returns a `Wave` with status `READY`.

### Open Questions
- `WaveID` extraction from filename: is `CHARINDEX('LD', InboundFilename)` pattern stable across all sites, or ULW-specific? If site-specific, the filename parser should be configurable (regex/format string).
- Should `TransportOrder.ProfileName` be part of `PandaLabelSet` (per label-set) or a top-level carton property (per carton)? Source has it as `PandaData.ProfileName` (per carton) — a single ProfileName governs all label types on the carton.
- `OrderPriority` from the host: should this be stored on `TransportOrder` for disambiguation, or is it only needed at ingestion time (ORDER BY ensures the correct insertion order makes the right record the "newest")?
- `sdisp_HI_ACK_ReadyPandALabel` is called inside an inner try/catch per record. Should C# failure handling be per-record (same) or per-batch? A partial batch leaving some records un-ACKed has operational implications.
- `Bypass` cartons: the advice sets `Bypass=1` on the PandaData record. At induct, LookupCarton returns `CartonStatus='Bypass'`. Should `TransportOrder.Bypass` be a first-class flag checked before printer selection, or derived from `PandaLabelSet` contents?

---

## Cross-Cutting Notes

### Settings discrepancies (domain owner review required)
| Setting | Seed value | Proc auto-create default | Effective |
|---|---|---|---|
| `Reprint Labels` | **0** (deny) | 1 (allow) — in `LookupCarton.sql:175` | 0 |
| `PurgeSetting_UsedData` | **7** | 21 — in `Purge.sql:59` | 7 |
| `PurgeSetting_InactiveData` | **21** | 14 — in `Purge.sql:67` | 21 |

The proc defaults only fire on environments missing the seed (dev/test freshly stood up). The discrepancy between proc-default and seed means these environments get different behavior from production — confirm with domain owner whether the proc defaults should be aligned to the seed values.

### Build order recommendation
The dependency chain for this cluster is strictly ordered:

```
SETTINGS-1 (ISettingsProvider)
    → SETTINGS-2 (reprint rules gate)       → F-ADV1
    → F17 (purge)
    → F20 (MinGap / gap check)              [already backlogged]
    
INBOUND-1 (AdviceMessage + WaveId + ProfileName + Bypass)
    → F18 (XRef, xref store)               → F24 (oLPN)
    → F23 (Wave auto-complete)             → also needs LOCK-1
    → F19 (ProfileName at induct)          [already backlogged]

LOCK-1 (ILockProvider) → F23 (wave auto-complete race guard) → F13 (engine status — later)

F21 (SlotIndex)         → fire-point outbound bundle (F08) [later]

F14 (MandA)             → depends on F23 (wave auto-complete fires on MandA verify)
```

**Minimum viable build order for this cluster:**
1. SETTINGS-1 (`ISettingsProvider` + `InMemorySettingsProvider` + `KnownSettings`)
2. SETTINGS-2 (wire `ReprintLabels` into `InductService` + `CartonAdviceService.AdviseAsync`)
3. INBOUND-1 fields (`WaveId`, `ProfileName`, `Bypass`, `VerifyEnabled` on `TransportOrder` + `AdviceMessage`)
4. F18 (XRef store + xref population in `AdviseAsync`)
5. F23 (Wave domain + `WaveService` + `VerifyStationService` hot-path wiring)
6. LOCK-1 (guard F23 race condition)
7. F24 (oLPN, depends on F18)
8. F14 (MandA, depends on F23)
9. F17 (Purge, depends on SETTINGS-1)
10. F21 (SlotIndex, standalone)
