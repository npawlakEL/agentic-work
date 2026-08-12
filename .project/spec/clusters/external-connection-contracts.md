# External Connection Contracts — Endpoint/Port Surface

**Status:** Spec (contract only — infrastructure OUT OF SCOPE)
**Date:** 2026-08-12
**Owner decisions:** 008 (divert→CBS), 009 (recovery), 013 (reprint), 014 (status), 015 (WMS),
016 (dynamic apply), 017 (xref), 018 (eHub binding).

This document defines **what each external endpoint is and what happens when it is called** — the
neutral Core port surface. It deliberately does **not** specify how connections are built
(sockets, ADS, eHub connectors, polling loops). Per decision-018, `PandA.Core`/`PandA.Sim` own
these contracts and build first; the adapter (`PandA.eHub`/`PandA.MFC`) satisfies them later.

Two directions:
- **Inbound** = the adapter calls a Core entry point when a device sends us something.
- **Outbound** = Core calls an adapter-implemented port when it needs to tell a device something.

All inbound inputs are **already-parsed neutral values** (the adapter does the wire parsing).
All outbound payloads are **neutral command/data objects** (the adapter does the marshalling).

---

## INBOUND — Core entry points (adapter → Core)

### IN-1 · Induct  (legacy frame 281)
**Trigger:** a carton is inducted / tracked onto the line.
**Input:** `InductMessage { LineId, BlindLabel, CartonLengthPulses, CartonWidth, CartonHeight,
CartonWeight, FrontGap, SorterNumber, DeviceId, SeqNum, TrackingDeviceId }`.
**What happens when called:**
1. Resolve the carton's advice by `BlindLabel`, then by cross-reference (IN/OUT barcodes) if no
   direct hit — `IBarcodeXRef.Resolve` (decision-017). Returns carton or a no-read.
2. Classify read quality / gap (F20); a bad read → reject status, still tracked.
3. Enforce reprint lock-out: if the carton is non-reprintable and already printed, hold for
   intervention rather than re-arming (decision-013 / 009).
4. Allocate the slot index (F21) for this carton.
5. Resolve fire points for the active profile/Map (decision-011): the **print** point (static int)
   and the **apply** point via `ApplyPointResolver` using carton dims + per-printer config
   (decision-016).
6. Resolve the divert intent seed (default FAIL lane) for later routing (decision-008).
7. Set carton status; **emit OUT-1 (fire-point/assign bundle)** to the PLC for this carton.
**Errors:** lock timeout = hard error (decision-007 E3); no advice found = reject + logged event.

### IN-2 · Print  (legacy frame 282)
**Trigger:** the carton reached its print fire point; the PLC asks PandA to print.
**Input:** `PrintMessage { LineId, DeviceId, SeqNum | CartonRef }`.
**What happens when called:**
1. Look up the carton + its resolved label slots (up to 6, decision-007 F5).
2. Build the label payload for the assigned printer.
3. **Emit OUT-3 (send label)** to the assigned printer.
4. Increment `PrintCount` **monotonically** (never reset — decision-009); set Printed/status.
5. If the assigned printer is offline, apply spare promotion (decision-014) before sending.

### IN-3 · Verify scan  (legacy frame 286)
**Trigger:** the carton passed the verify scanner.
**Input:** `VerifyMessage { LineId, DeviceId, ScannedBarcode(s), CartonRef? }`.
**What happens when called:**
1. Identify the carton by scanned value — primary blind label, else `IBarcodeXRef.Resolve`
   (carton- or label-level, exact match — decision-017).
2. Compare scanned vs expected → Pass / Fail(+reason code, F22).
3. On **Fail**: do NOT re-arm print; record the failed attempt (PrintCount already reflects
   attempts); carton is manually intervened; reprint rules stay locked (decision-013 / 004).
4. Append the verify result (Pass/Fail/reason) to the transport order for routing; the actual
   divert lane is decided by econtroller CriteriaBasedSorting (decision-008).
5. **Emit OUT-4 (verify→WMS notification)** via `IWmsVerifyNotifier` (decision-015).
6. Update carton status + divert dest for the PLC.

### IN-4 · Printer status (PLC-relayed)  (legacy frame 283)
**Trigger:** the PLC relays a printer's status as an integer code.
**Input:** `PlcPrinterStatus { LineId, PrinterId, StatusCode, SorterNumber }`.
**What happens when called:** map the code to online/offline (a paused/faulted printer →
offline, decision-014); update printer state; trigger spare promotion/demotion. This is the
**coarse** status path; the direct `~HS` channel (IN-6) is authoritative when both are present.

### IN-5 · Zone status  (legacy frame 284)
**Trigger:** the PLC reports a zone's run/stop condition.
**Input:** `ZoneStatus { LineId, ZoneNumber, StatusCode }`.
**What happens when called:** interpret the code as **stop** vs **slow** vs **run** (exact code
values are a bookmarked site gap — BluePaw tag codes). On stop → **emit OUT-2 LineStop** for
every line in the zone; on slow → **emit OUT-2 LineSlow**; on run → clear. (LINECTRL egress.)

### IN-6 · Printer engine status (direct `~HS`)
**Trigger:** a Zebra printer returns a host-status string (adapter drives the cadence — infra,
out of scope).
**Input:** `IngestPrinterStatus(PrinterId, RawHsString)`.
**What happens when called:** `PrintEngineStatusParser` parses the comma-delimited `~HS` payload
into ~28 flags (paper-out, pause, head-up, ribbon-out, buffer-full, temp, remaining-labels,
lid-open derived, …); store the active record (`IPrintEngineStatusStore`); derive online/offline
(`FlagPause>0 ⇒ offline`, decision-014); update spare promotion. **Authoritative** printer health.

### IN-7 · PLC lifecycle/recovery events  (F15)
**Trigger:** the PLC signals a carton lost / re-presented / other lifecycle event.
**Input:** `PlcEvent { LineId, PlcEventCode, CartonRef }`.
**What happens when called:** `PlcEventHandlerService` applies recovery semantics — re-arm only
when reprint rules allow, else hold for intervention; `PrintCount` stays monotonic (decision-009).
(Specific PLC integer codes are a bookmarked site gap, A1/A2.)

---

## OUTBOUND — Core-owned ports (Core → adapter)

### OUT-1 · Fire-point / assign bundle  → PLC
**Port:** `ILinePlcGateway.Send(LineControlCommand.AssignBundle{ CartonRef, PrintFirePoint,
ApplyFirePoint, DivertDest, SlotIndex, PrinterId })`.
**Called from:** IN-1 (induct). **Contract:** deliver the per-carton print/apply/dest/slot
assignment to the PLC. (Adapter marshals to the ADS struct — out of scope.)

### OUT-2 · Line control (stop / slow)  → PLC
**Port:** `ILinePlcGateway.Send(LineControlCommand.LineStop{ ZoneNumber })` /
`.LineSlow{ SorterNumber }` / `.Clear{…}`.
**Called from:** IN-5 (zone status) and lane-eval decisions. **Contract:** request the PLC to
stop a zone (`RemoteStop`) or slow a sorter (`SlowFlag`). Note the source distinction: stop
indexes by **zone**, slow indexes by **sorter** (decision spec: routing-status-control cluster).

### OUT-3 · Send label  → printer
**Port:** `IPrinterTransport.SendLabel(PrinterId, LabelPayload)`.
**Called from:** IN-2 (print). **Contract:** deliver the rendered label payload (ZPL) to the
identified printer; return success/failure. Building the socket/HTTP transport is OUT OF SCOPE
(backlog `ZebraConnector`); Core only guarantees *what* is sent and *when*.

### OUT-4 · Verify result  → WMS/DCMS
**Port:** `IWmsVerifyNotifier.NotifyVerifyAsync(VerifyNotification{ CartonId/BlindLabel,
Result, RejectReasonCode, VerifyTimestamp, PrinterId, LineId })`.
**Called from:** IN-3 (verify). **Contract:** notify the WMS of the verify outcome. Transport
(DTC event vs direct) is deferred to the adapter (decision-015).

### OUT-5 · Wave status  → DCMS  *(tabled)*
**Port:** `IDcmsWaveNotifier` (F23). Waves are tabled; listed for completeness only.

---

## Explicitly out of scope (infrastructure — per user, 2026-08-12)
- Building any connection (TCP sockets, ADS sessions, eHub connectors, HTTP clients).
- Polling loops / cadence for `~HS` and connection health (adapter concern).
- Wire framing/marshalling (CSV frames, ADS struct blitting) — adapter concern.
- No code added to eHub or its plugins.

## Bookmarked site gaps (do not block the contracts above)
- Zone stop/slow **code values** (IN-5) and PLC lifecycle **integer codes** (IN-7, A1/A2).
- ADS tag paths + outbound struct layouts (decision-018 G-TAG1/G-STRUCT1).
- Printer status cadence + transport choice (decision-018 G-ZEB1).

## References
`sdisp_BP2PA_Scan_Induct/Print/Scan_Verify/Status_Printer/Status_Zone`,
`sdisp_PA_Status_PrintEngine` (~HS parser), `sdisp_PA2TCP_SendTCPData`,
`sdisp_TOOL_PA_ShutLineDown/SlowLineDown/ShutZoneDown`; ownership-map rows F13/LINECTRL/F15/F14;
decisions 008/009/013/014/015/016/017/018.
