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

### Scanner buffer-order parsing (delimited string -> typed scanned labels)
**Added:** 2026-08-11
**Source:** User (Phase-2 verify grill)
**Context:** Verify (msg 286) receives a **delimited multi-barcode string** from the scanner; each position
maps to a `LabelName` via `Settings_LabelBufferOrder`. Phase-2 verify tests accept **pre-typed** scanned
labels to focus on the compare/code logic; the position-based parser is needed for real integration.
**Description:** Implement a `Settings_LabelBufferOrder`-driven parser that splits the raw scanner string and
maps each slot to a `LabelType`, producing the typed scanned-label list the verify service consumes. Feed
from the real verify message point at integration.
**Priority:** Medium (needed for final integration; deferred from Phase-2 logic tests)

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

### Global "Reprint Labels" allow-all setting
**Added:** 2026-08-11
**Source:** Settings RecID 17 (default 0)
**Context:** The source setting "Reprint Labels" permits reprinting even after successful verify when ON.
Core currently models only the per-carton operator authorization (the emphasized manual-intervention path);
the global allow-all bypass is not wired into the induct gate yet.
**Description:** When scheduled, plumb a line/global ReprintLabels flag into the induct reprint gate so that
ON allows reprints without per-carton authorization. Default OFF preserves current behavior.
**Priority:** Low (default-off; per-carton path covers the primary workflow)
