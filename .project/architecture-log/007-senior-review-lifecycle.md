# 007 — Senior review (Phase 1 + 2) & end-to-end lifecycle

**Author:** Senior Coder
**Date:** 2026-08-11
**Scope reviewed:** all of `PandA.Core` + `PandA.Sim` + tests as of this commit (advice → induct/print →
verify → threshold). **Result: sound.** One lifecycle gap found and closed (verify-station orchestrator).

## What was reviewed
- MP1 advice (`CartonAdviceService`, `TransportOrder`), MP2 induct/print (`InductService`,
  `PrinterSelectionService`, gateway/line ports), verify-core (`VerificationService`), fail-threshold
  (`VerifyThresholdTracker`), and the Sim doubles.
- Cross-checked against architecture-log 005 (selection) and 006 (verify).

## Findings
1. **Ports-and-adapters boundary is clean.** Zero econtroller/framework references in Core or Sim; all I/O
   (store, printer, line, clock) behind interfaces. Matches decision-002. ✔
2. **Gap (closed): verify was not wired to the carton lifecycle.** `VerificationService` + the threshold
   tracker were pure/standalone — nothing looked up the TO at the verify station, fed the threshold
   counter, or advanced the carton. Added **`VerifyStationService`** (MP286 analogue of `InductService`):
   lookup active TO → verify → threshold register → **pass/bypass ⇒ `MarkVerified`**, **fail ⇒
   `MarkVerifyFailed` (HeldForIntervention)**. See **decision-003**: a failed carton is **not** auto-re-armed
   (that corrects a source hole where `VerifyLabel` set `Printed=0`, defeating the reprint gate).
   `TransportOrder` carries a monotonic **`PrintCount`**, per-label-type print state, and the states
   `Printed`/`Verified`/`HeldForIntervention`/`ReprintAuthorized`.
3. **Reprint is operator-gated, not automatic.** A held carton returns to printable only via
   `AuthorizeReprint` (per-carton web-screen override). `InductService` gates on `CanPrint`, counts only
   **full** runs, and increments `PrintCount` (1→2…); an unauthorized reprint yields `InductStatus.NoReprint`.
4. **Bypass semantics correct.** `Ignore` (verify disabled + bypass) proceeds and does **not** increment
   the fail streak; a hard-disabled non-bypass carton fails.
5. **Threshold is per-line and consecutive.** A pass resets the streak; the pause signal is surfaced as a
   flag (`PrinterPaused`) — egress deferred to a connector (see BluePaw backlog item).

## End-to-end coverage added (`LifecycleEndToEndTests`, all green)
- Happy path advise→induct→verify pass → carton `Verified`.
- Mismatch → **held for intervention** → plain re-induct **blocked** (`NoReprint`).
- Operator authorizes reprint → exactly one more full run (`PrintCount 1→2`) → re-verify pass; authorization
  consumed (next blind re-induct blocked again).
- Consecutive failures across cartons trip the printer-pause threshold.
- Bypass completes without failing the threshold.
- Verify on an unknown blind label → `NoActiveOrder`.
- Multi-type carton (Shipping/Content/Parcel) prints all then verifies all.

Plus `TransportOrderTests` (lifecycle/counter transitions) and `InductServiceTests` reprint-gate cases.

**Suite total: 64 tests green.**

## Still deferred (unchanged, tracked in backlog)
- `PandA.EController` adapter + real telegram/MP codes (integration).
- Lane routing from status → folds into Exol criteria-based sorting.
- BluePaw stop/slow-line tag (codes TBD) + printer-pause egress.
- Scanner buffer-order string parser (verify input parsing).
- Height→orientation transfer; duplicate-advice toggle; operator UI; waves; purge.
