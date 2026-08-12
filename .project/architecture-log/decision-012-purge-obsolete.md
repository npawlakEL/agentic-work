# Decision 012 — Purge (F17) largely obsolete in the redesign

**Status:** Accepted
**Date:** 2026-08-12
**Owner ruling:** "table the xref discussion … but yes, that's better" (agreeing the general
purge is obsolete; only orphaned label-advice cleanup is a real candidate, and even that is
bookmarked).

## Context

The SQL source's nightly `sdisp_PA_Purge` deletes: `PandaData`, `PandaCartonList`,
`PandaDataXRef`, `Wave`/`WaveHistory`, `PrintEngineStatus`, `EventLog`/`uEventLog`/
`EventDescriptions`. We asked what PandA *exclusively* owns and persists in the new
architecture, since it now rides on econtroller transport orders and logs to `MfcLog`.

Mapping each purged table into the redesign:

| Source table | Redesign | Purge still needed here? |
|--------------|----------|--------------------------|
| `PandaData` (carton record) | **= the econtroller TransportOrder** | No — econtroller owns TO lifecycle/cleanup |
| `PandaCartonList` | not used (keyed on TO id; no `CartonListID`) | No — gone |
| `EventLog` / `uEventLog` / `EventDescriptions` | **`ILogger<T>` → `MfcLog`** (decision-005) | No — econtroller/DBA log retention |
| `PrintEngineStatus` | in-memory `PrinterState` | No — nothing persisted |
| `Wave` / `WaveHistory` | waves tabled (may not be needed) | Deferred with the wave discussion |
| `PandaDataXRef` (oLPN xref) | PandA-attached link | **Tabled** — needs its own discussion |

## Decision

1. **Drop the general PandA purge (F17).** Everything the old purge churned through is now
   the transport order (econtroller's), the log (`MfcLog`/DBA's), in-memory status, or a
   tabled subsystem. No nightly `sdisp_PA_Purge` equivalent is built.
2. **The only PandA-exclusive persistent data worth cleaning up is orphaned / never-used
   label advice** (`LabelData` pushed in by the outside source whose carton never ran — the
   source's "UnUsedData" case). This is **bookmarked** as a possible small scheduled cleanup
   (Quartz.NET job, matching econtroller's scheduler) — NOT built now.
3. **`PandaDataXRef` / oLPN cross-reference (F18/F24) is TABLED** for a dedicated discussion;
   its retention is not decided here.
4. econtroller reference: log retention is file-rotation via NLog (`archiveEvery=Day`,
   `maxArchiveFiles=5`); the `MfcLog` DB target has no in-app purge — retention is infra/DBA.

## Resolves / tables
- **E4 (F17 purge):** ✅ resolved — dropped; orphaned-advice cleanup bookmarked.
- **F1 (WaveID filename parse):** tabled with the wave subsystem.
- **C2–C5 (DYNAP):** tabled for a dedicated DynamicApplyPoint discussion.
- **D11/D12 (wave SUSPENDED semantics):** tabled with the wave subsystem.
- **xref/oLPN (F18/F24):** tabled.

## References
- Source: `sdisp_PA_Purge.sql`.
- econtroller: `eController.Mfc/NLog.config` (file rotation + `MfcLog` target, no purge).
- decision-005 (ILogger/MfcLog), `.project/spec/clusters/platform-settings.md` §F17.
