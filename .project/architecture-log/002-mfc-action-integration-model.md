# 002 — MFC Action Integration Model + PandA→Action Mapping

**Author:** Senior Coder (auto-engaged)
**Date:** 2026-08-11
**Phase:** Discovery / feasibility (pre-spec)
**Status:** Findings + recommended integration blueprint. No code written yet.

> Follows `001-panda-econtroller-port-findings.md`. This doc captures HOW the ported PandA C# logic is
> consumed by eController — the user's stated delivery vector: **"configuration into the MFC action JSON
> config file for message points relating to the print and apply line in a project."** Grounded in the
> actual econtroller source (`Element-Logic/econtroller` @ `172f365699…`).

---

## 1. The MFC action model (verified from source)

- An **action** is any class implementing the **marker interface** `IMfcAction`
  (`eController.Mfc.Library/Logic/Service/IMfcAction.cs`) — no methods; work is done by ordinary public
  methods discovered by reflection.
- **Method return** controls pipeline flow via `MfcActionResult { Continue, Break, Error, FatalError }`
  (`…/Logic/Service/MfcActionResult.cs`). `void/Task/ValueTask` ⇒ Continue; `bool` ⇒ true=Continue,
  false=Error.
- **Parameters are injected two ways:**
  - **Services** from DI: `MfcDbContext db`, `ITransportOrder trans`, `ITelegramOutbox<T> outbox`,
    `ITableProvider tables`, `ILayout`, `INavi`, `ILogger<T>`, `CancellationToken`, …
  - **`[ServiceValue]` params** — static values supplied from the JSON `ServiceValues` object
    (`…/Logic/Service/ServiceValueAttribute.cs`).
- **Registration is explicit in code** (no attribute auto-discovery): controller `Setup()` calls
  `.SetupMfcActions(ctx => { ctx.AddActions<PandaActions>(); ctx.AddActions<DefaultActions>();
  ctx.AddDispatcher<CommonDispatcher>(); })` (`MfcFlowengine.AddActions<T>()`).
- **JSON binds config → code by short type name + method name.** `TypeName` must equal the class's
  `Type.Name`; `MethodName` the method. (`MfcActionTypeDiscovery.TryFindActionType`).

### Example built-in actions (templates to mirror)
`DefaultActions` (routing/state: `SetFinalDestination`, `GetNextDefaultDest`, `SetStatus`,
`SendTransportOrderTelegram`, `HandOff`), `DefaultDtcActions` (host/DTC publish), `TrackingActions`
(counters). Files under `eController.Mfc.Library/Common/Actions/`.

## 2. Message points & inbound→action path

- A **message point (MP)** is a layout place (`PlaceId`, e.g. `"1005"`) flagged with
  `IsMessagePointBehavior` in `MfcLayout.json`.
- Inbound telegram (PLC/eHub) → persisted to `AtTelegramIn` → dispatcher creates/updates a
  `MfcTransportOrder` → `StepDefaultProcessActions` → `CommonDispatcher.GetMatchingActions(trans)`
  matches on **(ControllerName, Mp, TelegramType, Mf)** and runs matched actions ordered by
  `ServiceSequence` → on `Continue` the shared EF transaction commits → `TelegramOutAll` flushes
  `AtTelegramOut`. (Files: `CommonDispatcher.cs`, `MfcFlowEngineDefaultSteps`, `AtTelegramIn/Out.cs`.)
- Actions in the same (Mp, TelegramType) group form a **pipeline** sharing one `ITransportOrder` and one
  `MfcDbContext`; a `Break/Error/FatalError` stops it (Error/FatalError roll back the TO).

## 3. The `MfcAction.json` config (THE key artifact)

Per-project file at `econfig_root/<AppType>_<Environment>/MfcAction.json` (e.g. `PandA_Production/`).
Loaded by `ElementLogic.Configuration` ("EffortlessConfiguration"); supports **hot reload**.
Entity: `eController.Mfc.Library/Entities/Json/MfcAction.cs`. Each row:

| Field | Meaning |
|---|---|
| `Id` | unique long |
| `ControllerName` | controller key from `controllers.json` (comma-sep ⇒ cross-product) |
| `DeviceName` | optional device/PLC filter |
| `Mp` | message point place id(s); `"0"`/`"*"` = any |
| `Mf` | move-function filter; null/""/"0" = any |
| `TelegramType` | telegram tag (`"SE"` scan event, `"MP"`, `"HANDOFF"`, …) |
| `ServiceSequence` | execution order (negatives run before implicit behavior actions) |
| `TypeName` | **short** class name implementing `IMfcAction` |
| `MethodName` | method to call |
| `IsActive` | soft enable/disable |
| `ServiceValues` | JSON object → `[ServiceValue]` params (by name) |

Companion config in same folder: `controllers.json` (declares `PandAController -> FluentController`
+ `Config`), `MfcLayout.json` (devices/places/MPs/routes), `appsettings.Mfc.<Env>.default.json`
(`ConnectionStrings`, prefix `sqlserver://` / `postgresql://` / `sqlite://`).

## 4. Data & outbound for PandA actions

- **PandA's own tables** → **persistent tables** API: `builder.AddDbPersistantTable(ctx =>
  ctx.AddDynamicTable<PandaCartonRecord>())`, used via `ITableProvider tables =>
  tables.Table<PandaCartonRecord>().Query()…` (`eController.Mfc.Documentation/persistent-tables.md`).
  Cross-DB (SQL Server / Postgres / SQLite) via `MfcDbContext` subclasses; default schema `mfc`.
- **TU-scoped data** (travels with the carton) → `trans.ApplyExtension<PandaData>(…)` /
  `trans.TryGetExtension<PandaData>(out …)` (`transportorder.md`).
- **Outbound**: `ITelegramOutbox<T>.StoreAsync(HookKey, telegram)` — queued in `AtTelegramOut`, sent in
  `TelegramOutAll`. To PLC (`ITelegramOutbox<MfcTransportOrder>`), to host (`ITelegramOutbox<DtcTelegram>`),
  to **printer via TCP** ⇒ a custom `HookKey` + a connector in `eController.TransportInterface.*`
  (canonical model: `DefaultActions.SendTransportOrderTelegram`). Internal pub/sub via `IScopedMessenger`.

## 5. Recommended PandA → MFC action blueprint

- New plugin/library `eController.PandA` (RCL) + `eController.PandA.Library` (EF/persistent tables +
  services) + `eController.PandA.DevLauncher`, following `TestPlugin`/`CrudTable.DynamicTable` templates.
- A `PandaController : FluentController` (declared in `controllers.json`) whose `Setup()` registers the
  PandA persistent tables, the standard steps (`TelegramInAll → StepDefaultProcessActions →
  TelegramOutAll`), and `AddActions<PandaActions>()` (+ any sub-action classes) with `CommonDispatcher`.
- **PandA business logic**: SP bodies (LookupCarton, PickPrinter, VerifyLabel, LaneEval, wave lifecycle,
  label profile logic) → C# **services** injected into action methods; keep actions thin.
- **Entry points** map 1:1:

| Stored Procedure (entry) | Action class.method | TelegramType | Trigger MP |
|---|---|---|---|
| `sdisp_BP2PA_Scan_Induct` | `PandaActions.ScanInduct` | `SE` | induct scanner MP |
| `sdisp_BP2PA_Print` | `PandaActions.SendPrintCommand` | `MP` | printer arrival MP |
| `sdisp_BP2PA_Scan_Verify` | `PandaActions.VerifyScan` | `SE` | verify scanner MP |
| `sdisp_BP2PA_Status_Printer` | `PandaActions.PrinterStatus` | `MP` | printer status MP |
| `sdisp_BP2PA_Status_Zone` | `PandaActions.ZoneStatus` | `MP` | zone MP |
| `sdisp_BP2PA_Event` | `PandaActions.LineEvent` | `SE`/`MP` | event MP |
| wave completion (`*_PandaWaveComplete`) | `PandaActions.CompleteWave` | scheduled (`eScheduler`) | timer |

- Site config = a `MfcAction.json` per project wiring the above methods to that site's MPs +
  `ServiceValues` (printer keys, thresholds, lanes). This is precisely "configuration into the MFC action
  JSON config file for message points relating to the print and apply line in a project."

## 6. Known gaps / to confirm with hardware+delivery
- Actual PLC `TelegramType` codes and MP ids come from the site protocol/layout (not in repo).
- TCP-to-printer connector in `eController.TransportInterface.Mfc` not yet read in full — needs a spike.
- Production plugin registration into a customer `eProject`/`ePlugin.Engine` config is external to repo.
- Legacy `MfcMessagePoint.json` is superseded by `MfcLayout.json` (use the new format).
