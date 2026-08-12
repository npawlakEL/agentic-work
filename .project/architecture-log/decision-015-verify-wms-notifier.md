# Decision 015 — F14/F3 verify→WMS notification: PandA owns a port, transport adapter-deferred

**Status:** Accepted (Core shape); transport wiring deferred (needs site info)
**Date:** 2026-08-12
**Basis:** Senior research pass over econtroller/exol outbound messaging (2026-08-12).

## Context

The SQL source's `sdisp_WMS_PandAVerify_Insert` notifies an external WMS ("M&A"/DCMS) of a
carton's verify result. We tentatively assumed (F3) this would ride out through econtroller's
existing host/WMS messaging once PandA annotates the transport order — the same way lane
routing rides CriteriaBasedSorting (decision-008). **The research disproves that assumption.**

## Research findings (cited)

- econtroller has two outbound channels: **ATI TCP** (to PLC/sorter) and **DTC/eHub** (to
  host/WMS). Only DTC/eHub reaches a WMS.
- The DTC outbound event catalog (`eHub.DtcHttpBridge/.../DefaultOutgoingEndpoints.cs`) has
  **no verify/inspection event** — only handling-unit move/update/fault/delete and
  transport-order accepted/rejected/transmitted/deleted/cancelled.
- `DefaultDtcActions.HandlingUnitMoved` emits `HandlingUnitMovedInMfcV1` with
  **hardcoded `TransportStatus=0`, `Reason=0`, `FaultReasons=[]`** — it reads only `trans.Mp`,
  never a behavior extension. Annotating the TO does NOT produce a WMS verify message.
- **Behaviors are pure TO annotators.** `WeightActions.ProcessWeight` (the closest analogue to
  PandA verify) writes `SortCriteria{Type="Weight",Value="Fail"}` for routing and sends **no**
  WMS notification. Platform norm: behaviors annotate for routing; they do not own WMS output.
- Therefore F3 option "(a) econtroller handles it automatically" is **false**. Verify→WMS is
  not free.

## Decision

1. **PandA owns the verify-result notification** as a first-class concern, not something that
   falls out of routing. F14 is **in scope** (correcting the earlier tentative "out of scope").
2. **Core-side shape (buildable now, no econtroller dependency):** define an
   **`IWmsVerifyNotifier`** port in `PandA.Core`:
   `Task NotifyVerifyAsync(VerifyNotification n)` where `VerifyNotification` carries the fields
   the source inserted — carton/blind-label id, pass/fail, reject reason code (F22 taxonomy),
   verify timestamp, printer/line ids. `VerifyStationService` calls it after a verify outcome.
   `PandA.Sim` ships a capturing/no-op `FakeWmsVerifyNotifier` for tests. Default (unconfigured)
   = no-op.
3. **Transport wiring is DEFERRED to `PandA.EController`** and depends on one site question
   (below). Two candidate adapter implementations:
   - **DTC/eHub path** (if DCMS consumes DTC events): add a new DTC event
     `PandAVerifyResultV1` + a `PandAVerifyDtcAction : IMfcAction` that reads the verify
     extension off the TO and calls `_dtcOutbox.StoreAsync(...)`; add an eHub outbound endpoint.
     Mirrors the existing `DefaultDtcActions` pattern exactly.
   - **Direct path** (if DCMS expects a direct DB/TCP/REST connection): the adapter implements
     `IWmsVerifyNotifier` against that transport.

## Open question to resolve before adapter work (NOT blocking Core)

> Does the M&A/DCMS WMS receive line-controller data via the DTC/eHub pipeline (HTTP/NATS
> DTC events), or via a direct connection (SQL write / TCP / REST)? And what payload does it
> expect (does it match `sdisp_WMS_PandAVerify_Insert`: barcode, pass/fail, reject code,
> timestamp)?

Until answered, build the Core port + Sim fake; leave the EController transport unimplemented.

## References
- Research report (session): econtroller `eController.Mfc.Library/Common/Actions/DefaultDtcActions.cs`,
  `.../Steps/MfcHookDefaultSteps.cs`, `eHub.DtcHttpBridge/.../DefaultOutgoingEndpoints.cs`,
  `eController.Behavior.Weight/.../WeightActions.cs`, `eController.Behavior.CriteriaBasedSorting/.../CriteriaActions.cs`.
- decision-008 (lane routing delegated — the analogy that does NOT extend to WMS), decision-005 (ILogger),
  F22 reject reason-code taxonomy.
