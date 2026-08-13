# Changelog

## Versioning Scheme

**Format:** `MAJOR.MINOR.PATCH` (e.g., `1.2.3`)

| Position | When to increment | Example |
|----------|-------------------|---------|
| **PATCH** (0.0.X) | Minor revision, bug fix, hot-patch, small change | `0.0.1` → `0.0.2` |
| **MINOR** (0.X.0) | Major feature addition, significant new functionality | `0.1.0` → `0.2.0` |
| **MAJOR** (X.0.0) | Full version release — a component of all accumulated changes, milestone delivery | `0.2.3` → `1.0.0` |

## Ownership

- The **Learner** updates this file after every cycle (Gate 3)
- The **Orchestrator** approves version number increments
- Version bumps are committed alongside learnings and docs

---

## [Unreleased]

_(Next cycle's changes will be logged here by the Learner.)_

---

## [0.9.0] — 2026-08-14 — Backend serialized wiring wave: induct integration

Wires the five previously "domain-built;integration-deferred" slices into the `InductService` induct path.
Every feature edits the same hot files, so the wave was **serialized and Senior-owned** (no parallel coders),
one feature built + full-suite tested + committed at a time. **358 backend tests green** (up from 352 at wave
start, 331 after Wave 0). Build clean. DemoHost re-verified. Nothing pushed.

### Added
- **F20** — induct read-quality classification wired: `InductQualityClassifier` stamps
  `TransportOrder.StatusAtInduct` from blind-label markers + `FrontGap`/`MinGap` (via optional
  `IMinGapProvider`). Stamps only — does not gate the print. Feeds F-LOG1 and the F10 trigger.
- **F-LOG1** — carton run-history wired: a `CartonRunRecord` is created at induct from scan measurements +
  `StatusAtInduct` (optional `ICartonRunRepository`), with the first matched printer attached. `StableOrderId`
  FNV-1a hash groups re-inducts under a stable `PandaDataId`.
- **F18** — barcode cross-reference wired end-to-end: advice-time association (`AdviceBarcode` /
  `AdviceMessage.Barcodes` → `XRefService`) and induct-time `ResolveByBarcodeAsync` (blind label + scanned
  labels → xref TuIds → elect least-printed/earliest) when a direct TuId lookup misses (optional `IXRefStore`).
- **DYNAP** — dynamic apply-point wired: `ApplyPointResolver` converts the human APPLY point (inch/edge) into
  a carton-aware PLC pulse carried on `PrintJob.ApplyPulse`. Side-apply with known dimensions only; the PRINT
  point stays static; top-apply is left null pending tamp-kinematic commissioning; measurement→pulse unit
  calibration is an adapter concern.
- **F10** — local exception labels wired (decision-021): an unmatched carton with exceptions enabled gets a
  synthesized `{Reason}-{seq}` identity, a ZPL built from the injected `IExceptionLabelSource`
  (`LocalTemplateSource` default; DCMS/eHub source owned by the adapter), printed to a printer of the line's
  apply orientation, recorded as a printed run, and a verify-then-reject routing criterion (F08). New
  `IExceptionLabelSource`/`LocalTemplateSource`/`ExceptionLabelPolicy`; `LineConfig.PrintExceptionLabels`
  (nullable per-line switch, defined-global-override); `InductResult.ExceptionLabel` + `ExceptionCartonId` +
  `Routing`.

### Changed
- `InductService` gained four optional trailing constructor collaborators (`IMinGapProvider`,
  `ICartonRunRepository`, `IXRefStore`, `IExceptionLabelSource`), each null/no-op by default and placed before
  `logger`, so all existing construction sites compile unchanged.
- `spec_features` F20/F-LOG1/F18/DYNAP/F10 → `built`.

### Learnings
- reviewer-log/008, learnings/005: optional-trailing-param seams for incremental hot-file integration;
  integration tests must be pipeline-reachable; model "unset" explicitly for presence-based precedence rules;
  build to the architecture-log decision, keeping external-system variability behind a port.

---

## [0.8.0] — 2026-08-13 — Backend Wave 2: full-port feature slices

Ports seven PandA features into `PandA.Core`/`PandA.Sim` via a senior/coder workflow — background Coder
agents authored new-file domain slices in parallel while a single Senior owned the build, full-suite run,
review, and integration of hot-file features. **328 backend tests green** (up from 279). Nothing pushed.

### Added
- **FLOG1** — carton run-history audit (`CartonRunRecord`, `ICartonRunRepository` + in-memory store).
- **F10** — local exception-label builder (`ExceptionLabelBuilder` + `Labels/*` + template repository).
- **F18 Facet A** — multi-barcode identity xref (`XRef`, `XRefService`, `IXRefStore` + in-memory), per
  decision-017.
- **F12** — Zebra `~HS` host-status suffix (`ZplStatusSuffix`), and wired into the induct print path gated
  by `LineConfig.PrinterStatusSuffix`.
- **PROFSW** — per-carton fire-point profile selection: host-supplied `ProfileName` resolves against the
  line's `ProfileRegistry` (case-insensitive); set-but-unknown ⇒ `NoProfile` (print nothing, GAP F19);
  absent ⇒ `ActiveProfile` fallback. Adds `IProfileStore` + in-memory store.
- **F15** — PLC pre-verify carton recovery (decision-009): re-arms a `Printed` carton only, keeps
  `PrintCount` **monotonic**, reprint-gated via `ReprintLabels`, adds a `TrackingRearmed` flag;
  `PlcEventHandlerService` + event codes + `IVerifyDeviceProvider`; position gate; codes 217/218 ignored.
- **F08** — verify-outcome routing criterion (decision-008): PandA does **not** select lanes; it annotates
  the carton with a transport-agnostic `RoutingCriterion {Type,Value}` (`ForVerify`: Pass/Ignore→"Pass",
  else the outcome name) surfaced on `VerifyStationResult` for the EController adapter to project onto
  `SortCriteriaExtension`.

### Changed
- `InductService` print loop now appends `~HS` when the line opts in (F12).
- `VerifyStationService` populates the new `VerifyStationResult.Routing` criterion (null on NoActiveOrder).

### Learnings
- **learnings/004** — parallel-coder orchestration (partition by files, not just features; non-building
  coders need a mandatory Senior build gate), building to the **decision** over superseded spec text, and
  honest two-stage (domain-built → integrated) deferral.
- **reviewer-log/007** — Senior caught 3 pre-commit issues: an invalid coder-emitted record constructor, a
  lost first-file-in-new-folder create, and F08/F15 spec-vs-decision scope drift.

### Known follow-ups
- **Deferred integrations blocked on Wave-0 foundations:** FLOG1 induct wiring + F18 barcode-based induct
  resolution need the PLC **inbound-scan payload** plumbed through `InductAsync`; F10 exception-label-on-
  induct needs **F20 read-quality classification**. Exception-label design conversation to precede F10.
- Wave-0.5 backend foundations (SETTINGS provider, F16 ILogger retrofit, INBOUND TransportOrder fields,
  LineConfig omnibus, MandA Core manual entry point) remain.

### Tests
- 328 backend (+ 17 bUnit + 10 e2e from the prior UI wave) passing. Build clean; DemoHost `/`, `/sim` → 200.

---

## [0.7.0] — 2026-08-13 — Phase-2 Wave 2: interactivity fix + operator identity

Wave-2 integration hardening of the standalone Blazor UI module. Fixes a critical latent defect — the demo
host rendered as **static SSR with no interactivity**, so every button was dead in the browser — and wires
real operator identity through the audited reprint path. 256 tests green (231 backend + 16 bUnit + 9 e2e).

### Fixed
- **Critical: the DemoHost was non-interactive.** `App.razor` rendered `<Routes />` / `<HeadOutlet />` with
  no `@rendermode`, so despite `AddInteractiveServerRenderMode()` being registered, the whole component tree
  was static SSR — no Blazor circuit, so `onclick`/`ValueChanged` never fired (theme toggle, reprint, MandA
  print, config save were all inert in a real browser). Applied `@rendermode="InteractiveServer"` to both.
  The existing gates missed this: bUnit forces interactivity, and the screen-load gate only checked that a
  click didn't *error*, not that it *did* anything. (learnings/003.)

### Added
- **`IOperatorContext`** (`PandA.UI.Contracts.Common`) — injected operator identity port
  (`CurrentOperator` / `SetOperator` / `OperatorChanged`) so audited actions attribute to a real person.
  Demo-backed by a scoped-per-circuit `DemoOperatorContext`; a real host backs it with its auth session.
- **App-bar operator selector** in `PandaLayout` bound to the port; a `data-dark` state hook on the layout
  root so e2e can assert real interactivity.
- **`PandA.E2E.Tests/AppFlowTests`** — whole-app flows: nav reaches every screen, theme toggle flips
  `data-dark` (real-browser interactivity guard), operator identity persists across navigation.
- **`PandA.UI.Tests/PandaLayoutTests`** — shell chrome + operator-selector + theme-toggle unit gates.
- **Config Explorer create-new-entity flow** — selecting a group node (Labels/Stations/Lines/Printers/
  Fire Points/Maps) shows a **New** button that opens a blank editor; Save with a null id creates the entity
  through the pluggable editor and it appears in the refreshed tree. Group nodes are now selectable (only the
  Root stays inert); added a Printer-id field so new fire points can be parented. Gated by a bUnit create
  test (asserts the saved DTO has a null id) and an end-to-end Playwright create flow.

### Changed
- Label Lookup and Reject Cartons now pass `Operator.CurrentOperator` (was hardcoded `"operator"`) to
  `AuthorizeReprintAsync`; their bUnit tests assert the captured actor equals the current context.

### Learnings
- **learnings/003** — the static-SSR interactivity trap (a registered render mode ≠ an applied one), why
  "renders + no console error + clickable" is not "works", and operator identity behind a port.

### Known follow-ups
- Create-new-entity flows in Config Explorer and Wave-0.5 backend foundations (SETTINGS provider, F16 ILogger
  retrofit, INBOUND TransportOrder fields, LineConfig omnibus, MandA Core manual entry point) remain.

### Tests
- 231 backend + 16 bUnit + 9 e2e passing.

---

## [0.6.0] — 2026-08-13 — Standalone Blazor UI module (Phase-2, Wave 0+1)

Adds the standalone, pluggable Blazor UI module (decision-019): a portable view-model interface layer, a
Sim-backed demo host, the Axon/MudBlazor shell, and all five operator screens — each landed under automated
runtime gates (build + Playwright screen-load + bUnit interaction) per the domain owner's "everything must be
tested/touched" requirement. Backend engine unchanged (still 231 tests); no econtroller coupling introduced.

### Added
- **`PandA.UI.Contracts`** — the full portable interface seam the RCL depends on (Common results, Status
  streams, Lookup queries, Reprint command, Rejects query, MandA commands/stream, Config tree + `IConfigEditor<T>`
  editor family + `ISettingsEditor`). The RCL never references `PandA.Core` or a DB directly.
- **`PandA.UI`** (Razor Class Library) — Axon/MudBlazor shell (`PandaLayout`, light/dark toggle, nav) and the
  five screens:
  - **Status Dashboard** (`/`) — live line/printer status via subscription streams (UI-DASH).
  - **Label Data Lookup** (`/lookup`) — filterable transport-order grid, expandable per-carton label slots
    (raw ZPL), audited Authorize-Reprint on held cartons (UI-LOOKUP).
  - **Reject Cartons** (`/rejects`) — filterable reject list + shared Authorize-Reprint (UI-REJECT).
  - **MandA Station** (`/manda`) — station dropdown, scan→resolve→print-subset→per-label-verify (UI-MANDA, UI part).
  - **Config Explorer** (`/config`) — recursive entity tree + detail-edit CRUD across all 7 entity types
    (Settings/LabelDef/MandA/Line/Printer/FirePoint/Map) via the pluggable editors (UI-CFG).
- **`PandA.UI.DemoHost`** — thin Blazor Server app with Sim-backed implementations of every contract
  (`DemoDataStore` seed) + `AddPandaDemoBackend()` DI wiring. Runnable, clickable, zero real backend.
- **Automated gates + CI** — `PandA.UI.Tests` (bUnit render+interaction) and `PandA.E2E.Tests`
  (Playwright screen-load per route: 2xx, no error UI, heading, zero console errors, theme toggle);
  `.github/workflows/ci.yml`. Root `.editorconfig` silences transitively-included Meziantou style rules under
  warnings-as-errors.

### Changed
- Review-driven robustness hardening (reviewer-logs 005/006): every screen's backend interface calls wrapped
  in `try/catch`→Snackbar to protect the Blazor Server circuit when a real (non-Sim) adapter throws.
- Label Lookup reconciles the expanded row on reload (re-fetch or collapse); Config Explorer re-resolves the
  selected tree node after save so the detail title can't go stale.
- Config Explorer's CSV parsing preserves interior empty positions for positional fields (buffer order /
  tracking devices) instead of silently shifting them.

### Decisions
- **decision-019** — standalone Blazor UI: RCL render-mode-agnostic, depends only on view-model interfaces,
  Sim-backed demo host, Axon design system, light+dark, desktop-first, no RBAC yet.

### Reviews
- **reviewer-log 005** — Label Data Lookup: circuit-safety try/catch + stale-expansion reconcile (applied);
  operator-identity gap backlogged.
- **reviewer-log 006** — MandA + Config Explorer: positional-CSV corruption + stale selected-node (applied);
  behavioral test assertions strengthened. Draft↔DTO mappings verified faithful across all 7 entity types.

### Learnings
- **learnings/002** — gates-first UI, guard handlers against a throwing real backend behind a polite Sim,
  reconcile state on reload, assert payloads not call counts, Axon/MudBlazor alias + analyzer infra, test-hook
  patterns.

### Known follow-ups
- Operator identity is hardcoded `"operator"` on all audited reprints — must be wired to real identity before
  the audit trail is trustworthy (backlog: "Operator identity for audited reprint actions").
- MandA/Reprint **Core** manual entry points (`sdisp_MA_Scan_Induct`, `TransportOrder.AuthorizeReprint`) remain
  the Sim surface only; real Core wiring is the econtroller-adapter / Wave-0.5 backend work.

### Tests
- 231 backend + 11 bUnit + 6 Playwright e2e passing.

---

## [0.5.0] — 2026-08-12 — Lane-eval + verify-semantics cycle

First tagged version. Captures the accumulated PandA.Core / PandA.Sim / PandA.Harness engine built to date
plus this cycle's lane-eval slice and the retro-review verify/advice adjudications (decision-004).

### Added
- **Lane eval / spare / dynamic printer state** (arch-log 012, gap F09): dynamic `PrinterState`
  (`LastStatusUpdate`, `VerifyFailCount`, `IsOnline`), per-orientation `PrinterGroupPolicy` with a generalized
  degraded/slow-line policy (supersedes the source 2-Printer Rule), `ZoneState`, and `LaneEvalService`
  (promote/demote spare → `SlowLine`/`ShutLine`/`ShutZone`). Wired into `SimHost` + harness
  (`p <id> up|down`, `z up|down`, `s`).
- Verify bypass safety (decision-004 VF-3): a bypass that reads no-read/no-data now fails, holds the carton,
  and counts toward the pause threshold (a clean bypass still proceeds without counting).
- Configurable post-trip threshold reset (decision-004 VF-4): `VerifyThresholdTracker(resetOnTrip)`.
- Fire-point resolve-on-print integration coverage; `PrinterSelectionService.IsAvailable` invariant doc.

### Changed
- `VerifyThresholdTracker` constructor now takes `resetOnTrip` (default `false` = prior behavior).

### Decisions
- **decision-004** — verify-core & re-advice semantics: VF-1 (unexpected/duplicate scan ⇒ FAIL, kept strict),
  VF-2 (same-type expected = ordered slots, kept), VF-3 (bypass glitch fails+counts, implemented),
  VF-4 (configurable post-trip reset, implemented), AD-1 (reprint-rules-gated re-advice → backlog F-ADV1).

### Baseline (pre-0.5.0, previously untracked)
- Phase-1 print path: carton advice (MP1), induct/print (MP2), printer selection + load-balancing/collision
  rules, per-line label buffer order, fire-point profiles (inch/edge `1T/1L/0M`).
- Phase-2 verify: classification/toggles, per-line consecutive fail-threshold, verify-station lifecycle.
- Reprint lifecycle correction (decision-003): monotonic `PrintCount`, hold-for-intervention, operator-gated
  reprint.
- Message sim harness over real 281/286 wire frames; PLC↔PandA protocol doc (arch-log 009).

### Tests
- 231 passing.