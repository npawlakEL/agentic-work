# 001 — PandA → eController Port: Discovery Findings

**Author:** Senior Coder (auto-engaged)
**Date:** 2026-08-11
**Phase:** Discovery / feasibility (pre-spec)
**Status:** Findings only. Port plan + decisions to follow after objective alignment with user.

> Purpose: capture a durable, complete review of BOTH the PandA SQL source and the
> `Element-Logic/econtroller` target so the phased port can proceed without missing anything.
> Companion file: `panda-object-inventory.csv` (all 322 source objects, machine-generated).

---

## Part A — PandA source (the thing being ported)

**Source reviewed:** `127.0.0.1_SDI_PandA_20260811` (attached zip). Written **entirely in SQL Server T-SQL**.
Objects are organized into numbered install stages:

| Stage | Type | Count | Notes |
|------|------|------:|-------|
| 2.1 | System messages | 1 | `CreateSystemMessages.sql` — 495 KB single script (message catalog / localized event text) |
| 3.0 | UDFs | 6 | `sdiudf_PA_*` scalar lookups (carton status, label data, RecID↔PandaID↔PrinterID mapping) |
| 4.0 | Synonyms | 57 | Cross-database pointers — the integration boundary (see below) |
| 5.0 | Tables | 35 | Core schema (PandaData, PandaCartonList, Printers, Labels, Waves, Settings, EventLog…) |
| 6.0 | Seed data | 22 | Config/reference rows (Settings, LabelProfiles, PrinterFirePoints, Waves…) |
| 7.0 | Views | 31 | `sdivw_*` GUI/reporting views + per-CE bridge views |
| 8.0 | Stored procedures | 170 | All business logic lives here |
| **Total** | | **322** | |

### What PandA does (domain)
A warehouse **Print-and-Apply** line controller: a carton is scanned as it inducts onto a sorter,
PandA looks up the carton, picks a label printer, prints the label(s), verifies the applied label,
and routes the carton to a pass/fail lane. It is config-driven and integrates with PLC/host systems.

### Stored-procedure functional groups (by name prefix)
- **`sdisp_BP2PA_*` (5)** — *Business-Process → PandA* entry points. Called from the line/PLC process
  (e.g. `sdisp_BP2PA_Scan_Induct` = "induct msg 281"). These are the primary external inbound API.
- **`sdisp_PA_*` (core engine, ~12)** — the pipeline: `LookupCarton` → `PickPrinter` → `Print` →
  `VerifyCarton` → `LaneEval` (routing) → `Status_*`, plus `Lock`/`Purge`.
- **`sdisp_PA2*` (outbound)** — `PA2TCP_SendTCPData`, `PA2BP_SendPrinterFirePoints`, `PA2DCMS_WaveStatus`
  — PandA → external systems.
- **`sdisp_GUI_*` (~30)** — read/write procs backing the **operator GUI** (label profiles, wave control,
  printer status, panda list, scan logs, reject cartons). These define the screens to reimplement.
- **`sdisp_TOOL_SiteBuilder_*` (~45)** — site/config CRUD (create/get/update/remove pandas, printers,
  lanes, labels, fire points). This is the configuration/commissioning surface.
- **`sdisp_TOOL_SynBuilder_*` (5)** — generate the cross-DB synonyms per install (CORE/DCMS/PLC/TCP).
- **`sdisp_TOOL_*` (misc)** — label profile CRUD, printer fire-point CRUD, settings, table2file export.
- **`sdisp_eLog_* / sdisp_Log_Event` (event logging)** — writes to EventLog/uEventLog with severity levels.
- **`sdisp_MA_*` (2)** — manual apply variants of scan/verify.
- **`sdisp_ScratchPad_* (13)` / `sdisp_Support_TOOL_*`** — test/scratch harness + support utilities
  (candidate to DROP or convert to tests rather than port).
- **`_CUSTOM_` procs** — site-specific customizations (e.g. "LULU COLUMBUS CUSTOM") — flag for review.

### Integration boundary (critical for the port)
PandA does not call external systems directly; it calls **synonyms** that point at *other databases*:
```
CREATE SYNONYM [dbo].[sdisp_DB2VLC_Send_CE1] FOR [SDI_PLC_ULW_CP2].[dbo].[sdisp_DB2VLC_Send]
```
Synonyms are grouped per **"CE" (control engine) 1..7** — i.e. multi-line/multi-zone deployments.
Categories: `DB2VLC_*` (PLC bridge), `TCP_TX_*` (TCP transport send/response/insert),
`MessageLog_*`, `HI_*` (host interface / DCMS inbound label data), `base__installs`.
**Implication:** the port must define how eController reproduces these outbound/inbound channels
(TCP to printers/PLC, host/DCMS messaging). In econtroller these map to eHub connectors / transport
interfaces, NOT to cross-database synonyms.

### Config model
Behavior is driven by a key/value `Settings` table (e.g. `PrintExceptionLabels`, `MinGap`,
`DynamicPrintPoint`, `LoadBalanceEnabled`, `EncoderResolution`, purge retention). Procs read settings
via `sdisp_TOOL_GetSetting` with a default fallback. ~35 settings seeded.

### Code characteristics (porting effort signal)
- Heavy use of `OUTPUT` params, `TRY/CATCH` with centralized `sdisp_Log_Event`, `APP_NAME()`-based
  debug toggles, identity keys, filtered indexes.
- Business logic (routing, printer selection, label vetting, wave lifecycle) is **substantial and lives
  inside SP bodies** — this is the bulk of the re-implementation work.
- Some procs are large: `sdisp_PA_LookupCarton` (20 KB), `sdisp_TOOL_SiteBuilder` (17.6 KB),
  `sdisp_GUI_LabelProfile_Update` (14.5 KB), `sdisp_PA_Status_PrintEngine` (13.6 KB),
  `sdisp_TOOL_PA_VerifyLabel` (13.8 KB), `sdisp_PA_PickPrinter` (13.6 KB).

---

## Part B — eController target (where it's going)

**Repo:** `Element-Logic/econtroller` @ `172f365699ad69056958312d3eb9d31f6e06f009`.
A **.NET 9 / C# 13** "unified warehouse controller integration platform." Blazor Server WebUI,
heavily **plugin-driven**. Central NuGet versioning; solution `eController.slnx`.

### Plugin architecture (how PandA plugs in)
- Plugin SDK: **`eController.WebUI.PlugIn`** — the one required base class is
  **`ServerExtensionService`** (override `Configure(IApplicationBuilder)` and/or
  `ConfigureScope(IServiceProvider)`; constructor gets `IServiceCollection`, `IConfiguration`, etc.).
- Plugins are **Razor Class Libraries** (`Microsoft.NET.Sdk.Razor`) referencing `eController.WebUI.PlugIn`
  (`Private="false"`) + `ePlugin.MsBuild`. They ship `.razor` pages + `wwwroot/` assets.
- **Auto-discovery at startup** (no central registry): loaders scan plugin assemblies for
  `ServerExtensionService` subclasses, `[ApiController]`s, and `[MenuGroupItemAttribute]` /
  `[MenuItemAttribute]` + `[Route]` on Blazor pages (build the sidebar nav).
- **DevLauncher pattern** (`Microsoft.NET.Sdk.Web`): `DevPlugins.Load<Startup>(); DevPlugins.Run(args);`
  hosts the full WebUI + the plugin for local dev.
- **Templates:** the acknowledged template is to **copy `eController.WebUI.TestPlugin` +
  `.DevLauncher`** (there is no `dotnet new` scaffold). `CrudTable.DynamicTable` is a fuller real example.
- `eHub.PlugIn.AutostoreInterface` is a **different** pattern (backend `IConnector` on the eHub message
  bus, script-mode) — relevant only for PandA's hardware/PLC/TCP side-channels, not the UI.

### Data / persistence
- **EF Core code-first** is the modern path. `MfcDbContext` with `DbSet<T>` entities; per-install
  **schema isolation** (e.g. `mfc`) via `DynamicSchemaMigrationsAssembly`.
- **Three DB engines** supported by connection-string prefix: `sqlserver:`, `postgresql:`, `sqlite:`.
- **Migrations** are code-first, auto-applied on startup by a `MigrationChecker` (backs up tables before
  destructive/raw-SQL ops). Generated with `dotnet ef migrations add`.
- Legacy **`eController.DataLib`** = Element Logic's own lightweight ADO.NET micro-ORM
  (`session.List<T>()`, `ExecuteNonQuery(...)`, interpolated parameterized SQL). NOT Dapper.
- **No stored procedures anywhere.** No `CommandType.StoredProcedure`, no `EXEC` patterns, no SP
  helpers. Raw SQL in migrations is treated as destructive/backup-worthy.

### UI
- **Blazor Server** (SignalR circuit) — not React/Angular/WASM.
- Component kits: `ElUI.ComponentLib` (primary design system), some `MudBlazor`, `BlazorMonaco`.
- **`CrudTable.DynamicTable`** = configurable data-grid plugin → fits PandA list screens.
- **`PropertyPanel`** = auto-rendered property/config editors → fits PandA settings/config screens.

### Testing conventions
- **Unit:** `*.Tests`, xUnit v3 + NSubstitute + FluentAssertions. Naming
  `Given_..._When_..._Then_...`.
- **DB integration:** `*.Tests.Integrations`, Testcontainers (Docker), `[Trait("Category","MfcDbIntegration")]`.
- **Plugin integration:** `*.PluginIntegrations.Tests`, `WebApplicationFactory<DevLauncherProgram>`.
- E2E (Playwright) mentioned in `TEST_STRATEGY.md` but not present.

---

## Part C — Feasibility assessment (options, no decision yet)

The port is **not a SQL lift-and-shift.** The 35 tables / 31 views / 6 UDFs / seed data map cleanly to
EF code-first, but the **170 stored procedures are business logic** and econtroller has no SP execution
path. The central question is how to re-express that logic.

| Option | Approach | Trade-off |
|---|---|---|
| **A. Native EF + C# services (recommended target)** | Tables/views/UDFs → EF entities/migrations; SP logic → C# service methods; screens → Blazor + CrudTable/PropertyPanel. | Idiomatic, cross-DB, testable. Highest up-front effort (rewrites 170 SPs). |
| **B. Transitional: EF schema + raw SQL** | EF for tables; keep some SP logic as inline SQL via `DataLib`/`ExecuteNonQuery`. | Faster to first light; **SQL-Server-only**; logic still not in C#; interim only. |
| **C. Ship SPs verbatim in migrations** | `migrationBuilder.Sql("CREATE PROC…")`, call via `EXEC`. | Locks to SQL Server, fights MigrationChecker (backups), no PG/SQLite. Not recommended beyond stopgap. |
| **D. eHub connector plugin** | Backend `IConnector` on message bus. | Right home ONLY for the PLC/TCP/host hardware channels, not the app/GUI. Likely used *alongside* A. |

### Open questions to resolve with the user (objective alignment)
1. **Fidelity vs re-architecture** — faithful behavior port re-expressed in C#/EF (Option A), or
   pragmatic transitional (B) to get running sooner?
2. **DB engine target** — must PandA support PostgreSQL/SQLite like the rest of econtroller, or is
   SQL-Server-only acceptable (changes whether T-SQL can be reused)?
3. **Integration scope** — how much of the PLC / TCP-printer / DCMS-host channels (the synonyms) is in
   scope now vs stubbed? These become eHub connectors / transport interfaces, not synonyms.
4. **Operator GUI** — reimplement the `sdisp_GUI_*` screens in Blazor now, or backend-first?
5. **Site/config tooling** — port `SiteBuilder`/`SynBuilder` commissioning tools, or replace with
   econtroller-native config?
6. **Scope trims** — exclude `ScratchPad_*`, `_bak`, and `_CUSTOM_` site-specific procs from the core port
   (convert ScratchPad to tests; treat CUSTOM as per-site)? 
7. **Schema** — dedicated `panda` schema + own `PandaDbContext`/migration-checker (cleaner) vs extending
   `MfcDbContext`.
8. **Phase 1 definition** — what is the smallest vertical slice that proves the approach
   (e.g. schema + induct→lookup→pickprinter→print happy path, no UI)?
9. **Production plugin registration** — how a new plugin is added to a customer `eProject`/`ePlugin.Engine`
   config is external to the repo; needs the delivery team.

---

## Companion data
- `panda-object-inventory.csv` — all 322 objects (stage, type, name, bytes, relpath). The authoritative
  checklist so no object is missed during the phased port.
