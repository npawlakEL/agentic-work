# Decision 011 — Fire-point Map/Profile model + print-vs-apply point distinction

**Status:** Accepted
**Date:** 2026-08-12
**Owner ruling:** explicit (two-level Map/Profile model; print point ≠ apply point notation).

## Context

Fire points were previously modeled as `FirePointProfile` (per printer+label) plus a static
per-line `LineConfig.ActiveProfile` (arch-log 010). Grilling clarified the real operator
model has **two levels**, and that print/apply points use **different notations**.

## Decision — two-level Map / Profile model

1. **Profile** = the fire points for one **(printer × label-type)** pair — i.e. one
   `PrinterFirePoints` row: `PrintTrackingDevice`, `PrintFirePoint`, `ApplyTrackingDevice`,
   `ApplyFirePoint`.
2. **Map** = a **named collection of profiles** covering all (printer × label-type) pairs on
   a line. A Map is what an operator/client/shift selects.
3. **Exactly one Map is active per line at a time.** Switching customer/shift = activating a
   different Map, which swaps *all* fire points on the line at once. `LineConfig` holds the
   line's **active Map** (replaces the old single `ActiveProfile` pointer).
4. **Static clients** = one Map containing one static Profile that never changes — the model
   collapses cleanly to the simple case.
5. **Map selection sources** (in priority order, later ones are backlog/UI):
   - the line's currently-active Map (default / static case),
   - operator switch via GUI (**UI backlog** — see below),
   - host-sent map/profile name in the inbound message (**backlog**, PROFSW host-driven).
   When the host sends **no** map/profile name → **keep the line's active Map** (do not force
   "no profile"). Resolves D6.
6. **`Active` flag semantics (D5):** on a Profile/Map record, `Active` = "available/selectable
   in the catalog" (many can be Active at once), **not** "currently in use". The in-use
   selection is the per-line active-Map pointer. Resolves D5.

## Decision — print point vs apply point notation (source-confirmed)

Interrogated `PrinterFirePoints` (`5.0_CreateTables` + `6.0_PopulateTables`):

| Column | Type | Example seed | Meaning |
|--------|------|--------------|---------|
| `PrintFirePoint` | **`int`** | `800` | Raw encoder/device count where the label prints. Static. **NOT** inch+edge notation. |
| `ApplyFirePoint` | **`varchar(32)`** | `1T`, `1L`, `.4M`, `-.4M`, `5.25L` | Inch + edge notation; supports decimals and a leading `-`. Where the applicator fires. |
| `PrintTrackingDevice` / `ApplyTrackingDevice` | `int` | `2` / `3` | Photo-eye the print / apply event is anchored to. |

**Port status:** `PandA.Core/FirePoint.cs` ALREADY models this correctly — `PrintFirePoint`
is `int` (raw), `ApplyFirePoint` is `ApplyPoint` (inch+edge). No change needed to that split.
The new work is the **Map** grouping + per-line active-Map selection + switching.

## Consequences / follow-ups
- New Core types: `FirePointMap` (named set of `FirePointProfile`), per-line active-Map on
  `LineConfig`, resolver picks the active Map then the (printer,label) profile within it.
- **UI backlog additions** (this is UI code, per owner):
  - Map switch screen (operator selects the active Map per line).
  - Map + Profile CRUD (create/edit maps and their per-(printer,label) fire points).
- **Backlog (non-UI):** host-driven Map/Profile name in inbound message (PROFSW).
- D5, D6 resolved.

## References
- Source: `PrinterFirePoints.sql` (table + seed), `sdisp_TOOL_PA_GetPrinterFirePoints.sql`,
  `sdisp_PA2BP_SendPrinterFirePoints.sql`, SiteBuilder FirePoint procs.
- arch-log 010 (fire-point profiles — superseded on the grouping/notation points by this).
- `.project/spec/clusters/labels-firepoint.md` §PROFSW/DYNAP.
