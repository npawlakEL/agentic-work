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