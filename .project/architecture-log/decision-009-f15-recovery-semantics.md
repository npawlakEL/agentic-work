# Decision 009 — F15 PLC pre-verify carton recovery semantics

**Status:** Accepted
**Date:** 2026-08-12
**Owner ruling:** explicit (two-part).

## Context

A carton is inducted and printed, then conveyed toward the verify scanner while the PLC
tracks it via photo-eyes. If the PLC **loses tracking before the carton reaches the verify
point** (jam, fall-off, missed eye), it emits an event (`sdisp_BP2PA_Event`). Source
reaction: `UPDATE PandaData SET Printed=0, ActiveRecord=1` — "re-arms" the carton so the
next induct scan reprints it. Source gates only on position: `@DeviceID < @VerifyDevice`
(nothing past verify is recovered). Source also **zeros the print count** (via `Printed=0`)
and does **not** consult the `Reprint Labels` business rule.

This collides with decision-003 (monotonic `PrintCount`, no silent auto-re-arm after a
verify fail).

## Decision

1. **Re-arm keeps `PrintCount` MONOTONIC.** On a valid pre-verify recovery the carton is
   re-armed to print again, but `PrintCount` is **never zeroed** — it increments on the next
   print like any run. This preserves the audit trail of how many times a TO was run.
   (Supersedes the source's count-zeroing side effect.)
2. **Recovery IS gated by `ReprintLabels`.** If reprint is **disabled** (`ReprintLabels=0`),
   a lost carton is **NOT** auto-re-armed — it transitions to `HeldForIntervention` for an
   operator, exactly like a verify fail. Only when `ReprintLabels=1` does recovery
   auto-re-arm and return the TO to `Advised`.
3. **Position gate retained:** recovery applies only when the tracking event occurs before
   the verify device (`DeviceId < VerifyDeviceId`). Events at/after verify, or on a TO that
   is already `Verified` / `HeldForIntervention`, do **not** recover.
4. Recovery is **system-initiated**, distinct from operator `AuthorizeReprint`: it returns a
   re-armed TO to `Advised`, not `ReprintAuthorized`.

## Port shape

- `TransportOrder.ResetForTrackingEvent(...)` — new method: guards state (`Printed` only),
  checks `ReprintLabels`; if allowed → `Printed → Advised`, `PrintCount` untouched; if not →
  `Printed → HeldForIntervention`.
- `PlcEventHandlerService(ITransportOrderStore, ILogger<T>, IVerifyDeviceProvider,
  ISettingsProvider)` — ingests `PlcEvent(Code, CartonListId?, DeviceId, LineId)`, applies the
  position gate + reprint gate, logs via `ILogger<T>` (decision-005), and writes an F-LOG1
  run-history entry.
- Idempotent: a second recovery event for an already-re-armed (`Advised`) TO is a no-op with
  `PrintCount` unchanged.

## Open sub-questions still pending
- **OQ-F15-1:** meaning of event codes **217 / 218** (silently excluded at source line 71).
- **OQ-F15-4:** exact PLC message number carrying these events (280–299 range per arch-log 009).

## References
- `.project/spec/clusters/events-recovery-logging.md` §F15 (source detail, acceptance criteria).
- decision-003 (reprint rules + monotonic count), decision-005 (ILogger), F-LOG1 (run history).
