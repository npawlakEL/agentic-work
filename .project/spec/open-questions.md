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
- **C2 (DYNAP):** ⏸️ TABLED (decision-012): DynamicApplyPoint needs a dedicated discussion.
- **C3 (DYNAP):** ⏸️ TABLED (decision-012).
- **C4 (DYNAP):** ⏸️ TABLED (decision-012).
- **C5 (DYNAP):** ⏸️ TABLED (decision-012).
- **C6 (F-LOG1):** `PrinterNumber` trimmed by one char on update (`LEFT(..,LEN-1)`). What trailing
  char is stripped — should the port replicate?

## D. Behavior / policy decisions

- **D1 (SETTINGS):** Are settings **global** or ever **per-line** (e.g. different `MinGap` per line)?
  Source is a single global table.
- **D2 (SETTINGS):** ✅ RESOLVED (decision-013): `Reprint Labels` default = ON (1). Other mismatches
  were purge settings → moot (F17 descoped, decision-012).
- **D3 (SETTINGS-2):** ✅ RESOLVED (decision-013): manual lock-out (`SetPrintedFlag`) SUPERSEDED by the
  lifecycle; `AuthorizeReprint` is the only re-open path. Dropped.
- **D4 (F-ADV1):** ✅ RESOLVED (decision-013): re-advice of a non-reprintable carton OVERWRITES the
  stored label data but stays LOCKED (no re-arm) until operator `AuthorizeReprint`.
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
- **D11 (F23):** ⏸️ TABLED (decision-012): wave subsystem may not be needed this build.
- **D12 (F18):** ⏸️ TABLED (decision-012): oLPN/xref discussion deferred.

## E. Deployment topology (affects concurrency & counters)

- **E1 (F21):** ✅ RESOLVED (decision-010): assign counter is **per-line**, in-memory, thread-safe;
  array size defaults to 300 but configurable per line (`LineConfig`).
- **E2 (LOCK):** ✅ RESOLVED (decision-010): single process, multiple lines/PLCs → in-memory
  thread-safe locks keyed per-line/per-printer (no `sp_getapplock`); behind `IPandaLock`.
- **E3 (LOCK):** ✅ RESOLVED (decision-007): lock-acquire timeout is a hard error (fail fast).
- **E4 (F17):** ✅ RESOLVED (decision-012): general purge dropped (TO=econtroller's, logs=MfcLog/DBA,
  status=in-memory). Only orphaned/never-used label-advice cleanup is a candidate — bookmarked (Quartz).

## F. Ingestion / integration boundaries

- **F1 (INBOUND):** ⏸️ TABLED (decision-012): parsed with the wave subsystem, which may not be
  needed in this build. Revisit when waves are discussed.
- **F2 (INBOUND):** ✅ RESOLVED (decision-007): per-record ACK/error handling (bad record skipped, not batch).
- **F3 (F14):** `sdisp_WMS_PandAVerify_Insert` (WMS outbound) — is the MandA WMS integration
  in-scope now (as an `IWmsVerifyNotifier` port) or deferred with the EController adapter?
- **F4 (F16):** ✅ RESOLVED (decision-005): logging via `ILogger<T>` (sync structured logging), no `IPandaEventSink`.
- **F5 (F20):** ✅ RESOLVED (decision-007): check read-quality sentinels on all 6 label slots.
