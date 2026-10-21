# Decision 023 — eController plug-in integration plan (Exol-informed)

**Status:** Accepted 2026-10-21 (all §6 rulings made)
**Date:** 2026-10-21
**Basis:** Hyper-analysis of `eController-Projects/Exol@develop` (`5ecb36d`) — five research reports in
`.project/research/exol-econtroller/` (01 MFC framework + CBS, 02 PLC/ADS/eHub, 03 screens + packaging,
04 knowledge base, 05 `.agent` inventory). Builds on decisions 002, 006, 008, 010, 018, 019, 020.

## 1. Goal (owner, 2026-10-21)

"Make PandA a NuGet plugin, similar to the criteria based sorting plugin, that we can download, edit some
actions/config/etc." — i.e. a site project references a package, calls `builder.AddPandA()`, and tunes
behavior through `MfcAction.json`, PandA config JSON and PersistantTable-backed config.

## 2. The model we are copying — Criteria Based Sorting (CBS)

| Aspect | CBS (as consumed by Exol) | Evidence |
|---|---|---|
| Package | `eController.Behaviors.CriteriaBasedSorting` 0.2.0 via central package management | Exol `Directory.Packages.props:4`, `eController.MFC.csproj:19` |
| Registration | `builder.AddCriteriaBasedSorting()` inside the site's `MfcFluentControllerBase.Setup(ControllerBuilder)` | `ConveyorController.cs:106-118` |
| Behavior | Generic `IMfcAction` classes (e.g. `ProcessCriteriaWithLaneStatus`) + place behavior (`SortCriteriaBehavior`) + extension (`SortCriteriaExtension` on `MfcTransportOrder`) + tables (`CriteriaData`) | report 01 §4 |
| Site customization | Site adds its own `IMfcAction` class (`CriteriaActionsCustom`), registered with `ctx.AddActions<T>()`, ordered before/after the generic action in `MfcAction.json` (two-action pattern: custom decision → generic mapping) | report 04 §4.2 |
| Config | `MfcAction.json` (per MP / TelegramType, `ServiceValues`), `CriteriaConfig.json`, `CriteriaValues`, persistent tables | report 04 §4.4 |

## 3. Target architecture

```
TwinCAT PLC ──ADS──▶ eHub (ATI) ── ADS connector plugin (PandA frames 281/282/283/284/286)
                         │   hooks / eMessenger → MfcTransportOrder (+PandA extensions, OriginPlcName)
                         ▼
                 Site MFC controller (MfcFluentControllerBase)
                   builder.AddTransportOrderSetup().AddDbPersistantTable()
                          .AddCriteriaBasedSorting()      // divert routing (decision-008)
                          .AddPandA(o => …)               // ◀── THE NUGET PACKAGE
                   MfcAction.json → PandAActions.Induct / Verify / PrinterStatus / …
                         │                       │
                         │ PandA.Core services   └─▶ printer I/O port → ZPL over TCP (§6 Q4)
                         ▼
                 Shared eController DB (PersistantTable: PandA tables, leases, counters — decision-010)
                         ▲
             eController WebUI host ── PandA screens .eplugin (Dashboard, Config, Rejects, Lookup)
```

### 3.1 Packages / projects to add to this repo

| Project | Kind | Contents | Depends on |
|---|---|---|---|
| `PandA.Core` (exists) | netstandard-free .NET 9 lib | Domain + ports. **No eController dependency** (stays neutral, decision-006/018) | — |
| **`eController.Behaviors.PandA`** (new; `src/PandA.EController`) | NuGet package (like CBS) | `AddPandA()` builder extension; `PandAActions : IMfcAction` thin adapters calling Core; `PandABehavior : IPlaceBehavior`; `MfcTransportOrder` extensions (`PandALabelExtension`, `PandAInductExtension`, `PandAVerifyResult`); PersistantTable adapters for every Core port (TransportOrder store, lease lock, printer state, counters, run repo, xref, settings, config); config-table seeding step; printer gateway (§6 Q4) | `eController.Mfc.Library` 4.5.x, `eController.DataLib`, `eController.CrudTable.*`, CBS 0.2.x (criteria output), `PandA.Core` (packed into the nupkg) |
| **`PandA.eHub.Ads`** (new, phase B) | `.eplugin` for eHub | PandA PLC frames + converters + response serializer (mirrors `ExolAdsConnector`: Ready/Ack/Sending handshake, OriginPlcName stamping, batch ACK) | `eController.IntegrationHub.PlugIn`, ADS libs |
| **`PandA.Screens`** (new, phase C) | Razor class lib `.eplugin` (`@light` + `@deps`) | Existing `PandA.UI` pages re-hosted with `FrameworkComponent`, `[MenuGroupItem]/[MenuItem]`, `[Authorize]`, `ServerExtensionService` Startup; view-model contracts wired to PersistantTable-backed services | `eController.WebUI.PlugIn`/`.Utility`/`.Library`, host MudBlazor version (§6 Q3) |
| `PandA.Sim`, `PandA.UI.DemoHost` (exist) | dev only | Keep as the standalone simulator / demo; not shipped | — |

### 3.2 Action surface (what a site edits in `MfcAction.json`)

| PLC frame | MP behavior | Action(s) — `TypeName`/`MethodName` | `ServiceValues` (site-editable) |
|---|---|---|---|
| 281 induct scan | PandA induct MP | `PandAActions.Induct` → Core `InductService`; then `ProjectActions.SendRespToPlc` (site) | `LineId`, `VerifyEnabled` override |
| 282 print trigger / fire-point | printer MP | `PandAActions.Print` | `PrinterPlcNumber` |
| 283 printer status | — | `PandAActions.PrinterStatus` → lane eval | — |
| 284 zone / line status | — | `PandAActions.ZoneStatus` | — |
| 286 verify scan | verify MP | `PandAActions.Verify` (emits **one authoritative criterion** `PandAVerify=Pass|Fail|…`) → CBS `ProcessCriteriaWithLaneStatus` maps to `FinalDestMp` → `SendRespToPlc` | `RequiredLabels`, pass/fail criteria names |
| DTC/WMS advice | — | `PandAActions.Advice` (hook handler) → `CartonAdviceService` | — |

Sites customize exactly like CBS: insert their own `IMfcAction` before/after ours in `MfcAction.json`,
or replace a step. Core rules stay in the package.

### 3.3 Config surfaces

1. `PandAConfig.json` in the app config folder (`IEffortlessConfigurationRegistry.AppConfigFolder`, as the
   PlcSim PandA prototype does) → seeded into PersistantTables on startup (lines, printers, label defs,
   maps/fire points, settings). Validated at startup (uniqueness rules already in Core/UI).
2. PersistantTables are the runtime source of truth; the Config screens edit tables (and write back the
   JSON — note ADR-003 non-atomic DB+JSON pitfall; show "config drift" + reload in UI).
3. `MfcAction.json` / `MfcLayout.json` (`PandABehavior` on places) — site-owned.

### 3.4 Multi-host

Decision-010's port contracts map 1:1 onto PersistantTable conditional updates (lease rows, version CAS,
counter rows). Exol confirms eController ships **no distributed lock and no unique-TuId guarantee**
(report 04 §6.2) — PandA must bring its own (already designed). Multiple MFC controllers each get their
own schema (`CV.MfcTransportOrder`, `CV2.…`) — PandA tables must be addressed through the controller's
`ITableProvider`, and identical hooks on two controllers fan out duplicates (report 01 §7.2).

## 4. Phasing

| Phase | Deliverable | Exit criteria |
|---|---|---|
| **A** | `eController.Behaviors.PandA` package: `AddPandA()`, actions, extensions, PersistantTable adapters, config seeding, printer gateway | Builds against real eController packages; packs `.nupkg`; tests via `AddInMemoryMfcContext`/SQLite (Exol pattern) incl. two-controller lease tests; a sample `MfcAction.json` + `PandAConfig.json` |
| **B** | `PandA.eHub.Ads` connector plugin (frames 281–286 + outbound fire-point/divert/stop/slow) | Round-trip with PlcSim / makeshift PLC; batch-ACK idempotency |
| **C** | `PandA.Screens` `.eplugin` | Loads in eController WebUI host; bUnit tests; browser smoke |
| **D** | Exol-style site integration sample + deploy skills | Package installs into a scratch site controller; deploy runbook |

## 5. Exol pitfalls we must design for (from reports 03/04)

- `@light.eplugin` **and** `@deps.eplugin` both required; trailing backslash on `ePluginPkgOutputFolder`;
  `$(SolutionDir)` empty on bare builds. Don't ship host framework DLLs in deps.
- Host's MudBlazor wins at runtime; Exol screens pin **MudBlazor 6.19.0** vs our UI on **8.13 (Axon)**.
- Interactive server render mode required; content-root-relative config paths.
- Stale extracted `Content` cache on redeploy.
- Visu tracking loads the whole TO table per tick — don't enable it for PandA.
- `ScrubToteState` discipline: classify every extension field as host-owned / per-scan / controller-owned.
- `TouchChanged()`/ChangeDate invariant on every TO edit (retention purge depends on it).
- Batch ACK at the ADS connector ⇒ per-message idempotency required (PandA's carton lease + version give it).
- No supported in-memory full-flow test seam in `Mfc.Library` (Exol backlog) — need a real-host smoke test.
- Package version skew: pin to the host's `Mfc.Library` line (Exol: 4.5.1, .NET 9).

## 6. Open owner questions (block phase A start)

1. ~~Package id~~ **Ruled 2026-10-21:** supersede — publish our implementation as `eController.Behaviors.PandA`.
2. ~~Source location~~ **Ruled 2026-10-21:** source lives in this repo (`src/PandA.EController`); the
   `econtroller-behaviors-panda` repo is reference only.
3. ~~UI host MudBlazor~~ **Ruled 2026-10-21:** match Exol — screens ship as a separate `PandA.Screens` ePlugin
   targeting the host's MudBlazor 6.x (6.19.0, `FrameworkComponent`, interactive server). The standalone
   Axon/8.13 UI stays as the dev/demo surface only.
4. ~~Printer I/O~~ **Ruled 2026-10-21:** new eHub plugin `PandA.eHub.Zebra` (G-ZEB1). The MFC action emits a
   print telegram (ZPL + printer id + carton id) to an outbox; the connector owns TCP 9100, `~HS` polling,
   and retries, and pushes printer status back to MFC via a hook (same pattern as Exol's ADS connector).
5. ~~PLC transport~~ **Ruled 2026-10-21:** new eHub plugin `PandA.eHub.Ads`, copied from Exol's
   `ExolAdsConnector` pattern (Ready/Ack/Sending handshake, concatenated telegrams, batch ACK → idempotent
   handling, `OriginPlcName` stamping). Message layouts, codes (281–286), parameters, and the expected
   `vPA.Assign[i]`/`vCtn.Assign[i]` tag writes come from the SQL source (architecture-log 009) — these are
   the authority. Only the physical TwinCAT symbol root/ADS address is site config.
6. ~~Feed access~~ **Resolved 2026-10-21:** `artifactory_elementlogic` is reachable from this machine
   (`eController.Mfc.Library` up to 4.6.3; `eController.Behaviors.CriteriaBasedSorting`, `.Weight` present).
   **No `eController.Behaviors.PandA` package is published** — the `econtroller-behaviors-panda` repo
   (last updated 2026-06-11) was never released to the feed. The GitHub Packages feed returns an auth error.

## 7. Harness

Port into this repo's `.agent/skills` (adapted from Exol, see report 05): `exol-plugin-deploy` →
`panda-plugin-deploy`, `exol-screens-eplugin-deploy` → `panda-screens-eplugin-deploy`,
`exol-plugin-deploy-host-config-guards`, `exol-eplugin-dll-swap`, `exol-mfc-screen-bunit-testing`,
`bluepaw-201-telegram-mp-decoder` → `panda-telegram-decoder`; new `panda-print-job-idempotency`,
`panda-shared-db-concurrency`. Copy unchanged: `multi-ask-processing`, `open-deploy-package`.
