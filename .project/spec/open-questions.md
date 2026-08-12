# Domain-Owner Open Questions (Spec Lockdown)

Batched from the four spec passes. Grouped by theme. Each needs a ruling before the dependent
slice can be built (or an explicit "build the recommended default, revisit later").
IDs map back to the cluster spec docs.

## A. Unknown codes / taxonomies (blockers for exact fidelity)

- **A1 (F15):** PLC event codes **217 / 218** are silently ignored in source. What are they?
- **A2 (F13):** What integer values does msg 283 `PLCStatus` carry (0=offline/1=online? fault states?).
- **A3 (F13):** Nothing in source writes `PrinterState.EngineStatus` from `PrintEngineStatus`.
  Is the recommended derivation `EngineOnline = !(PaperOut || HeadUp || RibbonOut)` correct?
- **A4 (F13):** Should operator-set `FlagPause` force `EngineOnline=false` (and trigger spare
  promotion), or is pause a "known temporary offline" that should NOT promote spares?
- **A5 (F16):** Log level **`805`** in `sdisp_PA_Scan_Verify:149` — confirm it's a typo for `80`
  (Information); port will normalize.

## B. Deliberate divergences already flagged (confirm keep/adjust)

- **B1 (F15 vs decision-003):** PLC pre-verify recovery re-arms a carton (`Printed=0`). This is
  distinct from the verify-fail hole we closed. Confirm: recovery is allowed **only pre-verify**
  (`DeviceId < VerifyDevice`), and should it be gated by `ReprintLabels=0`?
- **B2 (F15):** On recovery, should `PrintCount` be **decremented / reset / left monotonic**?
  (Source zeroes it; decision-003 makes it monotonic — conflict.)
- **B3 (F22):** Gap-error cartons never reach verify and get no RejectHistory row in source.
  Should the C# port write a `RejectRecord(Code=4, "Gap Error")` at induct so they show on the
  reject screen?

## C. Source bugs found (fix in port? confirm intent)

- **C1 (LINECTRL):** `sdisp_TOOL_PA_ShutZoneDown` never assigns `@PandaID`
  (`SELECT PandaID=@PandaID` alias bug) — calls ShutLine with empty ID. Port will fix.
- **C2 (DYNAP):** `DynamicPrintPoint` gate is commented out — dynamic apply always runs. Restore
  the gate (default ON) or always compute?
- **C3 (DYNAP):** `LabelWidth` hardcoded to `4` at the call site despite being fetched from
  `LabelTypes`. Use the real per-label width, or keep 4?
- **C4 (DYNAP):** `EncoderResolution` default differs: `0.25` in DynamicApplyPoint vs `0.2` in
  PickPrinter, and the unit is inverted vs its description ("steppulses/inch" but algebra is
  inches/pulse). Canonical default? One setting or two?
- **C5 (DYNAP):** Magic constant **`0.113`** in the top-apply formula — universal or
  site-calibrated? Per-printer config or global?
- **C6 (F-LOG1):** `PrinterNumber` trimmed by one char on update (`LEFT(..,LEN-1)`). What trailing
  char is stripped — should the port replicate?

## D. Behavior / policy decisions

- **D1 (SETTINGS):** Are settings **global** or ever **per-line** (e.g. different `MinGap` per line)?
  Source is a single global table.
- **D2 (SETTINGS):** Seed vs proc-default mismatches (`Reprint Labels` 0 vs 1;
  `PurgeSetting_UsedData` 7 vs 21; `PurgeSetting_InactiveData` 21 vs 14). Align proc defaults to seed?
- **D3 (SETTINGS-2):** Should the GUI "lock a carton out of reprint" action (`SetPrintedFlag
  @PrintFlag=1`) survive in the new lifecycle model, or is it superseded by wave/lifecycle?
- **D4 (F-ADV1):** Re-advice of a non-reprintable carton — domain error, silent no-op, or alert?
- **D5 (PROFSW):** `Active=1` on 35+ profiles simultaneously — confirm `Active` means
  "selectable by host", not "currently selected".
- **D6 (PROFSW):** Host sends no ProfileName → fall back to `DefaultProfile`, or force `NoProfile`
  (require explicit profile per carton)?
- **D7 (F10):** `Active` filter on template lookup — source has none; add `Active=1` filter in port?
- **D8 (F10):** `DataMismatch` / `ScanError_Conflict` templates appear unreachable from the
  builder CASE. Dead data, or missing mapping?
- **D9 (F08):** Who writes `LaneDef.LastDiverted` back? Source only reads it. Without the write,
  round-robin degrades to a fixed pick. And is the NULL→GETDATE quirk (never-diverted lane loses)
  intentional?
- **D10 (F08):** REJECT lane absent from seed — always provisioned at install? Behavior when also
  missing: return null or throw config error?
- **D11 (F23):** Only `WaveStatus='ACTIVE'` auto-completes — what about `SUSPENDED`? And is DCMS
  wave notification fire-and-forget or blocking?
- **D12 (F18):** Allow oLPN association against a `SUSPENDED` wave? Source guard is only
  `<> 'COMPLETED'`.

## E. Deployment topology (affects concurrency & counters)

- **E1 (F21):** Is the PLC assign array strictly **300** slots, or site-specific/configurable?
  Per-line counter or shared across lines?
- **E2 (LOCK):** Is PandA ever deployed **multi-process** (multiple C# hosts, one DB)? If yes the
  real lock impl must use `sp_getapplock`; if single-process, in-memory semaphores suffice.
- **E3 (LOCK):** Lock acquire timeout (60s) — soft failure (log + proceed, as source) or hard error?
- **E4 (F17):** Purge trigger mechanism — hosted-service `PeriodicTimer`, SQL Agent, or Worker?
  And on a mid-purge failure: replicate source's abort-remaining, or continue each step?

## F. Ingestion / integration boundaries

- **F1 (INBOUND):** WaveID is parsed from the inbound **filename** (`...LD{WaveID}_...`). Stable
  across sites or ULW-specific? Should the parser be configurable (regex/format)?
- **F2 (INBOUND):** Per-record vs per-batch ACK failure handling?
- **F3 (F14):** `sdisp_WMS_PandAVerify_Insert` (WMS outbound) — is the MandA WMS integration
  in-scope now (as an `IWmsVerifyNotifier` port) or deferred with the EController adapter?
- **F4 (F16):** Should `IPandaEventSink` be sync (`void Emit`) or async (`Task EmitAsync`)?
  Async helps a future DB-writing adapter; Core services are currently sync.
- **F5 (F20):** Check quality sentinels on **Label1 only** (source) or all 6 labels?
