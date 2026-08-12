# PandA Port — Label Building / ZPL Vetting / Dynamic Apply-Point / Fire-Point Profile Switching
### Source-Coverage Spec Pass · 2026-08-12

---

## Preamble: Source → C# State of Play

Before the four blocks, a precise inventory of what is and is not in the current codebase.

**Built:**
- `ApplyPoint` (parse/format/sign-edge guard) — `npawlakel-ubiquitous-funicular:src/PandA.Core/ApplyPoint.cs`
- `FirePoint` (tracking-device + neglect-print guard) — `FirePoint.cs`
- `FirePointProfile` (case-insensitive `(printerId, labelType)` dict) — `FirePointProfile.cs`
- `FirePointResolver.Resolve(profile, printer, labelType)` — `FirePointResolver.cs`
- `ApplyOrientation` enum (Side/Top) — `ApplyOrientation.cs`
- `PrinterConfig.PrinterType` (ApplyOrientation) — `PrinterConfig.cs:14`
- `LineConfig.ActiveProfile` (single static `FirePointProfile?`) — `LineConfig.cs:15`
- Resolve-on-print: `InductService` lines 84–92 picks the profile, resolves, attaches to `PrintJob` — `InductService.cs:84-93`

**Not built (this cluster):**
- Exception label building (F10): no `ExceptionLabelBuilder`, no `LabelTemplate` model, no `PrintExceptionLabels` gate, no substitution (`<CartonID>`, `<LPN>`), no wiring into the induct path
- ZPL vetting (F11): raw ZPL passes unchanged through `InductService` → `IPrinterGateway.SendAsync`; no strip list applied, no `^LH13,0` injection
- Dynamic apply-point (DYNAP): `FirePointResolver` returns the stored `ApplyPoint` but makes no carton-size/orientation adjustment; no `DefaultApplyDistance`, no `EncoderResolution`
- Profile switching / host-driven ProfileName (PROFSW): `TransportOrder` carries no `ProfileName`; `LineConfig` holds one static profile; no per-carton selection logic; no `NoProfile` outcome

---

## F10 — Exception Label Building

### Source
| File | Key lines |
|---|---|
| `sdisp_TOOL_PA_BuildExceptionLabel.sql` | 8–75 (full proc) |
| `sdisp_PA_LookupCarton.sql` | 88–89 (settings decl), 144–156 (settings fetch), 468–558 (trigger + substitution + PandaData insert) |
| `6.0_PopulateTables/LabelTemplates.sql` | 1–15 (all seed templates) |
| `6.0_PopulateTables/Settings.sql` | line 19: `PrintExceptionLabels` default `0`; line 18: `PrinterStatusSuffix` default `0` |
| `5.0_CreateTables/LabelTemplates.sql` | 9–21 (schema: `LabelType`, `Customer`, `Active`, `Template nvarchar(max)`) |

### Behavior

**Trigger conditions** (`LookupCarton` line 468):
```
PrintExceptionLabels = 1
AND @Bypass = 0
AND @CartonStatus <> 'PrintReady'
```
The `PrintExceptionLabels` global setting seeds to `0` (off); customers commonly flip it on. A bypass carton never gets an exception label. `PrintReady` is the only non-exception status; all others trigger.

**Exception type mapping** (`LookupCarton` lines 470–479):

| `@CartonStatus` value | `@ExceptionReason` passed to builder |
|---|---|
| `@Status_PrintHold` | `'Not Received'` |
| `@Status_Duplicate` | `'Duplicate'` |
| `@Status_NoInfo` | `'No Information'` |
| `@Status_NoRead` | `'No Read'` |
| `@Status_NoData` | `'No Data'` |
| `@Status_LabelConflict` | `'Label Conflict'` |
| anything else | `'-'` (no label built) |

Note: `'DataMismatch'` appears in the builder's CASE (line 49) but is not in the `LookupCarton` exception map — it would be supplied from the verify path (not fully traced here, but the builder must handle it). Flag for domain owner.

**Two build paths** (`LookupCarton` lines 482–498):
- `DCMSExceptions = 1` → call `sdisp_PandA_BuildLabelFromTemplate` (external DCMS system, out of scope for this port — exclude)
- `DCMSExceptions = 0` → call `sdisp_TOOL_PA_BuildExceptionLabel` (local path, **this spec**)

**Builder logic** (`BuildExceptionLabel` lines 41–58):

| Input `@ExceptionType` | Tag looked up in `LabelTemplates` |
|---|---|
| `'NOT RECEIVED'` (case-insensitive; input is `'Not Received'`) | `'Not Received'` |
| `'Duplicate'` | `'DataError_Duplicate'` |
| `'No Information'` | `'DataError_NoInfo'` |
| `'No Read'` | `'ScanError_NoRead'` |
| `'No Data'` | `'ScanError_NoData'` |
| `'Label Conflict'` | `'DataError_LabelConflict'` |
| `'DataMismatch'` | `'DataMismatch'` |
| anything else | `'-'` → no row in LabelTemplates, `@LabelString = NULL` |

`SELECT TOP 1 @LabelString = Template FROM LabelTemplates WHERE LabelType = @ExceptionTag` — note NO `Active = 1` filter! The C# port **should** add an `Active` filter (the table has an active column and an index on `(LabelType, Customer) WHERE Active=1`).

**Seed templates** (all use `Customer='-'`, `Active=1`, from `LabelTemplates.sql`):

| LabelType | Template skeleton | Substitution slots |
|---|---|---|
| `ScanError_NoRead` | `^XA...^FDScanError^FS...^FDNoRead^FS...^FDCartonID = <CartonID>^FS^XZ` | `<CartonID>` |
| `ScanError_NoData` | same with NoData | `<CartonID>` |
| `DataError_NoInfo` | same + barcode: `^BY3,2,170^FO100,750^BC^FD<LPN>^FS` | `<CartonID>`, `<LPN>` |
| `DataError_Duplicate` | same + barcode | `<CartonID>`, `<LPN>` |
| `Not Received` | `^FDNot ^FS^FDReceived^FS...^FDCartonID = <CartonID>^FS^XZ` | `<CartonID>` |
| `DataError_LabelConflict` | `^FDLabelConflict^FS...^FDCartonID = <CartonID>^FS^XZ` | `<CartonID>` |
| `DataMismatch` | `^FDDataMismatch^FS...^FDNoData^FS` | none |
| `ScanError_Conflict` (RecID 13) | `^FDLabelConflict^FS^FDScanError...` | `<CartonID>` |

Note: `ScanError_Conflict` has LabelType `'ScanError_Conflict'` — there is NO path in `BuildExceptionLabel`'s CASE that produces this tag. It appears unreachable in the source. **Flag for domain owner.**

**Substitution rules** (`LookupCarton` lines 501–506):
1. Always: `REPLACE(template, '<CartonID>', cartonListId.ToString())`
2. Only for `Duplicate`, `NoInfo`, `PrintHold`: `REPLACE(template, '<LPN>', lpn)`

The `<LPN>` value is the `@LPN` variable in LookupCarton (the carton's LPN from the database record at the time of lookup), not the blind label.

**PandaData record created for exception** (`LookupCarton` lines 509–536):
- `LabelType1 = 'Exception'`
- `LabelData1 = @PrintLabel` (the substituted ZPL string)
- `LabelData2 = 'Side'` if any side printer exists, else `'Top'` (orientation)
- `LabelBarcode1 = 'ExceptionLabel'` (fixed sentinel value — not a real barcode)
- `VerifyEnabled = 1` (exception labels ARE sent through verify)
- `Bypass = 0`

This becomes a regular `PandaData` record that flows through `PickPrinter` and `Print` just like a normal label. The exception label is a `Label` with `LabelType='Exception'`, `Lpn='ExceptionLabel'`, `Zpl=<filled template>`.

**`sdisp_TOOL_PA_AppendStatusSuffix`:** Appends literal `~HS` to the ZPL string — a Zebra "host status" request command. Does NOT modify label content. Gate: `PrinterStatusSuffix` setting (default 0 = off). This is F12 in the backlog; not in scope here.

### Already Built?
Nothing. `InductService` never builds an exception label. `TransportOrder` has no exception-label path. `Label.LabelType` has no `'Exception'` handling. The `LabelTemplate` repository concept doesn't exist in C#.

### Target Module/Class
```
PandA.Core:
  ExceptionType (enum or discriminated union)
    - NotReceived, Duplicate, NoInformation, NoRead, NoData, LabelConflict, DataMismatch
  LabelTemplate (record)
    - LabelType: string
    - Template: string
    - Active: bool
  ILabelTemplateRepository (port/interface)
    - ValueTask<LabelTemplate?> FindActiveAsync(string labelType, CancellationToken)
  ExceptionLabelBuilder (pure domain logic, no I/O)
    - ExceptionBuildResult Build(ExceptionType type, string cartonId, string? lpn, LabelTemplate template)
    - // Handles <CartonID>/<LPN> substitution, validates template for required slots
  IExceptionLabelPolicy (setting gate)
    - bool PrintExceptionLabels { get; }
    - bool UseDcmsLabels { get; }   // if true → external path, C# delegates/ignores
```

Integration point: `InductService.InductAsync` needs to detect the "exception induct" scenario (the C# equivalent of `CartonStatus != 'PrintReady'`). This is coupled with **F20** (gap-error / read-quality detection) which produces the exception type. **F10 cannot be TDD-sliced independently of F20** — the exception type must come from somewhere. For the spec slice, model F10 as a pure builder that receives a pre-classified `ExceptionType`.

No econtroller dependency. `ILabelTemplateRepository` is the only I/O port (implemented by a Sim in-memory store seeded from the seed data, and later by a real DB adapter).

### Dependencies
- **F20** (gap-error / read-quality detection) provides the `ExceptionType` classification; F10 is downstream of F20
- **F11** (ZPL vetting): exception label ZPL **should also be vetted** before print, like normal labels (the exception label goes through the same `PickPrinter → Print` path which calls VetLabel)
- `IPrinterGateway` — unchanged, just receives a `PrintJob` with `LabelType='Exception'`
- `ILabelTemplateRepository` — new port; Sim implements as in-memory dict from seeded templates

### Acceptance Criteria

1. `ExceptionLabelBuilder.Build(ExceptionType.NoRead, cartonId: "C123", lpn: null, template: seed_NoRead)` returns ZPL with `<CartonID>` replaced by `"C123"` and no `<LPN>` substring remaining.
2. `ExceptionLabelBuilder.Build(ExceptionType.Duplicate, cartonId: "C123", lpn: "LPN001", template: seed_Duplicate)` replaces both `<CartonID>` and `<LPN>` in the ZPL.
3. `ExceptionLabelBuilder.Build(ExceptionType.DataMismatch, ...)` does NOT attempt `<LPN>` substitution (DataMismatch template has no `<LPN>` slot and DataMismatch is not in the LPN-substitution set).
4. Given `ExceptionType.NotReceived`, the builder maps to tag `'Not Received'` (case-insensitive); `ILabelTemplateRepository` returns the correct seed template (RecID 7).
5. Given `ExceptionType` value that maps to `'-'` (unknown), `Build` returns a `NullOrEmpty` label string (no template found) without throwing.
6. When `PrintExceptionLabels = false` (policy gate), `InductService` does NOT call the builder even if carton status is non-PrintReady.
7. When `UseDcmsLabels = true`, `InductService` skips the local builder (delegates to external path — C# yields the DCMS-path exception as a `NotSupported` or passes-through result; exact behavior TBD with domain owner).
8. The resulting exception `Label` has `LabelType="Exception"`, `Lpn="ExceptionLabel"`, and non-empty `Zpl` containing the substituted template.
9. Exception label passes through `VetLabel` before print (same pipeline as normal labels).
10. When the template repository returns `null` for a requested type (template not seeded/active), `Build` returns a `BuildResult.NoTemplate` discriminant — **never** sends a null/empty ZPL string to the printer.

### Open Questions for Domain Owner
1. **`DataMismatch` exception type**: it exists in the builder's CASE but is absent from the LookupCarton exception mapper. Is it produced by the verify path? If so, does it flow through the same `ExceptionLabelBuilder` on the verify side, or a separate call?
2. **`ScanError_Conflict` template** (LabelType in LabelTemplates): no CASE branch in `BuildExceptionLabel` produces that tag. Dead data? Or should `'Label Conflict'` map to `'ScanError_Conflict'` instead of `'DataError_LabelConflict'`? Currently two templates could serve LabelConflict.
3. **`Active` filter on template lookup**: source does `SELECT TOP 1 WHERE labeltype=tag` with **no** `Active=1` filter. Should the C# port add `Active` filtering? (The index is filtered on `Active=1`.)
4. **LPN value for substitution**: the source uses `@LPN` (the carton's LPN from DB lookup). In C#, is `Label.Lpn` the right equivalent, or is this the blind label `TuId`? At exception time there may be no `Label.Lpn` if the lookup failed.
5. **Exception label orientation** (LabelData2 = 'Side' or 'Top' based on printer existence): should C# replicate this dynamic check, or default to `Side` always (as induct defaults to Side)?

---

## F11 — ZPL Vetting (VetLabel)

### Source
| File | Key lines |
|---|---|
| `sdisp_TOOL_PA_VetLabel.sql` | 8–70 (full proc, 35 REPLACEs) |
| `sdisp_PA_Print.sql` | 64–86 (settings fetch + VetLabel call gate) |
| `6.0_PopulateTables/Settings.sql` | line 10: `FilterLabels` RecID 3, default `1` (ON) |

### Behavior

**Gate:** `FilterLabels` global setting (RecID 3, seed default **1 = ON**). Checked in `sdisp_PA_Print` line 81: `IF @FilterLabels = 1 EXEC sdisp_TOOL_PA_VetLabel @LabelString OUTPUT`.

VetLabel is **not a validator** — it never rejects a label. It is a **sanitizer**: strip printer-device-configuration ZPL commands that should not be in a print job, and inject the correct label-home offset. The name "vetting" is misleading — call it `ZplSanitizer` in C#.

**Complete strip list** (applied via sequential string replacement, order matters — `^XA^MCY^XZ` must be removed before `^XA` is modified, `VetLabel` lines 33–66):

| Literal string removed | ZPL command / purpose |
|---|---|
| `^XA^MCY^XZ` | Map Clear (whole label) |
| `^XA^MD-7^XZ` | Media Darkness (whole label) |
| `^SZ2` | ZPL II mode select |
| `^PRA` | Print Rate 2 (letter variant) |
| `^PRB` | Print Rate 3 |
| `^PRC` | Print Rate 4 |
| `^PRD` | Print Rate 6 |
| `^PRE` | Print Rate 8 |
| `^PR2` | Print Rate 2 (numeric variant) |
| `^PR3` | Print Rate 3 |
| `^PR4` | Print Rate 4 |
| `^PR5` | Print Rate 5 |
| `^PR6` | Print Rate 6 |
| `^PR8` | Print Rate 8 |
| `^PR9` | Print Rate 9 |
| `^PON` | Print Orientation Normal |
| `^PMN` | Print Mirror Image Normal |
| `^CI0` | Change International Font/Encoding |
| `^LRN` | Label Reverse Print Normal |
| `^JSN` | Sensor Select Normal |
| `^MMT` | Print Mode Tear-off |
| `^MTT` | Media Type Thermal Transfer |
| `^MTD` | Media Type Direct Thermal |
| `^MD16` | Media Darkness 16 |
| `^MD0` | Media Darkness 0 |
| `^MD2` | Media Darkness 2 |
| `^MNY` | Media Tracking |
| `^TA000` (^ prefix) | Tear-off position |
| `~TA000` (~ prefix) | Tear-off position (host prefix) |
| `~JSN` (~ prefix) | Change Backfeed Sequence |
| `^MCN` | Map Clear Normal |
| `^PQ1,0,0,N` | Print Quantity 1 |
| `^POI^FS` | Print Orientation Inverted (with FS terminator) |

**Substitution (not removal):**
- `^XA` → `^XA^LH13,0` — injects label-home offset of 13 dots horizontally, 0 vertically

**Ordering constraint:** The two whole-label-wrap removes (`^XA^MCY^XZ`, `^XA^MD-7^XZ`) must be applied **before** the `^XA` substitution so those form prefixes are stripped whole rather than partially modified to `^XA^LH13,0^MCY^XZ`. C# implementation must replicate this order.

**Scope:** Applied to the full ZPL string as a single `VARCHAR(MAX)`. A label ZPL may contain multiple `^XA…^XZ` pairs (multi-copy ZPL). The `^XA` substitution will inject `^LH13,0` after **every** `^XA` occurrence. This is intentional source behavior.

**`~HS` suffix (AppendStatusSuffix, F12):** Called after VetLabel in `sdisp_PA_Print` lines 89–110 when `PrinterStatusSuffix=1` (default 0). Does not modify label content. Appends literal `~HS` to the ZPL string to request a Zebra printer host status response. **This is F12 (Low priority); it is NOT part of F11.** The C# port should model it as a separate `IZplPostProcessor` step in the pipeline.

### Already Built?
Nothing. `Label.Zpl` is `string`, passed as-is to `IPrinterGateway.SendAsync` in `InductService`. No strip list is applied anywhere.

### Target Module/Class
```
PandA.Core:
  ZplSanitizer (pure static logic, no I/O, no state)
    - string Sanitize(string zpl)
    // All 33 strip-and-substitute operations; order-preserving
    // Returns empty string if input is null/empty (passthrough)

  IZplPipeline (optional composition boundary)
    - string Process(string zpl)
    // Default impl: ZplSanitizer, then optional AppendStatusSuffix (F12 only if PrinterStatusSuffix=1)
```

The `ZplSanitizer.Sanitize` method is pure: string-in, string-out, no settings dependency. The `FilterLabels` gate belongs in `InductService` (or a print orchestrator): `if (lineConfig.FilterLabels) zpl = ZplSanitizer.Sanitize(zpl)`. `FilterLabels` should be exposed as a `bool` on `LineConfig` (or a global settings port).

**Note on `^PR` variants:** `^PR2` through `^PR9` plus `^PRA`–`^PRE` must each be stripped as distinct literals. Using a regex like `\^PR[2-9A-E]` is acceptable but would not match the source exactly for edge cases. The spec is conservative: implement as 14 distinct literal replacements to match source exactly. The implementation could use a frozen array of strip-tokens for readability.

### Dependencies
- `LineConfig` (or a global settings service) must expose `FilterLabels: bool`
- Called before `IPrinterGateway.SendAsync` in `InductService` (or a wrapping print pipeline)
- Exception labels (F10) go through the same print path and therefore also get vetted
- F12 (`AppendStatusSuffix`) sits after F11 in the same pipeline; must not interfere

### Acceptance Criteria
1. `ZplSanitizer.Sanitize("^XA^SZ2^PR4^FO0,0^FDHello^FS^XZ")` → `"^XA^LH13,0^FO0,0^FDHello^FS^XZ"` (strips `^SZ2`, `^PR4`, injects `^LH13,0`).
2. `ZplSanitizer.Sanitize("^XA^MCY^XZ^XA^FO0,0^FDTest^FS^XZ")` → `"^XA^LH13,0^FO0,0^FDTest^FS^XZ"` (whole `^XA^MCY^XZ` block removed before `^XA` substitution; single `^LH13,0` injected).
3. `ZplSanitizer.Sanitize("^XA^MD-7^XZ^XA^PON^LRN^FDLabel^FS^XZ")` → `"^XA^LH13,0^FDLabel^FS^XZ"` (media-darkness block removed, `^PON` and `^LRN` stripped).
4. `ZplSanitizer.Sanitize("^XA^PQ1,0,0,N^POI^FS^FDFoo^FS^XZ")` → `"^XA^LH13,0^FDFoo^FS^XZ"` (print quantity and inverted orientation both stripped).
5. `ZplSanitizer.Sanitize("^XA~TA000~JSN^FDBar^FS^XZ")` → `"^XA^LH13,0^FDBar^FS^XZ"` (tilde-prefixed variants stripped).
6. Multi-copy input `"^XA^FDCopy1^FS^XZ^XA^FDCopy2^FS^XZ"` → both `^XA` occurrences become `^XA^LH13,0` (label home injected in both copies).
7. `ZplSanitizer.Sanitize("")` returns `""` without throwing.
8. When `lineConfig.FilterLabels = false`, `InductService` sends the raw ZPL to the gateway unchanged (gate respected).
9. `ZplSanitizer.Sanitize("^XA^MD2^MD0^MD16^FDTest^FS^XZ")` strips all three darkness variants in a single call.
10. Strip is **not** a remove-first-occurrence operation: `ZplSanitizer.Sanitize("^XA^PR2^PR2^FDFoo^FS^XZ")` removes both `^PR2` occurrences (source `REPLACE` removes all occurrences).

---

## DYNAP — Dynamic Apply-Point Resolution

### Source
| File | Key lines |
|---|---|
| `sdisp_TOOL_CUSTOM_DynamicApplyPoint.sql` | 8–203 (full proc) |
| `sdisp_PA2BP_SendPrinterFirePoints.sql` | 83–88 (DynamicPrintPoint setting fetch), 108–150 (profile+fire-point CTE), 219–228 (DynamicApplyPoint call, gate commented out), 231–234 (tag-write assembly) |
| `sdisp_PA_PickPrinter.sql` | 426–427 (orientation-based CartonDimensionInput computation), 430–438 (SendFirePoints call) |
| `5.0_CreateTables/PrinterDetails.sql` | `AttributeName='DefaultApplyDistance'` row |
| `5.0_CreateTables/LabelTypes.sql` | `LabelWidth INT` column |
| `6.0_PopulateTables/Settings.sql` | line 8: `DynamicPrintPoint` RecID 23, default `1` |

### Behavior

**Purpose:** Translate the stored `ApplyFirePoint` inch-and-edge string (e.g. `'1T'`, `'0M'`, `'-.4M'`) into a **final integer step-pulse count** that the PLC can use directly. This depends on the carton's physical size and the printer's orientation (Side vs Top).

**Inputs** (`DynamicApplyPoint.sql` lines 8–17):
- `@InputtedApplyPoint VARCHAR(32)` — e.g. `'1T'`, `'0L'`, `'-.4M'` (the `PrinterFirePoints.ApplyFirePoint` value, already parsed by `ApplyPoint`)
- `@CartonSize DECIMAL(8,2)` — carton dimension **in step pulses** (see below for how it's computed per orientation)
- `@Orientation VARCHAR(32)` — `'side'` or `'top'` (from `PrinterDetails.AttributeName='PrinterType'`)
- `@PrinterRecID VARCHAR(32)` — used to look up `DefaultApplyDistance` from `PrinterDetails`
- `@PandaID VARCHAR(32)` — logging only
- `@LabelWidth VARCHAR(32)` — label width in inches (***source bug: hardcoded to `4` at call site*** — `SendPrinterFirePoints.sql` line 227; the `@LabelWidth` variable fetched from `LabelTypes` on line 147 is never forwarded)
- `@OutputtedApplyPoint VARCHAR(32) OUTPUT` — final integer step-pulse count

**CartonSize pre-conversion** (done in `PickPrinter` before the call, lines 426–427):
- **SIDE**: `@CartonDimensionInput = @CartonLength` (raw carton length in step pulses from dimensioner)
- **TOP**: `@CartonDimensionInput = CAST( (@CartonHeight / 25.4) / CAST(@EncoderResolution AS FLOAT) AS INT)`
  - `CartonHeight` is in millimetres (from dimensioner)
  - `/25.4` converts mm → inches
  - `/ EncoderResolution` converts inches → step pulses (see unit note below)
  - Result: carton height expressed in step pulses

**EncoderResolution unit (source bug alert):** Setting description says `"steppulses/inch"` but the algebra is `inches / EncoderResolution = step_pulses`, meaning `EncoderResolution = inches / pulse`. The unit is **inches per step pulse** (not pulses per inch). The seed default is `0.25` (inches/pulse = 4 pulses/inch). The C# port must document this inversion. `PickPrinter` has a different default (`0.2`) — a source inconsistency; the actual site-calibrated value comes from the `Settings` table at runtime.

**DefaultApplyDistance:** Per-printer integer value from `PrinterDetails WHERE AttributeName='DefaultApplyDistance'` (line 70–76). Added to all computed outputs as a fixed offset accounting for the physical distance from the tracking sensor to the applicator head.

**Validation** (lines 81–96): if `RIGHT(InputtedApplyPoint, 1) NOT IN ('L','T','M')` → log error, RETURN without setting output. The C# equivalent should return a `Result` discriminant.

**Side orientation formulas** (lines 123–165), where:
- `ApplyInInches` = numeric prefix of InputtedApplyPoint (inches from edge)
- `CartonSizeInInches` = `CartonSize_pulses * EncoderResolution` (inches, via line 108)
- `EncoderRes` = `EncoderResolution` setting (inches/pulse)
- `LabelWidth` = 4 (inches, hardcoded at call site)
- `DefaultDistance` = per-printer integer from `PrinterDetails`

| Edge | Formula |
|---|---|
| `L` (leading) | `(ApplyInInches / EncoderRes) + DefaultDistance` |
| `T` (trailing) | `((CartonSizeInInches - ApplyInInches - LabelWidth) / EncoderRes) + DefaultDistance` |
| `M` (middle) | `((CartonSizeInInches / 2 − LabelWidth/2) + (sideOp + ApplyInInches)) / EncoderRes + DefaultDistance` |

Where `sideOp` (middle only):
- `ApplyInInches < 0` → `−LabelWidth/2`
- `ApplyInInches > 0` → `+LabelWidth/2`
- `= 0` → `0`

The comment (lines 114–122) says: "label is 4 inches wide, so half is 2; this centers the label on the middle line." The `sideOp` shifts the label center to account for half its width.

**Top orientation formula** (lines 167–186, `L` edge only — `T` and `M` not implemented for Top):
```
OutputtedApplyPoint = ROUND(0.113 * CartonSize + (ApplyInInches / EncoderRes), 0) + DefaultDistance
```
- `0.113` is a **magic geometry constant** (undocumented in source). It represents the angular scaling factor for the top-apply head geometry. The domain owner must confirm whether this is site-calibrated or universal.
- `CartonSize` here is the already-converted pulse count (height in pulses, from PickPrinter pre-conversion)
- Top orientation only supports `L` (leading) edge. If `T` or `M` is passed, `@OutputtedApplyPoint` is never set → returns `0` or whatever was initialized. This is a **source gap**: top apply with trailing/middle edge silently produces a zero or uninitialized output. The C# port should return a `Result.NotSupported` for that combination.

**DynamicPrintPoint gate (source bug):** `SendPrinterFirePoints` lines 219–229 show `--IF @DynamicPrintPoint = 1` **commented out**. The dynamic apply adjustment is **always called** at runtime regardless of the `DynamicPrintPoint` setting value. The C# port should either: (a) honor the setting properly (restore the gate), or (b) always compute (document the deviation). **Ask domain owner.**

**Final output is always INT:** `SET @OutputtedApplyPoint = CONVERT(INT, CONVERT(Decimal(8,1), computed) + DefaultDistance)` — truncated to integer before being written to PLC tags.

### Already Built?
- `ApplyPoint.Parse` and `ApplyPoint` model ✓ (handles the inch+edge input)
- `ApplyOrientation` enum ✓
- `PrinterConfig.PrinterType` ✓ (Side/Top)

**Not built:**
- No `DynamicApplyPointCalculator` or equivalent
- `PrinterConfig` has no `DefaultApplyDistance` property
- No `EncoderResolution` setting seam
- `FirePointResolver` returns the stored `ApplyPoint` but never computes the integer pulse output
- `TransportOrder` / `InductService` carry no carton dimensions (height/length)
- No carton-height or carton-length flows through the C# pipeline

### Target Module/Class
```
PandA.Core:
  DynamicApplyPointInput (record)
    - ApplyPoint StoredPoint         // parsed ApplyFirePoint from FirePoint
    - decimal CartonSizePulses       // pre-converted (Side=length, Top=height/25.4/res)
    - ApplyOrientation Orientation
    - decimal LabelWidthInches       // from PrinterConfig or LabelTypeConfig (not hardcoded)
    - decimal EncoderResolution      // inches/pulse (from line/global settings)
    - int DefaultApplyDistance       // per-printer, from PrinterConfig

  DynamicApplyPointResult (discriminated union / Result)
    - Ok(int stepPulses)
    - InvalidEdge(Edge edge)          // edge not in L/T/M
    - EdgeNotSupportedForOrientation  // e.g. T/M on Top
    - NoDefaultDistance               // DefaultApplyDistance not configured

  DynamicApplyPointCalculator (pure static / sealed class)
    - DynamicApplyPointResult Calculate(DynamicApplyPointInput input)

PrinterConfig additions:
  - int? DefaultApplyDistance        // new: from PrinterDetails AttributeName='DefaultApplyDistance'
  - decimal? LabelWidthInches        // optional: from LabelTypes (fix the source's hardcoded-4 bug)

LineConfig additions (or global settings seam):
  - decimal EncoderResolution        // or ISettingsProvider.Get("EncoderResolution")
  - bool DynamicPrintPoint           // gate; currently always-on in source (see source bug note)
```

**CartonDimensions must flow into `InductService`:** The induct scan (msg 281) carries carton height and length from the dimensioner. Today `InductService.InductAsync(lineId, blindLabel)` has no dimension parameters. This must change: either add a `CartonDimensions` parameter, or attach dimensions to `TransportOrder` at induct time (e.g. on the advice message or the scan event).

### Dependencies
- Resolved `FirePoint.ApplyFirePoint` (already built) — provides the `ApplyPoint` input
- `PrinterConfig.DefaultApplyDistance` (new field)
- `LineConfig.EncoderResolution` or a settings service
- Carton dimensions (height/length) on induct — new parameter to `InductService.InductAsync` or on `TransportOrder`
- **PROFSW** (below): DYNAP runs **after** profile resolution; it takes the profile's `ApplyFirePoint` and converts it — so profile must be resolved first
- **F21** (GAP — slot index / PLC `vPA.Assign[i]`): DYNAP output feeds the `ApplyPoint[printerId]` tag written to PLC; F21 provides the slot index

### Acceptance Criteria
1. `Calculate(side, L="1L", cartonSize=400, encoderRes=0.25, labelWidth=4, defaultDist=50)` → `(1/0.25) + 50 = 54` step pulses.
2. `Calculate(side, T="1T", cartonSize=400, encoderRes=0.25, labelWidth=4, defaultDist=50)` → `((400*0.25 - 1 - 4) / 0.25) + 50 = (100-5)/0.25 + 50 = 380 + 50 = 430` pulses.
3. `Calculate(side, M="0M", cartonSize=400, encoderRes=0.25, labelWidth=4, defaultDist=50)` → center calculation: `(400*0.25/2 - 4/2 + 0) / 0.25 + 50 = (50-2)/0.25 + 50 = 192 + 50 = 242` pulses.
4. `Calculate(side, M="-.4M", ...)` → `sideOp = -(4/2) = -2`; `ApplyInInches = -0.4`; formula: `(50 - 2 + (-2 + -0.4)) / 0.25 + 50`.
5. `Calculate(top, L="0L", cartonSize=400, encoderRes=0.25, defaultDist=50)` → `ROUND(0.113*400 + 0/0.25, 0) + 50 = 45 + 50 = 95` pulses.
6. `Calculate(top, T="1T", ...)` returns `EdgeNotSupportedForOrientation` (trailing not implemented for Top).
7. `Calculate(side, edge='X', ...)` returns `InvalidEdge` without throwing.
8. `Calculate(...)` with `LabelWidthInches = 6` (not hardcoded 4) uses the provided width in trailing/middle formulas — this verifies the source-bug fix.
9. `DynamicPrintPoint = false` in line config → `InductService` skips DYNAP and uses the stored `ApplyPoint` directly (raw inch+edge notation).
10. Result is always `INT`-truncated (no fractional pulse counts in output).

### Open Questions for Domain Owner
1. **Magic constant `0.113`**: is this a universal physical constant for all top-apply heads, or is it site-calibrated? Should it be in per-printer config or global settings?
2. **`DynamicPrintPoint` gate**: the source has this commented out (always-on). Should C# restore the gate (default ON per seed) or always compute? If always compute, remove the setting.
3. **LabelWidth source**: source hardcodes `4` despite fetching from `LabelTypes`. Which is authoritative — the site's label type, or 4? If the label type, `PrinterConfig` needs a `LabelWidthInches` per label type.
4. **Top apply T/M edges**: are trailing and middle positions ever needed for a top-apply printer? If yes, what formula applies?
5. **Carton dimensions on induct**: does height/length always arrive with the scan in msg 281, or can it arrive separately (msg 285 late-assign)? This determines where to attach dimensions in the C# model.
6. **`EncoderResolution` inconsistency**: `DynamicApplyPoint` defaults to `0.25`, `PickPrinter` defaults to `0.2`. Which is the canonical default? Is there one setting or two?

---

## PROFSW — Fire-Point Profile Switching + Host-Driven ProfileName

### Source
| File | Key lines |
|---|---|
| `sdisp_PA2BP_SendPrinterFirePoints.sql` | 108–150 (`CurrentProfile` + `CurrentFirepoints` CTEs — the full resolution query) |
| `sdisp_PA_PickPrinter.sql` | 75 (`@ProfileName` decl), 190–196 (read from PandaData), 383–390 (per-label loop extract), 437 (pass to SendFirePoints) |
| `sdisp_PA_LookupCarton.sql` | 120–124 (`@ProfileName` decl, `@IsValid INT = -1`) |
| `6.0_PopulateTables/LabelProfileHeader.sql` | all rows — seed data |
| `5.0_CreateTables/LabelProfileHeader.sql` | schema: `Active INT`, `ProfileName VARCHAR(64)` |
| `5.0_CreateTables/LabelProfileDetail.sql` | schema: `LabelHeaderRecID`, `PrinterFirepointRecID`, `Active INT` |
| `architecture-log/010-fire-point-profiles.md` | §3 (C# model, backlog items) |
| `architecture-log/011-source-coverage-gap-analysis.md` | GAP F19 |

### Behavior

**The source profile model** is not a "global active profile" — it is a **per-carton profile selection keyed by name**:

1. `PandaData.ProfileName` is set by the host advice message (one ProfileName per carton)
2. `PickPrinter` (line 192) reads the ProfileName from `PandaData` and attaches it to each label in its working table: `UPDATE @Labels SET profilename = PandaData.ProfileName WHERE PandaDataID = @PandaDataID`
3. In the print loop (line 388), ProfileName is extracted per-label and passed to `SendPrinterFirePoints` (line 437)
4. `SendPrinterFirePoints` uses it in the CTE (lines 108–120):
   ```sql
   CurrentProfile AS (
     SELECT lph.RecID, lpd.PrinterFirepointRecID
     FROM LabelProfileHeader lph
     INNER JOIN LabelProfileDetail lpd ON lpd.LabelHeaderRecID = lph.RecID
     WHERE lph.Active = 1
       AND lph.ProfileName = @ProfileName   -- case-insensitive match
       AND lpd.Active = 1
   )
   ```
   Then joins `PrinterFirePoints` filtered to `(PrinterRecID, LabelDefRecID)` matching the current carton's printer + label.

**Multiple `Active=1` profiles coexist** (seed evidence): 35+ profiles have `Active=1` in the seed. The `Active` flag does NOT mean "currently selected" — it means "available for selection." The host selects among available profiles by sending the ProfileName per carton.

**Operator/GUI switching of `Active`:** Setting `LabelProfileHeader.Active = 0` deactivates a profile (it will never be selected regardless of what the host sends). The GUI to set this is out of scope (Blazor — backlog). The backend seam for activation/deactivation is an `ActivateProfile`/`DeactivateProfile` method, called from the future GUI layer.

**What happens when ProfileName is missing or unknown** (`LookupCarton` line 120: `@IsValid INT = -1`):
- `LookupCarton` checks profile validity in a block that leads to `@Status_NoProfile` if `@IsValid = 0`
- From GAP F19 in backlog: "Missing/unknown ProfileName → `NoProfile` status, carton not printed"
- The C# port must add this check: if ProfileName is non-null but not found among active profiles → return a `NoProfile` induct outcome

**Seed ProfileName patterns** (from `LabelProfileHeader.sql`): Names like `A10A51D10`, `A10000D10`, `A20A61000`, `MDSMDSD10`, `Test_TOPAPPLY` — these appear to be encoded product/lane configurations. The naming convention is domain-specific; the C# model must be case-insensitive and treat names as opaque strings.

**`LabelProfileDetail.Active`:** Both the header (`lph.Active = 1`) and the detail (`lpd.Active = 1`) must be active for a fire-point slot to be reachable. The C# model must replicate this two-level active gate.

### Already Built?
**Current C# shape:**
- `FirePointProfile(name, entries)` — a single named profile object ✓
- `LineConfig.ActiveProfile: FirePointProfile?` — one static profile per line ✓
- `InductService` (line 85): `if (context.Config.ActiveProfile is { } profile)` — resolves from the one static profile ✓

**Gaps:**
- `TransportOrder` has no `ProfileName` field — there is nowhere to store the per-carton ProfileName from advice
- `LineConfig.ActiveProfile` is a single reference — cannot hold a registry of multiple available profiles
- `InductService` does not select a profile by carton name
- No `NoProfile` outcome in `InductResult` / `InductStatus`
- No profile-existence validation at induct time (GAP F19)
- `FirePointProfile` is always static config-time — no runtime activation/deactivation concept

### Target Module/Class

**`TransportOrder` addition:**
```
public sealed class TransportOrder
{
    // NEW: host-supplied per-carton profile selector
    public string? ProfileName { get; private set; }   // null = use line default

    // Set from advice (add to constructor and OverwriteAdvice)
    // Source: PandaData.ProfileName
}
```

**`LineConfig` refactoring:**
```
public sealed class LineConfig
{
    // REPLACE: FirePointProfile? ActiveProfile
    // WITH:
    /// Registry of available profiles (Active=1), keyed by ProfileName case-insensitive.
    /// Populated from LabelProfileHeader (Active=1) + LabelProfileDetail (Active=1) + PrinterFirePoints.
    /// Operator GUI will add/remove/activate entries here (future).
    public IReadOnlyDictionary<string, FirePointProfile> ProfileRegistry { get; }

    /// Optional fallback for lines that have one static profile and no per-carton ProfileName.
    /// Equivalent of current ActiveProfile; null means "no fire points configured."
    public FirePointProfile? DefaultProfile { get; }

    // Migration: keep ActiveProfile as a computed property = DefaultProfile (for backward compat)
    public FirePointProfile? ActiveProfile => DefaultProfile;
}
```

**`InductService` profile-selection logic:**
```csharp
FirePointProfile? ResolveProfile(LineConfig config, TransportOrder order)
{
    if (order.ProfileName is { } name)
    {
        if (!config.ProfileRegistry.TryGetValue(name, out var profile))
            return null;  // → NoProfile result
        return profile;
    }
    return config.DefaultProfile;  // fallback for no-ProfileName cartons
}
```

**`InductStatus` / `InductResult` addition:**
```
InductStatus.NoProfile  // ProfileName set but not found among active profiles → do not print
```

**`IProfileStore` (new port for future GUI activation):**
```
PandA.Core:
  interface IProfileStore
    ValueTask<IReadOnlyList<FirePointProfile>> GetActiveProfilesAsync(string lineId, CancellationToken)
    ValueTask ActivateAsync(string lineId, string profileName, CancellationToken)
    ValueTask DeactivateAsync(string lineId, string profileName, CancellationToken)
```
This port is provisioned but not wired to the operator GUI until that Blazor phase lands. The Sim implements it in-memory.

**`CartonAdviceService` addition:** The advice message (C# `AdviseAsync`) must accept and store `ProfileName` on the `TransportOrder`. Update `PandaLabelSet` or a separate advice parameter (design choice — probably as a constructor arg on `TransportOrder`).

### Dependencies
- `TransportOrder.ProfileName` → must be set at advice time (MP1 advice path)
- `LineConfig.ProfileRegistry` → replaces `ActiveProfile` (non-breaking if `ActiveProfile` is kept as computed property pointing to `DefaultProfile`)
- `FirePointResolver` — unchanged; receives the already-resolved `FirePointProfile` per existing design
- **DYNAP**: PROFSW resolves the profile first; DYNAP receives the resolved `FirePoint.ApplyFirePoint` and converts it to pulses
- **GAP F19**: PROFSW and F19 are the same feature. F19's "profile existence check" IS the `NoProfile` outcome described here; they should be implemented as a single slice
- **Operator GUI** (Blazor, backlog): `IProfileStore.ActivateAsync/DeactivateAsync` are the backend seams that the GUI will call; they can be stubbed with no-ops until then

### Acceptance Criteria
1. `InductService.InductAsync` for a carton with `ProfileName="A10A51D10"` resolves fire points from the `A10A51D10` profile, not the `DefaultProfile`.
2. `InductService.InductAsync` for a carton with `ProfileName=null` uses `LineConfig.DefaultProfile` (backward-compatible with existing tests).
3. `InductService.InductAsync` for a carton with `ProfileName="NonExistent"` returns `InductResult` with `Status=NoProfile` and does NOT dispatch any `PrintJob` to the gateway.
4. `LineConfig` with a `ProfileRegistry` containing two profiles (`"ProfileA"`, `"ProfileB"`) and `DefaultProfile=ProfileA` correctly routes cartons by their per-carton ProfileName.
5. ProfileName matching is **case-insensitive**: `ProfileName="a10a51d10"` resolves the same profile as `"A10A51D10"`.
6. `CartonAdviceService.AdviseAsync` sets `TransportOrder.ProfileName` from the incoming advice; a subsequent `OverwriteAdvice` call with a new ProfileName updates the field.
7. `CartonAdviceService.AdviseAsync` with a null/absent ProfileName sets `TransportOrder.ProfileName = null`; induct falls back to `DefaultProfile` (no error).
8. `FirePointResolver.Resolve(profile, printerId, labelType)` is unchanged — it does not need to know how the profile was selected.
9. The two-level active gate: a `FirePointProfile` built from `LabelProfileDetail.Active=0` rows only cannot be selected (detail rows with `Active=0` are excluded when building the profile registry).
10. Existing tests that use `LineConfig` with a non-null `ActiveProfile` still pass (computed `ActiveProfile` property returns `DefaultProfile`).

### Open Questions for Domain Owner
1. **`Active` semantics on `LabelProfileHeader`**: the seed has 35+ `Active=1` profiles simultaneously. Does the `Active` flag mean "selectable by the host" (our interpretation) or is there a separate "currently active for this line" concept? The profile page in the GUI presumably shows which are selectable.
2. **One ProfileName or many per carton**: in the source a single ProfileName is set per `PandaData` record and applies to all labels of that carton. Is there ever a need for different profiles for different label types on the same carton?
3. **Fallback behavior when ProfileName is missing**: if the host sends no ProfileName (empty string / null), is `DefaultProfile` correct, or should that also be `NoProfile`? Some sites may require every carton to have an explicit ProfileName.
4. **LabelProfileDetail.Active**: when is a detail row set `Active=0`? Is this used to temporarily disable individual fire-point slots within a profile without deleting them?
5. **Operator activation granularity**: does the operator activate whole profiles (header) or individual printer+label slots within a profile (detail)? Both `Active` columns exist.
6. **The `GAP F19` `@IsValid` block** in `LookupCarton`: this is where `NoProfile` status is set in the source. The `@IsValid = -1` init + subsequent profile-validation query is not fully shown in the excerpts reviewed. Can the domain owner confirm: if the profile is not found → carton status = NoProfile → no print, or → exception label, or → something else?

---

## Cross-Cluster Integration Notes

The four features form a pipeline in the order a carton traverses:

```
[Advice]
  └─ CartonAdviceService stores ProfileName on TransportOrder (PROFSW)

[Induct Scan]
  ├─ Gap/quality detection (F20) → ExceptionType → ExceptionLabelBuilder (F10)
  │     └─ Exception label ZPL → ZplSanitizer (F11) → IPrinterGateway
  └─ Normal carton:
        ├─ ProfileName → resolve FirePointProfile (PROFSW / F19)
        ├─ FirePointResolver → ApplyPoint (inch+edge)
        ├─ DynamicApplyPointCalculator → integer step pulses (DYNAP)
        ├─ ZplSanitizer (F11) applied to label ZPL before send
        └─ PrintJob(printer, zpl, firePoint) → IPrinterGateway
```

**Sequencing constraint:** PROFSW must land before DYNAP (DYNAP needs the resolved ApplyPoint from the profile). F10 can be built independently as a pure builder (tested without F20's classification, using a test ExceptionType directly). F11 is a pure string function — simplest, build first.

**Suggested implementation order:** F11 → F10 (pure builder) → PROFSW (wires TransportOrder + registry) → DYNAP (adds dimension inputs + calculator).
