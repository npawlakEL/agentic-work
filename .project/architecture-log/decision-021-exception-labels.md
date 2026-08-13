# Decision 021 — Exception Labels (F10 wiring design)

Status: **Accepted** (domain-owner grilled 2026-08-13). Supersedes the open-discussion
bullet in plan.md. Unblocks F10 induct-path wiring.

## Context
When a carton inducts but cannot get its real label (bad scan, missing data, duplicate,
etc.), PandA optionally prints a diagnostic **exception label** — a filled ZPL template
naming the carton and the fault — so a human at the reject/repair station knows what went
wrong. Source: `sdisp_TOOL_PA_BuildExceptionLabel` + `LookupCarton` lines 468–536.

The pure `ExceptionLabelBuilder` + `LabelTemplate` + `ILabelTemplateRepository` + policy
gate were already built (backend Wave-2 batch 1, commit `00b9147`). This decision defines
how F10 wires into the induct path.

## Decisions

### 1. Synthetic identity (no MFC-TO match)
An exception carton **never resolves to a real MfcTransportOrder** — there is no barcode
match (that is *why* it is an exception). PandA **synthesizes a placeholder record** so the
label has something to print. The synthetic id is **human-readable AND unique**, seeded from
the exception reason + PLC sequence/timestamp, e.g. `NoRead-000481`. The template's
`<CartonID>` slot is filled from this id. Two NoRead cartons must not collide in
run-history / reject-screen.

### 2. Lifecycle — print, mark Printed
Build the exception label, send it to the matched printer, and **mark the (synthetic) carton
Printed**. This applies uniformly to NoRead, NoData, NoInfo, Duplicate, LabelConflict, and
PrintHold/NotReceived.

### 3. Verify + destination — verify, then reject
Exception labels **ARE sent through verify** (source `VerifyEnabled=1`; sentinel barcode
`'ExceptionLabel'`). The verify outcome is treated as a **Fail → reject carton**. The carton
carries the **F08 `RoutingCriterion`** so the PandA.EController adapter diverts it to the
reject/repair lane. i.e. "verify it, but say it's failing → a reject carton."

### 4. Trigger + status→template map
Gate (source `LookupCarton` line 468):
```
effectivePrintExceptionLabels  AND  !Bypass  AND  CartonStatus != PrintReady
```
`PrintReady` is the ONLY non-exception status. Status→template map (all `Active=1`,
`Customer='-'`; seed `6.0_PopulateTables/LabelTemplates.sql`):

| CartonStatus | Reason | Template `LabelType` (RecID) | Slots |
|---|---|---|---|
| PrintHold | Not Received | `Not Received` (7) | `<CartonID>` |
| Duplicate | Duplicate | `DataError_Duplicate` (17) | `<CartonID>`,`<LPN>` |
| NoInfo | No Information | `DataError_NoInfo` (4) | `<CartonID>`,`<LPN>` |
| NoRead | No Read | `ScanError_NoRead` (2) | `<CartonID>` |
| NoData | No Data | `ScanError_NoData` (3) | `<CartonID>` |
| LabelConflict | Label Conflict | `DataError_LabelConflict` (22) | `<CartonID>` |
| *(verify path)* | DataMismatch | `DataMismatch` (21) | none |

- Unknown/other status ⇒ **no label built, no throw** (source `'-'` → NULL).
- `ScanError_Conflict` (13) is a **dead template** — no CASE branch produces it; do not use.
- Non-exception templates in the same table (`QA`, `VAS`, `Unit Sorter Shortage`,
  `Test Label`) are NOT exception-triggered — ignore for F10.
- **Active filter:** the source omits `Active=1` on the template lookup; the C# port
  **adds** it (table has the column + a filtered index).
- **LPN substitution set:** source substitutes `<LPN>` for Duplicate, NoInfo, **and
  PrintHold**. The current `ExceptionLabelBuilder.UsesLpn` covers Duplicate + NoInformation
  only; the `Not Received` template has no `<LPN>` slot so it is harmless today, but if that
  template ever gains an `<LPN>` slot, add `NotReceived` to `UsesLpn`. (Minor fidelity note.)

### 5. PrintExceptionLabels — per-line, global overrides when defined
`PrintExceptionLabels` is a **per-line** flag with a **global** override:
```
effective = global.HasValue ? global.Value : lineValue
```
A **defined global wins for every line**; only when the global is unset do per-line values
apply. (Global-override-with-per-line-fallback.) Source seed default = `0` (off).

### 6. DCMSExceptions (global) — bookmarked placeholder, eHub-sourced
`DCMSExceptions` is **global**. It controls WHO produces the exception ZPL:
- `DCMSExceptions = 0` → **local template source** (built — `ExceptionLabelBuilder` +
  `ILabelTemplateRepository`).
- `DCMSExceptions = 1` → the ZPL is supplied by an **external eHub connector** (the host/DCMS
  system). This is **bookmarked with a placeholder**: introduce an `IExceptionLabelSource`
  port in Core with a `LocalTemplateSource` implementation now; the concrete DCMS/eHub source
  is **deferred to the PandA.EController / eHub adapter**. Core stays backend-agnostic
  (decision-002/006/020).

### 7. Orientation + sentinel
- Orientation: `Side` if any side printer exists on the line, else `Top` (replicate source
  dynamic check).
- `LabelBarcode = 'ExceptionLabel'` (fixed sentinel — not a real barcode).

## Consequences / follow-ups
- F10 induct wiring lands with the F20 read-quality classification (F20 produces the
  `CartonStatus`/`ExceptionType`; F10 is downstream). Both edit `InductService.InductAsync`
  → serialize with the rest of the wiring wave.
- New Core surface needed for wiring: `IExceptionLabelSource` port (+ `LocalTemplateSource`),
  effective-flag resolution (global-override-with-line-fallback), synthetic-id minting
  (`{Reason}-{seq}`), F08 routing on the exception carton, orientation resolution.
- Seed the 7 exception templates into `InMemoryLabelTemplateRepository` (Sim) for tests.
- DCMS/eHub exception source: **backlog** (adapter-owned, wave-coupled with eHub connector).
