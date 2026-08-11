# 006 — Verify model (authoritative)

**Author:** Senior Coder
**Date:** 2026-08-11
**Status:** Authoritative. Verified against `sdisp_TOOL_PA_VerifyLabel`, `sdisp_PA_VerifyThreshold_Update`,
`sdisp_TOOL_PA_GetFinalLaneFromStatus`, `sdisp_PA_Scan_Verify` (PLC msg **286**).
**Supersedes** the "VerifyPass codes 0–12" shorthand in 004 §D — the real code space is a
**(label type × failure reason) matrix**, not a linear range.

> Companion: spec.md, decision-002, architecture-log 005 (selection). Phase 2 = verify.

## A. Inputs
- **Scanned string:** a delimited multi-barcode string from the verify scanner. Position → `LabelName`
  via `Settings_LabelBufferOrder` (buffer slot N ⇒ a named label like BlindLabel/Shipping/Content/Parcel).
- **Expected set (`@VerifyLabels`):** `PandaData.LabelBarcode1..6` + `LabelType1..6`, UNION xref barcodes
  tagged `BlindLabel`. Drop rows with null/empty barcode or `LabelName='Orientation'`.
- **Content toggle (`VerifyContentLabel`):** when 0, verify only `Shipping`/`Exception` (drop other types
  from both scanned and expected).
- **Enable/bypass:** `VerifyEnable=0` → if `Bypass=1` ⇒ `VerifyPass=3` (ignore/skip), else `0`.
  No PandaData record ⇒ `VerifyPass=0` (reject).

## B. Per-label comparison (cursor over scanned labels)
For each scanned `(value, LabelName)`, look up expected barcode for that `LabelName` (`@CurrentVerifyLabel`,
default `'-'`):
- **Match** ⇒ that label passes; remove it from the expected set.
- **Expected is `'-'` and value present but name has no expected** ⇒ ignore (no accompanying data).
- **Mismatch**, classified by sentinel characters in the *scanned* value:
  | Reason | Trigger | CartonStatus | VerifyPass by type |
  |---|---|---|---|
  | No Read | value contains `?` | No Read | Shipping=20, BlindLabel=14, Content=22, Parcel=27, else 0 |
  | Scanner error / No Data | value contains `!` or `~`, or `='0'` | No Data | **12** (all types) |
  | Label conflict | value contains `#` | Label Conflict | Shipping=33, BlindLabel=15, Content=34, Parcel=28, else 0 |
  | Extra label not in data | expected `'-'` but value read | No Read | **14** |
  | Genuine mismatch | none of the above | Verify - FAIL | Shipping=21, BlindLabel=14, Content=23, Parcel=29, else 0 |
  - **Xref rescue:** on a genuine mismatch, if the `LabelName` has multiple expected values (xref) and a
    scanned value matches one of them ⇒ **pass** instead of fail.
  - First failure **short-circuits** the cursor (`GOTO ENDCURSOR`).

## C. Post-loop outcome
- **Leftover expected labels** (not matched by any scan) ⇒ **missing label FAIL**, status `Verify - FAIL`,
  code by leftover type: BlindLabel=14, Shipping=20, Content=22, Parcel=27, else 0.
- **All matched, none leftover** ⇒ **PASS**: `VerifyPass=1`, status `Verify - PASS`; set PandaData
  `ActiveRecord=0`, `VerifyTime=now`.
- **`VerifyPass=1` is the only pass.** Any other value ⇒ set PandaData `Printed=0`, `ActiveRecord=1`
  (eligible to reprint/re-verify). Exceptions in TRY/CATCH ⇒ `VerifyPass=0`.
- **FinalDestination** = pass ? `VerifyPassDestination` : `VerifyFailDestination`.

### VerifyPass code legend (observed)
`1`=PASS · `3`=ignore/bypass · `0`=generic fail/error · `12`=scanner error/no-data ·
`14`=blind-label fail/no-read/extra · `15`=blind conflict · `20`=shipping no-read/missing ·
`21`=shipping mismatch · `22`=content no-read/missing · `23`=content mismatch · `27`=parcel no-read/missing ·
`28`=parcel conflict · `29`=parcel mismatch · `33`=shipping conflict · `34`=content conflict.
(Codes are host/GUI-facing telemetry — **preserve the exact numbers** in the port.)

## D. Verify-fail threshold (`sdisp_PA_VerifyThreshold_Update`, per line)
`FailFlag`/`VerifyPass<>1` ⇒ increment line `VerifyFailCount`; when `VerifyFailCount >= VerifyFailThreshold`
⇒ **force-pause the printer** (`~PP` command) + event, then refresh/reset. `VerifyPass=1` ⇒ **reset count to
0**. Tracks *consecutive* fails; a pass clears the streak.

## E. Lane routing (`sdisp_TOOL_PA_GetFinalLaneFromStatus`)
Given `CartonStatus` + `FinalDestination`, choose a `LaneDef` row (for the panda) where:
`LaneID = FinalDestination` **OR** `CartonStatus LIKE '%'+LaneID` (suffix) **OR**
`CartonStatus LIKE LaneID+'%'` (prefix) — i.e. **substring match** on status/destination. Among matches,
pick the **least-recently-diverted** lane (`MIN(LastDiverted)` → load-balances parallel lanes). If none
match ⇒ default to the **`REJECT`** lane.

## F. Entry point
`sdisp_PA_Scan_Induct` = PLC msg **281** (Phase 1). `sdisp_PA_Scan_Verify` / `sdisp_BP2PA_Scan_Verify` =
PLC msg **286** (Phase 2). Real telegram codes abstracted behind message points until integration.

## Port mapping (C#)
- `IVerificationService.Verify(expected PandaLabelSet + xref, scanned labels, options)` ⇒ `VerifyResult
  { int VerifyPass; CartonStatus; string? FinalDestination; per-label detail }`. Keep exact codes.
- `IVerifyThresholdTracker` (per line): `RegisterResult(lineId, pass)` ⇒ increments/reset; raises
  `PrinterPaused` when `>= threshold` (egress via a pause port, later).
- `ILaneRouter.Resolve(lanes, cartonStatus, finalDestination)` ⇒ lane number; least-diverted; REJECT default.
- Scanned-input parsing (buffer-order) modeled as a small mapper; Phase-2 tests may pass pre-typed scanned
  labels to keep focus on the code matrix. Threshold-pause egress + real lane divert deferred to connectors.
