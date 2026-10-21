# Exol UI/ePlugin Porting Report for PandA

Repository examined:

`C:\Users\NoahPawlak\.copilot\session-state\be5b169f-b89b-4f3c-9dee-ff9a3abeaede\files\exol`

The existing `src\Screens` project is the closest implementation model for porting a Blazor/MudBlazor PandA UI containing a dashboard, configuration explorer, reject-carton view, and line simulator.

---

## 1. How screens are built

### 1.1 Project type and target framework

The screen assembly is a Razor class library:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>$(ProjectNetFramework)</TargetFramework>
    <RootNamespace>eController.WebUI.Screens</RootNamespace>
  </PropertyGroup>
</Project>
```

`src/Screens/eController.WebUI.Screens.csproj:1-8`

The repository-wide default is currently `net10.0`:

```xml
<ProjectNetFramework>net10.0</ProjectNetFramework>
```

`Directory.Build.props:5-8`

However, the embedded MFC WebUI host is .NET 9. The deployment skill explicitly requires overriding the build:

```powershell
dotnet build src\Screens\eController.WebUI.Screens.csproj -c Release `
  -p:ProjectNetFramework=net9.0 `
  -p:ePluginPkgLight=true `
  -p:ePluginPkgDependencies=true `
  "-p:ePluginPkgOutputFolder=<stage>\"
```

`.agent/skills/exol-screens-eplugin-deploy.md:17-28`

**PandA requirement:** build and test may use different TFMs, but the `.eplugin` must be built for the host’s actual runtime, currently `net9.0`.

---

### 1.2 Shared base component and imports

All pages use the host framework base component:

```razor
@inherits FrameworkComponent
```

`src/Screens/EventLog.razor:1`

The shared imports establish the component, menu, authorization, and MudBlazor namespaces:

```razor
@using Microsoft.AspNetCore.Authorization
@using MudBlazor
@using eController.WebUI.Utility.DynamicComponent
@using eController.WebUI.Utility.Menu
```

`src/Screens/_Imports.razor:1-12`

The build guide identifies `FrameworkComponent` as coming from `eController.WebUI.Utility.DynamicComponent` and menu attributes from `eController.WebUI.Utility.Menu`:

`.project/architecture-log/eplugin-build-guide.md:43-60`

For PandA pages, use the same base class unless the host provides a newer equivalent. It is likely responsible for host-specific messaging, navigation, authentication, and data-context behavior.

---

### 1.3 MudBlazor version

The Screens project pins MudBlazor 6.19.0:

```xml
<PackageReference Include="MudBlazor" VersionOverride="6.19.0" />
```

`src/Screens/eController.WebUI.Screens.csproj:16`

The repository learning notes warn that MudBlazor 6.19.0 targets .NET 6–8 and may load on .NET 9 through compatibility, but JavaScript interop can fail silently:

`.project/learnings/002-nuget-tfm-js-interop-compat.md:8-27`

The deployment guide also warns that the host’s MudBlazor version wins at runtime and enum layout properties can cause runtime failures:

`.project/architecture-log/eplugin-build-guide.md:75-82`

Recommended PandA rules:

- Avoid relying on MudBlazor enum layout behavior that changed between versions.
- Prefer inline CSS such as:

```razor
<div style="display:flex;flex-wrap:wrap;gap:12px;">
```

- Keep `Color`, `Size`, and `Variant` usage conservative.
- Verify `_content/{package}/...` and Blazor/SignalR endpoints with a real browser smoke test.
- If the host can be upgraded, prefer a MudBlazor version targeting the host TFM natively.

---

### 1.4 Navigation/menu registration and routing

Each page self-registers in the host menu and route table using attributes.

Example:

```razor
@attribute [MenuGroupItem(200, "MFC UI", "tune", "", true)]
@attribute [MenuItem("Event Log", "list", "", true, 0)]
@attribute [Authorize(Policy = ScreenPolicies.EventLog)]
@page "/EventLog"
```

`src/Screens/EventLog.razor:1-4`

Existing page registrations include:

| Screen | File | Route | Menu order |
|---|---|---|---:|
| Event Log | `src/Screens/EventLog.razor:1-4` | `/EventLog` | 0 |
| Lane Configuration | `src/Screens/LaneConfiguration.razor:1-4` | `/LaneConfiguration` | 1 |
| System Values | `src/Screens/SystemValues.razor:1-4` | `/SystemValues` | 2 |
| Destination Aliases | `src/Screens/PlaceAliases.razor:1-4` | `/PlaceAliases` | 3 |
| Transport Orders | `src/Screens/TransportOrders.razor:1-4` | `/TransportOrders` | 4 |
| Tote Lookup | `src/Screens/ToteLookup.razor:1-4` | `/ToteLookup` | 5 |
| Throughput | `src/Screens/Throughput.razor:1-3` | `/Throughput` | 6 |
| Screen Access | `src/Screens/ScreenAccess.razor:1-4` | `/ScreenAccess` | 7 |

The build guide confirms the expected pattern:

```razor
@inherits FrameworkComponent
@attribute [MenuGroupItem(200, "Event Log", "List")]
@attribute [MenuItem("Event Log", "List")]
@page "/EventLog"
```

`.project/architecture-log/eplugin-build-guide.md:28-42`

For PandA, use a separate group or a stable group identifier, for example:

```razor
@attribute [MenuGroupItem(250, "PandA", "print", "", true)]
@attribute [MenuItem("PandA Dashboard", "dashboard", "", true, 0)]
@page "/PandA"
```

The exact icon names should be validated against the host’s menu/icon implementation.

---

### 1.5 Layout approach

The existing pages are component-oriented rather than using a separate shared PandA layout.

`EventLog.razor` uses:

- `MudPaper` for the page header/filter surface.
- Inline flex layout.
- `MudDataGrid` for server-side data.
- `MudAlert` for errors.
- `MudCard`, `MudText`, `MudChip`, and picker/select controls.

`src/Screens/EventLog.razor:6-93`

Example:

```razor
<MudPaper Elevation="0" Class="pa-4 mb-3">
    <MudText Typo="Typo.h5">Event Log</MudText>
    <div style="display:flex;flex-wrap:wrap;gap:12px;">
        ...
    </div>
</MudPaper>
```

For PandA, a likely composition is:

- Dashboard: cards plus trend/chart region.
- Config explorer: controller/device selector plus expandable tree/table.
- Reject cartons: server-side grid with filtering and detail row.
- Line simulator: state cards, command controls, and timer-driven status refresh.

Existing Throughput, Transport Orders, and Tote Lookup pages are especially relevant because they already implement operational dashboards and timed refreshes.

---

### 1.6 Dialogs and overlays

The current Screens implementation intentionally does **not** depend on a global MudDialog provider for several flows.

Lane Configuration documents:

```razor
@* Add-type popup (self-contained overlay; no MudDialogProvider dependency) *@
```

`src/Screens/LaneConfiguration.razor` search result around line 225

Throughput similarly documents:

```razor
@* Trend graph overlay (self-contained; no MudDialogProvider dependency) *@
```

`src/Screens/Throughput.razor` search result around line 106

The build guide mentions `ElDialogProvider`/dialog-provider issues indirectly through the warning that host runtime versions control component behavior, while the actual Screens code favors self-contained overlays.

**PandA recommendation:** use a local component overlay or the host’s verified dialog abstraction. Do not assume that adding a new `MudDialogProvider` or `ElDialogProvider` is safe without confirming that the host registers it. If a dialog provider is required, verify:

1. The provider is present exactly once in the host layout.
2. The plugin’s MudBlazor version is compatible with the host.
3. Dialog JavaScript/static assets are available.
4. bUnit can render the page without requiring a real browser circuit.

---

### 1.7 Render modes and interactivity

The repository does not contain the host `App.razor`, but the documented failure mode is explicit:

- Without `RenderMode.InteractiveServer`, pages render as static HTML.
- Lifecycle methods still execute, creating the illusion that the page works.
- MudBlazor controls then appear empty or non-interactive.

`.project/learnings/001-blazor-config-path-and-rendermode.md:8-22`

The recommended host-side pattern is:

```razor
<Routes @rendermode="RenderMode.InteractiveServer" />
<HeadOutlet @rendermode="RenderMode.InteractiveServer" />
```

`.project/learnings/001-blazor-config-path-and-rendermode.md:32-35`

The same document lists runtime smoke checks:

- `_framework/blazor.web.js` returns HTTP 200.
- `_content/{Package}/{file}.js` returns HTTP 200.
- HTML includes Blazor server markers.
- `/_blazor/negotiate` returns HTTP 200.

`.project/learnings/002-nuget-tfm-js-interop-compat.md:39-47`

**PandA requirement:** interactive server rendering must be verified in the actual eController WebUI host. A successful build or bUnit test is insufficient.

---

## 2. Access control and permissions

### 2.1 Page-level authorization

Each owned page has an authorization policy:

```razor
@attribute [Authorize(Policy = ScreenPolicies.EventLog)]
```

`src/Screens/EventLog.razor:3`

The same pattern is used by all seven owned screens.

`src/Screens/ScreenAccess.razor:1-4`

The plugin registers policies additively:

```csharp
services.AddAuthorizationCore(options =>
{
    options.AddPolicy(ScreenPolicies.AdminOnly, policy =>
        policy.RequireRole(MfcRoles.Administrator));

    foreach (var screen in MfcScreens.All)
    {
        options.AddPolicy(screen.Policy, policy =>
            policy.Requirements.Add(new MfcScreenRequirement(screen.Key)));
    }
});
```

`src/Screens/Startup.cs:39-57`

A shared authorization handler delegates to the file-backed screen matrix. The host `NavMenu` evaluates authorization before showing a page, so the same policy:

1. Hides the menu item.
2. Blocks navigation to the route.

`docs/mfc-screen-access-control.md:46-62`

The access matrix controls seven screens:

`docs/mfc-screen-access-control.md:15-27`

- Event Log
- Lane Configuration
- System Values
- Destination Aliases
- Transport Orders
- Tote Lookup
- Throughput

For PandA, add stable keys and policies to the same central registry rather than implementing per-page ad hoc checks.

---

### 2.2 Roles and fail-open behavior

The access model uses the platform roles:

- `Administrator`
- `Supervisor`
- `User`

`docs/mfc-screen-access-control.md:15-27`

Important semantics:

- Missing `ScreenAccess.json` means visible.
- Malformed configuration means visible.
- Unconfigured screen means visible.
- A user with none of the known roles sees screens.
- A screen is visible if any of the user’s roles is allowed.
- `elementAdmin` bypasses all screen restrictions.
- Only explicit `false` values are persisted.

`docs/mfc-screen-access-control.md:29-39`

The Screen Access grid itself requires Administrator:

```csharp
options.AddPolicy(ScreenPolicies.AdminOnly, policy =>
    policy.RequireRole(MfcRoles.Administrator));
```

`src/Screens/Startup.cs:45-49`

For PandA, preserve fail-open semantics unless the business requirement specifically calls for fail-closed safety behavior. Operational UI lockouts are treated as more dangerous than accidental visibility in this project.

---

### 2.3 Third-party host tabs

The plugin cannot attach policies to compiled third-party pages that have no authorization metadata. Instead it decorates the host `IComponentService`:

```csharp
var existing = services.LastOrDefault(
    d => d.ServiceType == typeof(IComponentService));

if (existing?.ImplementationInstance is not IComponentService inner)
{
    return;
}

services.Remove(existing);
services.AddScoped<IComponentService>(sp =>
    new RestrictedTabComponentServiceDecorator(
        inner,
        sp.GetRequiredService<AuthenticationStateProvider>()));
```

`src/Screens/Startup.cs:69-91`

The architecture record states that this hides the host MFC and Scheduler tabs from non-`elementAdmin` users, but does not block direct URLs:

`.project/architecture-log/2026-09-22-mfc-screen-access-control.md:7-22`

This is important for PandA if it needs to hide or filter an existing host tab. A policy attribute will only work for pages whose metadata is available to the authorization pipeline.

---

## 3. How screens get data

### 3.1 DI registration

`Startup` is the plugin composition root. It is public because the host finds plugin subclasses of `ServerExtensionService` through reflection:

```csharp
public class Startup : ServerExtensionService
{
    public Startup(IServiceCollection services)
    {
        RegisterEventLog(services);
        RegisterControllerStack(services);
        RegisterPlaceAliases(services);
        RegisterPlaceCatalog(services);
        RegisterToteLookup(services);
        RegisterThroughput(services);
        RegisterScreenAccess(services);
    }
}
```

`src/Screens/Startup.cs:13-26`

The build guide emphasizes:

```csharp
public class Startup : ServerExtensionService
```

and warns that a non-public class is silently skipped by reflection.

`.project/architecture-log/eplugin-build-guide.md:20-27`

For PandA, register its service layer here:

```csharp
services.AddSingleton<IPandaDashboardService, PandaDashboardService>();
services.AddScoped<IPandaLineSimulationService, PandaLineSimulationService>();
```

Choose singleton/scoped lifetimes based on whether the service owns state, database contexts, timers, or controller scope.

---

### 3.2 Multi-controller data sessions

The screen plugin supports multiple controllers. A controller registry resolves:

- Controller ID.
- Connection string.
- Database schema.
- Configuration folder.

`src/Screens/Startup.cs:175-184`

The database context is scoped and requires a controller-bound data session:

```csharp
services.AddScoped(sp =>
{
    var state = sp.GetRequiredService<IControllerScopeState>();
    var descriptor = state.Descriptor
        ?? throw new InvalidOperationException(
            "MfcDbContext was resolved outside a controller data session ...");

    var options = new MfcDbOptions
    {
        ConnectionString = descriptor.ConnectionString,
        Schema = descriptor.Schema,
    };

    return new MfcDataContextFactory(
        Options.Create(options)).CreateDbContext();
});
```

`src/Screens/Startup.cs:186-208`

The persistent-table stack is then registered per session:

```csharp
services.AddDbPersistantTable();
```

`src/Screens/Startup.cs:210-218`

`ControllerDataSessionFactory` binds the selected controller before resolving `MfcDbContext` and table repositories:

`src/Screens/Controllers/ControllerDataSessionFactory.cs:27-35`

**PandA requirement:** never resolve `MfcDbContext`, `ITableProvider`, or an MFC table repository outside a controller data-session scope. The exception is intentional and protects against reading the wrong controller/schema.

---

### 3.3 PersistantTable/ITableRepository

The existing design uses the MFC library’s persistent-table abstraction rather than directly embedding table-specific wiring in every screen:

```csharp
using eController.Mfc.Library.PersistantTable.Extensions;
```

`src/Screens/Startup.cs:4`

and:

```csharp
services.AddDbPersistantTable();
```

`src/Screens/Startup.cs:211-212`

This creates a scope-bound table provider/repository graph attached to the currently selected `MfcDbContext`.

The project search did not find direct `ITableRepository` usage in the Screens service folder, implying that many screen services access domain services or the EF context directly while the persistent-table stack is made available for services that need it.

For PandA:

- Use `ITableRepository`/persistent-table APIs where the target MFC table is already modeled there.
- Use a dedicated service layer for joins, projections, and operational queries.
- Do not let Razor components execute raw SQL or own transactions.
- Keep controller selection in the service/session layer.

---

### 3.4 Existing service boundaries

Examples of service registration:

```csharp
services.AddSingleton<IEventLogReader, EventLogReader>();
services.AddSingleton<IEventLogService>(sp =>
    new EventLogService(
        sp.GetRequiredService<IControllerRegistry>(),
        sp.GetRequiredService<IEventLogReader>(),
        sp.GetService<IScreenServices>()));
```

`src/Screens/Startup.cs:164-172`

Throughput is a read-only warehouse facade:

```csharp
services.AddSingleton<IThroughputService>(sp =>
    new ThroughputService(
        sp.GetRequiredService<IControllerRegistry>()));
```

`src/Screens/Startup.cs:129-132`

Tote lookup composes multiple services:

```csharp
services.AddSingleton<IToteLookupService>(sp =>
    new ToteLookupService(
        sp.GetRequiredService<IControllerRegistry>(),
        sp.GetRequiredService<IScreenServices>(),
        sp.GetRequiredService<IEventLogService>(),
        sp.GetRequiredService<IPlaceAliasService>()));
```

`src/Screens/Startup.cs:108-118`

The screen components inject interfaces, for example:

```razor
@inject IEventLogService EventLogService
@inject IControllerRegistry ControllerRegistry
```

`src/Screens/EventLog.razor:5-6`

PandA should follow this pattern:

```razor
@inject IPandaDashboardService DashboardService
@inject IPandaConfigService ConfigService
@inject IPandaRejectCartonService RejectCartonService
@inject IPandaSimulationService SimulationService
```

---

### 3.5 Server-side grids and paging

Event Log uses `MudDataGrid` with a server data callback:

```razor
<MudDataGrid T="EventLogRow"
             ServerData="LoadServerData"
             Dense="true"
             Hover="true"
             Striped="true"
             FixedHeader="true">
```

`src/Screens/EventLog.razor:43-49`

The callback translates MudBlazor’s zero-based page index to the service’s one-based page:

```csharp
_filter.Page = state.Page + 1;
_filter.PageSize = state.PageSize;

var result = await EventLogService.QueryAsync(
    _selectedController, _filter);

return new GridData<EventLogRow>
{
    Items = result.Rows,
    TotalItems = total
};
```

`src/Screens/EventLog.razor:117-136`

For reject cartons, this is the recommended model:

- Keep paging/filtering in the service.
- Return a row DTO and total count.
- Avoid loading the full reject history into the browser.
- Use a child/detail row for carton events or label/reject reason details.

---

### 3.6 Live updates

The repository uses `PeriodicTimer` rather than SignalR subscriptions in the Screens code:

- Throughput: `src/Screens/Throughput.razor:289`
- Transport Orders: `src/Screens/TransportOrders.razor:473`
- Tote Lookup: `src/Screens/ToteLookup.razor:430`

The general pattern is a component-owned refresh loop that calls the service periodically. The Event Log page also supports explicit refresh without reconstructing the page:

```csharp
private Task RefreshAsync() => SearchAsync();
```

`src/Screens/EventLog.razor:159`

and:

```csharp
await _grid.ReloadServerData();
```

`src/Screens/EventLog.razor:146-157`

No direct SignalR or `HubConnection` usage was found under `src/Screens`.

For PandA:

- Use `PeriodicTimer` for low-frequency dashboard/config refresh.
- Use SignalR only if the host already exposes a stable hub contract.
- Ensure cancellation/disposal when navigating away.
- Guard asynchronous callbacks against disposed components.
- Avoid concurrent refreshes or stale controller selections.
- For line simulation, separate the simulation clock from the UI refresh interval.

---

## 4. Packaging as NuGet and `.eplugin`

### 4.1 Screens are not packaged as an ordinary NuGet package

The Screens project is a Razor class library with ePlugin MSBuild packaging. It does not define ordinary NuGet package metadata such as:

```xml
<GeneratePackageOnBuild>true</GeneratePackageOnBuild>
<PackageId>...</PackageId>
```

The relevant packaging properties are ePlugin-specific.

The MFC plugin project demonstrates the complete configuration:

```xml
<TargetFramework>net9.0</TargetFramework>
<Version>1.0.4</Version>
<OutputType>Library</OutputType>

<ePluginPkgOutputFolder>
  $(MSBuildThisFileDirectory)..\..\plugins\Mfc\
</ePluginPkgOutputFolder>
<ePluginPkgDependencies>true</ePluginPkgDependencies>
<ePluginPkgLight>true</ePluginPkgLight>
```

`src/eController.MFC/eController.MFC.csproj:1-13`

The Screens deployment skill confirms that this creates two files:

- `@light.eplugin`: the plugin’s own assembly.
- `@deps.eplugin`: dependency closure.

`.agent/skills/exol-screens-eplugin-deploy.md:4-12`

The critical point:

> Without `@light.eplugin`, the plugin can appear in the host information page but its assembly is never loaded, so no tab renders.

`.project/architecture-log/eplugin-build-guide.md:64-72`

> Without `@deps.eplugin`, the plugin may not appear in the host plugin list at all.

`.agent/skills/exol-screens-eplugin-deploy.md:53-56`

---

### 4.2 Required ePlugin properties

For PandA’s screen assembly, use:

```xml
<PropertyGroup>
  <TargetFramework>net9.0</TargetFramework>
  <OutputType>Library</OutputType>

  <ePluginPkgDependencies>true</ePluginPkgDependencies>
  <ePluginPkgLight>true</ePluginPkgLight>
  <ePluginPkgOutputFolder>
    $(SolutionDir)plugins\PandA\
  </ePluginPkgOutputFolder>
</PropertyGroup>
```

The deployment guide says the trailing backslash is mandatory:

`.agent/skills/exol-screens-eplugin-deploy.md:21-28`

It also warns that `$(SolutionDir)` can be empty when building a bare project without the solution loaded:

`.project/architecture-log/eplugin-build-guide.md:64-72`

**Recommendation:** pass an explicit staging directory in CI and local build scripts:

```powershell
-p:ePluginPkgOutputFolder="C:\stage\PandA\"
```

---

### 4.3 Static web assets and JavaScript/CSS

The Screens project contains:

```text
src/Screens/wwwroot/lane-config.js
src/Screens/wwwroot/place-aliases.js
src/Screens/wwwroot/system-values.js
```

These assets are part of the Razor class library’s static web asset model.

The project itself does not show explicit `<Content>` or `<EmbeddedResource>` entries for these files, so the SDK is expected to include them automatically under `wwwroot`.

For PandA:

- Put plugin-owned JS/CSS under `src/PandaScreens/wwwroot`.
- Reference them using the static web asset path expected by the host/package.
- Verify the actual generated `.eplugin` contents.
- Verify browser requests to `/_content/{assembly-or-package}/...`.
- Avoid relying on host-side file paths or copying assets manually unless the ePlugin packaging output proves that is required.

The JS interop learning specifically calls out missing `_content/{Package}/{file}.js` as a runtime diagnostic:

`.project/learnings/002-nuget-tfm-js-interop-compat.md:39-47`

---

### 4.4 Dependency ownership and duplicate assemblies

The access-control documentation identifies an important packaging invariant:

> `eController.WebUI.Library`, `Utility`, `PlugIn`, `Mfc.Library`, and `eScheduler.Library` must ship as host-provided dependencies, not bundled duplicates.

`docs/mfc-screen-access-control.md:72-80`

The reason is type identity. If the plugin bundles a second `eController.WebUI.Library.dll`, the plugin’s `IComponentService` can be a different CLR type from the host’s, causing decoration to silently fail.

For PandA:

- Treat host framework assemblies as host-provided.
- Do not manually copy host `eController.WebUI.*` binaries into the light package.
- Inspect generated `.eplugin.deps.epspec`.
- Confirm that the package does not contain duplicate host framework DLLs.
- Use `VersionOverride` only when necessary to match the deployed host.

The Screens project currently overrides host package versions:

```xml
<PackageReference Include="eController.WebUI.PlugIn"
                  VersionOverride="2.0.14" />
<PackageReference Include="eController.WebUI.Utility"
                  VersionOverride="4.0.16" />
```

`src/Screens/eController.WebUI.Screens.csproj:11-13`

The architecture record explains that these overrides were required to avoid NU1605 and match the deployed host:

`.project/architecture-log/2026-09-22-mfc-screen-access-control.md:35-41`

---

### 4.5 NuGet feeds and package source mapping

The root `nuget.config` defines:

```xml
<packageSources>
  <clear />
  <add key="nuget.org"
       value="https://api.nuget.org/v3/index.json"
       protocolVersion="3" />
  <add key="artifactory_elementlogic"
       value="https://repo.sup-logistik.de/artifactory/api/nuget/v3/default-nuget-virtual/index.json" />
</packageSources>
```

`nuget.config:3-7`

Internal packages are mapped to Artifactory:

```xml
<packageSource key="artifactory_elementlogic">
  <package pattern="ElementLogic.*" />
  <package pattern="ELA.*" />
  <package pattern="eHub.*" />
  <package pattern="eController.*" />
  <package pattern="ePlugin.*" />
  <package pattern="eMessenger*" />
  <package pattern="ElUI.*" />
  <package pattern="eScheduler.*" />
</packageSource>
```

`nuget.config:10-21`

Credentials are environment-variable based:

```xml
<add key="Username" value="%ARTIFACTORY_SUP_USER%" />
<add key="ClearTextPassword" value="%ARTIFACTORY_SUP_TOKEN%" />
```

`nuget.config:25-28`

Central package versioning is enabled:

```xml
<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
```

`Directory.Build.props:6`

The central version file is `Directory.Packages.props`, which should be consulted for exact versions during implementation.

---

### 4.6 Versioning

The repository has several independent version notions:

1. Repository/project version:

```xml
<Version>1.0.0</Version>
```

`Directory.Build.props:20-23`

2. MFC project version:

```xml
<Version>1.0.4</Version>
```

`src/eController.MFC/eController.MFC.csproj:4-6`

3. Inner plugin assembly file version, documented for Screens builds.

The deployment skill records a current Screens build with inner DLL file version `1.1.0.0`:

`.agent/skills/exol-screens-eplugin-deploy.md:29-38`

For PandA, use a deliberate versioning scheme:

- Assembly/package version for compatibility.
- Feature/build identifier in the deployment folder name.
- SHA-256 for both `.eplugin` files.
- Host-compatible dependency versions.
- Separate versioning for simulator-only features if they should not be deployed to production.

---

### 4.7 CI and publishing

The GitHub workflows primarily publish eHub/connector/container artifacts. The workflow search did not find a dedicated Screens `.eplugin` release workflow.

Examples include:

- `.github/workflows/release-exol-adapter.yaml`
- `.github/workflows/release-exol-ads-connector.yaml`
- `.github/workflows/release-ehub-exol-image.yaml`

The repository’s UI publish helper is:

```powershell
& "$PSScriptRoot\runhelper.ps1" "UI" "Publish"
```

`run/Run.Publish.UI.ps1:1`

The dedicated Screens deployment process is therefore currently script/manual-package oriented rather than a standard GitHub NuGet release.

The documented assembly process requires a deployment folder containing:

```text
<name>@light.eplugin
<name>@deps.eplugin
Deploy-UiScreens-*.ps1
Verify-UiScreens-*.ps1
README-DEPLOY.txt
```

`.agent/skills/exol-screens-eplugin-deploy.md:39-46`

For PandA CI, add a pipeline step that:

1. Restores using `nuget.config`.
2. Runs screen tests.
3. Builds with `ProjectNetFramework=net9.0`.
4. Sets both `ePluginPkgLight=true` and `ePluginPkgDependencies=true`.
5. Uses an explicit output folder with trailing slash.
6. Verifies both files exist.
7. Records file sizes and SHA-256 hashes.
8. Inspects package contents for the PandA DLL, static assets, and absence of duplicate host framework DLLs.
9. Publishes the deployment bundle as a CI artifact.
10. Does not automatically copy into a live host.

---

## 5. How the host discovers and loads screen plugins

### 5.1 Startup discovery

The plugin entry point must be public:

```csharp
public class Startup : ServerExtensionService
```

`src/Screens/Startup.cs:13-15`

The source comment explicitly states:

> The host discovers plugin subclasses of `ServerExtensionService` via reflection and injects constructor parameters from the host service collection.

`src/Screens/Startup.cs:13-18`

The build guide warns that a non-public `Startup` is silently skipped:

`.project/architecture-log/eplugin-build-guide.md:20-27`

Therefore the PandA package must include:

- A public `Startup`.
- A constructor accepting `IServiceCollection`.
- Service registration in that constructor.
- The plugin’s light assembly in `@light.eplugin`.

---

### 5.2 Package directory expectations

Screens deploy into the MFC host plugin directory:

```text
plugins\Mfc\
```

`.agent/skills/exol-screens-eplugin-deploy.md:4-12`

The two ePlugin files must be copied there:

```text
<name>@light.eplugin
<name>@deps.eplugin
```

`.agent/skills/exol-screens-eplugin-deploy.md:39-46`

The host then unpacks/loads them. A stale extracted content directory can continue serving the old package:

```text
plugins\Mfc\Content\<plugin>\
```

`.agent/skills/exol-screens-eplugin-deploy.md:47-56`

Required deployment sequence:

1. Stop MFC UI host.
2. Copy both `.eplugin` files.
3. Move/remove the stale `Content\<plugin>` extraction.
4. Restart host.
5. Hard-refresh browser.
6. Verify plugin info page.
7. Verify menu item.
8. Click the page and verify rendering.

`.agent/skills/exol-screens-eplugin-deploy.md:47-66`

---

### 5.3 What must be in the PandA package

At minimum:

- PandA plugin light assembly.
- Dependency closure required by the host.
- Public `Startup : ServerExtensionService`.
- All page/component types.
- Services and DTOs.
- Razor-generated component metadata.
- Static web assets under `wwwroot`.
- Any plugin-owned JS/CSS.
- Compatible package/dependency metadata.

Do not include:

- A duplicate host `eController.WebUI.Library.dll`.
- A duplicate host `eController.WebUI.Utility.dll`.
- A duplicate host `eController.WebUI.PlugIn.dll`.
- A duplicate MFC UI library unless the host explicitly requires it.

The type identity warning is documented in:

`docs/mfc-screen-access-control.md:72-80`

---

## 6. DLL swap versus package deployment

### 6.1 Standard package deployment

The preferred deployment is two `.eplugin` files:

- `@light`: own plugin assembly.
- `@deps`: dependencies.

The deployment skill explicitly treats Screens as two-file ePlugin deployment rather than NuGet deployment:

`.agent/skills/exol-screens-eplugin-deploy.md:4-12`

### 6.2 DLL swap

The repository also contains:

`.agent/skills/exol-eplugin-dll-swap.md`

The DLL swap approach is useful for fast iteration, but it is not equivalent to a clean package deployment. It must account for:

- The host holding the plugin assembly open.
- Shadow/extracted copies under `Content`.
- Dependency version drift.
- Assembly load context behavior.
- Browser/static asset cache.
- Restart requirements.

Use DLL swap only for controlled development loops. For a PandA release, produce and deploy both `.eplugin` files.

### 6.3 Host config guards

The repository has a dedicated host-config guard skill:

`.agent/skills/exol-plugin-deploy-host-config-guards.md`

The deployment process should validate:

- Target host/process.
- Target plugin directory.
- Expected existing plugin files.
- Host configuration.
- Whether the host is running.
- Whether stale extracted content exists.
- Whether the intended plugin is being replaced rather than another plugin.

The Screens deployment procedure explicitly requires a read-only Verify script before deployment:

`.agent/skills/exol-screens-eplugin-deploy.md:39-46`

---

## 7. Testing patterns and test seams

### 7.1 bUnit setup

`ScreenAccessPageTests` demonstrates the canonical bUnit pattern:

```csharp
private readonly BunitTestContext _ctx = new();
private readonly IScreenAccessService _service =
    Substitute.For<IScreenAccessService>();
```

`eController.WebUI.Screens.Tests/ScreenAccessPageTests.cs:18-25`

The constructor registers:

```csharp
_ctx.Services.AddMudServices();
_ctx.Services.AddSingleton(_service);
_ctx.Services.AddSingleton(
    Substitute.For<IUserTopicScopedMessenger>());
_ctx.Services.AddSingleton(
    Substitute.For<IWebUITopicScopedMessenger>());
_ctx.Services.AddSingleton(
    Substitute.For<IScopedMessenger>());
_ctx.JSInterop.Mode = JSRuntimeMode.Loose;
```

`eController.WebUI.Screens.Tests/ScreenAccessPageTests.cs:27-35`

These registrations are necessary because `FrameworkComponent` injects host messaging services.

The test project is granted internal access:

```xml
<InternalsVisibleTo Include="eController.WebUI.Screens.Tests" />
```

`src/Screens/eController.WebUI.Screens.csproj:22-24`

---

### 7.2 Component behavior assertions

The tests render the real component:

```csharp
return _ctx.RenderComponent<ScreenAccess>();
```

`eController.WebUI.Screens.Tests/ScreenAccessPageTests.cs:52-56`

They verify:

- DOM row counts.
- Component counts.
- Text and role headers.
- Checkbox interaction.
- Unsaved state.
- Real Save button behavior.
- Service call payload.
- Version/concurrency handling.
- Malformed-file banner.
- Discard behavior.

`eController.WebUI.Screens.Tests/ScreenAccessPageTests.cs:58-177`

Example interaction:

```csharp
var checkbox = cut.FindComponents<MudCheckBox<bool>>()[index];

cut.InvokeAsync(() =>
    checkbox.Instance.ValueChanged.InvokeAsync(false));

Assert.Contains("unsaved changes", cut.Markup);
```

`eController.WebUI.Screens.Tests/ScreenAccessPageTests.cs:82-94`

Example service verification:

```csharp
cut.Find("button.mud-fab").Click();

Assert.NotNull(captured);
Assert.False(
    captured![MfcScreens.SystemValuesKey][MfcRoles.User]);
```

`eController.WebUI.Screens.Tests/ScreenAccessPageTests.cs:96-133`

For PandA bUnit tests:

- Mock the service interfaces, not EF/MFC internals.
- Set `JSInterop.Mode = JSRuntimeMode.Loose` unless testing a specific JS call.
- Register all services required by `FrameworkComponent`.
- Render actual components.
- Trigger `ValueChanged`, `Click`, and callback events.
- Assert both visual state and service payload.
- Test loading, empty, error, stale/concurrency, and disposed states.

---

### 7.3 Authorization test seams

The architecture documentation separates authorization testing from host navigation rendering:

- Test the real `IAuthorizationService` policy pipeline.
- Test the `NavMenu` indirectly through policy decisions.
- Do not rely only on rendering the host menu.

`docs/mfc-screen-access-control.md:82-95`

The project also has tests for:

- Screen policy registration.
- Decorator behavior.
- DI boot safety.
- File-backed access matrix semantics.

`docs/mfc-screen-access-control.md:82-95`

For PandA, add:

- Policy registration tests.
- Administrator/supervisor/user access cases.
- `elementAdmin` bypass test if retained.
- Missing/malformed configuration fail-open tests.
- Route metadata tests.
- Service registration/build validation tests.

---

### 7.4 Data-service test seams

The current architecture makes the following seams available:

- `IControllerRegistry`
- `IControllerDataSessionFactory`
- `IScreenServices`
- `IEventLogService`
- `IPlaceAliasService`
- `IThroughputService`
- `IToteLookupService`
- `ITableProvider`/persistent-table services
- `MfcDbContext` scope binding

`src/Screens/Startup.cs:108-218`

For PandA, create interfaces before implementing pages:

```csharp
public interface IPandaDashboardService { ... }
public interface IPandaConfigExplorerService { ... }
public interface IRejectCartonService { ... }
public interface IPandaLineSimulationService { ... }
```

This keeps bUnit tests independent of SQL Server and PLC/simulator processes.

---

## 8. Pitfalls and documented learnings

### 8.1 Missing `@light.eplugin`

**Symptom:** plugin appears in host information page, but no tab renders.

**Cause:** the plugin’s own DLL was not packaged.

**Fix:** enable:

```xml
<ePluginPkgLight>true</ePluginPkgLight>
```

`.project/architecture-log/eplugin-build-guide.md:64-72`

---

### 8.2 Missing `@deps.eplugin`

**Symptom:** plugin is absent from the host/plugin information page.

**Cause:** dependency package is missing.

**Fix:**

```xml
<ePluginPkgDependencies>true</ePluginPkgDependencies>
```

`.agent/skills/exol-screens-eplugin-deploy.md:53-56`

---

### 8.3 Wrong target framework

**Symptom:** package does not load or runtime compatibility problems occur.

**Cause:** Screens built for `net10.0` while host runs `net9.0`.

**Fix:**

```powershell
-p:ProjectNetFramework=net9.0
```

`.agent/skills/exol-screens-eplugin-deploy.md:17-28`

---

### 8.4 Empty `$(SolutionDir)`

**Symptom:** ePlugin files are generated in an unexpected location or misnamed.

**Cause:** building the `.csproj` outside a loaded solution leaves `$(SolutionDir)` empty.

**Fix:** use an explicit output directory, ending in `\`.

`.project/architecture-log/eplugin-build-guide.md:64-72`

---

### 8.5 Stale unpacked `Content` cache

**Symptom:** new package is copied, but old UI still renders.

**Cause:** host serves an old extracted plugin directory.

**Fix:** stop host, move/delete:

```text
plugins\Mfc\Content\<plugin>\
```

then restart and hard-refresh.

`.agent/skills/exol-screens-eplugin-deploy.md:47-66`

---

### 8.6 Duplicate host framework assemblies

**Symptom:** decorators or type checks silently fail.

**Cause:** plugin carries another copy of `eController.WebUI.Library.dll` or related host assemblies.

**Fix:** keep host framework assemblies host-provided and inspect the generated dependency spec.

`docs/mfc-screen-access-control.md:72-80`

---

### 8.7 MudBlazor runtime mismatch

**Symptom:** click or layout operations throw `ObjectDisposedException`, or controls render but do not behave.

**Cause:** plugin was compiled against a different MudBlazor/runtime combination than the host.

**Fixes:**

- Match host package versions with `VersionOverride`.
- Avoid fragile enum layout props.
- Prefer inline CSS.
- Verify static assets and JS interop.
- Perform a browser smoke test.

`.project/architecture-log/eplugin-build-guide.md:75-82`

`.project/learnings/002-nuget-tfm-js-interop-compat.md:8-27`

---

### 8.8 Static render mode

**Symptom:** lifecycle methods run and markup appears, but dropdowns/buttons are inert.

**Cause:** missing interactive server render mode.

**Fix:** configure interactive server rendering at the host route/head level.

`.project/learnings/001-blazor-config-path-and-rendermode.md:8-22`

---

### 8.9 Wrong config path

**Symptom:** UI loads but data/config lists are empty.

**Cause:** resolving relative paths from `AppContext.BaseDirectory`, which points to `bin/...`, not the project/content root.

**Fix:** use `builder.Environment.ContentRootPath`.

`.project/learnings/001-blazor-config-path-and-rendermode.md:23-35`

Also add startup validation and a smoke test for real config folders.

`.project/learnings/001-blazor-config-path-and-rendermode.md:38-55`

---

### 8.10 `sqlserver://` connection-string prefix

**Symptom:** ADO/EF connection fails or remains stuck in “connecting”.

**Cause:** MFC config connection strings include a provider prefix:

```text
sqlserver://Data Source=...
```

The build guide says to remove the prefix before passing the string to ADO/EF:

```csharp
if (raw.StartsWith("sqlserver://",
        StringComparison.OrdinalIgnoreCase))
{
    raw = raw["sqlserver://".Length..];
}
```

`.project/architecture-log/eplugin-build-guide.md:84-94`

The existing Screens controller stack delegates this to MFC’s data-context factory and preserves the prefix for that factory:

`src/Screens/Startup.cs:194-198`

PandA should follow whichever API it calls and avoid stripping the prefix twice.

---

### 8.11 Missing disposal/cancellation

The build guide requires pages with asynchronous work to override `Dispose()` and guard continuations with a disposed flag:

`.project/architecture-log/eplugin-build-guide.md:35-42`

This is particularly important for the `PeriodicTimer` patterns in:

- `src/Screens/Throughput.razor:289`
- `src/Screens/TransportOrders.razor:473`
- `src/Screens/ToteLookup.razor:430`

PandA simulation and live-update pages should cancel timers when the component is disposed.

---

### 8.12 Controller scope leakage

The MFC context throws if resolved outside a controller-bound session:

`src/Screens/Startup.cs:186-208`

Do not cache a controller-specific `DbContext` or repository in a singleton. Keep the registry and service factories singleton if appropriate, but create controller-bound database scopes per operation/session.

---

### 8.13 Access-control assumptions

The access-control architecture deliberately distinguishes:

- Owned plugin pages: policy hides menu and blocks route.
- Third-party pages: decorator hides menu only.

`.project/architecture-log/2026-09-22-mfc-screen-access-control.md:7-22`

Do not assume a menu-hidden third-party page is route-secure. If PandA exposes simulator controls, apply route authorization directly to the PandA page and separately consider whether menu filtering is needed.

---

## 9. Recommended PandA project shape

```text
src/
  PandaScreens/
    PandaScreens.csproj
    Startup.cs
    _Imports.razor
    Dashboard.razor
    ConfigExplorer.razor
    RejectCartons.razor
    LineSimulator.razor
    Security/
      PandaPolicies.cs
      PandaAuthorizationHandler.cs
    Services/
      IPandaDashboardService.cs
      PandaDashboardService.cs
      IPandaConfigExplorerService.cs
      PandaConfigExplorerService.cs
      IRejectCartonService.cs
      RejectCartonService.cs
      IPandaLineSimulationService.cs
      PandaLineSimulationService.cs
    Models/
    wwwroot/
      panda.js
      panda.css
```

Recommended project properties:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>$(ProjectNetFramework)</TargetFramework>
    <OutputType>Library</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <ePluginPkgLight>true</ePluginPkgLight>
    <ePluginPkgDependencies>true</ePluginPkgDependencies>
    <ePluginPkgOutputFolder>$(SolutionDir)plugins\PandA\</ePluginPkgOutputFolder>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="eController.WebUI.PlugIn"
                      VersionOverride="2.0.14" />
    <PackageReference Include="eController.WebUI.Utility"
                      VersionOverride="4.0.16" />
    <PackageReference Include="eController.WebUI.Library" />
    <PackageReference Include="eController.Mfc.Library" />
    <PackageReference Include="MudBlazor"
                      VersionOverride="6.19.0" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="PandaScreens.Tests" />
  </ItemGroup>
</Project>
```

Each page should follow:

```razor
@inherits FrameworkComponent
@attribute [MenuGroupItem(250, "PandA", "print", "", true)]
@attribute [MenuItem("PandA Dashboard", "dashboard", "", true, 0)]
@attribute [Authorize(Policy = PandaPolicies.Dashboard)]
@page "/PandA"
```

And `Startup.cs` should be public:

```csharp
public sealed class Startup : ServerExtensionService
{
    public Startup(IServiceCollection services)
    {
        services.AddAuthorizationCore(...);
        services.AddSingleton<IPandaDashboardService, PandaDashboardService>();
        services.AddSingleton<IPandaConfigExplorerService,
                               PandaConfigExplorerService>();
        services.AddScoped<IPandaLineSimulationService,
                            PandaLineSimulationService>();
    }
}
```

The core porting risks are not the MudBlazor markup. They are:

1. Building the package for the host TFM.
2. Producing both light and dependency ePlugins.
3. Avoiding duplicate host framework DLLs.
4. Preserving controller-scoped data access.
5. Providing interactive server rendering.
6. Clearing the host’s stale extracted `Content` directory.
7. Testing real service payloads and authorization behavior rather than only rendered markup.
