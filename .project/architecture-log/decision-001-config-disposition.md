# Decision 001 — PandA configuration disposition for the C# port

**Author:** Senior Coder (auto-engaged) + User
**Date:** 2026-08-11
**Status:** Accepted

## Context
PandA's behavior is driven by ~22 seed/config tables plus SQL-specific plumbing (event→SP map, message
catalog) and cross-DB synonyms. Deciding what becomes ported **configuration** vs what is an
**architectural replacement** (dropped, re-expressed natively) vs a **later bookmark** is required before
scoping the Phase-1 vertical slice (induct→lookup→pick-printer→print happy path, backend-only).

## Decision

### PORT as configuration (Phase 1) — groups A–D (~16 tables)
Commissioning/behavior config the induct→print path actually reads:
- **A. Global settings:** `Settings` (27) — behavior toggles (print rules, gap, dynamic apply point,
  height check, load balance, purge retention, etc.). Port whole table.
- **B. Vocabulary/enums:** `Settings_CartonStatuses` (20), `LabelTypes` (3), `LabelDef` (5),
  `LabelPrintLocations` (5), `LaneDef` (6), `PandAState` (5), `PrinterState` (7).
- **C. Device/topology:** `PandAs` (4), `PandADetails` (20), `Printers` (7), `PrinterDetails` (62),
  `PrinterFirePoints` (201).
- **D. Label formatting:** `LabelProfileHeader` (56), `LabelProfileDetail` (233), `LabelProfileMap` (7),
  `LabelTemplates` (14), `Settings_DefaultAttributes` (18), `Settings_LabelBufferOrder` (10).

These become EF/persistent-tables + seed, exposed for per-project configuration in econtroller.

### BOOKMARK (later phase) — group E
- `Wave` (435), `WaveRange` (27) — live/operational wave data, not commissioning config. Not needed for
  the Phase-1 happy path. Logged to backlog. (Wave *lifecycle logic* is a later functional phase.)

### ARCHITECTURAL REPLACEMENT — NOT ported as data, NOT bookmarked — groups F & G
Per user: these are architecture-specific to the SQL implementation and change completely in the C# port.
They are **replaced by native econtroller mechanisms**, not carried over or revisited as a "port later"
item.
- **F. SQL eventing plumbing:** `EventStoredProcedureList` (44) [event→SP-name dispatch table],
  `CreateSystemMessages` (495 KB) + `EventDescriptions` [message/event text catalog] →
  **replaced by** econtroller logging/eventing: `ILogger<T>`, `MfcLog`, `IScopedMessenger`, structured
  event types. The event *semantics* (what conditions get logged/raised) are reproduced inside the C#
  services/actions, not via a SQL dispatch table or message-id catalog.
- **G. Cross-DB integration:** 57 `Synonym`s + `sdisp_TOOL_SynBuilder_*` (CORE/DCMS/PLC/TCP per control
  engine 1–7) → **replaced by** econtroller transport/connector configuration
  (`eController.TransportInterface.*`, `ITelegramOutbox<T>`, `HookKey`, `controllers.json`). Phase 1 uses
  a **stubbed printer outbound**; real PLC/host/printer connectors are a later integration phase, but the
  *synonym-based approach itself is not ported*.

## Consequences
- Phase-1 config scope is fixed at groups A–D (~16 tables) — the authoritative list to seed + expose.
- No effort is spent porting F/G as data; instead their behavior is designed natively when the relevant
  services/actions are built (eventing alongside the services; connectors in the integration phase).
- The 322-object inventory remains the master checklist; F/G objects are marked "replaced (architectural)"
  rather than "pending" so coverage tracking stays honest.
- Open (not blocking Phase 1): real TelegramType codes / MP ids (site protocol), TCP-to-printer connector
  spike, production ePlugin registration.
