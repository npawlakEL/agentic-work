# Decision 014 — F13 printer/PLC status ingestion semantics (A3, A4; A1/A2 bookmarked)

**Status:** Accepted (A3/A4); bookmarked (A1/A2)
**Date:** 2026-08-12
**Owner ruling:** "when a printer is paused we should get a status message from the printer
telling us its paused, therefore setting it offline."

## Context

F13 ingests printer/PLC status (msg 283 `PLCStatus`, `PrintEngineStatus`) and updates the
in-memory `PrinterState` (online/offline, fault flags) that drives printer selection and
spare/degraded-line promotion (`LaneEvalService`, arch-log 012). Open questions asked how to
derive online-ness and whether an operator pause promotes a spare.

## Decision

### A4 — Pause = offline (status-driven), normal spare handling
A printer pause is **not** a special "known temporary" state in PandA. The printer/PLC emits a
**status message** indicating paused; F13 ingestion sets that printer **offline**. Being
offline then flows through the **normal** offline path — including spare-printer promotion /
degraded-line handling. There is no separate pause carve-out; the incoming status drives it.

### A3 — `EngineOnline` derived from status flags (pause included)
`PrinterState.EngineOnline` is derived from the printer's reported status flags. A printer is
**offline** if any blocking condition is set — at minimum:
`PaperOut || HeadUp || RibbonOut || Paused` (extend as the real status word is confirmed).
`EngineOnline = !(any blocking flag)`. This supersedes the "no explicit derivation in source"
gap: the port computes online-ness from the status message rather than trusting a single field.

### A1 / A2 — bookmarked pending PLC/BluePaw protocol docs
- **A2:** the exact integer values of msg 283 `PLCStatus` (0=offline / 1=online / fault codes)
  must be confirmed against the PLC/BluePaw protocol spec. Port models an `enum`/mapping behind
  the ingestion boundary so the concrete integers are a config/adapter detail, not baked into
  Core logic.
- **A1:** event codes **217 / 218** (silently excluded at `sdisp_BP2PA_Event` line 71) remain
  unidentified — confirm meaning against protocol docs; until then the port preserves source
  behavior (ignore them) but logs at Trace so they're observable.

## Port shape
- F13 ingestion maps the raw status word → `PrinterState` fault flags → `EngineOnline`.
- Offline (incl. paused) feeds `LaneEvalService` spare/degraded logic unchanged.
- Raw PLC integer encodings live at the ingestion/adapter boundary (interface-mapped), keeping
  `PandA.Core` free of magic protocol numbers.

## Open (bookmarked)
- **OQ-A2:** concrete `PLCStatus` integer taxonomy.
- **OQ-A1:** meaning of event codes 217/218.
- (F12 `~HS` ZPL status-request suffix — prerequisite for real Zebra engine status — remains
  its own backlog item.)

## References
- `.project/spec/clusters/routing-status-control.md` §F13; arch-log 012 (lane-eval/spare).
- Source: `sdisp_BP2PA_Event` (line 71 exclusion), `PrintEngineStatus`, msg 283 handling.
