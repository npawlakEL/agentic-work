# 003 — Retro-review: carton advice (MP1) + message sim harness

**Slices:** `CartonAdviceService` / `TransportOrder` advice path + `PandA.Harness` / `SimHost` wire framing (281/286).
**Found by:** Reviewer (code-review agent), retroactive Gate 2 review.
**Iteration:** 1 (retro) · **Date:** 2026-08-12
**Source ported:** advice/overwrite semantics (PandaData ADVISED), 281/286 telegram handling.

## Verdict: no Critical/High. **One Medium needs a domain decision (escalated).** 4 Low findings.

## Findings & resolutions

| # | Sev | Finding | Resolution | Verified |
|---|-----|---------|------------|----------|
| 1 | Medium | `CartonAdviceService.AdviseAsync` unconditionally calls `TransportOrder.OverwriteAdvice(...)` for ANY existing order, which resets `PrintCount→0` / `Status→Advised` / clears lifecycle timestamps and re-arms `CanPrint` on an already-Printed/Verified/HeldForIntervention carton — with no operator authorization. This can reopen the decision-003 reprint hole via the advice path. **However**, existing test `LifecyclePermutationTests.DuplicateAdvice_BeforePrint_StartsNewGeneration` deliberately asserts the opposite: re-advising a printed carton with **new data** SHOULD start a fresh printable generation (legitimate blind-label reuse). The two readings conflict → this is a domain-semantics call, not a clear bug. | **RESOLVED (decision-004 / AD-1):** re-advice may re-arm **only when the carton is reprintable** (reprint-rules policy); if not reprintable the operator must intervene. Depends on the not-yet-ported reprint-rules setting → **backlogged as F-ADV1**; current always-reset behavior retained meanwhile (assumes reprint-allowed). Carton run-history logging bookmarked as **F-LOG1**. Prototype gate was implemented, shown to break the reuse test, then reverted. | resolved — backlogged |
| 2 | Low | `PandaLabelSet` / harness hardcode `LabelStatus:0`; the parsed per-label `LabelStatus` from the 286 frame is dropped (not carried onto the label). | Bookmark — folds into engine/label-status ingestion (backlog F13). No change now. | n/a (backlog) |
| 3 | Low | 281 frames with `DeviceId > 1` are not discriminated (the `IsInduct` distinction is effectively dead); only induct (DeviceId 1) is exercised. | Bookmark — verify-station device routing lands with the real telegram map / EController adapter. | n/a (backlog) |
| 4 | Low | Thin harness test coverage for malformed frames / partial buffers (framing is faithful but under-tested). | Bookmark — harden when the wire adapter is built for integration. | n/a (backlog) |
| 5 | Low | `LabelBufferOrder.Type()` skips empty slots — a documented intentional divergence from the source INNER JOIN behavior; noted here for traceability only. | No change (already documented as intentional in the buffer-order slice). | n/a |

## Open decision surfaced to user
- **AD-1 (advice re-arm vs. blind-label reuse):** **RESOLVED (decision-004):** re-advice re-arms only when
  the carton is reprintable (reprint-rules policy); otherwise the operator must intervene. Implementation
  depends on the reprint-rules setting → backlog F-ADV1. Run-history logging → backlog F-LOG1.

## Post-fix state
- No code change landed from this review (Finding 1 resolved by decision-004 → backlogged as F-ADV1;
  Findings 2–5 are backlog/bookmark).
- Tests: unchanged at 225 green.
