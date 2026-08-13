# UI Screens — standalone Blazor module (Axon design system)

**Status:** Specced (grill complete 2026-08-12). Rolled into the Phase-2 coding marathon as a
first-class workstream. See decision-019.
**Design system:** `ElementLogic.Axon` Blazor component library + design tokens
(https://element-logic.github.io/axon-blazor). Light **and** dark themes with a toggle. Modern,
sleek, visually appealing is an explicit requirement.

## 0. Architecture (locked)

Three layers — standalone today, drop-in for another Blazor host (e.g. econtroller) tomorrow:

1. **`PandA.UI` (Razor Class Library)** — all pages, components, Axon theming. Render-mode-agnostic
   (must work whether the host is Blazor Server or WASM). Any host references it, maps the routes,
   and the screens appear.
2. **View-model interface contracts** — the RCL depends **only** on a set of interfaces (view/query
   + command services), **never** on `PandA.Core` or a database directly. The host wires these to
   whatever backs them (Core in econtroller; anything else elsewhere). This is what makes it portable.
3. **`PandA.UI.DemoHost` (thin standalone Blazor Server app)** — references the RCL + an in-memory
   / **Sim-backed** implementation of the interfaces (reuse `PandA.Sim` seed data). Runnable,
   clickable app with zero backend for design review, demos, and UI tests.

**Cross-cutting decisions**
- **Desktop-first**, single surface, responsive (a touch/floor-terminal layout is a later theme
  concern, not a rearchitecture). **No role-gating** for now (single user).
- **Config Explorer is static CRUD** — edits committed via an explicit **Save**; screen refreshes
  after save. No live streaming here.
- **Status Dashboard is live-push** — the view-model contracts expose subscription streams; the
  demo host (Blazor Server / SignalR) raises them from simulated events.
- **Reprint authorization is an audited action button on a carton**, not a standalone screen
  (appears in the Label-Data grid and Reject Cartons).
- **Event Log is out of scope** — econtroller's UI already provides one.

## Screen set (marathon scope)

1. Config Explorer (CRUD)
2. Status Dashboard (live)
3. Label Data Lookup (read-only grid + reprint action)
4. MandA manual station
5. Reject Cartons (list + reprint)
6. ~~Reprint Authorization~~ → action button, not a screen
7. ~~Event Log~~ → dropped (econtroller)

---

## 1. Config Explorer — full CRUD, Save-based

**Ports the source SiteBuilder procs** (`sdisp_TOOL_SiteBuilder_*`, `sdisp_GUI_LabelProfile_*`,
`sdisp_GUI_GetLabel*`). Layout = **left tree explorer + right detail-edit panel**.

**Entity tree**
```
System (root)
├─ Settings            global flags/thresholds: encoder res, reprint rules,
│                       load-balance, 2-printer rule, verify thresholds, purge…
├─ Label Definitions   label types/names, orientations, print positions
├─ MandA Stations      name, IP, port  (station entities; feed the MandA screen dropdown)
└─ Lines
   └─ [Line]           zone(s), tracking devices, buffer order, active Map,
      │                 line-control policy: allow-degraded / slow-floor / online-min
      ├─ Printers
      │  └─ [Printer]  IP/port, orientation Side/Top, label types it can print,
      │     │           config order, encoder res, label width, spare-eligible,
      │     │           top-apply kinematics: tamp height / belt speed / tamp speed
      │     └─ Fire Points / Profiles   per label type: print device+point,
      │                                   apply device+point, dynamic-apply
      └─ Maps          named profile collections; exactly one active per line
```

**Behavior**
- Select a node → detail-edit panel binds its fields (Axon form controls / PropertyPanel).
- **Save** persists via a command interface; **Cancel** reverts; **dirty-state tracking** with an
  unsaved-changes guard when navigating away; tree/panel **refresh after save**.
- Create/edit/delete for every entity (lines, printers, fire points, profiles, maps, MandA
  stations, settings, label defs). Destructive actions confirm.
- No live status here (static config).

**View-model contracts (illustrative):** `IConfigTreeQuery`, `ILineEditor`, `IPrinterEditor`,
`IFirePointEditor`, `IMapEditor`, `IMandaStationEditor`, `ISettingsEditor`, `ILabelDefEditor`.

---

## 2. Status Dashboard — separate, live-push

At-a-glance health of **lines and printers** in the system.

**Shows (live):** per line — running / slow / stopped (+ reason), active Map; per printer —
online / offline, spare state, verification threshold, last-printed, label types. Updates in real
time (push).

**View-model contracts:** `ILineStatusStream`, `IPrinterStatusStream` (subscription/observable).
Demo host raises them from Sim events.

---

## 3. Label Data Lookup — transport-order grid (read-only + reprint action)

**Ports** `sdisp_GUI_GetPandaData` (grid over `sdivw_PandaDataXRef`, TOP 500). A grid of transport
orders; **expand a row** to reveal its associated PandA label data.

**Row detail (expansion):** the 6 label slots as a **structured table per slot** — LabelType,
LabelBarcode, LPN — plus a **collapsible raw-ZPL view** per slot. Also: carton status, verify
time/result, printed count, profile, pass/fail dest, wave.

**Filters (locked):**
- **Fuzzy search box** matching BlindLabel **OR** UPC **OR** GTIN **OR** EAN **OR** LabelBarcode
  (mirrors the source `LIKE '%…%'` across xref barcodes)
- WaveID
- Date range (creation / verify)
- CartonStatus
- Verify state (enabled / passed / failed)
- Printed flag/count
- ProfileName
- Line / Printer

**Actions:** read-only, **except** the audited **Authorize Reprint** action on a carton row.

**View-model contracts:** `ITransportOrderQuery` (filtered, paged), `ICartonLabelDetailQuery`,
`IReprintAuthorizationCommand`.

---

## 4. MandA — manual print-and-apply station

Single **station panel**; multiple MandA stations may exist. A MandA is a **config entity**
(name, IP, port) — created in the Config Explorer; the panel offers a **dropdown of available
MandA stations**.

**Operator flow**
1. Select a MandA station from the dropdown.
2. **Scan the carton's barcode** → system resolves the carton and shows its available **ZPL
   label(s)** (the slots).
3. Operator **selects which label(s) to print** from the carton.
4. System prints the selected label(s) to the station (IP/port).
5. Operator applies by hand.
6. Operator **scans each printed label to verify** — **per-label** verify.

Live station state on the panel. Differs from the automated line by: operator-selected label
subset + per-label verify scanning.

**Backend note:** ports `sdisp_GUI_MandA_Scan` → `sdisp_MA_Scan_Induct`, `sdisp_GUI_MandA_Verify`,
`sdisp_GUI_MandA_Screen_Update`, `sdisp_GUI_GetMandaList`,
`sdisp_TOOL_SiteBuilder_Get_MandAs_NextAvlName`. Core needs a **manual entry point**: look up
carton by scanned barcode, print a chosen subset of its labels, verify per scanned label. Reuses
the existing lifecycle services where possible (PickPrinter already has the `MANDA%` bypass).

**View-model contracts:** `IMandaStationQuery` (dropdown), `IMandaScanCommand` (resolve carton +
labels), `IMandaPrintCommand` (print selected), `IMandaVerifyCommand` (per-label), plus a station
state stream.

---

## 5. Reject Cartons — list + reprint

**Ports** `sdisp_GUI_GetPandARejectCartons`. A filterable list of rejected cartons. Each row shows
the **rejection reason**. Per-carton action: audited **Authorize Reprint** (same command as the
lookup grid). No release/clear action for now.

**View-model contracts:** `IRejectCartonQuery`, `IReprintAuthorizationCommand` (shared with #3).

---

## Open / deferred UI items
- Touch / floor-terminal layout (responsive theme pass) — later.
- Role-based access / permissions — later (single user now).
- Fire-point Map **switch** as an operator quick-action (decision-011) — currently editable in the
  Config Explorer; a dedicated quick-switch control can come later.
- Rendered label-image preview (ZPL→image) — not now; raw ZPL only.

---

## As-built status (2026-08-13, v0.6.0 — Wave 0+1 complete)

The architecture above is implemented as specced. All five screens shipped, each gated (build under
warnings-as-errors+nullable, Playwright screen-load, bUnit interaction) and reviewed (reviewer-logs 005/006).

| Tracker | Screen / layer | Route | As-built | Gate |
|---------|----------------|-------|----------|------|
| UI-ARCH | RCL + contracts + Sim demo host | — | `PandA.UI` / `PandA.UI.Contracts` / `PandA.UI.DemoHost` all built; `AddPandaDemoBackend()` DI | build + boot 200 |
| UI-DASH | Status Dashboard | `/` | live line/printer status streams, push updates | bUnit + e2e |
| UI-LOOKUP | Label Data Lookup | `/lookup` | filter grid + expandable slots (raw ZPL) + authorize-reprint | bUnit + e2e |
| UI-REJECT | Reject Cartons | `/rejects` | filtered list + shared authorize-reprint + empty state | bUnit + e2e |
| UI-MANDA | MandA Station (UI part) | `/manda` | station dropdown → scan → print subset → per-label verify | bUnit + e2e |
| UI-CFG | Config Explorer | `/config` | recursive tree + detail-edit CRUD across all 7 entity types | bUnit + e2e |
| UI-REPRINT | Reprint authorization action | (in Lookup/Rejects) | audited button wired to `IReprintAuthorizationCommand` | covered by Lookup/Reject tests |

**Cross-cutting as-built rules added during the build (not in the original spec):**
- All backend interface calls are wrapped `try/catch`→Snackbar so a throwing real adapter can't kill the
  Blazor Server circuit (the Sim never throws; the real Core/econtroller adapter can). See reviewer-log 005.
- Reloads reconcile dependent view state (Lookup re-fetches/collapses the expanded row; Config re-resolves the
  selected node) rather than only re-querying the list. See reviewer-log 006.
- Positional collection fields (line buffer order, tracking devices) preserve interior empty positions through
  the CSV edit round-trip.

**Deferred to Wave 0.5 / econtroller-adapter (Sim-only today):**
- `UI-MANDA` **Core** manual entry point (`sdisp_MA_Scan_Induct`: resolve-by-barcode, print subset, per-label
  verify reusing the lifecycle + `MANDA%%` bypass) — currently satisfied by `DemoMandaServices`.
- `UI-REPRINT` **Core** `TransportOrder.AuthorizeReprint(reason)` audit sink + **real operator identity**
  (hardcoded `"operator"` today — backlogged).

**Remaining Wave-2 UI hardening:** cross-screen nav e2e, theme-toggle coverage on every route,
create-new-entity flows in Config Explorer, operator-identity wiring.
