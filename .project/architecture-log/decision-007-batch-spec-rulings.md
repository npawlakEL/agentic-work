# Decision 007 — Batch rulings on low-risk spec open questions

**Status:** Accepted
**Date:** 2026-08-12
**Owner ruling:** domain owner approved the recommended defaults (lane items carved out → decision-008)

These clear low-risk items from `.project/spec/open-questions.md`. Each is a sensible
default; none changes PASS/FAIL safety semantics beyond what is noted.

| Q | Ruling |
|---|--------|
| **A5** (F16) | Source log level `805` (`sdisp_PA_Scan_Verify:149`) is a typo for `80` → **Information**. Port normalizes. (Also in decision-005.) |
| **C1** (LINECTRL) | Fix the `sdisp_TOOL_PA_ShutZoneDown` `SELECT PandaID=@PandaID` alias bug so the intended zone/line ID is actually populated before `ShutLine`. |
| **C6** (F-LOG1) | The `PrinterNumber` one-char trim (`LEFT(..,LEN-1)`) is treated as a **suspected source bug**: store the untrimmed ID. Only replicate the trim if a concrete source char is confirmed. |
| **D7** (F10) | Add an `Active=1` filter on exception-label template lookup (source lacks it). Safer; avoids selecting retired templates. |
| **D8** (F10) | Treat the `DataMismatch` / `ScanError_Conflict` templates as **reachable**: wire them to their corresponding verify reason codes rather than leaving them dead. |
| **F2** (INBOUND) | **Per-record** ACK/error handling — a single malformed advice record does not fail the whole batch; bad records are logged and skipped. |
| **F5** (F20) | Read-quality sentinels checked on **all 6 label slots**, not just Label1. |
| **E3** (LOCK) | Lock-acquire timeout is a **hard error (fail fast)**, not source's log-and-proceed. (Revisit if E2 topology says single-process makes contention impossible.) |

## Consequences
- These feed directly into the relevant feature slices' acceptance criteria.
- open-questions.md items A5, C1, C6, D7, D8, F2, F5, E3 are now **resolved**.
- D9/D10 (lane routing) were **not** accepted as written — superseded by decision-008.
