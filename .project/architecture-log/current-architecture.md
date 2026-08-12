# Current Architecture — PandA (Print & Apply) port

**As of:** 2026-08-12 · version 0.5.0 · **231 tests green**
Living snapshot maintained by the Learner. For the *why* behind each piece see the numbered architecture-log
entries and decision-00x records; this file is the *what it is now*.

## Shape
Ports-and-adapters. Everything below compiles and is tested today with **zero econtroller dependency**; the
`PandA.EController` adapter is deferred to integration.

```
PandA.Core   — domain + services (no I/O, no econtroller)
PandA.Sim    — in-memory adapters, wire framing (281/286), SimHost orchestration
PandA.Harness — interactive console over SimHost
tests/PandA.Tests — xUnit (231)
(deferred) PandA.EController — thin adapter onto MfcTransportOrder/DynamicField + real telegrams
```
Solution: `PandA.slnx`. Target net9.0, Nullable + ImplicitUsings, `TreatWarningsAsErrors=true`.

## Core domain model
- **`TransportOrder`** — the carton, keyed by blind label (`TuId`), carrying a typed `PandaLabelSet`.
  Reprint lifecycle (decision-003): monotonic `PrintCount`, `Status` (Advised/Printed/Verified/
  HeldForIntervention/ReprintAuthorized), per-label `LabelPrintState`, `CanPrint` gate, operator-only
  `AuthorizeReprint`. No auto re-arm on verify fail.
- **`PandaLabelSet` / `Label`** — the advised label set; label type drives printer routing and verify.
- **`LineConfig`** — per-line printers, `BufferOrder`, `ActiveProfile` (fire points), `PrinterPolicies`,
  load-balance toggle.
- **`PrinterConfig` / `PrinterState`** — static provisioning vs. live health. `PrinterState` is dynamic:
  `PlcOnline`/`EngineOnline`→`IsOnline`, `IsSpare`, `IsAvailable`, `LastPrinted`, `LastStatusUpdate`,
  `VerifyFailCount`.
- **Fire points** — `ApplyPoint` (inch+edge `1T/1L/0M`), `FirePoint` (print/apply tracking devices +
  points), `FirePointProfile` (per printer+label), `FirePointResolver`.
- **`PrinterGroupPolicy` / `ZoneState`** — per apply-orientation min-count/spare/degraded policy; zone state.

## Services (the pipeline)
1. **`CartonAdviceService`** (MP1) — creates/overwrites the transport-order shell from host advice.
   *(re-advice re-arm is reprint-rules-gated per decision-004/AD-1 → backlog F-ADV1; currently always-reset.)*
2. **`InductService`** (MP2) — on induct scan: selects a printer per label, resolves fire points from
   `ActiveProfile` onto each `PrintJob`, dispatches ZPL, marks per-label printed, and completes a full run
   (increments `PrintCount`). Enforces the reprint gate (`NoReprint` when not authorized).
3. **`PrinterSelectionService`** — eligibility (online + not spare + orientation + emits type) → least-
   recently-printed ranking → collision/backup fallback → ConfigOrder tie-break. Missing `PrinterState` =
   fail-open available (provider must populate state).
4. **`VerificationService`** (part of MP286) — compares scanned vs expected, classifies failures
   (no-read/no-data/conflict/mismatch/missing/extra). Ordered required slots (VF-2); unexpected/duplicate
   scan ⇒ FAIL (VF-1); bypass proceeds unless it read no-read/no-data (VF-3). Xref alternates via `LabelXref`.
5. **`VerifyStationService`** — orchestrates verify: pass/clean-bypass → Verified + clear streak; fail →
   HeldForIntervention + count toward pause.
6. **`VerifyThresholdTracker`** — per-line consecutive-fail streak; pauses at threshold. `resetOnTrip`
   configurable (VF-4).
7. **`LaneEvalService`** — per line/orientation: promote/demote spare, emit `LaneEvalResult`
   (Balanced/SlowLine/ShutLine/ShutZone). Mutates `PrinterState` and returns the control decision.

## Sim + harness
- **Framing** — real 281 (induct/verify trigger) / 286 (label data) wire frames parsed in `PandA.Sim`.
- **`SimHost`** — seeds a line (printers, policies, zone), runs advise→induct→print→verify, exposes printer/
  zone status mutation and lane-eval. BluePaw stop/slow-line egress is stubbed (codes TBD).
- **`PandA.Harness`** — interactive console (seed/list/view, `r`/`v` by index, `p`/`z`/`s` for lane control).

## Deliberate divergences from source (intentional, adjudicated)
- **decision-003** — reprint: monotonic counter + no auto re-arm (source erased `Printed` on verify fail).
- **decision-004** — verify: VF-1 unexpected/duplicate scan fails (source ignores), VF-2 ordered slots,
  VF-3 bypass-glitch fails, VF-4 configurable post-trip reset.
- **arch-log 012 §4** — lane-eval: degraded generalization, demote non-spare, offline-clear on either signal.

## Not yet built (see backlog/README.md)
EController adapter + real telegram codes; operator GUI (Blazor); reprint-rules setting + gated re-advice
(F-ADV1); carton run-history event logging (F-LOG1); engine-status ingestion (F13); fire-point profile
switching/host-driven ProfileName; dynamic height→orientation; waves/purge; concurrency hardening on the
print gate.
