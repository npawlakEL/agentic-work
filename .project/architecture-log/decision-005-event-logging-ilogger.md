# Decision-005 — Event logging adopts econtroller's `ILogger<T>` pattern (supersedes F16 bespoke sink)

**Status:** Accepted · **Date:** 2026-08-12 · **Domain owner:** confirmed
**Supersedes:** the F16 "bespoke `IPandaEventSink` / `PandaEvent` / `PandaEventLevel`" design in
`.project/spec/clusters/events-recovery-logging.md`.

## Context
The source PandA logs to `EventLog`/`uEventLog` on nearly every operation, with rich columns
(`CartonListID`, `PandaID`, `PrinterID`, `LPN`, `PandaDataID`). The spec pass proposed a bespoke
`IPandaEventSink`. The domain owner directed that PandA logging **match econtroller / exol**:
exol's UI has an event-log screen that displays all logged records, so PandA's events must land
in the same pipeline.

## Findings (from econtroller source)
- econtroller services use **`Microsoft.Extensions.Logging.ILogger<T>`** with **structured
  message templates**, e.g. (`eController.TransportInterface.Sim/Actions/SimActions.cs`):
  ```csharp
  logger.LogInformation("TU {TuId} starts moving from {SourceMp} to {DestinationMp}.",
      trans.TuId, trans.Mp, trans.FinalDestMp);
  logger.LogError("Current Mp {Mp} not found in layout", trans.Mp);
  ```
- Named placeholders become NLog event-properties. NLog's **database target** persists to a log
  table (`MfcLog`: `LogDate, LogLevel, Process, Logger, EventName, Message, Topic, Exception`,
  per `eController.Mfc/NLog.config`), which the exol event-log screen reads.
- `LogMessageObject(EVENT_NAME, Message, Topic)` (`eController.DataLib/Logging`) is the structured
  state object econtroller passes for DB logging.

## Decision
1. **`PandA.Core` (and services) use `ILogger<T>`** from `Microsoft.Extensions.Logging.Abstractions`
   (a standard NuGet — does NOT violate the "no econtroller dependency in Core/Sim" rule).
2. **No bespoke `IPandaEventSink`, `PandaEvent`, or `PandaEventLevel`.** Use structured message
   templates with named placeholders carrying the relevant domain fields.
3. **No `CartonListID`.** That is a SQL artifact of the `PandaCartonList` table. The port keys on
   the **TransportOrder** (`TuId` / TO id). Include whatever is relevant per call —
   `{TuId}`, `{PrinterId}`, `{LabelType}`, `{Lpn}`, `{Lane}`, `{ProfileName}`, etc.
4. **Source log-level → `LogLevel` mapping:**
   | Source | Name | `LogLevel` |
   |--------|------|-----------|
   | 30 | Critical | `Critical` |
   | 40 | Error | `Error` |
   | 50 | Warning | `Warning` |
   | 80 | Information | `Information` |
   | 100 | Verbose | `Trace` |
   (The source `805` in `sdisp_PA_Scan_Verify:149` is a typo → `Information`.)
5. **`PandA.Sim`** uses a capturing/list `ILogger` (e.g. a small `FakeLogger`) so tests assert on
   logged records instead of a bespoke in-memory sink.
6. **`PandA.EController` adapter** wires NLog (or the platform logger) so PandA records surface in
   exol's existing event-log screen. F-LOG1 carton run-history is a **domain** record
   (`ICartonRunRepository`), separate from this observability log; it remains as specced.

## Consequences
- **F16 target modules change** to: `ILogger<T>` injection into `InductService`,
  `VerifyStationService`, `CartonAdviceService`, `LaneEvalService` (+ new services), structured
  templates, and a `FakeLogger` in Sim. Removes `IPandaEventSink.cs`/`PandaEvent.cs`/
  `PandaEventLevel.cs`/`NullPandaEventSink.cs`/`InMemoryEventSink.cs` from the plan.
- Every other spec that said "emit via `IPandaEventSink`" now means "log via `ILogger<T>` with
  structured fields." F15/F20/F22/F17/LINECTRL logging references updated accordingly.
- `ownership-map.md` and `000-index.md` cross-cutting contracts updated; `spec_features` F16
  `target_module` updated.
