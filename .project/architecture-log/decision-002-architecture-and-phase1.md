# decision-002 — Architecture pivot + Phase-1 lock

**Date:** 2026-08-11
**Participants:** Orchestrator + Planner + Senior Coder (with user)
**Status:** ACCEPTED. Supersedes the EF/persistent-table config parts of decision-001.

Consolidates the decisions reached while pressure-testing the port with the user, grounded in the
`Element-Logic/econtroller` source and the `eController-Projects/Exol` (SimTest) reference.

## D1 — Configuration is JSON files, not a config DB
PandA configuration is authored as **JSON files** loaded by the plugin via **EffortlessConfiguration**
(`IEffortlessConfigurationRegistry.AppConfigFolder`), hot-reloadable — the same family as
`MfcAction.json`/`MfcLayout.json`. The database is used **only for runtime host data** (see D2), never for
configuration. A strongly-typed in-memory provider holds the parsed config; a persistent/dynamic table is
only a runtime read mechanism if needed, not the source of truth.
- **File 1 `PandaLine.json`** — per-line array; one block per PandA line, each nesting line details +
  printers(+details, later fire points) + lanes.
- **File 2 `PandaLabeling.json`** — shared label/behavior config (profiles/templates/types/locations,
  label buffer/attributes, settings, status/state vocab).

## D2 — Carton = econtroller Transport Order
The carton is a **`MfcTransportOrder`** (TU). No PandaCartonList/PandaData tables are ported.
- `TuId` = **blind label** (verified: `MfcTransportOrder.TuId`, "TUID1 from PLC"; string). PK is `Id`, so
  `TuId` is **not unique** → lookups must filter to the in-flight/active TO (see D9).
- Per-carton data rides as **TU extensions** (`SetExtension`/`TryGetExtension`), persisted to the DB via
  `DynamicEntityBase.DynamicField` (JSON column) — so extension data survives between telegrams.

## D3 — Ports-and-adapters; thin actions over services
- **`PandA.Core`** — pure domain, services, config model, label/printer logic. **Zero econtroller
  dependency.** Compiles + fully unit/integration-tested here.
- **`PandA.Sim`** — simulated backend adapter implementing Core's ports (in-memory TO store, captured
  printer egress). Enables standalone end-to-end validation without econtroller.
- **`PandA.EController`** — thin adapter: `IMfcAction` actions, EffortlessConfiguration loader,
  `ITelegramOutbox` wiring, TU-extension mapping. Written against the real interfaces; compiled at
  integration (see D4). Actions stay thin and delegate to Core services (`ICartonLookup`,
  `IPrinterSelection`, `IPrinterGateway`, later `ILabelBuilder`, `IRouter`).
- Principle from user: build with a simulated backend, keep the codebase adaptable so it unplugs from the
  sim and plugs into a real econtroller project easily. The seam sits exactly at the econtroller boundary.

## D4 — Build location + package access
Build under branch `npawlakel-print-and-apply` in `agentic-work` for now (may port to another repo later).
econtroller ships as **private NuGet packages** on a JFrog feed (`suplogistik.jfrog.io`) that returns 401
here; its source is readable but its transitive private deps (`eController.Dtc.Messages`,
`eController.Util.Embed`, `eScheduler.*`) still require the locked feed. **No creds for now** →
`PandA.Core`+`PandA.Sim` are the compilable/testable deliverable; `PandA.EController` compiles when creds
are available or when dropped into a real econtroller solution.

## D5 — Phase-1 vertical slice (LOCKED)
Advice → induct → pick printer → send ZPL. **The host provides ready ZPL**, so Phase 1 is a
**pass-through** — the label-template/profile→ZPL engine is **out of Phase 1** (later capability).

## D6 — Two message points
1. **Label-advice (host inbound):** carries blind label + typed ZPL set → creates the TO shell
   (`TuId`=blind label, status `ADVISED`) and stores the label set as a TU extension. Uses econtroller's
   built-in find-or-create-by-`TuId` (`TransportOrderTelegramIn`).
2. **Induct scan (physical):** blind label → matches existing TO by `TuId` → label set already present →
   pick printer(s) → emit ZPL.

## D7 — Typed label-set model
The TU extension is a **`PandaLabelSet`** = a list of `{ LabelType, Lpn, Zpl }` entries (NOT a single
ZPL). One blind label carries **multiple typed labels** (source: PandaData `LabelType{1..6}` /
`LabelBarcode{1..6}` / `LabelData{1..6}`; we allow N). Printer selection matches each entry's `LabelType`
against the printer's configured **`LabelMap`**; a carton with multiple typed labels fires multiple
printers. Optional carry-through: `ProfileName`, `CartonStatus`.

## D8 — Multiple records per blind label (disambiguation)
Source `sdisp_PA_LookupCarton` elects one winner via `SELECT TOP 1 ... ORDER BY`:
`Printed ASC` (prefer unprinted) → `ActiveRecord DESC` (prefer active) → wave `StatusTime ASC` (deferred) →
`CreationTime` (oldest if append / newest if overwrite, per `OverwriteLabelData`) → `PandaDataID ASC` (PK
tiebreak); excludes `LabelType1='Exception'`.
- **TO-centric mapping:** `TransportOrderTelegramIn` already find-or-creates **one TO per `TuId`**, so
  multiplicity becomes *repeated advice for one blind label*. We apply the source rule: an
  `OverwriteLabelData`-style setting decides **append vs replace** of the label set; **don't overwrite an
  already-printed TO** unless reprint is allowed.

## D9 — TO identity / lifecycle
Because `TuId` isn't unique, all lookups filter to the **active/in-flight** TO. Advice-created shells get a
distinct status (`ADVISED`); completed TOs are purged/aged (port PandA's Purge later) so blind-label reuse
can't match a stale carton.

## D10 — Printer egress
Core depends on an **`IPrinterGateway`** port. `PandA.Sim` captures/asserts (ZPL + target printer
`IP:port`). `PandA.EController` maps it to `ITelegramOutbox` + a `eController.TransportInterface.*` TCP
connector — **real TCP-to-printer connector deferred** to integration.

## D11 — Phase-1 "done" bar
In the Sim: an advice message followed by an induct scan yields the **correct ZPL sent to the correctly
label-type-matched printer(s)**, proven by unit + integration tests (TDD per harness). No live hardware.

## Bookmarked / deferred (unchanged)
Wave/WaveRange + wave lifecycle; operator GUI; label-template→ZPL engine; verify + lane routing;
real TCP/PLC/host connectors; SiteBuilder commissioning UI.
