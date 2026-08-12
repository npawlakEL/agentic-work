# Decision 019 — Standalone Blazor UI module (Axon), packaging + screen set

**Status:** Accepted (spec). UI is a first-class Phase-2 workstream.
**Date:** 2026-08-12
**Basis:** Domain-owner grill (16 questions) + source review of `sdisp_GUI_*` procs
(`GetPandaData`, `GetLabelData`, `MandA_Scan/Verify/Screen_Update`, `GetMandaList`,
`GetPandARejectCartons`) and the SiteBuilder config procs. Design system:
`ElementLogic.Axon` (https://element-logic.github.io/axon-blazor).
Full spec: `.project/spec/clusters/ui-screens.md`.

## Packaging (locked)

A standalone UI that plugs into another Blazor host easily. Three layers:

1. **`PandA.UI` (Razor Class Library)** — pages/components/theming, render-mode-agnostic.
2. **View-model interface contracts** — the RCL depends only on these interfaces, never on
   `PandA.Core` or a DB. The host wires them to real implementations.
3. **`PandA.UI.DemoHost` (thin Blazor Server app)** — RCL + Sim-backed interface impls (reuse
   `PandA.Sim` seed data). Runnable with zero backend.

Rationale: keeps the UI as portable as Core/Sim are today. Standalone now (demo host + Sim),
drop-in later (reference RCL, wire interfaces to Core in the econtroller adapter).

## Cross-cutting rulings

| # | Ruling |
|---|--------|
| U1 | Desktop-first, single surface, responsive. No role-gating yet (single user). |
| U2 | Light + dark themes with a toggle; Axon tokens throughout. Modern/sleek is a requirement. |
| U3 | **Config Explorer is static CRUD** — explicit Save, dirty-guard, refresh-after-save. No live push. |
| U4 | **Status Dashboard is live-push** — view-model streams; demo host raises them from Sim events. |
| U5 | Reprint authorization is an **audited action button on a carton**, not a screen. |
| U6 | **Event Log dropped** — econtroller UI already has one. |

## Screen set (marathon scope)

1. **Config Explorer** — left tree (System / Settings / Label Definitions / MandA Stations /
   Lines → Printers → Fire Points·Profiles / Maps) + right detail-edit panel. Full CRUD. Ports
   SiteBuilder + `LabelProfile_*` + `GetLabel*` procs.
2. **Status Dashboard** — live line & printer statuses (online/offline, spare, verify threshold,
   line running/slow/stopped).
3. **Label Data Lookup** — transport-order grid (ports `GUI_GetPandaData`) with expandable
   PandA-data detail: 6 slots as a table (type/barcode/LPN) + collapsible raw ZPL. Filters: fuzzy
   search (blind/UPC/GTIN/EAN/barcode), WaveID, date range, CartonStatus, Verify state, Printed
   flag, ProfileName, Line/Printer. Read-only + Authorize Reprint action.
4. **MandA** — single station panel; MandA = config entity (name/IP/port); dropdown of available
   stations. Flow: scan carton → show its ZPL labels → operator selects which to print → print →
   apply by hand → per-label verify scans. Ports `MandA_*` + `MA_Scan_Induct`.
5. **Reject Cartons** — list (ports `GetPandARejectCartons`) with rejection reason + Authorize
   Reprint action.

## Backend implications (feed the marathon)

- **New Core manual entry point for MandA:** resolve carton by scanned barcode → print a chosen
  **subset** of its labels → **per-label** verify. Reuse lifecycle services + PickPrinter's
  `MANDA%` bypass.
- **MandA station** becomes a config entity (name/IP/port) alongside printers.
- **View-model contracts** are net-new (query + command + status-stream interfaces). They are the
  UI's only dependency surface; Core stays UI-agnostic. The econtroller adapter later implements
  them over Core.

## Deferred
Touch/floor layout; RBAC/permissions; Map quick-switch control; ZPL→image label preview.
