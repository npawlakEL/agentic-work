# Decision 020 — Induct message boundary: 281 → ADS plugin → MfcTransportOrder+extension → Core `InductScan`

**Status:** Accepted
**Date:** 2026-08-13
**Owner ruling:** explicit — "the induct msg will come in as a 281, hit the ADS plugin, and then that will
turn it into an MfcTransportOrder object with extension data."

## Context

Wave-0 needs a way for the physical carton measurements taken at the induct scanner (dimensions, front gap,
PLC sorter/device/seq index bundle) to reach the domain. Prior to this the induct seam was the minimal
`IInductService.InductAsync(lineId, blindLabel)` — identity only, no measurements — which is what blocked the
Wave-2 deferred integrations (F-LOG1 run history, F20 read-quality classification, F18 barcode resolution,
DYNAP apply-point).

The open question was **where the raw PLC induct message is decoded**. Building a raw PLC-281 parser inside
`PandA.Core` would have pulled transport/protocol concerns into the backend-agnostic domain, violating the
ports-and-adapters split (decision-002/006).

## Decision

The raw induct message is **never seen by `PandA.Core`**. The real pipeline is:

```
PLC 281 (raw induct message on the sorter line)
   → ADS plugin (econtroller)         decodes the 281
   → MfcTransportOrder + extension data  (per-carton payload rides as TU extension data)
   → PandA.EController adapter         maps MfcTransportOrder + extension → InductScan
   → PandA.Core InductService.InductAsync(InductScan)
```

- `PandA.Core` owns a transport-agnostic **`InductScan`** record (namespace `PandA.Core.Induct`): the decoded
  induct event carrying `LineId`, `BlindLabel`, `FrontGap`, dimensions (`Length`/`Width`/`Height`/`Weight`),
  the PLC index bundle (`SorterNumber`/`SorterMode`/`DeviceId`/`SeqNum`), and raw `ScannedLabels`.
- `IInductService.InductAsync(InductScan)` is the primary entry point. `InductAsync(lineId, blindLabel)` is a
  thin convenience that builds an identity-only `InductScan.ForBlindLabel(...)` (measurements defaulted;
  `FrontGap = int.MaxValue` so gap classification always passes) — used by the print-path tests and any
  caller that has not plumbed the measurement bundle.
- At induct the carton is stamped with its measurements via `TransportOrder.StampInductScan(...)`, persisted
  as `TransportOrder.InductMeasurements` (`InductScanMeasurements`). This is **pure capture with no gating
  behavior** in Wave 0; F-LOG1/F20/DYNAP read it once they are wired onto the induct path in a later wave.
- The `281 → MfcTransportOrder + extension → InductScan` mapping lives entirely in the **`PandA.EController`
  adapter / ADS plugin**, alongside the existing TU-extension mapping (vision.md). The Sim produces
  `InductScan` directly, with no PLC/ADS layer.

## Consequences

- `PandA.Core` stays backend-agnostic: no PLC framing, no `MfcTransportOrder`, no ADS types in the domain.
- The Wave-2 deferred integrations are now **unblocked** — they have a measurement-carrying induct event to
  read from. Their wiring (and acceptance tests) can proceed in the next wave.
- The adapter carries one more mapping responsibility (`MfcTransportOrder` extension keys → `InductScan`
  fields); the exact extension-key names are an adapter-phase detail, deferred with the rest of
  `PandA.EController`.
- Backward compatible: every existing `InductAsync(lineId, blindLabel)` caller/test is unchanged (the
  convenience overload delegates to a bare `InductScan`).

## Follow-ups

- Wire F-LOG1 (`CartonRunRecord` from `InductScan` + `StatusAtInduct`), F20 (classify using `FrontGap` +
  `IMinGapProvider`), F18 (barcode-based resolution from `BlindLabel`/`ScannedLabels`), and DYNAP
  (apply-point from `Length`/`Height`) onto the new induct path.
- Define the concrete `MfcTransportOrder` extension-key ↔ `InductScan` field map in the `PandA.EController`
  adapter when that module is built.
