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
- **A5 (F16):** ✅ RESOLVED (decision-007): `805` is a typo for `80` (Information).

## B. Deliberate divergences already flagged (confirm keep/adjust)

- **B1 (F15 vs decision-003):** ✅ RESOLVED (decision-009): recovery allowed only pre-verify
  (`DeviceId < VerifyDevice`) AND gated by `ReprintLabels` (if 0 → HeldForIntervention).
- **B2 (F15):** ✅ RESOLVED (decision-009): `PrintCount` stays MONOTONIC on recovery (never zeroed).
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
- **D5 (PROFSW):** ✅ RESOLVED (decision-011): `Active` = "selectable in the catalog" (many at
  once), NOT "currently selected". The in-use selection is the per-line active-Map pointer.
- **D6 (PROFSW):** ✅ RESOLVED (decision-011): host sends no map/profile name → keep the line's
  currently-active Map (static clients always have one active); no "force NoProfile".
- **D7 (F10):** ✅ RESOLVED (decision-007): add `Active=1` filter on template lookup.
- **D8 (F10):** ✅ RESOLVED (decision-007): wire `DataMismatch`/`ScanError_Conflict` templates
  to their verify reason codes (treat as reachable).
- **D9 (F08):** ✅ RESOLVED (decision-008): lane/place selection is NOT PandA's — delegated to
  econtroller CriteriaBasedSorting. No LaneDef/round-robin/LastDiverted in the port.
- **D10 (F08):** ✅ RESOLVED (decision-008): no REJECT lane in PandA; reject destinations are a
  CriteriaConfig mapping on the reject reason-code value.
- **D11 (F23):** Only `WaveStatus='ACTIVE'` auto-completes — what about `SUSPENDED`? And is DCMS
  wave notification fire-and-forget or blocking?
- **D12 (F18):** Allow oLPN association against a `SUSPENDED` wave? Source guard is only
  `<> 'COMPLETED'`.

## E. Deployment topology (affects concurrency & counters)

- **E1 (F21):** ✅ RESOLVED (decision-010): assign counter is **per-line**, in-memory, thread-safe;
  array size defaults to 300 but configurable per line (`LineConfig`).
- **E2 (LOCK):** ✅ RESOLVED (decision-010): single process, multiple lines/PLCs → in-memory
  thread-safe locks keyed per-line/per-printer (no `sp_getapplock`); behind `IPandaLock`.
- **E3 (LOCK):** ✅ RESOLVED (decision-007): lock-acquire timeout is a hard error (fail fast).
- **E4 (F17):** Purge trigger mechanism — hosted-service `PeriodicTimer`, SQL Agent, or Worker?
  And on a mid-purge failure: replicate source's abort-remaining, or continue each step?

## F. Ingestion / integration boundaries

- **F1 (INBOUND):** WaveID is parsed from the inbound **filename** (`...LD{WaveID}_...`). Stable
  across sites or ULW-specific? Should the parser be configurable (regex/format)?
- **F2 (INBOUND):** ✅ RESOLVED (decision-007): per-record ACK/error handling (bad record skipped, not batch).
- **F3 (F14):** `sdisp_WMS_PandAVerify_Insert` (WMS outbound) — is the MandA WMS integration
  in-scope now (as an `IWmsVerifyNotifier` port) or deferred with the EController adapter?
- **F4 (F16):** ✅ RESOLVED (decision-005): logging via `ILogger<T>` (sync structured logging), no `IPandaEventSink`.
- **F5 (F20):** ✅ RESOLVED (decision-007): check read-quality sentinels on all 6 label slots.
