# Backlog

Features, ideas, and enhancements that came up during conversation but are **not part of the current development cycle**.

## Purpose

When the user or any agent mentions a feature/idea that isn't needed right now, the Orchestrator automatically logs it here. Nothing gets lost — it just gets parked for later.

## Format

Each entry:

```markdown
### {Short title}
**Added:** YYYY-MM-DD
**Source:** {Who mentioned it — user, Planner, Senior Coder, etc.}
**Context:** {Why it came up, what conversation it was part of}
**Description:** {What the feature/idea is}
**Priority:** Low / Medium / High (estimated)
```

## Rules

1. The Orchestrator adds items here AUTOMATICALLY — user should never have to say "add that to the backlog"
2. If a feature is discussed and deemed out of scope for current work → it goes here immediately
3. When starting a new cycle, the Planner reviews the backlog for items that are now relevant
4. Items are never deleted — they're moved to "Done" or "Won't Do" sections when addressed

## Items

### eHub `ZebraConnector` — raw-TCP printer connector (ZPL send + `~HS` status)
**Added:** 2026-08-12
**Source:** Senior research pass (eHub connector architecture) + User (external-connections discussion)
**Context:** External-connections design (decision-018). eHub has NO raw-TCP/Zebra connector — all
existing connectors are HTTP, AT-Protocol (closed), ADS/BluePaw, or NATS. PandA's printer channel
(direct TCP per printer: ZPL label data out, Zebra `~HS` host-status string in) has no precedent.
**Description:** Build `PandA.eHub.ZebraConnector` as a new `IPacketTransfer` connector: maintain a
`TcpClient` per printer; on `FromMfc`/print-job channel packets send raw ZPL bytes; poll `~HS` on a
cadence, feed the response to Core's `PrintEngineStatusParser`, and publish a status record to the MFC.
One connector instance per printer (each its own JSON config). Alternative to investigate: Zebra
Link-OS HTTP API via `DefaultHttpConnector` (no custom socket code) if the site's printers support it.
This is **adapter-layer, deferred** — Core/Sim build first, neutral. Core already owns the `~HS` parser.
**Priority:** High (required for the eHub adapter; net-new build)

### eHub adapter site-coordination gaps (PLC tags, ADS structs, transport mode, NuGet feed)
**Added:** 2026-08-12
**Source:** Senior research pass (decision-018 gap list)
**Context:** Binding PandA's Core ports to eHub connectors needs site/PLC-team facts not yet known.
**Description:** Track and resolve before the eHub adapter is built (none block Core): (G-ADS1) ADS
transport mode — MQTT vs native vs embedded-router (embedded router is half-built; server components
commented out → MQTT is the reliable path); (G-TAG1) real TwinCAT tag paths + CSV type codes for
frames 281–286 (does the PLC use the `vMsg.Msgs_eHub.*` buffer convention? are codes literally
`281`…?); (G-STRUCT1) blit-able unmanaged struct layouts for outbound fire-point/divert/stop-slow
commands must match the TwinCAT DUTs exactly; (G-NUGET1) access to the Element-Logic private NuGet
feed and whether `IBluePawPLCResponseConverter` is overridable from a consumer plugin or needs a
`BluePawConnector` subclass; (G-WMS1) WMS/DTC transport REST vs NATS/ELWS (pairs with decision-015).
**Priority:** High (gating for adapter phase)

### Wave / WaveRange data + wave lifecycle
**Added:** 2026-08-11
**Source:** Senior Coder + User (config disposition triage)
**Context:** Scoping the PandA→eController Phase-1 vertical slice (induct→print happy path). Config
disposition decision (see architecture-log/decision-001).
**Description:** `Wave` (435 rows) and `WaveRange` (27 rows) are live/operational wave data, not
commissioning config. The wave *lifecycle* logic (`sdisp_GUI_PandaWaveAction_*`, `sdisp_PA2DCMS_WaveStatus`,
`sdisp_TOOL_CUSTOM_Wave*`) is a later functional phase. Not required for Phase 1.
**Priority:** Medium

### Operator GUI (Blazor reimplementation of sdisp_GUI_* screens)
**Added:** 2026-08-11
**Source:** User (Phase-1 = backend-only; reaffirmed as essential-but-not-yet)
**Context:** Phase 1 is backend-only. The ~30 `sdisp_GUI_*` procs back operator screens (label profiles,
wave control, printer/print-engine status, panda list, scan logs, reject cartons).
**Description:** Reimplement operator screens as Blazor pages using CrudTable/PropertyPanel once the
backend services exist. User has flagged this as **essential** to the overall product (not optional) — it
is deferred, not dropped. Schedule as a dedicated phase after the backend engine slices land.
**Priority:** High (essential; deferred)

#### GUI screen inventory (33 `sdisp_GUI_*` source procs, catalogued 2026-08-12)
For the dedicated UI phase, map each source screen group to a Blazor page:

| Screen group | Source procs | Notes |
|---|---|---|
| **Label profile / Map CRUD** | GetLabelData, GetLabelDefList, GetLabelOrientations, GetLabelPrintPositions, GetLabelProfile{Details,Header,List}, LabelProfile_{Insert,Update,Delete} | Fire points / orientations / print positions CRUD. Overlaps the fire-point Map/Profile switching + CRUD item below (decision-011). |
| **Live monitoring** | GetPandaData, GetPandaEvents, GetPandAList, GetPandaScanLog_{In,Verify}, GetPrintEngineStatus, GetPrinterList, GetPrinterStatus | Carton/event/scan-log/printer dashboards. Read-mostly. |
| **Reject cartons** | GetPandARejectCartons | Rejected-carton screen. |
| **MandA** | GetMandaList, MandA_Scan, MandA_Verify, MandA_Screen_Update | Manual scan/induct/verify station — see dedicated item below. |
| **Waves** | GetPandAWave{Cartons,Control,History}, GetPandaWaves, PandaWaveAction_{Start,Suspend,Complete} | Wave control. TABLED (waves out of initial scope). |
| **Data export** | GetTableExports, QueueTableExport | Table export / reporting. |
| **Reprint lockout** | SetPrintedFlag | Superseded by lifecycle (decision-013) — do NOT reimplement as-is. |

### UI — Label data lookup screen (GetLabelData)
**Added:** 2026-08-12
**Source:** User ("for UI, add these topics: label data lookup").
**Context:** `sdisp_GUI_GetLabelData` returns the label-slot data (LabelData1..6 / LabelBarcode1..6 /
LabelType1..6) for a carton so an operator can inspect/preview what will print. Backend already models the
six label slots on the transport order (decision-007 F5).
**Description:** A read-only lookup screen: operator enters a carton / blind-label id and sees the resolved
label slots (data, barcode, type per slot), current fire-point/profile, and print/verify state. Also the
place to surface the multi-barcode xref identifiers (BL/UPC/GTIN/EAN/ItemID/oLPN) once xref lands.
**Priority:** Medium (UI phase)

### UI — MandA manual scan/verify station (MandA_Scan / MandA_Verify / MandA_Screen_Update / GetMandaList)
**Added:** 2026-08-12
**Source:** User ("for UI, add these topics: ... MandA").
**Context:** MandA is a **manual** print/apply + verify mode: instead of the automated PLC line driving
induct→print→verify via 281–286 frames, an operator drives it from a GUI screen — `MandA_Scan` (manual
induct), `MandA_Verify` (manual verify), `MandA_Screen_Update` (live screen refresh), `GetMandaList` (work
list). This is the manual fallback / low-volume station path parallel to the automated lifecycle.
**Description:** Investigate the MandA procs to determine how much is a *new backend path* (a manual
induct/verify entry point into the same lifecycle services) vs. purely a screen over existing services.
Likely a thin GUI over `VerifyStationService` + the induct/print services, but confirm against source
whether MandA bypasses any automated gates (e.g. fire-point resolution, threshold pausing). Then build the
Blazor screen. **Open:** confirm what "MandA" stands for and its exact relationship to the automated line.
**Priority:** Medium (UI phase); backend-investigation task first

### Dynamic height→orientation apply-point transfer (Side↔Top)
**Added:** 2026-08-11
**Source:** User (during load-balancing grill)
**Context:** Reviewing printer selection (architecture-log 005). A label type (e.g. `Shipping`) can map to
both a Side-apply and a Top-apply printer. The rule: print **Side by default**, but **transfer to Top when
the carton height is below a threshold**. Lives in custom `sdisp_TOOL_CUSTOM_DynamicApplyPoint` /
`DynamicPrintPoint` setting; not in the core `PickPrinter` body.
**Description:** Implement height-driven orientation resolution as a pre-filter to printer selection:
resolve required orientation (Side default / host value / height<threshold→Top), then load-balance within
printers of that orientation. **Provisioned now** — `IPrinterSelectionService` treats candidates as
(label type + orientation), printer config carries `PrinterType`, and carton height flows into selection;
only the threshold-transfer decision is deferred. Threshold scope (global vs per-line vs
per-label-type/profile) to be confirmed when scheduled.
**Priority:** Medium (provisioned; deferred)

### Duplicate-advice handling toggle (overwrite ↔ append + reprint rules)
**Added:** 2026-08-11
**Source:** User (during design grill)
**Context:** MP1 receives label-advice keyed by blind label (`TuId`). Source has `OverwriteLabelData`
(default 0) plus a don't-reprint-if-printed rule and append/oldest-wins disambiguation
(`sdisp_PA_LookupCarton`). User wants this **toggleable**, but Phase 1 uses **overwrite / last-wins**.
**Description:** Introduce a config setting that selects duplicate-advice semantics: (a) overwrite/last-wins
[Phase-1 default], (b) append + disambiguate at induct (oldest-wins unless overwrite; skip if already
printed and reprint off). Model the setting seam now; implement append + reprint rules when scheduled.
**Priority:** Medium (Phase-1 uses fixed overwrite; toggle deferred)

### Scanner buffer-order parsing (delimited string -> typed scanned labels) — DONE 2026-08-11
**Added:** 2026-08-11
**Source:** User (Phase-2 verify grill)
**Context:** Verify (msg 286) receives a **delimited multi-barcode string** from the scanner; each position
maps to a `LabelName` via `Settings_LabelBufferOrder`. Phase-2 verify tests accepted **pre-typed** scanned
labels; the position-based parser is needed for real integration.
**Resolution:** Implemented `LabelBufferOrder` (+ `LabelBufferPosition`) in Core — a **per-line** position→type
map (default 1=BlindLabel,2=Shipping,3=Content,4=Parcel) with `Type(buffer)` doing the source's
`STRING_SPLIT`+`JOIN ON rownum=LabelNumber` (INNER JOIN, empty slots = physically-absent, skipped). Wired to
`LineConfig.BufferOrder`; `SimHost` now types the real 286 buffer through it. Covered by `LabelBufferOrderTests`
(15 tests) + live harness. See architecture-log 009 §8.
**Priority:** ~~Medium~~ Done.

### BluePaw stop-line / slow-line tag (codes TBD)
**Added:** 2026-08-11
**Source:** User
**Context:** There is a "BluePaw" tag/command used to **stop a line** or **slow a line** down. The exact
tag values/telegram codes are **not yet known** and need to be captured from the site/protocol.
**Description:** Once the codes are known, model a line-control egress (stop / slow-down) alongside the
verify-threshold printer-pause signal. Likely a small `ILineControl` port emitting the BluePaw command.
Related to the shutdown / slow-down behavior noted in architecture-log 005 (LaneEval spare logic).
**Priority:** Medium (blocked on codes)

### Operator reprint authorization (web-screen action)
**Added:** 2026-08-11
**Source:** User (decision-003)
**Context:** A verify-failed carton is HeldForIntervention and cannot reprint automatically. The operator
manually authorizes a reprint per carton on the web screen (source sdisp_GUI_SetPrintedFlag / per-carton
reprint override). Core exposes TransportOrder.AuthorizeReprint(reason); the operator-facing GUI action
+ audit is not built yet.
**Description:** Build the operator web action (+ permissions/audit) that calls AuthorizeReprint for a
carton, and surface held cartons for intervention. Also consider a reprint-only-the-missing-labels flow,
enabled by the per-label print state now tracked on TransportOrder.
**Priority:** High (operational; part of operator GUI)
**Update 2026-08-13 (v0.6.0):** The GUI **action** is now built — the audited **Authorize Reprint** button
in the Label Data Lookup grid and Reject Cartons list, wired to `IReprintAuthorizationCommand`
(whole-carton + per-slot), with held cartons surfaced. **Still open:** the real Core `AuthorizeReprint`
audit sink (Sim-only today) and **operator identity** (see next item). Per-missing-label-only reprint is
supported at the contract level (`AuthorizeSlotReprintAsync`) but has no dedicated UI yet.

### Operator identity for audited reprint actions
**Added:** 2026-08-13
**Source:** Reviewer (reviewer-log 005), Phase-2 UI
**Context:** Every audited reprint from the Lookup/Reject screens currently passes the literal string
`"operator"` as the actor (`AuthorizeReprintAsync(cartonId, "operator", ...)`). With no RBAC/auth in the
standalone module yet, there is no real identity to attribute the action to, so the audit trail is not
trustworthy.
**Description:** Wire real operator identity into the audited reprint calls (and any future audited action)
— from the host's auth context when embedded in econtroller, or a lightweight operator-select/login in the
standalone demo. Replace the hardcoded `"operator"`.
**Priority:** High (audit integrity; blocks trusting the reprint audit trail) — targeted for Wave 2.

### Global "Reprint Labels" allow-all setting
**Added:** 2026-08-11
**Source:** Settings RecID 17 (default 0)
**Context:** The source setting "Reprint Labels" permits reprinting even after successful verify when ON.
Core currently models only the per-carton operator authorization (the emphasized manual-intervention path);
the global allow-all bypass is not wired into the induct gate yet.
**Description:** When scheduled, plumb a line/global ReprintLabels flag into the induct reprint gate so that
ON allows reprints without per-carton authorization. Default OFF preserves current behavior.
**Priority:** Low (default-off; per-carton path covers the primary workflow)

### Sim harness — advanced data creation / editing
**Added:** 2026-08-11
**Source:** User (sim harness discussion)
**Context:** Pass-1 sim seeds cartons on startup and only runs the fixed slice. Users will want to create /
edit carton + line/printer data interactively (add cartons, change label sets, toggle printer health).
**Description:** Add data-authoring commands (or a small scenario file) to the harness so operators can
build and mutate test data without recompiling.
**Priority:** Medium

### Sim harness — expand message coverage beyond the bare-bones slice
**Added:** 2026-08-11
**Source:** User (sim harness discussion)
**Context:** Pass-1 speaks only 281 (induct, DeviceId=1) and 286 (verify) on the happy path (auto-pass).
**Description:** Layer on, in order: user-selectable verify outcome (pass / wrong / no-read → Verified vs
Held), the reprint/authorize loop, then codes 282 (print), 283 (printer status), 284 (zone status),
285 (late assign). Also type the 286 label buffer via the real Settings_LabelBufferOrder map instead of
the advised-order stand-in.

### Fire points / tracking devices / apply-vs-print + lane destination (outbound to PLC)
**Added:** 2026-08-11
**Source:** PLC Process_PA reverse-engineering (ItmSort-AI)
**Context:** The real 281 response is NOT "send ZPL" — it writes a bundle of vPA.Assign[i] tags to the PLC
(RecID, Seq, Sorter, SourceMode, Dest lane, PrintPoint/PrintPointDevice[printerId], ApplyPoint/
ApplyPointDevice[printerId], MsgRdy) computed by sdisp_TOOL_PA_GetPrinterFirePoints. This coordinates WHERE
on the conveyor to fire the print head and the applicator, and which lane to route to.
**Status:** **PARTIALLY DONE (2026-08-11).** The fire-point *model* is now in Core (architecture-log 010):
`ApplyPoint` (inch+edge notation, sign/edge guard), `FirePoint` (print/apply tracking devices + points,
neglect-print + tracking-device guards), `FirePointProfile` (per printer+label), `FirePointResolver`, and a
static per-line `LineConfig.ActiveProfile`. Fire points resolve during induct and surface on `PrintJob`
(harness logs them). **Still deferred (below):** lane destination, the vPA.Assign tag *transport* (owned by
the ADS BluePaw connector in the eController adapter — 009 §2), profile *switching*, host-driven ProfileName,
and DynamicApplyPoint.
**Priority:** Model done; remaining items tracked below.

### Fire-point profile switching + host-driven ProfileName + DynamicApplyPoint
**Added:** 2026-08-11 · **Updated:** 2026-08-12 (decision-011 locks the Map/Profile model)
**Source:** User (fire-point design conversation) + sdisp_PA2BP_SendPrinterFirePoints
**Depends on:** Operator/GUI screens (see "GUI / operator screens" backlog item) — profile switching is a **UI**
feature and should be built with those Blazor screens, not before.
**Model (decision-011):** two levels — **Profile** = fire points for one (printer × label-type) pair;
**Map** = a named collection of profiles covering the line; exactly one Map active per line. Switching
customer/shift = activating a different Map (swaps all fire points at once). Static clients = one Map with one
static Profile. `Active` = "selectable in catalog", not "in use". Host with no map name → keep the active Map.
Print point = raw `int` (e.g. 800); apply point = inch+edge string (`1T/1L/.4M/-.4M/5.25L`) — already split in Core.
**UI tasks (this is UI code, per owner):**
  - **Map switch screen** — operator selects the active Map per line.
  - **Map + Profile CRUD** — create/edit Maps and their per-(printer,label) fire points (print device/point,
    apply device/point). Ports the SiteBuilder FirePoint procs.
**Non-UI backlog:** (a) host-driven Map/Profile name in the inbound message (PROFSW pattern-match);
(b) `sdisp_TOOL_CUSTOM_DynamicApplyPoint` size/orientation adjustment of the resolved `ApplyFirePoint`.
**Core work (not UI, can land earlier):** `FirePointMap` container + per-line active-Map pointer on
`LineConfig` (replaces single `ActiveProfile`) + resolver picks active Map → (printer,label) Profile.
**Priority:** Medium (happy path covered; switching + CRUD land with the UI work)

---

## Gaps from senior source-coverage review (architecture-log 011, 2026-08-12)

The senior diffed all 322 source objects vs implemented + planned work. 18 gap families below
(functionality neither built nor previously backlogged). Detail + source citations in `011`.

### GAP F08 — Lane routing (status → divert lane)
**Source:** `sdisp_TOOL_PA_GetFinalLaneFromStatus`, `LaneDef` table, `LastDiverted` column.
**Description:** Carton status maps to a physical divert lane (round-robin across lanes for a status). No
`LaneDef` model in C#; `InductResult`/`VerifyOutcome` carry no `DivertLane`. **Coupling:** consumes verify
status we already produce; completes the fire-point/PLC outbound bundle (arch-log 010).
**Priority:** High

### GAP F09 — Lane evaluation + spare printer management + dynamic printer state ✅ DELIVERED (arch-log 012)
**Source:** `sdisp_PA_LaneEval`, `sdisp_PA_Status_Printer`, `sdisp_PA_Status_Zone`, `PandaState`,
`PandADetails`, `2 Printer Rule` setting.
**Description:** Dynamic spare-printer promotion/demotion driven by printer/zone status messages; today
`PrinterState.PlcOnline/EngineOnline` are static bools never updated at runtime. **Coupling:** feeds printer
selection/load-balancing (arch-log 005).
**Status (2026-08-12):** Built. `PrinterState` now dynamic (LastStatusUpdate/VerifyFailCount, IsOnline);
`PrinterGroupPolicy` (per-orientation min/count + generalized degraded policy superseding the 2-Printer Rule);
`ZoneState`; `LaneEvalService` (promote/demote/slow/shut/zone) returning `LaneEvalResult`; wired into SimHost
+ harness (`p <id> up|down`, `z up|down`, `s`). **Remaining (bookmarked):** engine-status message ingestion
(F13), real BluePaw slow/shut/zone egress tags + codes, cross-process status lock.
**Priority:** High

### GAP F10 — Exception label building
**Source:** `sdisp_TOOL_PA_BuildExceptionLabel`, `LabelTemplates` table, exception block in `sdisp_PA_LookupCarton`.
**Description:** On NoRead/NoData/NoInfo/etc., print a templated exception label (8 ZPL templates).
`PrintExceptionLabels` seed default 0 but commonly enabled. No C# path. Depends on scanner-quality detection (F20).
**Priority:** High

### GAP F15 — PLC event / carton recovery
**Source:** `sdisp_BP2PA_Event` (codes 2012, 2015, 2016, 2017, 2019).
**Description:** Tracking events reset `Printed=0`/`ActiveRecord=1` (re-arm a carton) before the verify
scanner. No `TransportOrder.ResetForTrackingEvent()` equivalent. **Coupling:** interacts with reprint lifecycle (decision-003).
**Priority:** High

### GAP F16 — Structured event logging
**Source:** `sdisp_Log_Event`, `sdisp_eLog_LogIt`, `sdisp_eLog_add`, `EventLog`, `uEventLog`, `EventDescriptions`.
**Description:** Two-tier event/audit log called by nearly every SP. No `IEventLog` abstraction in C#.
**Coupling:** cross-cutting — cheaper to add the port before more services are built.
**Priority:** High

### GAP F18 — XRef multi-barcode matching
**Source:** `PandaDataXRef` table, `sdivw_PandaDataXRef`, xref joins in `sdisp_PA_LookupCarton` + `sdisp_TOOL_PA_VerifyLabel`.
**Description:** Induct and verify both fan out to alternate barcodes (UPC/GTIN/EAN) via `PandaDataXRef`. C#
only matches `TuId`/`Label.Lpn` — silent correctness gap on multi-barcode sites. **Coupling:** touches
`TransportOrder`, induct, and verify together.
**Priority:** High

### GAP F11 — ZPL vetting (VetLabel)
**Source:** `sdisp_TOOL_PA_VetLabel`; `FilterLabels` setting (default 1/ON).
**Description:** Strips 25+ ZPL config commands (`^MCY`, `^MD`, `^PR*`, …) and forces `^LH13,0` before print.
C# sends raw ZPL.
**Priority:** Medium

### GAP F13 — PrintEngine status ingestion
**Source:** `sdisp_PA_Status_PrintEngine`, `PrintEngineStatus` table, `sdiudf_PA_GetPrinterRecIDFromConnections`.
**Description:** Parses 3 Zebra TCP status messages (11/10/1 comma formats) into 27 health flags. No C#
equivalent; operator GUI (printer status screen) depends on it. Prereq: printer status suffix (F12).
**Concurrency (from lane-eval review, doc 012 §9):** when this ingestion path lands, it must
**serialize lane evaluation per line** — `LaneEvalService` mutates shared `PrinterState` objects
without locking (fine under single-threaded SimHost today). Concurrent printer/engine/zone signals
on the same line will otherwise race the spare-flag mutations. This is the in-process "per-line guard"
that doc 012 §9 promises in place of the source `sdisp_PA_Lock 'PA_Status'` mutex.
**Priority:** Medium

### GAP F14 — MandA manual apply stations
**Source:** `sdisp_MA_Scan_Induct`, `sdisp_MA_Scan_Verify`, `sdisp_GUI_MandA_*`, `sdisp_GUI_GetMandaList`.
**Description:** Operator-staffed print-and-apply workflow (`MANDA%` panda prefix; first-printer selection).
No mode switch in C#.
**Priority:** Medium

### GAP F19 — ProfileName validation at induct
**Source:** `@IsValid` block in `sdisp_PA_LookupCarton`, `sdivw_LabelProfiles`.
**Description:** Missing/unknown ProfileName → `NoProfile` status, carton not printed. No profile-existence
check in `InductService`. Relates to fire-point profile work (arch-log 010).
**Priority:** Medium

### GAP F20 — Gap error + scanner read-quality detection
**Source:** gap check (`@Gap < @MinGap`) and `?`/`!`/`#`/`*` character detection in `sdisp_PA_LookupCarton`.
**Description:** Detect no-read/no-data/label-conflict/bypass characters and too-close cartons at induct
before lookup. `MinGap` setting not read in C#. Provides exception types for F10.
**Priority:** Medium

### GAP F21 — Carton slot number / PLC index
**Source:** `sdisp_TOOL_GetSlotNumber`, `CartonAssignSeq` SQL SEQUENCE (1–300 cycling).
**Description:** Cycling index that keys the `vPA.Assign[i]` PLC array. `PrintJob`/`InductResult` have no
slot index. **Coupling:** part of the fire-point/lane PLC outbound bundle (pairs with F08).
**Priority:** Medium

### GAP F22 — Reject history audit trail
**Source:** `sdisp_CUSTOM_RejectHistory_Insert`, `RejectHistory` table (17 numeric verify codes).
**Description:** Per-failure reject rows never persisted in C#. GUI reject screen + wave reports depend on it.
**Priority:** Medium

### GAP F23 — Wave auto-complete (hot-path coupling)
**Source:** `sdisp_TOOL_CUSTOM_CheckWaveCmp` (called inside `sdisp_PA_VerifyCarton`).
**Description:** Auto-completes a wave on every verify-pass. Must wire into `VerificationService.VerifyAsync`,
not a later wave phase. Relates to planned Wave lifecycle item.
**Priority:** Medium

### GAP F24 — oLPN xref association
**Source:** `sdisp_TOOL_CUSTOM_LPNxRef`, `sdisp_TOOL_CUSTOM_LPNxRef_Disassociate`.
**Description:** ULW/RF flow associates an outer LPN to PandaData post-advice. Depends on XRef model (F18).
**Priority:** Medium

### GAP F17 — Purge / data lifecycle
**Source:** `sdisp_PA_Purge` + `PurgeSetting_*` settings (7–21 day retention).
**Description:** Nightly cleanup of PandaData, EventLog, PrintEngineStatus, etc. No retention policy or
scheduled service in C#.
**Priority:** Medium

### GAP F12 — Printer status suffix (~HS)
**Source:** `sdisp_TOOL_PA_AppendStatusSuffix`; `PrinterStatusSuffix` setting (default 0).
**Description:** Appends `~HS` to ZPL to request a Zebra status reply. Prerequisite for PrintEngine status
ingestion (F13).
**Priority:** Low

### GAP F25 — SiteBuilder commissioning CRUD
**Source:** `sdisp_TOOL_SiteBuilder_*` (26 procs).
**Description:** GUI-driven config authoring (labels, lanes, pandas, printers, fire points). C# uses JSON
config; admin CRUD path not built. Useful as a reference for entity field specs.
**Priority:** Low


### F-ADV1 — Reprint-rules-gated re-advice (AD-1)
**Added:** 2026-08-12
**Source:** User (retro-review of the advice path; decision-004 / reviewer-log 003)
**Context:** `CartonAdviceService.AdviseAsync` currently wipes lifecycle and re-arms `CanPrint` for ANY
re-advice of an existing carton — a back-door around the decision-003 reprint protection.
**Description:** Gate the re-advice reset on whether the carton is **reprintable** per the reprint-rules
policy ("Reprint Labels" setting, source RecID 17): if reprintable, reset to printable (event log records the
re-run); if not reprintable, do NOT re-arm — the operator must intervene. Requires porting the reprint-rules
setting first. Until then the current always-reset behavior is retained (assumes reprint-allowed).
**Priority:** Medium

### F-LOG1 — Carton run-history event logging (AD-1 bookmark)
**Added:** 2026-08-12
**Source:** User ("bookmark the conversation about logging")
**Context:** Discussing AD-1 re-advice — the operator relies on event logs to see a carton barcode's full
history (how many times it ran, each outcome) as the audit trail behind re-runs/reprints.
**Description:** Emit structured lifecycle events keyed by carton barcode (advise, print run N, verify
pass/fail/bypass, hold, authorize-reprint) so the run history of a barcode is queryable. Ports the source
event-log writes (`sdisp_*Event*` / PandaEvent) into the C# model. Lands with the operator-GUI/observability
work.
**Priority:** Medium

### F-VF4CFG — Expose VF-4 post-trip reset policy in line config
**Added:** 2026-08-12
**Source:** Senior Coder (decision-004 VF-4)
**Context:** `VerifyThresholdTracker(resetOnTrip)` is configurable in code but not yet surfaced as a
per-line/global setting.
**Description:** Surface the post-trip reset policy as configuration (line or global setting) once the
config/settings port lands, so operators can choose refresh-on-trip vs. stay-tripped per line.
**Priority:** Low
