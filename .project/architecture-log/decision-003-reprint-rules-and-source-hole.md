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
