# Decision 018 — External connections bind to eHub connectors; adapter split; ZebraConnector gap

**Status:** Accepted (architecture); adapter is deferred (Core/Sim build first, neutral)
**Date:** 2026-08-12
**Basis:** Senior research pass over `Element-Logic/ehub` (`f0f9978`), `ehub-plugin-bluepaw`
(`7b9efa6`), `ehub-plugin-messagebus` (`501feb4`), `eController-Projects/BG-Softbank` (`8c7e7c3`).
Supersedes the open "PandA owns its own sockets" question and refines decisions 008/015.

## The substrate (what we learned)

- **eHub is a host process** (standalone exe / Docker image), not a library. A project declares
  it in `eproject.json` and loads **plugins** (`.eplugin` zips) into it. BG-Softbank is the
  reference pattern: separate eHub processes per "Environment" (ATI = conveyor side, HTI = host
  side), MFC behaviors in a separate `MfcFluentControllerBase` project, connectors wired by
  per-connector JSON config.
- **Connector model:** `IConnector` (long-running `RunAsync`) and `IPacketTransfer` (SQLite
  inbox/outbox of `PacketData` on named `Channel`s). eHub bridges packets to the MFC via
  `IEhubHookFactory` / `DefaultEhubHook<T>` → `MfcTransportOrder`. DI via
  `ISetupDependenciesConnector`; config via `[ScriptOptions]` options + `IConnectorConfigurator`.
- **BluePaw plugin already IS the ADS bridge to Beckhoff.** The PLC writes CSV frames
  (`<281,...>`) into an ADS string tag (`vMsg.Msgs_eHub.Sending`) and sets a Ready flag; BluePaw
  reads via ADS notification, parses to `BluePawMessageBase` subclasses, converts to
  `MfcTransportOrder` (`IBluePawTransportOrderConverter<T>`), and acks. Outbound: an
  `IBluePawPLCResponseConverter` maps a TO to a blit-able unmanaged struct written via
  `WriteTagAsync` (ADS). Custom frames declared with `[MessageType("281","INDUCT_SCAN")]` +
  `[TelegramField(Order,Datatype)]`. Transport modes: ADS-over-MQTT (reliable), native ADS
  (needs TwinCAT router installed), embedded router (**partially built — server components
  commented out**).
- **messagebus plugin = NATS/ELWS WMS integration** (MasterData/ASN/Orders/Inventory). Not a
  general bus and not suitable for printer TCP.

## Decision — channel → connector mapping

| PandA channel | eHub binding | Build status |
|---|---|---|
| **PLC inbound** (induct 281, print 282, printer-status 283, zone 284, verify 286) | custom `BluePawMessageBase` subclasses + `IBluePawTransportOrderConverter<T>` on `BluePawConnector` (ADS) | adapter, deferred |
| **PLC outbound** (fire-point bundle, divert dest, line stop `RemoteStop` / slow `SlowFlag`) | `IBluePawPLCResponseConverter` → blit struct → `WriteTagAsync` (ADS) | adapter, deferred |
| **Zebra printer** (ZPL label send + `~HS` status) | **NEW `IPacketTransfer` connector (`ZebraConnector`) — no eHub precedent exists** | adapter, deferred, **new build** |
| **WMS / DTC** | `DefaultHttpConnector` (REST) or `ehub-plugin-messagebus` (NATS/ELWS); see decision-015 | adapter, deferred |

## Decision — adapter project split (deferred; replaces the single "PandA.EController" notion)

- **`PandA.eHub`** — eHub plugin (loaded into the ATI eHub process): PandA's custom BluePaw
  frame messages (281/282/283/284/286), `PandAPlcResponseConverter`, and the new `ZebraConnector`.
- **`PandA.MFC`** — econtroller MFC behavior (`MfcFluentControllerBase`) that hosts `PandA.Core`
  and does WMS/DTC via the HTTP connector / `IScopedMessenger`.
- **`PandA.Core` / `PandA.Sim` stay neutral and build FIRST with zero eHub/econtroller
  dependency** (unchanged). Research CONFIRMS the port design: Core receives a **neutral parsed
  message** (adapter parses BluePaw CSV → calls Core), and `ILinePlcGateway` emits **neutral
  command objects** the adapter maps to BluePaw structs/ADS writes. `PrintEngineStatusParser`
  (the `~HS` parser) stays in Core; the ZebraConnector just feeds it the raw string.

## Deferred site-coordination gaps (bookmarked; block ONLY the adapter, not Core)

- **G-ADS1** ADS transport mode (MQTT vs native vs embedded-router). Embedded router is
  half-built; MQTT is the reliable path. Needs site infra confirmation.
- **G-ZEB1** No eHub Zebra/raw-TCP connector — `ZebraConnector` is net-new (backlog item added).
  Alternative: Link-OS HTTP API via `DefaultHttpConnector` if the printers support it.
- **G-TAG1** Actual TwinCAT tag paths + CSV type codes for frames 281–286 (does the PLC use the
  BluePaw `vMsg.Msgs_eHub.*` buffer convention? are the type codes literally `281`…?).
- **G-STRUCT1** Blit-able unmanaged struct layouts for outbound commands (fire point, divert
  dest, line stop/slow) must match the TwinCAT DUT definitions exactly.
- **G-NUGET1** Access to the Element-Logic private NuGet feed (`eHub.PlugIn`,
  `eHub.Hook.PacketTransfer`, `eHub.PlugIn.BluePaw.*`); and whether `IBluePawPLCResponseConverter`
  is overridable from a consumer plugin or requires a `BluePawConnector` subclass.
- **G-WMS1** WMS/DTC transport (REST vs NATS/ELWS) — pairs with decision-015's open site question.

## References
- Research report (session), citing the four repos above. decision-008 (divert→CBS),
  decision-015 (verify→WMS), decision-014 (status/pause→offline), F13/LINECTRL/F15 ownership-map rows.
