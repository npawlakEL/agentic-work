# PandA → eController porting report

## 1. Topology and process boundaries

### Runtime topology

```text
TwinCAT PLC CP1/CP2
        │
        │ ADS/AMS: logical ADS port 851;
        │ AMS router commonly TCP 48898
        ▼
eHub / IntegrationHub process
  └─ Exol ADS connector (.eplugin)
        │
        │ eHub connector hooks / in-process eMessenger
        │ PLC telegram → MfcTransportOrder
        ▼
eController.MFC
  ├─ MfcTransportOrder persistence
  ├─ criteria/lane routing
  └─ PLC-specific response hook
        │
        ├─ ADS 1201 response → originating PLC
        └─ DTC messages → WMS/eManager/ELWS
```

The architecture log explicitly defines TwinCAT as the real-time conveyor controller exposing ADS symbols, eHub as the connector-host/I/O boundary, eController/MFC as the routing brain, and DTC as the WMS/ELWS/eManager enrichment bridge:  
`C:\Users\NoahPawlak\.copilot\session-state\be5b169f-b89b-4f3c-9dee-ff9a3abeaede\files\exol\.project\architecture-log\2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md:13-20`

The current topology is one eController controller (`CV`) with two PLC configurations/hooks (`01`, `02`) and one shared MFC schema/database, rather than separate MFCs per PLC:  
`...\2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md:102-114`

### Transports

| Boundary | Transport/process model |
|---|---|
| eHub ↔ TwinCAT | ADS symbols over AMS routing; PLC task uses logical ADS port 851 |
| eHub ↔ MFC | Connector hooks and in-process `eMessenger`; not RabbitMQ/MassTransit |
| eManager/WMS ↔ MFC | HTTP ingress to eManager, DTC telegram hooks/outboxes |
| MFC ↔ WMS/ELWS | DTC events through SQLite-backed durable hook outboxes, then HTTP from ELWS |
| eController ↔ Emulate3D | Custom TCP protocol |
| PandA ↔ label printer | New TCP client required; ZPL can use the existing TCP reliability pattern |

ADS addressing and transport details are documented here:  
`...\2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md:48-66`

DTC transport is in-process messaging plus SQLite-backed at-least-once outboxes. eManager listens on port 5001 and ELWS on port 37000 according to connector configuration:  
`...\2026-08-26-ehub-connectors-messaging-and-deploy.md:81-89`

### Implication for PandA

PandA should not become a direct PLC-to-printer monolith inside the ADS connector. The recommended split is:

1. ADS connector: PLC symbol access, telegram conversion, acknowledgement, PLC response serialization.
2. eController/MFC extension: print/apply/verify state machine and routing/action decisions.
3. Printer service/connector: TCP/ZPL delivery, retry, printer health, print-job correlation.
4. Optional DTC integration: carton metadata or WMS print content enrichment.

---

## 2. ADS services and telegram implementation

## 2.1 Existing ADS connector surface

`ExolAdsConnector` is the principal implementation:

```csharp
public sealed class ExolAdsConnector :
    BaseAdsConnector,
    ISetupDependenciesConnector,
    IConnectorConfigurator
```

Evidence:  
`...\src\eHub.PlugIn.Exol.Ads.Connector\ExolAdsConnector.cs:27-30`

It receives configuration through `IOptions<ExolAdsConnectorConfig>`, registers dependencies, configures connector defaults, establishes ADS tag registrations, and exposes inbound/outbound connector channels:  
`...\ExolAdsConnector.cs:79-178`

The connector registers the PLC ready/sending path with:

```csharp
.AddTagRaw(_config.ReadReadyTag, ReadReadyTagHandler)
```

and then calls:

```csharp
await RegisterTagsAsync(registrations, cancellationToken);
```

Evidence:  
`...\ExolAdsConnector.cs:270-294`

### Important PLC tags

The current protocol is symbol/tag based rather than raw-memory struct access. The connector reads and writes configured symbols including:

- ready/trigger tag
- sending/buffer tag
- acknowledgement tag
- response array, e.g. `vCtn.Assign[i]`
- heartbeat/host-ack tags
- optional merge flags

The topology document describes the handshake as `Ready`, `Ack`, `Sending`, `Pending`, and response-array symbols:  
`...\2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md:58-64`

## 2.2 Inbound handshake

The inbound sequence is:

1. PLC raises or changes `Ready`.
2. Connector applies an `Ack == false` generation guard.
3. Connector reads the `Sending` register.
4. It parses one or more concatenated telegrams.
5. It converts each telegram into an MFC transport order.
6. It writes the PLC acknowledgement only after processing the batch.
7. For heartbeat telegrams, it writes `HostAck` only after the buffer ACK succeeds.

Relevant implementation points:

- Ready callback and drain entry: `...\ExolAdsConnector.cs:386-426`
- Safety polling for missed notification edges: `...\ExolAdsConnector.cs:398-472`
- Buffer drain: `...\ExolAdsConnector.cs:561-690`
- Strict ACK-first heartbeat handling: `...\ExolAdsConnector.cs:684-747`

The architecture log records that the `Sending` value may contain concatenated telegrams such as:

```text
<201...><201...>
```

and is split and processed as a batch:  
`...\2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md:89-100`

### Porting warning: batch ACK semantics

ACK is batch-level. If a later telegram fails, earlier telegrams can replay unless the connector has per-message idempotency. The architecture log identifies this as a latent failure mode:  
`...\2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md:94-100`

For PandA, add an explicit correlation/sequence identifier to print-trigger and verify telegrams, or make MFC handlers idempotent by carton/job ID.

## 2.3 Reconnection, keepalive, online change, and errors

### ADS reconnect/error behavior

The connector validates the ADS endpoint before connecting and emits actionable errors for missing/invalid address or port:  
`...\ExolAdsConnector.cs:234-255`

It contains:

- safety polling for missed Ready notification edges,
- PLC heartbeat watchdog,
- recovery logging after heartbeat resumption,
- contained exceptions during watchdog iterations,
- explicit ADS write result checking.

Evidence:

- Ready safety poll: `...\ExolAdsConnector.cs:398-472`
- Heartbeat watchdog: `...\ExolAdsConnector.cs:512-556`
- ACK result/error handling: `...\ExolAdsConnector.cs:658-690`
- Heartbeat host acknowledgement: `...\ExolAdsConnector.cs:694-747`

The documented ADS failure mode is `ReadDeviceState … Timeout after 5000ms`, commonly caused by missing AMS routes or unreachable AMS TCP transport rather than application logic:  
`...\2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md:51-57`

### Online change

TwinCAT online change increments:

```text
TwinCAT_SystemInfoVarList._AppInfo.AppTimestamp
```

The vendor client can monitor this and recycle the connection. The repository documents an online-change/NRE issue and the `DetectOnlineChange` configuration tradeoff:  
`...\2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md:62-68`

For a new PandA telegram, do not assume a PLC online change is harmless. Test:

- symbol handle invalidation,
- notification re-registration,
- in-flight print trigger behavior,
- whether a PLC restart replays a pending scan,
- whether ACK state survives reconnect.

### AMS idle/NAT keepalive

The repository learning files specifically identify AMS idle connection drops and PLC bounce/offline-read error spam:

- `...\ .project\learnings\007-ads-ams-idle-connection-drop-nat-keepalive.md`
- `...\ .project\learnings\009-ads-plc-bounce-crash-and-offline-read-errspam.md`

Use the existing watchdog/heartbeat design rather than adding only a TCP-level keepalive.

## 2.4 Telegram definitions and exact extension points

The generic cyclic telegram converter is registered in:

```csharp
public static void Init(ILoggerFactory loggerFactory)
{
    ConverterTransportInterface =
        DefineConverterTransportInterface(
            new TelegramConverterBuilder<DefaultTelegramHeader>(loggerFactory))
        .Build();
}

public static TelegramConverterBuilder<DefaultTelegramHeader>
    DefineConverterTransportInterface(
        TelegramConverterBuilder<DefaultTelegramHeader> builder)
    => builder
       .Register<DefaultTransportTelegramDef>("TO", "MP", "SE", "SL")
       .Register<DefaultStatusTelegramDef>("ST", "SR");
```

Evidence:  
`...\src\Common\Telegram\Definitions.cs:7-17`

The cyclic converter serializes registered telegram properties in metadata order and expands array fields into named values:

```csharp
protected override string TelDataGet(DefaultTelegramHeader tel)
```

Evidence:  
`...\src\Common\Telegram\PacketConverterCyclicConnector.cs:6-44`

The current common telegram classes are:

- `TransportTelegramDef`
- `StatusTelegramDef`
- `DefaultTransportTelegramDef`
- `DefaultStatusTelegramDef`

Evidence:  
`...\src\Common\Telegram\TransportTelegramDef.cs:7`  
`...\src\Common\Telegram\StatusTelegramDef.cs:7`

### Recommended implementation for a new PandA telegram

For a new PLC telegram type:

1. Define a strongly typed telegram class/struct in `src/Common/Telegram` or the Exol-specific message project.
2. Derive from the framework header/base expected by the vendor converter.
3. Add all fields in wire order and match PLC field widths/string lengths exactly.
4. Register the telegram with the converter builder.
5. Add a converter from PLC telegram → MFC domain object.
6. Add an MFC handler/action or hook for the new domain event.
7. Add an outbound converter if PLC acknowledgement/result is required.
8. Register the telegram in the controller setup if it is handled by eController rather than only by eHub.
9. Add simulator serialization/deserialization and end-to-end tests.

The MFC fluent setup exposes the relevant extension points:

```csharp
.SetupTelegrams(ctx => { ... })
.SetupMfcActions(ctx => { ... })
.SetupConnectorHook(ctx => { ... })
```

Evidence:  
`...\src\eController.PlcSim\CustomSimControllerWork\CustomSimController.cs:45-95`

For PandA, likely domain types include:

```text
CartonInducted
PrintRequested
PrintAccepted / PrintFailed
ApplyCompleted
VerifyScanReceived
RoutingDecision
PrinterHealthChanged
```

Do not encode the entire PandA state machine as raw PLC telegram parsing logic. Keep wire conversion at the connector boundary.

---

## 3. End-to-end scan/routing flow

The existing 201 → MFC → 1201 path is the closest template.

### Step 1: PLC sends destination request

A PLC 201 telegram is placed into the configured sending symbol. The connector is notified through the Ready tag, reads the sending buffer, and parses the telegram.

Architecture evidence:  
`...\2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md:89-100`

### Step 2: Connector converts it to an MFC transport order

`ExolAdsTransportOrderConverter` converts the native request into an `MfcTransportOrder`.

Important mappings documented by the repository:

- 201 destination request:
  - `Mp = 1000 + (telegram.Device + MessagePointOffset)`
  - `TuId = Label1`
- 203:
  - `Mp = 2000 + ...`
- 204:
  - `Mp = 3000 + ...`
- 205 lane status:
  - `Mp = "205"` with sorter/lane data carried separately

Evidence:  
`...\2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md:118-126`

The connector stamps the originating PLC:

```csharp
transportOrder.ExtensionData[OriginPlcNameExtensionKey] = _config.PlcName;
```

Evidence:  
`...\src\eHub.PlugIn.Exol.Ads.Connector\ExolAdsConnector.cs:1147`

This stamp is essential for a shared controller with multiple PLCs.

### Step 3: MFC receives the order through a hook

`ConveyorController.RegisterConnectorHooks` registers one hook per configured PLC and one DTC hook:

```csharp
foreach (var plc in _mfcConfig.EffectivePlcs)
```

Evidence:  
`...\src\eController.MFC\Device\Conveyor\ConveyorController.cs:207-237`

The originating PLC is represented by a `HookKey`, normally the PLC name (`01`, `02`) unless overridden by configuration.

### Step 4: MFC selects the action chain

The routing action chain for a shipping scan is documented as:

```text
ResetDestReq
→ ShippingRouteHandler
→ ProcessCriteriaWithLaneStatus
→ SendRespToPlc
```

Evidence:  
`...\2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md:128-134`

`MfcAction.json` selects the action chain for the message point. Criteria and lane status determine `FinalDestMp`.

### Step 5: Response action selects the original PLC

`ProjectActions.SendRespToPlc` obtains the origin PLC and creates a hook key:

```csharp
var originPlcName = transportOrder.GetOriginPlcNameOrNull();
var hookKey = new HookKey(originPlcName);
```

Evidence:  
`...\src\eController.MFC\Actions\ProjectActions.cs:14-42`

The implementation intentionally refuses to guess a fallback PLC if `OriginPlcName` is missing. That behavior should be retained for PandA print/apply replies.

### Step 6: Connector converts the MFC order to PLC response

The outbound converter uses:

```text
native destination =
    FinalDestMp ?? DestMp
```

then removes the configured PLC offset and validates the configured place set. The response is serialized as the native PLC 1201/assignment structure.

Evidence:  
`...\2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md:124-126`

### PandA equivalent

A likely PandA flow is:

```text
PLC carton induct / scan
  → ADS connector
  → CartonInducted MFC event
  → create/find MfcTransportOrder by carton ID
  → print action
  → printer TCP/ZPL service
  → PrintAccepted or PrintFailed
  → PLC print result/permission response
  → apply trigger
  → verify scan
  → routing decision
  → PLC destination response
```

Use one persistent carton/job identity across all events. `TuId`/`HuName` is the existing identity pattern:  
`...\2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md:137-164`

---

## 4. eHub plugin model, packaging, DI, and deployment

## 4.1 Plugin interface/base pattern

The ADS plugin implements:

```csharp
BaseAdsConnector,
ISetupDependenciesConnector,
IConnectorConfigurator
```

Evidence:  
`...\src\eHub.PlugIn.Exol.Ads.Connector\ExolAdsConnector.cs:27-30`

The plugin exposes static dependency/configuration hooks:

```csharp
public static void SetupDependencies(ConnectorServicesBuilder builder)
public static void ConfigureDefaults(IConnectorConfigBuilder builder)
```

Evidence:  
`...\ExolAdsConnector.cs:112-178`

The intended PandA plugin should follow the same pattern:

```csharp
public sealed class PandAConnector :
    BaseConnector,
    ISetupDependenciesConnector,
    IConnectorConfigurator
```

or derive from the relevant TCP/ADS connector base depending on whether PandA owns PLC I/O or is split into an ADS plugin plus printer service.

## 4.2 Configuration binding

The ADS connector uses `IOptions<ExolAdsConnectorConfig>`. Configuration is environment-specific and loaded under the connector’s `Config` section. The topology log warns that nested connector configuration wins over flat fallback values:  
`...\2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md:70-78`

For PandA configuration, use explicit sections for:

```json
{
  "Connection": {
    "Address": "...",
    "Port": 851
  },
  "PlcName": "01",
  "Printer": {
    "Host": "...",
    "Port": 9100,
    "ConnectTimeoutMs": 3000,
    "ReconnectDelayMs": 1000
  },
  "Tags": {
    "Ready": "...",
    "Sending": "...",
    "Ack": "...",
    "Response": "..."
  }
}
```

Avoid relying on flat fallback properties for new deployments.

## 4.3 Versioning and `.eplugin`/NuGet packaging

The ADS project currently declares version `1.3.7`:  
`...\src\eHub.PlugIn.Exol.Ads.Connector\eHub.PlugIn.Exol.Ads.Connector.csproj:8`

The deployed runtime keeps connector packages under:

```text
C:\Runtime\exol\plugins\eHub_<ENV>\Pkg\
```

The host loads the highest version present and extracts it into disposable connector content. Deployment is additive: copy a higher-version package and clear the extracted content; old packages remain rollback points.

Evidence:  
`...\2026-08-26-ehub-connectors-messaging-and-deploy.md:93-99`

### Dependency/versioning trap

The DTC package must be directly pinned by consuming connectors. A transitive `eController.Mfc.Library` dependency can otherwise bring in an older DTC message assembly. `MfcTransportOrderCompletedV1` exists only in DTC `0.0.4`.

Evidence:  
`...\2026-08-26-ehub-connectors-messaging-and-deploy.md:100-108`

For PandA:

- directly reference all required DTC contracts;
- verify the bundled assembly version in the package;
- avoid depending on transitive versions;
- increment the plugin version for every deployable package;
- keep old packages for rollback;
- clear extracted content after package replacement.

## 4.4 DI lifecycle

The MFC simulator demonstrates the expected registration pattern:

```csharp
.SetupDependencies(ctx =>
{
    ctx.AddSingleton(...);
    ctx.UseProjectToolboxTcp();
    ctx.AddControllerHostedService<TCPConnector>();
})
```

Evidence:  
`...\src\eController.PlcSim\CustomSimControllerWork\CustomSimController.cs:90-111`

Printer delivery should likely be a singleton hosted service or singleton connection manager with scoped job handlers layered over it. Do not open a new TCP connection for every ZPL job unless the printer protocol and throughput require it.

---

## 5. Existing TCP client patterns for ZPL printers

The best reusable pattern is:

```text
src\eController.Emulate3D.Adapter\Transport\TcpWriter.cs
```

The class is an async, generation-aware TCP writer:

```csharp
internal sealed class TcpWriter : IAsyncDisposable
```

Evidence:  
`...\src\eController.Emulate3D.Adapter\Transport\TcpWriter.cs:22-47`

Key design features:

- serialized writes with `SemaphoreSlim`;
- cancellation/lifetime control;
- connection-generation tracking;
- stale-generation protection;
- endpoint factory;
- explicit ready state;
- `WaitReadyAsync`;
- reconnect delay validation;
- connection-state event handling;
- socket-loop ownership.

Evidence:

- fields and state: `...\TcpWriter.cs:22-68`
- constructors: `...\TcpWriter.cs:73-116`
- readiness: `...\TcpWriter.cs:121-136`
- startup/endpoint creation: `...\TcpWriter.cs:173-222`

### Adaptation for ZPL

Create a `PrinterTcpWriter` based on this pattern:

```csharp
public interface ILabelPrinterClient
{
    Task<PrintResult> PrintAsync(
        string printerId,
        ReadOnlyMemory<byte> zpl,
        CancellationToken cancellationToken);
}
```

Recommended behavior:

1. Maintain one connection generation per printer.
2. Serialize writes per printer.
3. Wait for connection readiness.
4. Treat disconnect during write as a failed job.
5. Reconnect with bounded delay.
6. Do not silently resend unless the job is explicitly idempotent.
7. Include a PandA print-job ID in ZPL metadata/barcode where possible.
8. Track `Submitted`, `Accepted`, `TimedOut`, `Failed`, and `UnknownAfterDisconnect`.

The existing repository learning also warns that older toolbox TCP behavior did not reliably retry/reconnect:

`...\ .project\learnings\003-toolbox-tcp-no-retry-reconnect.md`

Therefore, use `TcpWriter` as the stronger reference rather than assuming `UseProjectToolboxTcp()` provides printer-grade delivery semantics.

### Printer protocol caveat

Typical Zebra raw printing uses TCP port 9100 and no application response. Consequently:

- a successful socket write proves only that bytes were accepted by the TCP stack;
- it does not prove that the printer printed or applied the label;
- print verification must come from the PLC scanner or printer status channel;
- duplicate print protection is required after reconnect ambiguity.

---

## 6. Multi-controller and multi-PLC handling

The current design supports multiple PLCs under one controller:

```csharp
public List<MfcPlcConfig> Plcs { get; set; } = new();
public IEnumerable<MfcPlcConfig> EffectivePlcs => ...
```

Evidence:  
`...\src\eController.MFC\Config\MfcConfig.cs:10-10,51-67`

Each PLC has:

- a PLC name;
- a hook key;
- an ADS connector instance/configuration;
- a message-point offset;
- its own symbol/tag configuration.

Inbound orders are stamped with `OriginPlcName`:  
`...\src\eHub.PlugIn.Exol.Ads.Connector\ExolAdsConnector.cs:1147`

Outbound responses use that stamp to route to the matching hook:  
`...\src\eController.MFC\Actions\ProjectActions.cs:14-42`

The current topology uses offsets CP1=100 and CP2=200 to avoid message-point collisions, applying the offset consistently to device, sorter, and lane:  
`...\2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md:106-126`

### PandA recommendation

Represent each line/controller as a configured logical origin:

```text
PandA-01
PandA-02
```

Carry it through every event:

```csharp
public sealed record PandAEvent(
    string OriginController,
    string CartonId,
    string CorrelationId,
    DateTimeOffset Timestamp);
```

Printer selection should be based on the originating line/controller, not on a global current printer:

```text
Origin PLC → PandA lane config → printer ID → print job
```

Never infer the origin from carton ID or destination. Preserve the existing “missing origin means no response” safety rule.

---

## 7. Simulation and testing

## 7.1 `eController.PlcSim`

`CustomSimController` is a fluent MFC simulator that configures:

```csharp
.SetupSteps(...)
.SetupTelegrams(...)
.SetupMfcActions(...)
.SetupConnectorHook(...)
.AddControllerHostedService<TCPConnector>()
.UseProjectToolboxTcp()
```

Evidence:  
`...\src\eController.PlcSim\CustomSimControllerWork\CustomSimController.cs:45-111`

It also supports:

- custom transport-order handlers;
- custom DTC handlers;
- PandA-specific action/behavior files;
- Emulate3D message builders;
- in-memory eManager test data;
- simulated controller hooks.

Relevant files include:

```text
src\eController.PlcSim\CustomSimControllerWork_PandA\PandAActions.cs
src\eController.PlcSim\CustomSimControllerWork_PandA\PandABehavior.cs
src\eController.PlcSim\CustomSimControllerWork_PandA\PandASteps.cs
src\eController.PlcSim\CustomSimControllerWork_PandA\LabelExtension.cs
src\eController.PlcSim\CustomSimControllerWork\CustomSimTelegramHandler.cs
src\eController.PlcSim\CustomSimControllerWork\Emulate3DMessageBuilder.cs
```

This is directly relevant to PandA because the repository already contains a PandA simulation slice.

## 7.2 Emulate3D adapter

The eController adapter has:

```text
Emulate3DInboundPump
Emulate3DOutboundSink
TcpWriter
Emulate3DProtocol
Emulate3DTranslator
```

It provides a TCP simulation boundary and a robust reconnecting writer. Use it to test PandA event sequencing without a real TwinCAT PLC.

## 7.3 Suggested PandA test matrix

### Unit tests

- PLC telegram binary/string parsing.
- 201/203/205 or new PandA telegram field mapping.
- origin-controller stamping.
- print-job ID generation.
- ZPL escaping and payload construction.
- printer routing by controller/lane.
- duplicate scan/trigger idempotency.
- online-change/reconnect behavior.
- response conversion and native PLC offsets.

### Integration tests

```text
simulated PLC event
  → ADS connector/parser
  → MFC transport order
  → PandA action chain
  → fake printer TCP server
  → print result
  → PLC response
```

### Failure tests

- PLC sends Ready but Sending is empty.
- notification edge is missed.
- ACK write fails.
- a later telegram in a batch fails.
- PLC reconnects during processing.
- printer disconnects after partial ZPL write.
- printer connection is lost after successful TCP send but before application acknowledgement.
- duplicate print trigger arrives.
- verify scan does not match printed carton.
- two PLCs submit identical carton IDs.
- DTC enrichment arrives before and after the PLC scan.
- MFC restart occurs with a pending print job.

### End-to-end acceptance criterion

For each carton:

```text
one stable carton/job identity
one origin PLC
one print decision
one printer target
one apply trigger
one verify outcome
one routing response
```

The existing system’s DTC/ADS join uses `TuId == HuName`/LPN and merges host enrichment with the physical scan flow:  
`...\2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md:137-164`

That same identity discipline should be used for PandA print/apply/verify correlation.

---

# Recommended porting architecture

## Minimal viable decomposition

```text
PandA.Ads.Connector
  - derives from ADS connector base
  - owns PLC tags and handshake
  - parses PandA telegrams
  - stamps OriginPlcName
  - converts to domain events/MfcTransportOrder
  - serializes PLC acknowledgements/results

PandA.Mfc
  - PandA actions/steps/handlers
  - print/apply/verify state machine
  - printer selection
  - routing decision
  - idempotency and timeout policy

PandA.Printer
  - PrinterTcpWriter per printer
  - ZPL generation
  - reconnect/readiness
  - print-job state and telemetry

PandA.Sim
  - fake PLC event generator
  - fake printer TCP listener
  - Emulate3D integration
  - end-to-end flow tests
```

## Highest-risk areas

1. PLC handshake edge semantics and batch-level ACK.
2. Online change and symbol-handle invalidation.
3. Duplicate print after reconnect ambiguity.
4. Correct origin-PLC response routing.
5. Printer TCP write success being mistaken for physical print success.
6. DTC contract version pinning.
7. Shared-controller identity and concurrent carton creation.
8. Whether PandA events should be represented as standard MFC transport orders or as a separate custom event/telegram pipeline.

The safest first implementation is to reuse the existing ADS connector handshake, `OriginPlcName` routing, MFC hook model, `TcpWriter` reliability design, and `PlcSim`/Emulate3D test seams, while keeping ZPL delivery as a separate idempotent service rather than embedding raw printer I/O into the PLC connector.
