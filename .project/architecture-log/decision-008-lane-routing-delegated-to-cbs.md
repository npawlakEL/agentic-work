# Decision 008 — Lane routing delegated to econtroller CriteriaBasedSorting

**Status:** Accepted
**Date:** 2026-08-12
**Owner ruling:** explicit — "we append the verification result onto the transport order,
then it goes into the econtroller builder and hits
`econtroller.behaviors.criteriabasedsorting` which maps it to a place and directs the
routing from there."

## Context

The spec-routing pass modeled **F08 lane routing** as PandA logic: a `LaneDef` table,
round-robin via `LastDiverted`, a dedicated REJECT lane, and NULL→GETDATE tie-breaking
(open questions D9/D10). This assumed PandA selects the physical destination.

The real econtroller platform already owns destination selection through
`Element-Logic/eController.Behavior.CriteriaBasedSorting`:

- `SortCriteriaExtension` (`[ExtensionKey("SortCriteriaExtension")]`) holds a
  `List<SortCriteria>`, each `{ string Type; string Value; }`.
- `CriteriaConfig.json` maps `CriteriaType` + `CriteriaValue` → `PlaceID` (per-place rows,
  `CriteriaData`). The sorting behavior reads the extension and routes the TO to a place.

## Decision

1. **PandA does NOT own lane/place selection.** F08's `LaneDef` / round-robin /
   REJECT-lane / `LastDiverted` model is **OUT of scope** for the port.
2. **PandA's routing responsibility = annotate the transport order with its verify
   result** as decision data. In the adapter this becomes one or more `SortCriteria`
   entries appended to `SortCriteriaExtension`, e.g.
   `{ Type = "PandaVerify", Value = "Pass" | "Fail" | <RejectReasonCode> }`.
   The place→criteria mapping lives in CriteriaBasedSorting's `CriteriaConfig.json`,
   configured per site — NOT in PandA.
3. In **`PandA.Core`** (econtroller-free), this surfaces as a small, transport-agnostic
   result contract: the verify/exception outcome already produced by
   `VerifyStationService` (Pass / Fail + `VerifyReasonCode`) is exposed as a
   **routing-decision value** on the result object. `PandA.Sim` records it; the future
   `PandA.EController` adapter maps it onto `SortCriteriaExtension`.

## Consequences

- **F08 (lane routing) is largely retired**: no LaneDef/round-robin/REJECT-lane build.
  What remains is ensuring our verify/exception outcome is expressed as a clean
  criterion value ready for the adapter to project onto `SortCriteria`.
- D9 and D10 in open-questions are **resolved as "not our concern"**.
- **F13 (printer status ingestion)** and **LINECTRL (shut/slow-line egress, BluePaw)**
  remain IN scope — they drive printer selection, spare promotion, and line-state, which
  are PandA's, not the sorter's.
- The verify reason-code taxonomy (F22) becomes doubly important: those codes are the
  `Value`s a site will map to reject/rework places in CriteriaConfig.

## References
- `Element-Logic/eController.Behavior.CriteriaBasedSorting`:
  `Config/SortCriteria.cs` (`SortCriteria`, `SortCriteriaExtension`),
  `Config/CriteriaConfig.cs`, `Steps/CriteriaSteps.cs`, `Actions/CriteriaActions.cs`.
- decision-006 (standalone is authority — behaviors repos are integration reference only).
- decision-004 (verify semantics), F22 reject-code taxonomy.

## Amendment — owner ruling 2026-10-21

The verify routing criterion now carries `Pass|Fail`, the source reject reason code and the destination (`TransportOrder.VerifyPassDest` / `VerifyFailDest`, defaults `Verify - PASS` / `Verify - FAIL`), matching source procs `sdisp_TOOL_PA_VerifyLabel` L470 and `sdisp_TOOL_PA_GetFinalLaneFromStatus` L83-90. An unknown carton at verify routes `Fail` with code 0.

## Amendment — owner ruling 2026-10-21, R9 (no profile → no print)

A label whose line has **no active fire-point profile** is treated like a missing fire point: it is **not
printed** (`LabelSelectionStatus.NoFirePoint`), a Warning is logged with carton, printer and line, and the
remaining labels are evaluated the same way (so a line with no profile prints nothing). Previously such labels
printed with a null fire point. Tests: `OwnerRulings20261021Tests.R9_*`,
`InductServiceTests.Induct_NoActiveProfile_PrintsNothing`.
