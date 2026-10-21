# Decision 003 — Reprint rules & the source auto-re-arm hole

**Date:** 2026-08-11
**Status:** Accepted. The port **intentionally diverges** from `sdisp_TOOL_PA_VerifyLabel` here.
**Deciders:** Domain owner + Senior Coder. Companion: decision-002, architecture-log 006 (verify), 007 (lifecycle).

## The hole in the source
1. `PandaData.Printed` is an **`int`**. The reprint policy setting **"Reprint Labels"** (RecID 17, default
   `0` = "cannot reprint after successful verify") is enforced in `sdisp_PA_LookupCarton`:
   `IF @ReprintLabels = 0 AND @PrintedFlag > 0 → CartonStatus = 'No Reprint'` (print cancelled).
2. **But** `sdisp_TOOL_PA_VerifyLabel` (lines 460–467) on any failure does
   `IF @VerifyPass <> 1 → UPDATE PandaData SET Printed = 0, ActiveRecord = 1`
   (comment: *"added to set printed = 0 if it verify fails"*, 2024-09-04).
3. **Consequence:** a verify failure **erases the `Printed>0` evidence** the reprint gate relies on, so the
   failed carton silently becomes eligible to reprint — defeating "cannot print more than once" and skipping
   the intended **manual intervention**. It also clobbers the counter so `Printed` can never legitimately
   reach 2. The 2024-09-04 patch looks like an accidental regression of the reprint policy.

## Intended (corrected) behavior — what we port
- **`Printed` is a monotonic run counter.** A **full** print run (all labels dispatched) sets it 1, 2, 3…
  A **partial** print (some label type had no eligible printer) does **not** count and does **not** mark the
  carton printed.
- **No auto re-arm on verify fail.** A failed carton is **held for manual intervention**; the system does
  not make it printable again on its own.
- **Reprint = operator-authorized, per carton.** The operator flips the carton's reprint permission on the
  web screen (source `sdisp_GUI_SetPrintedFlag` / a per-carton reprint override). That authorization allows
  **exactly one** further print run (counter → 2). Alternatively the operator handles the carton off-system
  (outside our control) and it is simply never reprinted.
- The per-line **consecutive verify-fail threshold** (006 §D) is unchanged; only a pass/bypass clears the
  streak; a manual reset does **not** clear it.

## C# model (see architecture-log 007 update)
- `TransportOrderStatus`: `Advised → Printed → Verified`, plus `HeldForIntervention` (verify failed) and
  `ReprintAuthorized` (operator override consumed on next full print).
- `TransportOrder.PrintCount` (int, monotonic); per-label-type print state
  `{ Printed, PrinterId, PrintedAt }` (separate from the immutable advised `Label` data).
- `MarkVerifyFailed()` replaces the automatic `ReArmForReprint()`. `AuthorizeReprint(reason)` is the only
  path back to printable and is **never** called automatically.
- Induct gate: `PrintCount == 0 && Advised` OR `ReprintAuthorized` ⇒ may print; else `NoReprint`.

## Backlog
- Operator web action to authorize a carton reprint (GUI, source `sdisp_GUI_SetPrintedFlag`).
- Reprint-only-the-missing-labels (enabled by per-label print state) — future optimization.

## Amendment — owner ruling 2026-10-21 ("Reprint Labels" governs re-arm)

The induct reprint gate now reads the `Reprint Labels` setting, matching source proc `sdisp_PA_LookupCarton` (L173-178, L308-347): **on** (also the default when unset, source fallback `'1'`) → an already-printed carton prints again without operator authorization (`TransportOrder.CanPrintWith(true)`, print states reset); **off** → the operator-authorization gate above still applies (`InductStatus.NoReprint`). The verify-fail auto re-arm hole remains unported.

## Amendment — owner ruling 2026-10-21, R7 (verify-fail re-arm is ported)

"No auto re-arm on verify fail" is **superseded**. After a verify failure the carton is re-armed so its next
induct prints it again — indefinitely, no attempt cap — **when `Reprint Labels` is on**. `TransportOrder.MarkVerifyFailed(failedAt, rearm: true)` returns the order to `Advised`, resets the
per-label print states, clears any reprint authorization and sets `VerifyFailRearmed` (which `CanPrint`
honors and the next completed print clears). `PrintCount` stays **monotonic** (history kept). The write runs
under the carton lease with version CAS (`SaveWithRetryAsync`). The station returns
`VerifyStationStatus.ReArmed` (still routes Fail, writes reject history, counts toward the streak). With
`Reprint Labels` off the carton is `HeldForIntervention` and needs `AuthorizeReprint` as before. Exception
cartons (no data) are never re-armed. Tests: `OwnerRulings20261021Tests.R7_*`.

**Follow-up ruling (2026-10-21): `Reprint Labels` is evaluated at induct time.** The setting is read on the verify
fail (to decide whether to re-arm) **and again on every induct**. With it off at induct, any carton that has
already printed (`PrintCount` > 0) is blocked (`InductStatus.NoReprint`) even if it was re-armed by a verify
fail or a tracking recovery, whenever that re-arm happened. Only an explicit operator `AuthorizeReprint`
lets it print. Turning the setting back on lets the re-armed carton print. Code:
`TransportOrder.CanPrintWith`. Tests: `OwnerRulings20261021Tests.R7_ReArmedCarton_SettingTurnedOffBeforeInduct_DoesNotPrint`
and `R7_ReArmedCarton_SettingOffAtInduct_OperatorAuthorizationStillPrints`.
