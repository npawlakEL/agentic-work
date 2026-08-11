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
- For each label in the set, select printer(s) per the authoritative algorithm (**see architecture-log
  005**): candidates = line printers whose `LabelMap` contains the label's `LabelType`, `Online=true` and
  **not spare**; choose the **least-recently-printed** eligible printer **for that label type** (per-type
  round robin keyed on `LastPrinted`) when `LoadBalance=true`, else the first configured. **Collision rule:**
  if one printer is the primary pick for more than one of the carton's label types, the colliding type
  **falls back to its backup (next least-recently-printed) printer** so the carton's labels spread across
  distinct printers when possible. If no printer matches a type → status `NO_PRINTER` for that label
  (logged; other labels still print).
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

## 6a. Locked Phase-1 behaviors (from 2026-08-11 design grill)
- **Deliverable = tests only** (xUnit). MP1/MP2 are exercised as service calls; no telegram parsing and no
  runnable harness yet. A msg-in/msg-out Sim comes **after** the verify phase.
- **Duplicate advice = overwrite / last-wins** for Phase 1. This must be a **toggle** later
  (`OverwriteLabelData`-style); see backlog. So §5.3/§7.2 collapse to last-wins for now.
- **Line scope:** test a **single line**, but config + services stay **multi-line-capable** (line resolved
  from the message point).
- **Load-balance tie-break:** when eligible printers share an equal `LastPrinted` (incl. cold start / never
  printed), **configured order wins** — the first printer listed in `PandaLine.json` for that label type.
  Deterministic and testable.
- **Orientation** is a provisioned selection dimension (label type + orientation); the height→Side/Top
  transfer is deferred (backlog). Phase 1 uses orientation = default/host value only.

## 7. Acceptance criteria (Gate — TDD)
1. Advice creates a TO shell with `TuId=blind label`, status `ADVISED`, and a `PandaLabelSet` extension.
2. Repeated advice **overwrites** the label set (last-wins) for Phase 1; the append-vs-overwrite +
   don't-reprint-if-printed rules are behind a deferred toggle (backlog).
3. Induct scan with a matching TO emits **each** label's ZPL to a printer whose `LabelMap` includes that
   label's type; multi-type carton fires multiple printers.
4. Load-balancing distributes across eligible printers when `LoadBalance=true`, scoped **per label type** by
   least-recently-printed; a printer colliding across two of the carton's label types yields the second to
   its backup printer (per architecture-log 005).
5. No matching printer / no data / no active TO produce the correct status and **no** erroneous print.
6. All of the above proven by xUnit tests running against `PandA.Sim` (in-memory store + capturing gateway).
7. `PandA.Core` + `PandA.Sim` build clean and tests pass here; `PandA.EController` present and written
   against real interfaces (compile deferred).

## 7b. Phase 2 — Verify (locked 2026-08-11 grill)
**Scope:** verify-core + verify-fail threshold. Lane routing **deferred** (folds into criteria-based
sorting from Exol at integration). Authoritative model: architecture-log **006**.
- **Verify-core:** compare scanned labels to the TO's expected `PandaLabelSet` (+ xref backups). Produce a
  result carrying an **outcome enum** `{Pass, Fail, NoRead, NoData, Conflict, Ignore}` **plus label-type
  detail** (which label, which type, which reason). The exact numeric `VerifyPass` codes (006 legend) are
  **not** surfaced on the model now — kept in 006 for a later host/GUI telemetry mapper.
- **Only `Pass` passes.** Any non-pass re-arms the carton to reprint (Printed=0, ActiveRecord=1 semantics).
  `Ignore` = bypass/disabled. Missing PandaData ⇒ Fail (reject).
- **`VerifyContentLabel=false`** ⇒ verify only Shipping/Exception types.
- **Scanner input:** Phase-2 tests pass **pre-typed scanned labels** (`{LabelType, ScannedValue}`). The raw
  delimited-string + `Settings_LabelBufferOrder` position parser is **bookmarked** (backlog) — needed for the
  final integration.
- **Fail threshold:** per-line **consecutive** fail counter; increment on non-pass, **reset on pass**; when
  `count >= VerifyFailThreshold` raise a **pause-printer** signal (egress port deferred).

## 8. Risks / open items
- `TuId` non-unique → active-status filtering + TO purge/aging (full purge port is later).
- Real telegram/MP ids + printer TCP framing come from the site protocol at integration (abstracted behind
  ports now).
- ZPL size stored in `DynamicField` JSON — acceptable for single labels; revisit if very large.

## 9. Deliverables
Code (Core+Sim+EController skeleton) + passing tests + updated architecture-log coverage against the
322-object inventory (mark Phase-1 objects addressed).