# Decision 004 — Verify-core & re-advice semantics (retro-review resolutions)

**Date:** 2026-08-12
**Status:** Accepted. Resolves the retro-review findings in reviewer-log 003 (advice) and 004 (verify).
**Deciders:** Domain owner + Senior Coder. Companion: architecture-log 006 (verify), decision-003 (reprint).

These five items came out of the Gate-2 retro reviews. Each was surfaced to the domain owner because it
changes carton PASS/FAIL or reprint semantics; the source SQL is not treated as automatically authoritative
(cf. decision-003, where we deliberately corrected a source hole).

## VF-1 — Unexpected / duplicate scanned label ⇒ **FAIL** (deliberate divergence from source)
- **Source behavior:** `sdisp_TOOL_PA_VerifyLabel.sql:271-278` — a scanned label whose type has no expected
  barcode (`@CurrentVerifyLabel = '-'`) is **ignored** (cursor continues); the carton can still PASS. The
  later "extra label ⇒ code 14" block (lines 343-350) is **dead/unreachable**.
- **Decision:** The port **keeps the stricter behavior**: any scanned label outside the carton's required
  barcode array (the blind label + its associated barcodes) — including an **extra/unexpected read** or a
  **duplicate read of an already-matched valid label** — is a **verify FAIL**. Rationale (domain owner): a
  label type with no barcode to verify against cannot be verified; anything outside the required set is not a
  valid pass. This mirrors the decision-003 stance of correcting an over-lenient source.
- **Code:** No change — current behavior is correct-as-intended. `VerificationService` returns
  `VerifyOutcome.NoRead` for the unmatched-scan case (`Extra` reason).

## VF-2 — Same-type expected barcodes are **ordered slots**, not order-independent alternates
- **Source behavior:** `VerifyLabel.sql:354-366` — multiple `@VerifyLabels` rows for one `LabelName` are
  treated as an xref set (scan any acceptable value, order-independent).
- **Decision:** **Slot numbering matters.** If the carton legitimately expects the same LPN on two labels,
  the data expects that LPN scanned **twice**, corresponding to the slot order. Keep the port's ordered
  required-slot model. The genuine *alternate-acceptable-value* xref case (one physical label, values A **or**
  B) is handled separately via the `LabelXref` param, which the port already applies.
- **Code:** No change.

## VF-3 — Bypass does **not** count toward the fail streak, **unless** it is a no-read/no-data
- **Source behavior:** threshold update is called unconditionally; `VerifyPass=3` (bypass) is `<> 1` ⇒
  increments the streak.
- **Decision:** A **clean bypass** proceeds and clears the streak (does not count). **But** if the scan came
  back a no-read (`?`) or no-data (`!`/`~`/`0`) — e.g. the scanner glitched during a bypass — that is **not a
  valid bypass**: it FAILS, holds the carton for intervention, and **counts** toward the pause threshold.
- **Code (implemented):** `VerificationService` bypass branch now classifies each read; a no-read/no-data
  returns `NoRead`/`NoData` (fail) instead of `Ignore`. `VerifyStationService` unchanged (a fail outcome
  already holds + counts). Tests: `Disabled_WithBypass_NoReadStillFails`,
  `Disabled_WithBypass_NoDataStillFails`, `Station_Bypass_NoRead_HoldsAndCountsTowardThreshold`.

## VF-4 — Post-trip counter reset is **configurable**
- **Source behavior:** `_Refresh` sets the count to 0 when the threshold trips.
- **Decision:** Both policies are plausible, so make it **configurable**. `resetOnTrip: true` refreshes to 0
  on trip (source — next pause needs a fresh full streak after an operator un-pauses); `resetOnTrip: false`
  (default, current behavior) keeps the counter tripped so every later fail re-signals the pause until an
  explicit reset.
- **Code (implemented):** `VerifyThresholdTracker(bool resetOnTrip = false)`. Test:
  `ResetOnTrip_RefreshesCounterToZeroWhenPausing`.

## AD-1 — Re-advice re-arm is governed by whether the carton is **reprintable**
- **Finding:** `CartonAdviceService.AdviseAsync` currently calls `OverwriteAdvice` for ANY existing order,
  wiping lifecycle and re-arming `CanPrint` with no operator authorization — a back-door around decision-003.
- **Decision:** Re-advice of an already-run carton should reset it to printable **only when it is
  reprintable** (per the reprint-rules policy). If it is **not reprintable**, the operator must intervene
  (re-advice must not silently re-arm it). This depends on the **reprint-rules / "Reprint Labels" setting**
  which is not yet ported (backlog), so:
  - **Now:** current always-reset behavior is retained (documented as assuming reprint-allowed) and the
    reprint-rules-gated re-advice is **backlogged** (see backlog F-ADV1). No code change this cycle.
  - **Bookmark:** **event logging** of a carton barcode's full run history (how many times it ran, each
    outcome) — the audit trail the domain owner relies on to see re-runs. Backlogged (F-LOG1).

## Net code impact this cycle
- Implemented: VF-3 (bypass glitch fails+counts), VF-4 (configurable post-trip reset).
- No change: VF-1, VF-2 (current behavior confirmed correct-as-intended), AD-1 (backlogged pending
  reprint-rules setting).

## Amendment — owner ruling 2026-10-21 (match source verify)

VF-1, VF-2 and VF-3 are superseded; C# now matches source proc `sdisp_TOOL_PA_VerifyLabel` behavior:
- An unexpected scanned label whose type has no expected slot is ignored (pass, recorded as an `Extra` detail); duplicate same-type expected barcodes match by name slot in any order.
- The blind label is **required** when `VerifyContentLabel` = 1 (seed): expected set = carton TuId + every xref barcode recorded for the carton (`IXRefStore.FindByTuIdAsync`); a leftover blind label fails with reason 14.
- Every non-pass maps to a source `VerifyReasonCode` (no-read family 20/22/27/14, incorrect 21/23/29, conflict 15/28, scan error 12, PandA logic/no data 0, bypass 3) and writes reject history (`sdisp_PA_Scan_Verify` L117-132).
- The fail streak is counted **per printer that printed the carton** (`sdisp_PA_VerifyThreshold_Update` L129-186); on trip the printers are returned for pause and their counters reset (`_Refresh`), a pass resets them.
AD-1 is refined by decision-013 amendment (re-arm governed by `Reprint Labels`).

## Amendment — owner rulings 2026-10-21 (R4 bypass, R12 xref alternates)

- **R4 — a bypass carton passes verify when its carton id is read** (supersedes VF-3 and the "bypass 3" code
  in the amendment above). A bypass carton prints no labels at induct. At verify, if any scan matches the
  carton id (the TuId, e.g. tote `C123`, or one of its `BlindLabel` xref aliases) the carton **passes**: Pass
  destination, no reject record, the per-printer fail streak is not incremented. Other label reads are not
  compared. If the carton id was not read (no read, no data, wrong value, missing) the bypass carton fails
  normally with the matching reason code (e.g. 14) and writes reject history. This applies whether or not
  verify is enabled for the carton. `VerifyOutcome.Ignore` / reason code 3 are no longer produced (enum kept).
  Code: `VerificationService.VerifyBypass`. Tests: `OwnerRulings20261021Tests.R4_*`,
  `VerificationServiceTests.Disabled_WithBypass_*`, `VerificationMatrixTests.*WithBypass*`,
  `LifecyclePermutationTests.Station_Bypass_*`.
- **Owner ruling (kept behavior) — non-blind xref alternates substitute for label slots of their type.** An
  xref alternate value recorded for a non-blind label type may satisfy the expected slot of that type at
  verify. The owner reviewed this and chose to **keep** it; no code change.
