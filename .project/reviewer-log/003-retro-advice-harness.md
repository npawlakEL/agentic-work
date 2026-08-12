# 003 — Retro-review: carton advice (MP1) + message sim harness

**Slices:** `CartonAdviceService` / `TransportOrder` advice path + `PandA.Harness` / `SimHost` wire framing (281/286).
**Found by:** Reviewer (code-review agent), retroactive Gate 2 review.
**Iteration:** 1 (retro) · **Date:** 2026-08-12
**Source ported:** advice/overwrite semantics (PandaData ADVISED), 281/286 telegram handling.

## Verdict: no Critical/High. **One Medium needs a domain decision (escalated).** 4 Low findings.

## Findings & resolutions

| # | Sev | Finding | Resolution | Verified |
|---|-----|---------|------------|----------|
| 1 | Medium | `CartonAdviceService.AdviseAsync` unconditionally calls `TransportOrder.OverwriteAdvice(...)` for ANY existing order, which resets `PrintCount→0` / `Status→Advised` / clears lifecycle timestamps and re-arms `CanPrint` on an already-Printed/Verified/HeldForIntervention carton — with no operator authorization. This can reopen the decision-003 reprint hole via the advice path. **However**, existing test `LifecyclePermutationTests.DuplicateAdvice_BeforePrint_StartsNewGeneration` deliberately asserts the opposite: re-advising a printed carton with **new data** SHOULD start a fresh printable generation (legitimate blind-label reuse). The two readings conflict → this is a domain-semantics call, not a clear bug. | **ESCALATED to user (open decision AD-1).** Prototype gate (`OverwriteAdvice` only while `Status==Advised && PrintCount==0`) was implemented, shown to break the reuse test, then **reverted** pending the user's decision on same-label reuse vs. re-arm protection. | no — pending |
| 2 | Low | `PandaLabelSet` / harness hardcode `LabelStatus:0`; the parsed per-label `LabelStatus` from the 286 frame is dropped (not carried onto the label). | Bookmark — folds into engine/label-status ingestion (backlog F13). No change now. | n/a (backlog) |
| 3 | Low | 281 frames with `DeviceId > 1` are not discriminated (the `IsInduct` distinction is effectively dead); only induct (DeviceId 1) is exercised. | Bookmark — verify-station device routing lands with the real telegram map / EController adapter. | n/a (backlog) |
| 4 | Low | Thin harness test coverage for malformed frames / partial buffers (framing is faithful but under-tested). | Bookmark — harden when the wire adapter is built for integration. | n/a (backlog) |
| 5 | Low | `LabelBufferOrder.Type()` skips empty slots — a documented intentional divergence from the source INNER JOIN behavior; noted here for traceability only. | No change (already documented as intentional in the buffer-order slice). | n/a |

## Open decision surfaced to user
- **AD-1 (advice re-arm vs. blind-label reuse):** When the host re-advises a blind label that is already printed/verified/held, should PandA (a) start a fresh printable generation (current behavior + existing test), (b) require the new advice to carry a *different* label set to start a new generation while a same-set re-advice is ignored, or (c) never re-arm without operator authorization (strict decision-003)? **Awaiting user.**

## Post-fix state
- No code change landed from this review (Finding 1 reverted pending decision; Findings 2–5 are backlog/bookmark).
- Tests: unchanged at 225 green.
