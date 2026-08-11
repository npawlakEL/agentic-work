# Specification — PandA Phase 1 (Advice → Induct → Pick Printer → Send ZPL)

> Written by the **Planner Agent**. Awaiting user approval (Gate 1) before implementation.
> Grounded in decision-002, architecture-log/002–003, and the econtroller/Exol source.

## 1. Objective
Prove the PandA integration pattern end-to-end in a **simulated backend**, with a codebase that unplugs
from the sim and plugs into a real econtroller project. A carton advised by the host (blind label + typed
ZPL) is, on induct scan, matched to its transport order and printed on the correct label-type-matched
printer(s). **Backend-only; host provides ready ZPL (pass-through, no template engine).**

## 2. In scope
- Two flows: **label-advice** (create TO shell + label-set extension) and **induct scan** (lookup → pick
  printer(s) → emit ZPL).
- Per-line config (`PandaLine.json`): lines → printers (`IP`, `Port`, `LabelMap`, `LoadBalance`).
- Typed label-set extension `[{ LabelType, Lpn, Zpl }]` on the TO.
- Printer selection by matching label `LabelType` → printer `LabelMap`, with load-balancing.
- Append-vs-overwrite of the label set on repeated advice; no reprint of an already-printed TO unless allowed.
- Ports-and-adapters split: `PandA.Core` + `PandA.Sim` (compiled+tested); `PandA.EController` (written, not
  compiled here).

## 3. Out of scope (deferred)
Label-template/profile → ZPL engine; verify scan + lane routing; waves; operator GUI; real TCP/PLC/host
connectors; SiteBuilder UI; multi-controller (CE 1–7) topology.

## 4. Architecture & projects
```
PandA.Core          (net9.0, no econtroller refs)
  Domain:   Carton (view over a TO), PandaLabel { LabelType, Lpn, Zpl }, PandaLabelSet
  Config:   PandaLineConfig, PandaPrinterConfig  (+ IPandaConfigProvider)
  Ports:    ITransportOrderStore, IPrinterGateway, IClock, ILogger
  Services: ICartonAdviceService, ICartonLookupService, IPrinterSelectionService, IPrintDispatchService
PandA.Sim           (references Core; in-memory TO store, capturing printer gateway, config from JSON file)
PandA.EController   (references Core; IMfcAction actions, EffortlessConfiguration loader,
                     ITelegramOutbox/HookKey mapping, TU-extension bridge)   [compiled at integration]
PandA.Core.Tests / PandA.Sim.Tests   (xUnit v3)
```
Actions are thin; all logic lives in Core services behind ports so the same tests prove behavior in Sim and
(later) in econtroller.

## 5. Behavioral requirements

### 5.1 Label-advice (MP1)
- Input: `{ BlindLabel, Labels: [{ LabelType, Lpn, Zpl }] , OverwriteLabelData? }`.
- Find TO by `TuId == BlindLabel` among **active/in-flight** TOs; if none, create a shell
  (`TuId=BlindLabel`, `TuStatus=ADVISED`).
- Store/merge the `PandaLabelSet` extension:
  - If no existing set or `OverwriteLabelData=true` → replace.
  - Else append (dedupe by `{LabelType, Lpn}`).
  - If the matched TO is already `PRINTED` and reprint disabled → reject (no overwrite).
- Persisted via TU extension (`DynamicField`).

### 5.2 Induct scan (MP2)
- Input: `{ BlindLabel, LineId (or resolved from message point) }`.
- Lookup active TO by `TuId`. If none / no label set → status `NO_DATA` (logged; no print).
- For each label in the set, select printer(s): candidates = line printers whose `LabelMap` contains the
  label's `LabelType`, `Online=true`; choose by **load-balance** (least-recently-used / round-robin) when
  `LoadBalance=true`, else the first configured. If no printer matches a type → status `NO_PRINTER` for that
  label (logged; other labels still print).
- Emit each label's `Zpl` to its chosen printer via `IPrinterGateway.SendAsync(printer, zpl, lpn, type)`.
- Mark TO `PRINTED` (and record which printer/type) on success.

### 5.3 Disambiguation (multiple candidates)
Mirror `sdisp_PA_LookupCarton` ordering, adapted to the TO model: prefer not-yet-printed, then active,
then (append→oldest / overwrite→newest) by creation, then stable id. Exclude `Exception` type from reprint.

## 6. Config schema (`PandaLine.json`)
```json
[
  { "LineId": "L1", "PlaceId": "...", "LoadBalance": true, "VerifyThreshold": "…",
    "Printers": [ { "PrinterId": "P1", "IP": "10.0.0.5", "Port": 9100,
                    "LabelMap": ["Shipping","Content"], "Online": true } ] }
]
```
Loaded by `IPandaConfigProvider`: Sim reads the JSON file directly; econtroller adapter reads it via
`IEffortlessConfigurationRegistry.AppConfigFolder` (file name contains `PandaLine`), hot-reloadable.

## 7. Acceptance criteria (Gate — TDD)
1. Advice creates a TO shell with `TuId=blind label`, status `ADVISED`, and a `PandaLabelSet` extension.
2. Repeated advice appends or overwrites per the setting; already-printed + reprint-off is rejected.
3. Induct scan with a matching TO emits **each** label's ZPL to a printer whose `LabelMap` includes that
   label's type; multi-type carton fires multiple printers.
4. Load-balancing distributes across eligible printers when `LoadBalance=true`.
5. No matching printer / no data / no active TO produce the correct status and **no** erroneous print.
6. All of the above proven by xUnit tests running against `PandA.Sim` (in-memory store + capturing gateway).
7. `PandA.Core` + `PandA.Sim` build clean and tests pass here; `PandA.EController` present and written
   against real interfaces (compile deferred).

## 8. Risks / open items
- `TuId` non-unique → active-status filtering + TO purge/aging (full purge port is later).
- Real telegram/MP ids + printer TCP framing come from the site protocol at integration (abstracted behind
  ports now).
- ZPL size stored in `DynamicField` JSON — acceptable for single labels; revisit if very large.

## 9. Deliverables
Code (Core+Sim+EController skeleton) + passing tests + updated architecture-log coverage against the
322-object inventory (mark Phase-1 objects addressed).