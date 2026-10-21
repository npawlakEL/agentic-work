# Exol eController MFC Framework Analysis and PandA Porting Plan

> Repository analyzed: `C:\Users\NoahPawlak\.copilot\session-state\be5b169f-b89b-4f3c-9dee-ff9a3abeaede\files\exol`  
> The checked-out source contains the Exol MFC and PlcSim implementations, while much of the eController host itself is supplied by NuGet packages. Therefore, the exact host `Program.cs`/builder composition is partly external to this repository.

---

## 1. Executive summary

PandA should be ported as an **MFC behavior/action extension**, not as an independent host and not as an eHub connector.

The Exol pattern is:

1. A host creates an `MfcFluentControllerBase` implementation.
2. The controller overrides `Setup(ControllerBuilder builder)`.
3. Fluent calls add:
   - transport-order processing,
   - Criteria Based Sorting,
   - Weight handling,
   - persistent tables,
   - connector hooks,
   - telegram inbox/outboxes,
   - MFC actions,
   - DI services,
   - flow steps,
   - area-status management.
4. PLC telegrams arrive through connector hooks keyed by PLC name.
5. MFC flow steps dispatch actions from registered `IMfcAction` classes.
6. Actions operate on `MfcTransportOrder`, usually by:
   - reading/writing `SortCriteriaExtension`,
   - reading persisted configuration through `ITableProvider`,
   - reading `SystemSnapshot`,
   - updating destinations/status,
   - sending responses through `ITelegramOutbox`.
7. The Criteria Based Sorting package supplies the generic criteria-routing framework; Exol adds site-specific actions, handlers, configuration, lane/location tables, DTC enrichment, recirculation behavior, and routing overrides.
8. The MFC executable/host and ADS/eHub connectors are separate concerns. Exol’s architecture explicitly avoids starting ADS inside the MFC controller.
9. A PandA plugin should follow the same controller extension model:
   - reference `eController.Mfc.Library`,
   - implement `IMfcAction`,
   - register actions with `SetupMfcActions`,
   - register required handlers/services with `SetupDependencies`,
   - add flow steps with `SetupSteps`,
   - use hooks for PLC ingress/egress,
   - package through the project’s ePlugin/MSBuild mechanism only if the target deployment requires a plugin package.

---

# 2. Host composition

## 2.1 MFC controller composition

The concrete Exol controller is:

```csharp
public sealed class ConveyorController : MfcFluentControllerBase
```

`C:\Users\NoahPawlak\.copilot\session-state\be5b169f-b89b-4f3c-9dee-ff9a3abeaede\files\exol\src\eController.MFC\Device\Conveyor\ConveyorController.cs:34`

It receives host-provided services and options:

```csharp
public ConveyorController(
    IOptions<MfcControllerConfig> mfcConfig,
    ILoggerFactory loggerFactory,
    IServiceProvider serviceProvider,
    IScopedMessenger messenger)
    : base(serviceProvider)
```

`ConveyorController.cs:46-61`

The controller overrides:

```csharp
protected override void Setup(ControllerBuilder builder)
```

`ConveyorController.cs:64`

The MFC library owns the base controller lifecycle and invokes this setup method during controller construction/finalization. The Exol source does not contain a local `Program.cs` for the MFC application; the application host and controller discovery are supplied through the eController host/runtime packages.

## 2.2 Fluent Builder API used by Exol

The production controller uses the following fluent operations.

### Controller features

```csharp
builder
    .AddTransportOrderSetup()
    .AddCriteriaBasedSorting()
    .AddWeightHandling()
    .AddDbPersistantTable()
    .AddAreaStatusManager();
```

`ConveyorController.cs:106-118`, `ConveyorController.cs:188`

Purpose:

| Builder call | Purpose |
|---|---|
| `AddTransportOrderSetup()` | Adds standard transport-order setup and lifecycle behavior. |
| `AddCriteriaBasedSorting()` | Installs generic Criteria Based Sorting behavior/actions/configuration. |
| `AddWeightHandling()` | Installs generic weight-processing behavior. |
| `AddDbPersistantTable()` | Enables database-backed `ITableProvider`/persistent tables. |
| `AddAreaStatusManager()` | Installs area/availability state management. |
| `AddVisuTracking()` | Optional visualization synchronization; disabled in production because of full-table performance impact. |

The production code intentionally disables Visu tracking:

```csharp
//.AddVisuTracking()
```

`ConveyorController.cs:108-112`

The comment states that Visu synchronization loaded the entire `MfcTransportOrder` table on every flow tick and produced unacceptable latency.

## 2.3 Hook-only setup path

Exol has a second setup path used when only connector hooks must be registered:

```csharp
builder
    .AddInMemoryMfcContext()
    .SetupConnectorHook(RegisterConnectorHooks)
    .SetupDependencies(...)
    .SetupTelegrams(...);
```

`ConveyorController.cs:71-101`

This path is controlled by `_hookRegistrationOnly`. It does not install the full flow-engine pipeline. Its purpose is to initialize connector hooks with an in-memory MFC context while avoiding the normal full MFC setup.

This distinction matters for PandA testing and deployment:

- **Full controller setup** executes the MFC flow engine and actions.
- **Hook-only setup** is appropriate for connector registration or lightweight hook tests.
- It is not sufficient for full PandA behavior execution.

## 2.4 Telegram configuration

Production setup:

```csharp
.SetupTelegrams(ctx =>
{
    ctx.SetInboxStorage<MfcDbTelegramInbox, long>();
    ctx.SetOutboxStorage<JsonOutbox<DtcTelegram, long>, DtcTelegram, long>();
    ctx.SetOutboxStorage<MfcDbStringTelegramOutbox, string, long>();
})
```

`ConveyorController.cs:121-130`

The configured stores are:

- durable database-backed MFC telegram inbox,
- JSON DTC telegram outbox,
- database-backed string telegram outbox.

The commented code also shows that the library supports transport-order outbox registration:

```csharp
//ctx.SetOutboxStorage<JsonOutbox<MfcTransportOrder, long>, MfcTransportOrder, long>();
```

`ConveyorController.cs:124-128`

PandA should use the same outbox abstraction rather than directly invoking PLC transport code.

## 2.5 MFC action registration

Exol registers the dispatcher and action containers:

```csharp
.SetupMfcActions(ctx =>
{
    ctx.AddDispatcher<ExolMfcDispatcher>();
    ctx.AddActions<DefaultActions>();
    ctx.AddActions<ProjectActions>();
    ctx.AddActions<CriteriaActionsCustom>();
})
```

`ConveyorController.cs:133-143`

The registration model is class-level:

- one action class can contain multiple public action methods;
- the action class implements `IMfcAction`;
- action methods are discovered by the MFC action infrastructure;
- the flow/action dispatcher invokes matching methods based on action names and parameter resolution.

PandA should use the same pattern:

```csharp
public sealed class PandAActions : IMfcAction
{
    public async Task ProcessPandA(
        MfcTransportOrder transportOrder,
        SystemSnapshot snapshot,
        ITableProvider tableProvider,
        CancellationToken cancellationToken = default)
    {
        // PandA behavior
    }
}
```

Then register:

```csharp
ctx.AddActions<PandAActions>();
```

## 2.6 Flow steps

Exol’s full flow is:

```csharp
.SetupSteps(ctx =>
{
    ctx.TelegramInAll();

    ctx.Step(CriteriaActionsCustom.GenerateTableForLaneStatus);
    ctx.Step(CriteriaActionsCustom.GenerateTableForLocationStatus);
    ctx.Step(ElevatorSettingRelay.Relay);

    ctx.StepDefaultProcessActions();

    ctx.TelegramOutAll();

    ctx.Step(RetentionPurgeStep.Purge);
})
```

`ConveyorController.cs:173-186`

The broad lifecycle is:

1. `TelegramInAll()` — process inbound telegrams.
2. Generate/refresh site-specific persistent tables.
3. Relay special commands such as elevator settings.
4. `StepDefaultProcessActions()` — execute registered MFC actions.
5. `TelegramOutAll()` — emit queued outbound telegrams.
6. Retention purge — remove old finished orders.

PandA behavior should normally execute inside `StepDefaultProcessActions()` through action registration, unless it needs a dedicated pre/post-flow step.

## 2.7 Connector hooks and discovery

The controller registers one ADS-style hook per configured PLC:

```csharp
foreach (var plc in _mfcConfig.EffectivePlcs)
{
    ctx.Hook<MfcDbContext, MfcTransportOrder, long>(
        CreateAdsHookKey(plc))
        .SetHandler<CustomTransportOrderTelegramIn, MfcTransportOrder>();
}
```

`ConveyorController.cs:205-214`

It also registers DTC hooks:

```csharp
ctx.Hook<MfcDbContext, DtcTelegram, long>(_hookKeyEmanager)
    .SetHandler<DtcMessageHandlerCustom>();

ctx.Hook<MfcDbContext, DtcTelegram, long>(_hookKeyWms)
    .SetHandler<DtcMessageHandlerCustom>();
```

`ConveyorController.cs:216-221`

The important architecture is:

- connectors publish messages to named hooks;
- hooks resolve a handler;
- handlers create/update MFC transport orders;
- the flow engine subsequently invokes actions.

The architecture logs explicitly state that the ADS connector is a separate eHub connector plugin, not an in-process MFC hosted service:

- `C:\Users\NoahPawlak\.copilot\session-state\be5b169f-b89b-4f3c-9dee-ff9a3abeaede\files\exol\.project\architecture-log\2026\07-28-phase3-ads-spec.md:8-29`
- `07-28-phase3-ads-spec.md:145`
- `07-28-phase3-ads-spec.md:597`

The connector owns the ADS session and sends parsed telegrams into MFC hooks. The MFC controller should not start ADS itself.

---

# 3. MfcActions

## 3.1 Base interface

Exol custom action classes implement:

```csharp
public sealed class ProjectActions(MfcControllerConfig mfcConfig) : IMfcAction
```

`src\eController.MFC\Actions\ProjectActions.cs:12`

```csharp
public sealed class CriteriaActionsCustom(...) : IMfcAction
```

`src\eController.MFC\Actions\CriteriaActionsCustom.cs:21`

The interface is provided by `eController.Mfc.Library.Common.Actions` and is imported by `ConveyorController.cs:7`.

Actions are not derived from a project-specific base class. The primary contract is `IMfcAction`.

## 3.2 Action method dependency injection

Methods declare their dependencies as parameters. The MFC action service resolves:

- the current transport order,
- snapshots,
- persistent table access,
- config,
- logging,
- outboxes,
- services registered in `SetupDependencies`,
- special values marked with `[ServiceValue]`.

Example:

```csharp
public async ValueTask<bool> SendRespToPlc(
    MfcTransportOrder transportOrder,
    ITelegramOutbox<MfcTransportOrder> outbox,
    ILogger<ProjectActions> logger,
    [ServiceValue] string? responseType = null)
```

`ProjectActions.cs:14-17`

The `[ServiceValue]` parameter is a service/action argument supplied by the action invocation metadata rather than being read from the transport order.

Another example:

```csharp
public async Task ProcessCriteriaWithLaneStatus(
    MfcTransportOrder trans,
    SystemSnapshot snapshot,
    MfcConfigBase config,
    ITableProvider tableProvider,
    ...)
```

`CriteriaActionsCustom.cs:161`

This is the intended PandA seam: action methods can remain small and testable while dependencies are explicit.

## 3.3 Lifecycle and triggering

Actions are triggered from the MFC flow engine, especially through:

```csharp
ctx.StepDefaultProcessActions();
```

`ConveyorController.cs:181`

The triggering source is generally a processed transport-order telegram/state transition. The incoming telegram is first handled by a connector hook and handler; the flow engine then selects actions based on the current transport order and configured action metadata.

The action dispatcher is customized:

```csharp
public sealed class ExolMfcDispatcher : ...
```

`src\eController.MFC\Dispatch\ExolMfcDispatcher.cs:37-69`

The custom dispatcher suppresses repeated benign warnings for recognized DTC telegram types while retaining normal action dispatch behavior.

Actions may also be invoked as explicit flow steps. Exol uses static action methods as steps:

```csharp
ctx.Step(CriteriaActionsCustom.GenerateTableForLaneStatus);
ctx.Step(CriteriaActionsCustom.GenerateTableForLocationStatus);
```

`ConveyorController.cs:176-177`

## 3.4 Custom action inventory

### `ProjectActions`

File:

`C:\Users\NoahPawlak\.copilot\session-state\be5b169f-b89b-4f3c-9dee-ff9a3abeaede\files\exol\src\eController.MFC\Actions\ProjectActions.cs`

#### `SendRespToPlc`

```csharp
public async ValueTask<bool> SendRespToPlc(
    MfcTransportOrder transportOrder,
    ITelegramOutbox<MfcTransportOrder> outbox,
    ILogger<ProjectActions> logger,
    [ServiceValue] string? responseType = null)
```

`ProjectActions.cs:14`

Purpose:

- prevents duplicate responses;
- obtains the originating PLC from the transport order;
- changes telegram type/status;
- enqueues the response to a PLC-specific hook;
- restores the original state if enqueueing fails so the response remains retryable.

#### `ResetDestReq`

```csharp
public bool ResetDestReq(ITransportOrder transportOrder)
```

`ProjectActions.cs:60`

Purpose: clears `FinalDestMp`.

### `CriteriaActionsCustom`

File:

`C:\Users\NoahPawlak\.copilot\session-state\be5b169f-b89b-4f3c-9dee-ff9a3abeaede\files\exol\src\eController.MFC\Actions\CriteriaActionsCustom.cs`

| Method | Purpose |
|---|---|
| `ProcessCriteriaWithLaneStatus` | Core criteria processing that considers lane status and routing availability. `CriteriaActionsCustom.cs:161` |
| `RouteToteHandler` | Routes tote/order criteria through configured destinations. `:311` |
| `RouteAutoCmcHandler` | Handles AutoCMC routing and lane selection. `:441` |
| `ShippingRouteHandler` | Routes shipping orders/cartons. `:589` |
| `ShippingAutoRouteDatalessCarton` | Handles dataless shipping cartons and selects enabled shipping lanes. `:706` |
| `ResetWeightVerdict` | Removes stale per-scan weight verdict before reprocessing. `:977` |
| `LogWeightVerdict` | Logs weight-derived routing state. `:1002` |
| `GenerateTableForLaneStatus` | Initializes/publishes persistent lane-status rows. `:1026` |
| `GenerateTableForLocationStatus` | Initializes/publishes persistent location-status rows. `:1089` |
| `InsertLaneStatus` | Inserts or updates a lane-status row for a PLC/lane. `:1155` |
| `LaneStatusHandler` | Applies lane status to routing decisions. `:1303` |
| `ScrubToteState` | Removes stale dynamic extensions and previous-hop state. `:1338` |
| `EmptiesReturnHandler` | Handles empty-tote return routing. `:1391` |
| `PickRejectAcceptanceHandler` | Handles pick acceptance/rejection criteria. `:1426` |
| `ProcessLaneStatusCriterion` | Processes the lane-status criterion and available lanes. `:1493` |

Private methods further implement:

- criteria partitioning,
- route selection,
- round-robin selection,
- load balancing,
- rejection diagnosis,
- destination selection,
- routing outcome cleanup,
- criteria intersection,
- persisted criteria lookup.

## 3.5 Transport-order access

Actions receive `MfcTransportOrder` directly. They manipulate fields and extensions through library APIs:

```csharp
trans.TryGetExtension<SortCriteriaExtension>(out var ext)
```

`CriteriaActionsCustom.cs:121`, `:196`, `:276`

```csharp
trans.ApplyExtension<SortCriteriaExtension>(m =>
{
    m.SortCriterias.RemoveAll(...);
});
```

`CriteriaActionsCustom.cs:196-209`

```csharp
trans.RemoveExtension<SortCriteriaExtension>();
```

`CriteriaActionsCustom.cs:1347`

They also use:

- `FinalDestMp`,
- `DestMp`,
- `Mp`,
- `TuId`,
- `TuStatus`,
- `TelegramType`,
- origin-PLC metadata,
- `SystemSnapshot`,
- `MfcConfigBase`,
- `ITableProvider`.

---

# 4. Criteria Based Sorting plugin

## 4.1 Package identity and version

Central package management defines:

```xml
<PackageVersion
    Include="eController.Behaviors.CriteriaBasedSorting"
    Version="0.2.0" />
```

`C:\Users\NoahPawlak\.copilot\session-state\be5b169f-b89b-4f3c-9dee-ff9a3abeaede\files\exol\Directory.Packages.props:4`

The MFC project references it:

```xml
<PackageReference Include="eController.Behaviors.CriteriaBasedSorting" />
```

`src\eController.MFC\eController.MFC.csproj:19`

PlcSim also references it:

```xml
<PackageReference Include="eController.Behaviors.CriteriaBasedSorting" />
```

`src\eController.PlcSim\eController.PlcSim.csproj:24`

The Emulate3D adapter also references it:

`src\eController.Emulate3D.Adapter\eController.Emulate3D.Adapter.csproj:18`

## 4.2 What the package supplies

The package supplies generic types and behavior infrastructure used by Exol:

```csharp
using eController.Behaviors.CriteriaBasedSorting;
using eController.Behaviors.CriteriaBasedSorting.Actions;
using eController.Behaviors.CriteriaBasedSorting.Config;
```

`CriteriaActionsCustom.cs:3-5`

Observed package-provided concepts include:

- `SortCriteriaExtension`,
- `SortCriteria`,
- generic Criteria Based Sorting actions,
- `CriteriaData`,
- criteria configuration tables,
- criteria behavior associated with places,
- generic routing actions,
- extension registration/serialization,
- criteria configuration schemas.

Exol uses these package types as an extension surface rather than replacing the package.

## 4.3 Criteria extension schema

The transport order carries a `SortCriteriaExtension` containing a list of criteria:

```csharp
SortCriteriaExtension.SortCriterias
```

Exol accesses it as:

```csharp
if (!trans.TryGetExtension<SortCriteriaExtension>(out var ext))
{
    ...
}
```

`CriteriaActionsCustom.cs:121`, `:276`

Criteria are represented as type/value pairs:

```csharp
new SortCriteria
{
    Type = "...",
    Value = "..."
}
```

`CriteriaActionsCustom.cs:204-205`, `:1417`

DTC data is expected in a shape such as:

```json
{
  "SortCriteriaExtension": {
    "SortCriterias": [
      {
        "Type": "Store",
        "Value": "STR01"
      }
    ]
  }
}
```

`src\eController.MFC\Handlers\DtcMessageHandlerCustom.cs:400-430`

The handler also accepts a bare extension shape:

```json
{
  "SortCriterias": [
    {
      "Type": "Store",
      "Value": "STR01"
    }
  ]
}
```

`DtcMessageHandlerCustom.cs:415-430`

## 4.4 Exol’s criteria partitioning

Exol explicitly partitions criteria into three domains.

### Order-shape criteria

```csharp
internal static readonly HashSet<string> OrderShapeCriteriaTypes =
{
    "BatchType",
    "OrderType",
    "OrderTypeID",
    "Store",
    "ToteType",
    "VAS",
};
```

`CriteriaActionsCustom.cs:77-86`

These classify the order for routing.

### Per-scan derived criteria

```csharp
internal static readonly HashSet<string> PerScanDerivedCriteriaTypes =
{
    "Weight",
};
```

`CriteriaActionsCustom.cs:93-98`

Weight is derived again on every scan and must not be treated as durable host provenance.

### Non-routing markers

```csharp
internal static readonly HashSet<string> NonRoutingMarkerCriteriaTypes =
{
    "DestinationLane",
    "Exception",
    "EmptyToteReturn",
    "MultiToteOrder",
    "PickAcceptance",
    "PickDesc",
    QcCriterionType,
};
```

`CriteriaActionsCustom.cs:105-117`

These markers should not establish whether a carton contains enough order-shape data to route.

This classification is a significant PandA design lesson: PandA should distinguish:

- host-owned input data,
- per-scan derived state,
- controller-owned routing outcomes,
- diagnostic/status markers.

Do not indiscriminately clear all criteria on every scan.

## 4.5 Exol customization of generic CBS

Exol customizes Criteria Based Sorting through:

1. `CriteriaActionsCustom`.
2. Custom criteria handlers.
3. DTC enrichment.
4. Criteria-data table initialization.
5. Lane-status and location-status tables.
6. Custom destination selection.
7. Custom rejection semantics.
8. Recirculation persistence and reset logic.
9. Shipping-specific routing.
10. Weight handling coordination.
11. PLC-specific lane generation.
12. A criteria-data cache.

The cache is:

```csharp
public interface ICriteriaDataCache
{
    ValueTask<IReadOnlyList<CriteriaData>> GetAllAsync(
        ITableProvider tableProvider,
        CancellationToken cancellationToken = default);
}
```

`src\eController.MFC\Handlers\CriteriaDataCache.cs:34-40`

It reads:

```csharp
tableProvider.Table<CriteriaData>().Query().GetAsync(ct)
```

`CriteriaDataCache.cs:154-157`

The cache uses immutable snapshots, a TTL, stale-serving behavior, and protection against caching an empty result. This avoids repeated criteria-table queries while preserving the last known good configuration.

---

# 5. Transport orders, dynamic fields, extensions, and persistence

## 5.1 Transport-order entity

The primary entity is:

```csharp
MfcTransportOrder
```

It comes from:

```csharp
eController.Mfc.Library.Entities.Db
```

`ConveyorController.cs:10`

It is used by:

- `MfcDbContext`,
- connector hooks,
- telegram inbox/outboxes,
- MFC actions,
- DTC handlers,
- retention purge.

The production configuration explicitly uses:

```csharp
ctx.Hook<MfcDbContext, MfcTransportOrder, long>(...)
```

`ConveyorController.cs:211`

## 5.2 Dynamic extension storage

Exol uses extension APIs:

```csharp
trans.TryGetExtension<T>(out var extension)
trans.ApplyExtension<T>(...)
trans.RemoveExtension<T>()
```

The extension payload is serialized into the transport order’s dynamic data field. The architecture documents refer to this as dynamic-field state; Exol’s custom extension types include:

- `SortCriteriaExtension`,
- `RecircInfo`,
- `ShippingAutoRouteProvenance`,
- `ShippingNiProvenance`.

Examples:

`src\eController.MFC\Actions\CriteriaActionsCustom.cs:1313-1347`

`src\eController.MFC\CbsExtensions\RecircInfo.cs:1-7`

`src\eController.MFC\CbsExtensions\ShippingAutoRouteProvenance.cs:1-7`

The extension key convention is type-name based. DTC parsing explicitly uses:

```csharp
private const string SortCriteriaExtensionKey = "SortCriteriaExtension";
```

`DtcMessageHandlerCustom.cs:400-402`

## 5.3 Extension lifecycle

`ScrubToteState` is the key lifecycle boundary. It removes stale per-cycle state:

```csharp
if (trans.TryGetExtension<SortCriteriaExtension>(out _))
{
    trans.RemoveExtension<SortCriteriaExtension>();
}
```

`CriteriaActionsCustom.cs:1338-1347`

The code comments state that stale dynamic-field data must not survive the next scan. It clears:

- criteria,
- routing scratch data,
- weight verdicts,
- recirculation markers,
- other prior-cycle state.

PandA should document each extension as one of:

- persistent across transport-order hops,
- refreshed per scan,
- cleared after successful print/apply,
- retained for audit only.

## 5.4 Persistent tables

Exol enables DB-backed tables with:

```csharp
.AddDbPersistantTable()
```

`ConveyorController.cs:113`

The table API is:

```csharp
ITableProvider
```

Typical access:

```csharp
var table = tableProvider.Table<LaneStatus>();
```

`CriteriaActionsCustom.cs:1043`, `:1106`, `:1164`

Tables used or referenced by Exol include:

- `CriteriaData`,
- `LaneStatus`,
- `LocationStatus`,
- `CartonRecirculationState`,
- `RecircThresholdData`,
- `WeightData`,
- `ElevatorSettingData`.

Evidence:

- `CriteriaActionsCustom.cs:1026-1164`
- `CriteriaDataCache.cs:154-157`
- `LaneStatusStore.cs:22`, `:104`
- `CartonRecirculationStore.cs:12`, `:58`, `:70`
- `RecircThresholdStore.cs:67`
- `CustomTransportOrderTelegramIn.cs:368-369`
- `ElevatorSettingRelay.cs:47`

Exol uses table builders/initializers in flow steps:

```csharp
ctx.Step(CriteriaActionsCustom.GenerateTableForLaneStatus);
ctx.Step(CriteriaActionsCustom.GenerateTableForLocationStatus);
```

`ConveyorController.cs:176-177`

PandA should use persistent tables for site-editable configuration and state such as:

- printer/applicator definitions,
- print templates,
- apply stations,
- device health,
- retry state,
- lane/station availability,
- PandA-specific criteria mappings.

## 5.5 EF/MFC context

Handlers use:

```csharp
MfcDbContext
```

Examples:

- `ConveyorController.cs:10`
- `ConveyorController.cs:211`
- `DtcMessageHandlerCustom.cs:26`
- `CustomTransportOrderTelegramIn.cs:50`
- `RetentionPurgeStep.cs:56`

The MFC library provisions the context and transport-order storage. The architecture log notes that the context is controller-specific and schema-specific:

`C:\Users\NoahPawlak\.copilot\session-state\be5b169f-b89b-4f3c-9dee-ff9a3abeaede\files\exol\.project\architecture-log\2026\07-30-dtc-handler-port.md:52`

The documented consequence is that multiple controllers can have separate schemas/tables, for example:

- `CV.MfcTransportOrder`
- `CV2.MfcTransportOrder`

The `MfcTransportOrder` entity itself does not contain `PlcName`; controller scoping is through the database schema/context.

## 5.6 Retention

Retention is implemented by a custom flow step:

```csharp
ctx.Step(RetentionPurgeStep.Purge);
```

`ConveyorController.cs:184`

Configuration is optional:

```csharp
public RetentionPurgeConfig? RetentionPurge { get; set; }
```

`src\eController.MFC\Config\MfcConfig.cs:22-25`

If the block is absent, retention is disabled:

`src\eController.MFC\Device\Conveyor\RetentionPurgeStep.cs:56-72`

The purge operates on aged, non-routing rows and uses separate rules for carton shells and ordinary orders:

- `RetentionPurgeStep.cs:171-203`
- `RetentionPurgeStep.cs:250-271`

A sidecar file can override retention settings:

```text
RetentionPurgePolicy.json
```

`src\eController.MFC\Device\Conveyor\RetentionPolicyReader.cs:9-42`

The sidecar can override configured values but cannot enable retention if the script configuration disables it.

---

# 6. Configuration

## 6.1 Project/runtime configuration

The MFC project includes the publish appsettings file:

```xml
<Content Include="..\..\config\appsettings.Mfc.Publish.default.json"
         Link="appsettings.Publish.json" />
```

`src\eController.MFC\eController.MFC.csproj:15`

The source configuration is under:

```text
config\
```

The project’s architecture distinguishes:

- application settings,
- MFC controller configuration,
- layout/place/lane JSON,
- criteria configuration,
- retention sidecars,
- controller/PLC settings.

## 6.2 Strongly typed options

Exol binds:

```csharp
IOptions<MfcControllerConfig>
```

`ConveyorController.cs:46`

The custom configuration class is:

```csharp
public class MfcControllerConfig : MfcConfigBase
```

`src\eController.MFC\Config\MfcConfig.cs:7`

It includes:

```csharp
public List<MfcPlcConfig> Plcs { get; set; } = new();
```

`MfcConfig.cs:10`

And supports fallback/default PLC configuration:

```csharp
public IEnumerable<MfcPlcConfig> EffectivePlcs
```

`MfcConfig.cs:51-62`

PLC records contain:

```csharp
public string PlcName { get; set; } = null!;
```

`MfcConfig.cs:64-66`

The controller registers the options object into DI both as the base type and concrete type:

```csharp
ctx.AddSingleton<MfcConfigBase>(_mfcConfig);
ctx.AddSingleton(_mfcConfig);
```

`ConveyorController.cs:146-148`

PandA should follow this pattern for plugin-specific options:

```csharp
public sealed class PandAOptions
{
    public string DefaultTemplate { get; set; } = "";
    public List<PandAStationOptions> Stations { get; set; } = [];
}
```

Register through the plugin/controller setup:

```csharp
services.Configure<PandAOptions>(
    configuration.GetSection("PandA"));
```

Then inject `IOptions<PandAOptions>` or register a validated singleton.

## 6.3 Hot-reload sidecar configuration

Retention demonstrates the intended runtime override pattern:

```text
RetentionPurgePolicy.json
```

`RetentionPolicyReader.cs:42`

The reader layers sidecar settings over script-bound configuration field by field. This allows operational retuning without rebuilding the plugin.

PandA can use the same pattern for site-specific settings such as:

- printer endpoint,
- retry timeout,
- label/template selection,
- station enablement,
- per-lane behavior,
- print/apply mode.

## 6.4 Layout/places/lanes

The generic MFC configuration uses place and lane definitions supplied through MFC configuration and persistent Criteria Based Sorting data. Exol generates derived tables for:

- lane status,
- location status,
- criteria mappings.

The table-generation steps are:

```csharp
ctx.Step(CriteriaActionsCustom.GenerateTableForLaneStatus);
ctx.Step(CriteriaActionsCustom.GenerateTableForLocationStatus);
```

`ConveyorController.cs:176-177`

The code resolves configured PLC names from `MfcConfigBase`:

```csharp
private static IEnumerable<string> GetEffectivePlcNames(
    MfcConfigBase config)
```

`CriteriaActionsCustom.cs:1213-1216`

PandA should not hard-code site lanes or places inside action logic. Prefer:

- MFC layout/place configuration for physical topology;
- persistent criteria/configuration tables for editable routing;
- plugin-specific JSON for PandA station/device settings;
- derived runtime tables for health/status.

## 6.5 Configuration validation

Exol performs runtime validation in several places:

- rejects missing PLC names before hook registration;
- validates effective PLC names;
- validates configured lane ranges;
- checks criteria domains exhaustively;
- rejects invalid/unknown routing state;
- avoids selecting places without the expected `SortCriteriaBehavior`.

Examples:

```csharp
if (string.IsNullOrWhiteSpace(plc.PlcName))
{
    throw new InvalidOperationException(...);
}
```

`ConveyorController.cs:228-231`

Lane-range validation:

`CriteriaActionsCustom.cs:24-35`

Criteria domain/exhaustiveness comments:

`CriteriaActionsCustom.cs:53-63`, `:77-117`

For PandA, configuration validation should occur at startup where possible, with explicit validation for:

- required station IDs,
- unique printer/applicator names,
- valid transport hooks,
- valid label/template identifiers,
- supported response telegram types,
- nonempty PLC/controller names,
- mutually exclusive operating modes.

---

# 7. Multi-controller and multi-host behavior

## 7.1 PLC modeling

The controller supports multiple PLC configurations:

```csharp
foreach (var plc in _mfcConfig.EffectivePlcs)
```

`ConveyorController.cs:209`

Each PLC receives a distinct hook key generated from its config:

```csharp
CreateAdsHookKey(plc)
```

`ConveyorController.cs:211`

Transport-order responses use the original PLC name:

```csharp
var originPlcName = transportOrder.GetOriginPlcNameOrNull();
...
var hookKey = new HookKey(originPlcName);
await outbox.StoreAsync(hookKey, transportOrder);
```

`ProjectActions.cs:25-53`

This is preferable to choosing a configured fallback PLC. Exol explicitly refuses fallback when the origin is missing.

## 7.2 Multiple MFC controller instances

The architecture supports multiple controller instances, but the current database model is controller-scoped. The architecture log states:

`07-30-dtc-handler-port.md:52`

Important consequence:

- each controller can have its own schema/table;
- `MfcTransportOrder` does not carry the PLC/controller name;
- identical DTC hooks registered on multiple controllers can deliver the same DTC telegram to both;
- both controllers may attempt to process or reject it;
- the model is not inherently controller-agnostic or globally idempotent.

This is a critical PandA design constraint. If PandA must operate across multiple controllers:

1. include an explicit controller/station key in the inbound message, or
2. route messages to a single shared ingest host/store, or
3. ensure only the correct controller receives the hook message, or
4. use an idempotency key and shared coordination store.

Do not assume that registering the same DTC hook on two controllers provides safe fan-out.

## 7.3 Concurrency and locking

Exol uses explicit process-local synchronization for table initialization:

```csharp
private static readonly object LaneStatusTableInitializationSync = new();
private static readonly HashSet<string> LaneStatusInitializedPlcNames = new(...);
private static readonly HashSet<string> LocationStatusInitializedPlcNames = new(...);
```

`CriteriaActionsCustom.cs:39-41`

This prevents repeated initialization within a process. It is not a distributed lock.

The criteria cache uses immutable snapshots and stale-serving behavior, which avoids concurrent mutation of the active criteria set:

`CriteriaDataCache.cs:34-40`, `:127-185`

For PandA, distinguish:

- process-local synchronization,
- database uniqueness/idempotency,
- distributed locking,
- PLC/device serialization.

A printer/applicator operation likely requires station-level serialization. A static `lock` is insufficient if multiple MFC hosts can target the same physical device.

---

# 8. Testing seams

## 8.1 Test project configuration

The primary test project is:

```text
eController.Test/eController.Test.csproj
```

It targets:

```xml
<TargetFramework>$(ProjectNetFramework)</TargetFramework>
```

`eController.Test/eController.Test.csproj:4`

It references:

```xml
Microsoft.EntityFrameworkCore.InMemory
Microsoft.EntityFrameworkCore.Sqlite
xunit.v3
xunit.runner.visualstudio
```

`eController.Test/eController.Test.csproj:16-23`

PlcSim tests include:

```text
eController.PlcSim.Test/eController.PlcSim.Test.csproj
```

and target:

```xml
<TargetFramework>net10.0</TargetFramework>
```

`eController.PlcSim.Test/eController.PlcSim.Test.csproj:4`

## 8.2 Existing test seams

Exol uses multiple seams:

- EF Core InMemory for lightweight database tests.
- SQLite for relational behavior.
- PlcSim controller implementations.
- direct action tests;
- reflection-based MFC context workarounds;
- fake/stub MFC CRUD contexts;
- custom stores for recirculation and thresholds.

The backlog explicitly identifies the limitation of the current seam:

`C:\Users\NoahPawlak\.copilot\session-state\be5b169f-b89b-4f3c-9dee-ff9a3abeaede\files\exol\.project\backlog\mfc-library-inmemory-test-seam-request.md:11-27`

The requested supported seam is a real `MfcFluentControllerBase` running its full setup with in-memory persistence and executing real action bodies:

`mfc-library-inmemory-test-seam-request.md:90-120`

The current workaround cannot fully execute the packaged real MFC flow and therefore can miss package version skew.

## 8.3 PlcSim structure

The simulator derives from the same controller model:

```csharp
internal class SimController : CustomSimController
```

`src\eController.PlcSim\Device\SimController.cs:20`

It overrides `Setup(ControllerBuilder builder)` and calls:

```csharp
base.Setup(builder);
```

`SimController.cs:45-48`

It adds:

```csharp
builder.SetupSteps(ctx =>
{
    ctx.StepSyncPlaceDataToVisu();
})
.AddVisuTracking();
```

`SimController.cs:50-55`

PlcSim contains a PandA action implementation:

```csharp
public class PandAActions : IMfcAction
```

`src\eController.PlcSim\CustomSimControllerWork_PandA\PandAActions.cs:12`

and registers it through:

```csharp
.SetupMfcActions(ctx =>
{
    ctx.AddActions<PandAActions>();
});
```

`src\eController.PlcSim\CustomSimControllerWork_PandA\ClassExtensions.cs:13`

This is the most direct existing model for testing PandA behavior inside the MFC/PlcSim framework.

## 8.4 Recommended PandA tests

PandA should have at least:

1. **Pure action tests**
   - input `MfcTransportOrder`;
   - synthetic `SortCriteriaExtension`;
   - fake `ITableProvider`;
   - assert destination/status/extension changes.

2. **Persistence tests**
   - EF Core InMemory or SQLite;
   - create PandA configuration tables;
   - verify lookups and idempotency.

3. **Flow-engine tests**
   - instantiate a `CustomSimController`/real MFC subclass;
   - register PandA actions;
   - inject a telegram;
   - assert the action executes through the real dispatcher.

4. **Hook tests**
   - verify PLC-specific ingress hook selection;
   - verify outbound response hook uses `OriginPlcName`.

5. **Concurrency tests**
   - two orders targeting one printer;
   - duplicate telegram delivery;
   - retry after transport failure.

6. **Package smoke test**
   - build the actual plugin/package;
   - load it into the target host;
   - execute at least one real action path.

The backlog specifically warns that build-only tests may not detect packaged dependency/version skew.

---

# 9. Versions and package matrix

## 9.1 Framework targets

Core MFC projects target .NET 9:

```xml
<TargetFramework>net9.0</TargetFramework>
```

- `src\eController.MFC\eController.MFC.csproj:3`
- `src\eController.PlcSim\eController.PlcSim.csproj:3`
- `src\Common\eController.Common.csproj:3`
- `src\eController.ThroughputWarehouse\eController.ThroughputWarehouse.csproj:5`

Some unrelated eHub projects target .NET 10. For PandA, the target must match the actual host process and plugin compatibility requirements.

## 9.2 Relevant eController packages

From `Directory.Packages.props`:

| Package | Version | Evidence |
|---|---:|---|
| `eController.Behaviors.CriteriaBasedSorting` | `0.2.0` | `Directory.Packages.props:4` |
| `eController.Behaviors.Weight` | `0.0.6` | `:5` |
| `eController.Mfc.Library` | `4.5.1` | `:35` |
| `eController.Mfc.UI` | `4.4.2` | `:17` |
| `eController.DataLib` | `4.5.1` | `:14` |
| `eController.CrudTable.Api` | `4.5.1` | `:12` |
| `eController.CrudTable.DynamicTable` | `4.5.1` | `:13` |
| `eController.TransportInterface.Mfc` | `4.5.1` | `:39` |
| `eController.TransportInterface.Shared` | `4.5.1` | `:40` |
| `eController.TransportInterface.Sim` | `4.4.2` | `:22` |
| `eController.WebUI.ApplicationManager` | `4.5.2` | `:24` |
| `eController.IntegrationHub.PlugIn` | `1.13.0` | `:18` |
| `ePlugin.MsBuild` | `2.4.22` | `:31` |
| `eController.Abstractions` | `3.2.0-g03e73bfa3e` | `:36` |

The MFC project references the core runtime packages in:

`src\eController.MFC\eController.MFC.csproj:19-29`

## 9.3 MudBlazor

Central version:

```xml
<PackageVersion Include="MudBlazor" Version="6.1.2" />
```

`Directory.Packages.props:29`

The Screens project overrides it:

```xml
<PackageReference Include="MudBlazor" VersionOverride="6.19.0" />
```

`src\Screens\eController.WebUI.Screens.csproj:15`

This is an important version-skew risk. A PandA MFC plugin should not introduce UI dependencies unless necessary. If it does, it should use the host’s actual MudBlazor version and avoid packaging a conflicting copy.

---

# 10. Recommended PandA plugin shape

## 10.1 Minimal MFC action assembly

Recommended structure:

```text
src/
  PandA/
    PandA.csproj
    Actions/
      PandAActions.cs
    Config/
      PandAOptions.cs
      PandAOptionsValidator.cs
    Handlers/
      PandATransportOrderHandler.cs
    Tables/
      PandAStation.cs
      PandATemplate.cs
      PandADeviceState.cs
    Extensions/
      PandARequestExtension.cs
      PandAResultExtension.cs
    Steps/
      PandADeviceHealthStep.cs
```

## 10.2 Project references

At minimum:

```xml
<TargetFramework>net9.0</TargetFramework>

<PackageReference Include="eController.Mfc.Library" />
<PackageReference Include="eController.DataLib" />
<PackageReference Include="eController.CrudTable.Api" />
<PackageReference Include="eController.CrudTable.DynamicTable" />
<PackageReference Include="eController.TransportInterface.Mfc" />
```

Add Criteria Based Sorting only if PandA routing depends on criteria:

```xml
<PackageReference Include="eController.Behaviors.CriteriaBasedSorting" />
```

Use central package management from `Directory.Packages.props`.

## 10.3 Registration

The host/controller setup should resemble:

```csharp
builder
    .AddTransportOrderSetup()
    .AddDbPersistantTable()
    .SetupMfcActions(ctx =>
    {
        ctx.AddActions<PandAActions>();
    })
    .SetupDependencies(ctx =>
    {
        ctx.AddSingleton<PandAOptions>(...);
        ctx.AddScoped<PandAStationStore>();
        ctx.AddScoped<PandADeviceHandler>();
    })
    .SetupSteps(ctx =>
    {
        ctx.StepDefaultProcessActions();
        ctx.Step(PandADeviceHealthStep.Run);
    });
```

Do not add a second independent MFC host inside the plugin.

## 10.4 Device communication

If PandA communicates with a printer/applicator over TCP/HTTP/PLC:

- keep the device connector behind an injectable interface;
- do not block the MFC flow thread;
- make retries/idempotency explicit;
- persist request/result state where recovery matters;
- use an outbox or MFC telegram response mechanism for PLC output;
- avoid treating a successful network call as equivalent to a durable MFC state transition.

## 10.5 Extension design

Use separate extensions for separate lifetimes:

```csharp
public sealed class PandARequestExtension
{
    public string TemplateId { get; set; } = "";
    public string PrintPayload { get; set; } = "";
    public string StationId { get; set; } = "";
}

public sealed class PandAResultExtension
{
    public string Result { get; set; } = "";
    public string? DeviceReference { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
```

Clearly define whether each extension:

- survives recirculation,
- is cleared after a scan,
- is retained after completion,
- is controller-owned or host-owned.

Follow Exol’s `ScrubToteState` discipline rather than allowing stale dynamic-field data to propagate.

---

# 11. Key architectural conclusions for the PandA port

1. **PandA should be an MFC action/behavior plugin.**  
   Use `IMfcAction`, `SetupMfcActions`, `SetupDependencies`, and `SetupSteps`.

2. **The plugin should not own the MFC host.**  
   The host/controller is supplied by eController and configured through `ControllerBuilder`.

3. **The PLC connector should remain separate.**  
   Exol’s ADS connector is an eHub plugin that publishes to MFC hooks. PandA device connectivity should follow the same separation where appropriate.

4. **Use `MfcTransportOrder` as the state carrier.**  
   Use normal entity fields for lifecycle/routing state and dynamic extensions for PandA-specific structured data.

5. **Use `ITableProvider` for site-editable and operational data.**  
   Do not embed printer/station/lane mappings in action code.

6. **Treat multi-controller processing as controller-scoped unless explicitly designed otherwise.**  
   Database context/schema isolation does not automatically provide cross-controller idempotency.

7. **Test through the real flow engine eventually.**  
   Direct action tests are useful but do not prove dispatcher registration, package loading, hook routing, or actual MFC lifecycle behavior.

8. **Pin versions consistently.**  
   The relevant runtime baseline is primarily .NET 9, `eController.Mfc.Library 4.5.1`, Criteria Based Sorting `0.2.0`, and `ePlugin.MsBuild 2.4.22`.

9. **Avoid UI dependencies in the PandA MFC plugin unless required.**  
   MudBlazor is primarily relevant to the WebUI plugin and has known version overrides.

10. **Make device operations durable and retry-safe.**  
    Exol’s `SendRespToPlc` pattern—mutate, enqueue, restore on failure—is a good model for PandA print/apply acknowledgements.
