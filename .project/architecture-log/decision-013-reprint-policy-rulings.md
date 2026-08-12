# Decision 013 — Reprint-adjacent policy rulings (D2, D3, D4)

**Status:** Accepted
**Date:** 2026-08-12
**Owner ruling:** explicit (three selections).

Settles three policy questions around the reprint lifecycle (decision-003) and settings.

## D2 — "Reprint Labels" default = ON (1)

Source had a mismatch (seed `0` vs proc-default `1`). **Ruling: default ON (1)** — reprints are
allowed by default; a site disables them explicitly. Consequences:
- Matches the stored-proc default.
- With decision-009, F15 pre-verify recovery is gated by `ReprintLabels`; default ON means
  recovery **auto-re-arms by default**.
- The other D2 mismatches (`PurgeSetting_UsedData`, `PurgeSetting_InactiveData`) are **moot** —
  purge is descoped (decision-012).
- **D2 resolved.**

## D3 — Manual "lock out of reprint" action is SUPERSEDED

Source `sdisp_GUI_SetPrintedFlag @PrintFlag=1` let an operator manually mark a carton printed to
lock it out. **Ruling: drop it.** The lifecycle already locks a completed/verified carton by
default; `TransportOrder.AuthorizeReprint(reason)` is the *only* re-open path. No manual
lock-out method in Core, no UI action. **D3 resolved.**

## D4 — Re-advice of a non-reprintable carton: OVERWRITE ADVICE, STAY LOCKED

When new label advice (MP1) arrives for a carton that already completed and is **not**
reprintable: **update the stored label data** (the source's `OverwriteLabelData` behavior) **but
do NOT re-arm** the carton — it remains non-printable until an operator calls `AuthorizeReprint`.
Consequences:
- The advice ingestion path updates `LabelData` for the TU even when the TO is in a
  completed/locked state; it does not transition the TO back to `Advised`.
- Log the overwrite-while-locked at Information/Warning (decision-005).
- This is the concrete behavior for backlog **F-ADV1** (was "reprint-rules-gated re-advice",
  decision-004 AD-1). **D4 resolved.**

## Port shape
- Advice service: `Overwrite` updates `LabelData`; state transition to `Advised` only occurs if
  the TO is in a re-armable state OR `AuthorizeReprint` has been granted.
- No `LockPrint()` / manual printed-flag method.
- `ReprintLabels` setting resolves to `true` when unset (`ISettingsProvider` default).

## References
- decision-003 (reprint rules + monotonic count), decision-009 (F15 recovery gating),
  decision-004 (AD-1 re-advice), decision-012 (purge descoped → purge settings moot).
- Source: `sdisp_GUI_SetPrintedFlag`, `OverwriteLabelData`, `sdisp_TOOL_GetSetting` seed vs default.
