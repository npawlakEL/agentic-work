# 003 — PandA → eController Concept & Functionality Map

**Author:** Senior Coder (auto-engaged)
**Date:** 2026-08-11
**Status:** Design mapping (drives the Phase-1 spec). "Bake in" econtroller-native primitives; avoid
reproducing PandA's SQL structures.

> User direction: configuration = JSON files (no config DB); the carton = an econtroller **Transport
> Order**; map every PandA construct to its econtroller-native equivalent so the port is as idiomatic
> ("baked in") as possible. Only live host data uses dynamic-table integration.

---

## 1. Data & runtime state

| PandA (SQL) | eController-native | Notes |
|---|---|---|
| `PandaCartonList` / a carton | **`MfcTransportOrder`** (the TU) | The carton IS a transport order flowing through the line. No carton-list table. |
| `PandaData` (per-carton label data, barcodes, types, statuses) | **TU extension data** `trans.ApplyExtension<PandaData>(…)` / `TryGetExtension<PandaData>` | Rides with the TU; not a table. |
| `BlindLabel` / carton barcode | `MfcTransportOrder.TuId` | Primary carton identity. |
| `CartonStatus` (`Settings_CartonStatuses` vocab) | `MfcTransportOrder.TuStatus` + a PandA status vocabulary (config) | Native status field; PandA status names live in config/enum. |
| `PandaDataXRef` (LPN↔carton xref) | TU extension field(s) or **dynamic table** if sourced from host | Prefer extension; dynamic table only if host-fed. |
| Host inbound label data (`HI_*`, `sdisp_HI_DCMSCore_Inbound_PandALabels_Job`) | **Dynamic-table integration for host data** | The one sanctioned DB touch: inbound host/WMS data. |
| `Wave` / `WaveRange` (operational) | *(bookmarked)* later: TU grouping/attribute or dynamic table | Not Phase 1. |
| `EventLog` / `uEventLog` / `MessageLog` | `MfcLog` + `ILogger<T>` + telegram tables (`AtTelegramIn/Out`) | Native logging; no PandA event tables. |

## 2. Configuration → JSON files (no DB)

Authored per project under `econfig_root/<App>_<Env>/`, loaded by EffortlessConfiguration, hot-reloadable
(same family as `MfcAction.json` / `MfcLayout.json`).

**File 1 — `PandaLine.json` (per-line array; one block per PandA line):**
```
[
  { line details (from PandAs + PandADetails),
    printers: [ { printer (Printers) + details (PrinterDetails) + firePoints (PrinterFirePoints) } ],
    lanes:    [ { LaneDef } ] },
  { …second line… }
]
```
Sources: `PandAs`, `PandADetails`, `Printers`, `PrinterDetails`, `PrinterFirePoints`, `LaneDef`.
> Placement note: fire points nested under their printer (physical property of the printer on the line).
> Confirm vs. keeping them in File 2.

**File 2 — `PandaLabeling.json` (shared labeling/behavior config):**
Sources: `LabelProfileHeader/Detail/Map`, `LabelTemplates`, `LabelTypes`, `LabelDef`,
`LabelPrintLocations`, `Settings_DefaultAttributes`, `Settings_LabelBufferOrder`.

**Global settings / vocab** — `Settings` (behavior toggles), `Settings_CartonStatuses`, `PandAState`,
`PrinterState` → a `PandaSettings.json` section (or folded into File 2), plus C# enums where fixed.

## 3. Entry points (message points) → MFC actions

Wired via `MfcAction.json` (Mp + TelegramType + TypeName/MethodName + ServiceValues, sequenced) at the
print-and-apply message points defined in `MfcLayout.json`.

| PandA entry proc | Action (IMfcAction) method | Trigger (message point) |
|---|---|---|
| `sdisp_BP2PA_Scan_Induct` (induct msg 281) | `PandaActions.ScanInduct` | induct scanner MP |
| `sdisp_BP2PA_Print` | `PandaActions.SendPrintCommand` | printer MP |
| `sdisp_BP2PA_Scan_Verify` | `PandaActions.VerifyScan` | verify scanner MP |
| `sdisp_BP2PA_Status_Printer` | `PandaActions.PrinterStatus` | printer status MP |
| `sdisp_BP2PA_Status_Zone` | `PandaActions.ZoneStatus` | zone MP |
| `sdisp_BP2PA_Event` | `PandaActions.LineEvent` | event MP |
| `sdisp_MA_Scan_*` (manual apply) | `PandaActions.ManualInduct/Verify` | manual station MP |

## 4. Core engine logic (SP bodies) → C# services

Actions stay thin; logic lives in injectable services (unit-testable).

| PandA proc | eController C# |
|---|---|
| `sdisp_PA_LookupCarton` | `ICartonLookupService` — resolve/create the TU, attach `PandaData` extension (host dynamic-table lookup if needed) |
| `sdisp_PA_PickPrinter` (+ `sdiudf_PA_Get*`) | `IPrinterSelectionService` — reads `PandaLine.json` (printers, fire points, load-balance setting) |
| `sdisp_PA_Print` / `sdisp_TOOL_PA_VetLabel` / `VerifyLabel` | `ILabelBuildService` (profiles/templates → ZPL) + print action → `ITelegramOutbox<PrintTelegram>` |
| `sdisp_PA_VerifyCarton` / `sdisp_TOOL_PA_VerifyThreshold_*` | `IVerificationService` |
| `sdisp_PA_LaneEval` / `sdisp_TOOL_PA_GetFinalLaneFromStatus` | `IRoutingService` → set TU final destination via `INavi`/`ILayout` + lanes config |
| `sdisp_PA_Status_*` / `PrintEngineStatus` | status services updating TU/printer state |
| `sdisp_PA_Lock` / `LockRemove` / `Purge` | concurrency via EF/TU transaction; purge via `eScheduler` task |
| `sdisp_TOOL_GetSetting` | `IPandaSettings` reading `Settings` config |
| `sdiudf_PA_*` (UDFs) | plain C# helper methods |
| `sdivw_*` (views) | LINQ queries (GUI/reporting phase) |

## 5. Outbound integration → transport/connectors (replaces synonyms)

| PandA outbound | eController-native |
|---|---|
| `sdisp_PA2TCP_SendTCPData`, `TCP_TX_*` synonyms | `ITelegramOutbox<T>` + `eController.TransportInterface.*` connector (TCP) |
| `sdisp_PA_Print` → printer (Zebra ZPL over TCP) | print telegram via printer `HookKey` + printer connector (**stubbed in Phase 1**) |
| `sdisp_PA2BP_SendPrinterFirePoints`, `DB2VLC_*` | outbound telegram to PLC (`ITelegramOutbox<MfcTransportOrder>`) / PLC connector |
| `sdisp_PA2DCMS_WaveStatus`, `HI_*` host msgs | DTC/host outbox (`ITelegramOutbox<DtcTelegram>`) |
| 57 synonyms + `SynBuilder` (per control-engine 1–7) | `controllers.json` + transport-interface connector config (NOT ported) |

## 6. Commissioning / tooling / GUI → native

| PandA | eController |
|---|---|
| `sdisp_TOOL_SiteBuilder_*` (create/get/update/remove pandas, printers, lanes, labels, fire points) | **Author the JSON config files** (`PandaLine.json`, `PandaLabeling.json`); optional Blazor config editor later |
| `sdisp_TOOL_SynBuilder_*` | transport/connector config (replaced) |
| `sdisp_GUI_*` (~30 operator procs) | Blazor pages via CrudTable/PropertyPanel *(later phase)* |
| `sdisp_ScratchPad_*` (test harness) | xUnit tests / DevLauncher (not production) |
| `_CUSTOM_` site-specific procs | per-project customization (out of core) |

## 7. Topology / message points → `MfcLayout.json`

| PandA concept | eController |
|---|---|
| Sorter / PLC zone / device (`PandAs.SorterPLCRecID`, `PLCZone`) | layout **devices** + **places** in `MfcLayout.json` |
| Trigger point where a `BP2PA` proc fired ("induct msg 281") | a **place** flagged `IsMessagePointBehavior` (the message point) |
| Apply point / fire point geometry (`PrinterFirePoints`, `DynamicPrintPoint`) | fire-point config (File 1) + dynamic apply logic in `IPrinterSelectionService` |
| Final lane / destination (`LaneDef`, lane eval) | route/destination place; set via `INavi`/`ILayout` |

## 8. Reference implementation alignment (Exol `SimTest` branch)

`eController-Projects/Exol` already contains an in-flight PandA implementation
(`src/eController.PlcSim/CustomSimControllerWork_PandA/`). It validates this map almost exactly and
supplies the concrete patterns to follow:

**Config file + loading (confirms the JSON-config pivot):**
- `PandAConfig { List<PandAConfigData> Lines }` — **per-line array**, exactly as proposed.
- `PandAConfigData : IEntity { PlaceId PlaceID; string PandAID; bool LoadBalance; string VerifyThreshold;
  List<PandAPrinterConfigData> Printers; }` — printers **nested per line**.
- `PandAPrinterConfigData { IPAddress; PortNumber; LabelMap; int Online; }`.
- Loaded by the plugin itself (no MFC core change) via **EffortlessConfiguration**:
  `IEffortlessConfigurationRegistry.AppConfigFolder.GetFiles().FirstOrDefault(f => f.Name.Contains("PandAConfig"))`
  → `JsonSerializer.Deserialize<PandAConfig>(json)`.
- Then materialized into a **persistent table** for query: `tableProvider.Table<PandAConfigData>()`
  (`Delete` → `Create().AddRange(Lines)`), run once as a startup **Step**. So: JSON file = source of
  truth (authoring); persistent table = runtime access mechanism. This satisfies "config in JSON, not a
  config DB."

**Registration (DI/builder):**
```csharp
public static ControllerBuilder AddPandA(this ControllerBuilder builder)
{
    builder.SetupSteps(ctx => ctx.Step(PandASteps.GenerateTableForConfig))
           .SetupMfcActions(ctx => ctx.AddActions<PandAActions>());
    return builder;
}
```

**Message point:** `PandABehavior : IPlaceBehavior` (empty marker) attached to a layout place; actions find
the current MP via `snapshot.MfcPlaces.Where(mp => mp.HasBehavior<PandABehavior>())`.

**Actions:** `PandAActions : IMfcAction` with method-per-entrypoint (`ExecuteVerifyScan`,
`RouteAfterVerify`). Params injected: `MfcTransportOrder trans`, `SystemSnapshot snapshot`,
`ITableProvider tableProvider`, `[ServiceValue] string[] VerifyLabels` (from `MfcAction.json`
ServiceValues).

**Per-carton data:** TU **extensions** — `LabelExtension { string[] Labels }` and polymorphic
`iPandaVerificationResult` (`PandaVerificationSuccess`/`Fail` with `reasonCode`), via
`trans.SetExtension<T>` / `TryGetExtension<T>`. Confirms carton fields ride the TransportOrder, not a table.

**Label/host data:** `LabelData : IEntity { blindLabel; labelBarcode; labelType; labelData }` table, queried
`Where(row => row.blindLabel == trans.TuId)` — i.e. host label data keyed by `TuId`.

### Plugin-owned JSON config — verified (agent `json-config-ext`)
- `MfcCrudContextService.AddJsonTable<T>()` is **private** → cannot add new MFC-core JSON tables from a
  plugin. BUT **EffortlessConfiguration** (`IEffortlessConfigurationRegistry`, `AppConfigFolder`,
  `AddJsonFileSource()`) is fully injectable and hot-reloadable → a plugin owns its own JSON files with its
  own reader, **zero MFC core changes**. The reference impl uses exactly this path. (Optional small core PR
  only if we want CrudTable UI editing of PandA config later.)

## Open confirmations (mostly resolved by reference impl)
- Per-line structure — **confirmed** (`PandAConfig.Lines`, printers nested per line).
- Config loading — **confirmed**: `PandAConfig*.json` via EffortlessConfiguration → persistent table.
- Fire points: reference config carries printer IP/port/LabelMap/Online only (no fire points yet). Treat
  fire points as a later addition nested under the printer. Not required for Phase-1 verify/print happy path.
- `Settings`/status vocab file split (own `PandaSettings.json` vs. section) — still our choice; low risk.
