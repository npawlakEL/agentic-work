# Exol eController Knowledge Base — PandA Plugin Briefing

**Repository reviewed:** `C:\Users\NoahPawlak\.copilot\session-state\be5b169f-b89b-4f3c-9dee-ff9a3abeaede\files\exol`  
**Purpose:** Guidance for building a new PandA NuGet/eController plugin for PLC-driven print/apply/verify on conveyor lines, with Blazor screens, configuration, and multi-host shared-DB operation.

---

## 1. eController glossary

### Core runtime and topology

| Term | Definition | Citation |
|---|---|---|
| **TwinCAT** | Beckhoff PLC runtime controlling conveyor hardware and exposing named PLC symbols/tags through ADS. | `.project/architecture-log/2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §0–1 |
| **PLC** | Physical conveyor controller. The current topology has PLCs `01` and `02` hosted by one logical MFC controller. | Same, §3; `.project/architecture-log/ADR-003-multi-controller-gate-1-5-decisions.md` |
| **ADS** | Beckhoff Automation Device Specification transport used between eHub and TwinCAT. Logical PLC task port is normally `851`; AMS routing/TCP is separate. | `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §1 |
| **AMS Router** | TwinCAT routing layer through which ADS clients reach a PLC by AMS NetId. Static routes and bidirectional network reachability are required. | Same, §1 |
| **eHub / IntegrationHub** | `eController.IntegrationHub.exe`; host process that loads connector plug-ins and transfers telegrams between PLCs, simulations, host systems, and the MFC. | Same, §0–2 |
| **eHub connector** | An eHub plug-in that handles a transport boundary, such as ADS, Emulate3D, eManager, or ELWS/DTC. | Same, §2 |
| **eplugin** | The deployable plug-in package format used by eController/eHub hosts. The `@light` package is the effective executable plug-in unit; `@deps` contains dependencies. | `.project/architecture-log/eplugin-build-guide.md`; `.agent/skills/exol-eplugin-dll-swap.md` |
| **eController / MFC** | The routing brain. It stores transport orders, runs action chains, evaluates criteria, and determines destinations. | `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §0 and §3 |
| **MFC** | Material Flow Controller; the eController routing subsystem/controller instance. | Same, §0 |
| **Controller** | A configured MFC instance. In the multi-controller UI model it is the tuple `{connection, schema, config folder}`. | `.project/architecture-log/ADR-003-multi-controller-gate-1-5-decisions.md`, Context |
| **DTC** | Host/external-comms bridge for WMS, ELWS, eManager, and related business data. It enriches transport orders with business criteria; ADS owns physical PLC routing. | `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §0 and §4 |
| **DTC message** | A host command such as `CreateMfcTransportOrderV1`, keyed by handling-unit/LPN identity and carrying `AdditionalData` extensions. | Same, §4 and §4a |
| **Shared schema** | Database schema used by a controller. In the consolidated topology, both PLC flows and DTC use controller/schema `CV`. | Same, §3; `ADR-003`, Decisions 1–3 |
| **OriginPlcName** | Per-order stamp identifying the source PLC (`01` or `02`) so outbound responses return through the correct hook. | `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §3 |
| **Hook** | Named connector endpoint registered by the controller; ADS has one hook per PLC and DTC has one host hook. | Same, §2–3 |
| **MP / message point** | Logical conveyor decision or communication point. The MFC derives MP values from telegram type, device, and connector offset. | Same, §3 |
| **Place / PlaceID** | Configured physical destination location, usually a sorter/lane or routing point. Criteria resolve to PlaceIDs. | `spec.md`; `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §3 |
| **Lane** | Physical destination lane represented by a configured PlaceID. Lane status controls whether it is enabled/full/available. | `spec.md`; `.project/vision/vision-plcsim-integration.md` |
| **LaneStatus** | Persisted/configured state for a lane, including enabled/full status and parent sorter/place relationships. | `2026-08-04-routing-sorter-decision-tree.md`, Gate 2 |
| **Sorter** | Physical sorting device or routing group containing lanes. | `spec.md`; `2026-08-04-routing-sorter-decision-tree.md` |
| **Telegram** | XML/structured message exchanged between PLC/eHub/MFC. Important types include `201`, `203`, `204`, `205`, `1201`. | `spec.md`; `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §3 |
| **Telegram 201 / DestinationRequest** | Inbound PLC scan/request that creates or updates the physical transport-order flow. | `spec.md`; `2026-08-04-routing-sorter-decision-tree.md`, “DTC → 201 → 1103 seam” |
| **Telegram 203** | Inbound telegram mapped to the `2000+…` MP range. | `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §3 |
| **Telegram 204** | Carton recirculation telegram mapped to the `3000+…` MP range. | Same, §3 |
| **Telegram 205** | Lane-status telegram. Its MFC MP is literally `"205"`; sorter/lane values are normalized separately. | Same, §3 |
| **Telegram 1201** | Outbound PLC destination assignment. MFC computes destination; ADS converts it to the native PLC response. | Same, §3 |
| **Telegram number offset** | Per-PLC arithmetic used to avoid MP collisions. Current examples: CP1 offset `100`, CP2 offset `200`; offset applies uniformly to device, sorter, and lane. | Same, §3 |
| **MfcTransportOrder** | Persisted MFC record representing a carton/transport unit and its routing state/extensions. | Same, §3–4 |
| **TuId** | Transport-order identity. ADS uses `Label1`; DTC uses `HuName`; both represent the same LPN/carton identity. | Same, §4 |
| **FinalDestMp** | MFC-computed final destination. Outbound conversion uses `FinalDestMp ?? DestMp`. | Same, §3 |
| **DestMp** | Current or default destination MP on a transport order. | Same, §3 |
| **MfcAction** | Configuration describing the ordered action chain executed at an MP. | `spec.md`; `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §3 |
| **MfcAction sequence** | Numeric execution order for actions. Routing examples use reset/handler/process/send stages. | Same; `2026-08-04-routing-sorter-decision-tree.md`, Decisions |
| **CriteriaAction** | Action that computes or assigns a criterion to a transport order. | `2026-08-04-routing-sorter-decision-tree.md` |
| **CriteriaActionsCustom** | Custom action implementation surface used for project-specific criteria computation, such as `RouteToteHandler`. | Same |
| **ProcessCriteriaWithLaneStatus** | Generic action that maps criteria to configured lanes while considering lane status. It should remain a pure mapper where possible. | Same, Gate 2 |
| **Criteria** | A typed routing fact, e.g. `Exception=Reject`, `DestinationLane=ManualPack`, `Weight=Fail`, `Store=STR03`. | `spec.md`; `2026-08-04-routing-sorter-decision-tree.md` |
| **CriteriaData** | Persisted/configured criterion row containing type/value and destination mapping. | `.project/vision/vision-plcsim-integration.md`; `spec.md` |
| **CriteriaConfig.json** | MFC routing configuration mapping criterion type/value combinations to PlaceIDs. | `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §3 |
| **CriteriaValues.json** | UI master list of valid criterion values. It is consumed by the lane-config WebUI, not by the MFC routing engine. | `2026-08-04-routing-sorter-decision-tree.md`, “CriteriaValues.json porting” |
| **SortCriteriaExtension** | Transport-order extension containing WMS/business sorting criteria. DTC attaches it through keyed `AdditionalData`. | Same; `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §4 |
| **SortCriteria** | Individual criterion within `SortCriteriaExtension`, typically `{Type, Value}`. | `2026-08-04-routing-sorter-decision-tree.md` |
| **DynamicField** | Dynamically persisted MFC data field/table data used by the framework’s configurable persistence model. | `ADR-003`; `.project/vision/vision-plcsim-integration.md` |
| **PersistantTable** | Framework abstraction for DB-backed dynamic/configurable tables, commonly registered with `AddDbPersistantTable()`. The spelling is framework terminology. | `ADR-003`; `.project/vision/vision-plcsim-integration.md` |
| **ITableProvider** | Framework table-access abstraction used by configuration screens and dynamic tables. Multi-controller work replaces one captured provider with per-controller sessions. | `ADR-003`, Decisions 2–3 |
| **MfcDbContext** | EF/database context for MFC data. Its schema must be tied to the selected controller. | `ADR-003` |
| **MfcLog / EventLog** | NLog/MFC event-log data read from the controller’s schema; it is not a `PersistantTable` entity. | `.project/planning-sessions/2026-07-28-regroup-2.md`; `ADR-001` |
| **ExtensionKey** | Attribute/registry mechanism used to identify and deserialize transport-order extensions from keyed JSON. | `2026-08-04-routing-sorter-decision-tree.md`, “DTC → 201 → 1103 seam” |
| **AdditionalData** | DTC field containing one JSON string/envelope of named extensions. | `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §4a |
| **Extension prefill** | Pattern where an extension key is present, even with `{}`, so downstream processing sees a materialized extension and can safely populate it. | `2026-07-27-extension-prefill-pattern.md` |
| **Transport-order extension** | Keyed, serializable data attached to an MFC transport order, such as destination request, weight, sort criteria, or origin metadata. | `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §3–4 |
| **CreationDate** | Original creation timestamp for a persisted transport order. Must not be overwritten on edits. | `.project/learnings/010-mfc-retention-purge-and-changedate-invariant.md` |
| **ChangeDate** | Edit timestamp used by retention/purge logic. Must be touched and persisted on every application-side edit. | Same |
| **TouchChanged()** | Required application-side helper/invariant that preserves `CreationDate` and updates `ChangeDate`. | Same |
| **Retention purge** | Bounded cleanup of old MFC rows using effective age and explicit scheduling windows. | `.project/learnings/010-mfc-retention-purge-and-changedate-invariant.md`; `docs/mfc-retention-purge-ui.md` |

### PandA-specific mapping

For PandA, the expected model is:

- A PLC scan/trigger creates or updates the transport order.
- DTC/WMS supplies label/business data through extensions.
- PandA actions calculate print/apply/verify state and destination criteria.
- A generic criteria processor maps resulting criteria to configured lanes.
- PLC-facing output is sent through the origin PLC hook.
- Blazor screens edit the correct controller’s configuration folder and shared DB schema.

The existing routing-sorter implementation is the closest documented pattern.

---

## 2. Enforced rules, invariants, and conventions

### Transport-order and routing invariants

1. **DTC enriches; ADS owns physical routing.**  
   DTC must not set `FinalDestMp` or directly determine the PLC destination. It merges business criteria; the ADS/201 flow and MFC criteria actions compute the destination.  
   **Citation:** `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §4; `2026-07-30-dtc-handler-port.md`; BM-DTC-ADS-BOUNDARY.

2. **Join DTC and PLC data by the same carton identity.**  
   DTC `HuName` maps to `TuId`; ADS `Label1` maps to the same `TuId`.  
   **Citation:** `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §4.

3. **DTC-before-201 and 201-before-DTC must converge functionally, but concurrent creation is not proven safe.**  
   There is no unique `TuId` index or atomic upsert guarantee.  
   **Citation:** Same, §4 and “Known open threads.”

4. **201 creates/updates the physical TO; DTC enriches an existing or newly found TO.**  
   A missing `SortCriteriaExtension` at routing time means DTC enrichment did not arrive; the routing sorter treats this as order unknown.  
   **Citation:** `2026-08-04-routing-sorter-decision-tree.md`, “Verified: order unknown detection.”

5. **Extension merges are keyed and non-destructive.**  
   201 extension copying is per-key upsert, not clear-and-replace; DTC `SortCriteriaExtension` survives the subsequent 201 merge.  
   **Citation:** Same, “DTC → 201 → 1103 seam verified.”

6. **Reject must remain a criterion when downstream behavior must see it.**  
   Use `Exception=Reject` as a mapped criterion rather than only calling `SetFinalDest`; this prevents later finalization logic from overwriting the exception.  
   **Citation:** `2026-08-04-routing-sorter-decision-tree.md`, Gate 2.

7. **Action order is contractual.**  
   Routing actions must have unique sequence numbers and run in the intended order. The model is: compute/enrich criterion, process criteria/lane status, send PLC response.  
   **Citation:** Same, Decisions and Gate 3; `spec.md`.

8. **Exactly one routing criterion should be emitted where the action owns a single route decision.**  
   The routing sorter emits exactly one of `Exception=Reject` or `DestinationLane={AutoCMC, ManualPack, ManualCMC, Recirc}`.  
   **Citation:** `2026-08-04-routing-sorter-decision-tree.md`, Gate 2 and Gate 3.

9. **Use stable physical identity for recirculation.**  
   Recirculation is keyed by the stable 201 `Label1` LPN, not a per-loop `RecId` or re-minted transport-order ID.  
   **Citation:** Same, Gate 3.

10. **Lane-status behavior must be explicit.**  
    The documented routing implementation fails open when no lane-status rows exist, but production must seed the rows or overflow handling becomes inert.  
    **Citation:** Same, Gate 2; `2026-08-04-lane-status-missing-row-enabled.md`.

11. **Criteria configuration and MFC action configuration must agree.**  
    Criterion names must appear consistently in the MfcAction criterion array, handler logic, and `CriteriaConfig.json`; values must be represented in `CriteriaValues.json` for UI editing.  
    **Citation:** `spec.md`; `2026-08-04-routing-sorter-decision-tree.md`; `.project/learnings/004-config-domain-exhaustiveness.md`.

12. **Config-driven domains must be exhaustive.**  
    Do not test only the values mentioned in a ticket. Enumerate all MPs, telegram types, actions, lanes, and mappings from their authoritative configuration and prove coverage/disjointness.  
    **Citation:** `.project/learnings/004-config-domain-exhaustiveness.md`; `.agent/skills/senior-coder-checklist.md`.

13. **Config structural errors fail closed.**  
    Missing config folder, missing `controllers.json`, or schema absent from the folder’s controller keys must fail startup rather than silently selecting a wrong controller.  
    **Citation:** `ADR-003`, Decision 1.

14. **Controller identity is explicit, not ambient.**  
    Every operation carries an immutable `ControllerId`; services must not read a shared “current controller.”  
    **Citation:** `ADR-003`, Decisions 2–3.

15. **DB writes target the selected controller’s schema and folder.**  
    Before destructive writes, validate the session, schema/table existence, and that the selected sorter/place belongs to the selected controller configuration.  
    **Citation:** `ADR-003`, Decision 7.

16. **DB is the hot-reload source; JSON is the restart source.**  
    Perform DB transaction first; only backfill JSON after commit. DB and JSON are not atomic with each other, so JSON failure must be surfaced clearly.  
    **Citation:** `ADR-003`, Decision 7; `.project/vision/vision-plcsim-integration.md`.

### Audit and retention rules

17. **`CreationDate` is create-only; `ChangeDate` changes on every edit.**  
    Because the entity is in a compiled third-party NuGet, this is an application-side invariant implemented at every edit site.  
    **Citation:** `.project/learnings/010-mfc-retention-purge-and-changedate-invariant.md`.

18. **Effective age is `max(ChangeDate, CreationDate)`.**  
    Legacy rows can have `ChangeDate == DateTime.MinValue`; using only `ChangeDate` would purge valid historical data.  
    **Citation:** Same.

19. **Retention requires both a time window and an interval.**  
    An interval alone causes a supposedly nightly job to run throughout the day.  
    **Citation:** Same; `docs/mfc-retention-purge-ui.md`.

20. **Use one purge predicate for selection and deletion re-check.**  
    This closes races between selecting and deleting rows.  
    **Citation:** `.project/learnings/010-mfc-retention-purge-and-changedate-invariant.md`.

### Blazor/UI rules

21. **Interactive render mode is mandatory for interactive screens.**  
    `App.razor` must set `@rendermode="RenderMode.InteractiveServer"` on `<Routes>` and `<HeadOutlet>` where required. Static HTML can look rendered but remain unusable.  
    **Citation:** `.project/learnings/001-blazor-config-path-and-rendermode.md`.

22. **Resolve config paths from ASP.NET content root.**  
    Do not use `AppContext.BaseDirectory` for project-relative configuration; use `builder.Environment.ContentRootPath`.  
    **Citation:** Same.

23. **Startup validation must check config dependencies.**  
    Log resolved paths and file existence; fail fast for required configuration. Add a real-config smoke test, not only temp-directory unit tests.  
    **Citation:** Same.

24. **A controller dropdown is per-tab state.**  
    Selection must not leak between EventLog, Lane Configuration, and System Values. Switching controllers requires stale-load cancellation and an unsaved-edit guard.  
    **Citation:** `ADR-003`, Decisions 5–7.

25. **Authorization is policy-based.**  
    Screen access is enforced through `IAuthorizationService`, not by hiding the host navigation menu.  
    **Citation:** `docs/mfc-screen-access-control.md`; `2026-09-22-mfc-screen-access-control.md`.

### Build/package/deployment rules

26. **Check target frameworks before adopting JS-interop NuGets.**  
    A package can compile through forward compatibility while its JavaScript silently fails. Prefer a package targeting the host runtime natively.  
    **Citation:** `.project/learnings/002-nuget-tfm-js-interop-compat.md`.

27. **The deployed eController MFC host is net9 even if tests are net10.**  
    The deployed DLL must target the host runtime.  
    **Citation:** `.agent/skills/exol-eplugin-dll-swap.md`.

28. **Pin contracts directly in consumers.**  
    A transitive dependency can override a central package pin. Add a direct `PackageReference` and verify the shipped DLL by identity/hash, not just version text.  
    **Citation:** `.project/learnings/006-ehub-connector-nupkg-deploy-and-dtc-pin.md`.

29. **Deploy eHub packages additively with higher versions.**  
    The host selects the highest package version; rollback is removal of the newly added package. Avoid in-place overwrites.  
    **Citation:** Same; `.agent/skills/exol-ehub-connector-nupkg-deploy.md`.

30. **Use Verify-then-Deploy.**  
    The user runs a read-only verifier first; deployment proceeds only after the output is reviewed.  
    **Citation:** Same; `.agent/skills/exol-ehub-connector-nupkg-deploy.md`.

31. **Deploy scripts must enforce guards themselves.**  
    Verify payload hashes, refuse a running host, enforce path ownership, back up, write atomically, and support rollback.  
    **Citation:** Same; `.agent/skills/exol-plugin-deploy-host-config-guards.md`.

32. **Do not run deployment scripts as the agent.**  
    The user executes them on the target box and returns verification output.  
    **Citation:** `.agent/skills/exol-config-only-connector-deploy.md`; `.agent/skills/exol-eplugin-dll-swap.md`.

---

## 3. Documented pitfalls and learnings that can hurt PandA

- **Static Blazor page mistaken for working UI.**  
  Missing interactive render mode causes empty/non-functional MudBlazor controls even though server initialization and build succeed.  
  **Citation:** `.project/learnings/001-blazor-config-path-and-rendermode.md`.

- **Wrong config path silently produces empty screens.**  
  `AppContext.BaseDirectory` resolves beneath `bin`; relative config paths must use the ASP.NET content root.  
  **Citation:** Same.

- **Unit tests do not prove real config loading.**  
  Temp-directory tests can pass while deployed config paths are wrong. Add a real-folder smoke test.  
  **Citation:** Same.

- **JS interop can fail silently under TFM forward compatibility.**  
  A net8-only library may compile on net9 but not initialize its JavaScript runtime.  
  **Citation:** `.project/learnings/002-nuget-tfm-js-interop-compat.md`.

- **The PLC notification model is edge-based, not level-based.**  
  ADS notifications fire once on subscription and then on change; handshake logic that expects repeated level notifications can process only once per restart.  
  **Citation:** `.project/learnings/005-ads-vendor-client-handshake-and-online-change.md`; `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §1.

- **PLC symbol names are contracts.**  
  A tag-name mismatch such as `.Sending` versus `.Buffer` can look like a transport failure.  
  **Citation:** Same.

- **ADS connections can drop after idle NAT timeouts.**  
  Keepalive/reconnect handling is required; the toolbox TCP path has no automatic retry/reconnect by default.  
  **Citation:** `.project/learnings/003-toolbox-tcp-no-retry-reconnect.md`; `.project/learnings/007-ads-ams-idle-connection-drop-nat-keepalive.md`.

- **Online-change detection caused connection cleanup failures.**  
  Vendor online-change handling can trigger NREs; the documented mitigation was disabling `DetectOnlineChange`, with a guarded vendor-client fix deferred.  
  **Citation:** `.project/learnings/005-ads-vendor-client-handshake-and-online-change.md`; `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, “Known open threads.”

- **PLC bounce can crash or spam offline reads.**  
  ADS read loops need safe offline handling and bounded logging.  
  **Citation:** `.project/learnings/009-ads-plc-bounce-crash-and-offline-read-errspam.md`.

- **Batch-level ADS acknowledgement can replay earlier telegrams.**  
  If parsing/sending a later item fails before the batch Ack, the PLC may replay the whole batch.  
  **Citation:** `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §2.

- **Network/AMS route failures are not necessarily code defects.**  
  Missing bidirectional static routes or AMS/TCP reachability produces `ReadDeviceState` timeouts.  
  **Citation:** Same, §1.

- **DTC producer is not actually present in this repository.**  
  The runtime WMS/eManager producer and connector configuration are deployment-specific. The code-verifiable boundary is the MFC DTC hook.  
  **Citation:** Same, §2 and §4; `2026-07-30-dtc-handler-port.md`.

- **A DTC message does not necessarily create the physical TO in the expected order.**  
  The validated flow is 201 creates/updates the physical order and DTC enriches it; unknown HU handling currently rejects in some paths.  
  **Citation:** `2026-08-04-routing-sorter-decision-tree.md`; `2026-07-30-dtc-handler-port.md`.

- **Concurrent DTC/PLC creation is not guaranteed safe.**  
  There is no proven unique `TuId` constraint or atomic upsert.  
  **Citation:** `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §4.

- **DTC must not own destination assignment.**  
  Consuming DTC `destinations` or setting `FinalDestMp` would violate the ADS/MFC ownership boundary.  
  **Citation:** Same, §4a.

- **Extension prefill is easy to miss.**  
  Some extension processing requires the extension key to exist, even as `{}`.  
  **Citation:** `2026-07-27-extension-prefill-pattern.md`.

- **Generic extension copying is not always a replacement.**  
  201 merge is per-key upsert; assuming clear-and-copy can cause criteria loss bugs.  
  **Citation:** `2026-08-04-routing-sorter-decision-tree.md`, “DTC → 201 → 1103 seam verified.”

- **A criterion name can exist in code but not be usable.**  
  It must be aligned across `MfcAction.json`, `CriteriaConfig.json`, handler logic, and UI `CriteriaValues.json`.  
  **Citation:** `.project/learnings/004-config-domain-exhaustiveness.md`; `2026-08-04-routing-sorter-decision-tree.md`.

- **Testing only known MPs is not exhaustive.**  
  A new terminal or scan point can still reach an unmapped branch. Tests must load the authoritative layout and prove every MP is bound or explicitly terminal.  
  **Citation:** `.project/learnings/004-config-domain-exhaustiveness.md`.

- **Missing lane-status rows silently disable overflow behavior.**  
  The routing implementation fails open on zero rows; production must seed all relevant lanes.  
  **Citation:** `2026-08-04-routing-sorter-decision-tree.md`, Gate 2; `2026-08-04-lane-status-missing-row-enabled.md`.

- **Recirculation counts can be inert if the wrong PLC path owns increments.**  
  The original count path was CV2-only, so routing on PLC01 never incremented. PandA should own its lifecycle if it owns the recirculation decision.  
  **Citation:** `2026-08-04-routing-sorter-decision-tree.md`, Gate 3.

- **Per-loop IDs are not stable carton identity.**  
  Recirculation must use a stable LPN/Label1 key rather than `RecId` or transport-order ID.  
  **Citation:** Same.

- **`ChangeDate` was not maintained automatically.**  
  Vendor-owned entities require explicit app-side touch logic.  
  **Citation:** `.project/learnings/010-mfc-retention-purge-and-changedate-invariant.md`.

- **Legacy rows have zero `ChangeDate`.**  
  Retention must use `max(ChangeDate, CreationDate)`.  
  **Citation:** Same.

- **A purge interval is not a production schedule.**  
  Add a permitted off-peak window.  
  **Citation:** Same.

- **EF InMemory is insufficient for all persistence tests.**  
  It cannot translate `ExecuteDeleteAsync`; retention/delete tests need real SQLite or equivalent.  
  **Citation:** Same.

- **The library has no simple in-memory controller test seam.**  
  `MfcCrudContext` is abstract and runtime bootstrap generates the real context. Tests currently use reflection/workarounds and cannot execute the real packaged action end-to-end.  
  **Citation:** `.project/backlog/mfc-library-inmemory-test-seam-request.md`.

- **Transitive DTC package versions can break completion messages.**  
  The wrong DTC DLL can cause reflection/type resolution failures and endless outbound retry loops.  
  **Citation:** `.project/learnings/006-ehub-connector-nupkg-deploy-and-dtc-pin.md`.

- **A package version string alone is not enough.**  
  Verify DLL size/hash and actual deployed contents.  
  **Citation:** Same.

- **In-place deployment destroys rollback confidence.**  
  Use additive versioned packages and preserve previous versions.  
  **Citation:** Same; `.agent/skills/exol-ehub-connector-nupkg-deploy.md`.

- **Screens can be deployed to the wrong host/cache.**  
  The real deploy unit is the `@light` eplugin; restart/cache behavior matters.  
  **Citation:** `.agent/skills/exol-eplugin-dll-swap.md`; `.agent/skills/exol-screens-eplugin-deploy.md`.

- **Test suites contain known parallel-load flakes.**  
  ADS seam tests and two-hook tests may fail in the full suite but pass in isolation; classify known flakes before treating them as regressions.  
  **Citation:** `.project/learnings/010-mfc-retention-purge-and-changedate-invariant.md`.

- **Trimmed standalone sidecars can remove required runtime pieces.**  
  Verify publishing/trimming assumptions before using a standalone sidecar architecture.  
  **Citation:** `.project/learnings/008-standalone-sidecar-publish-trim.md`.

---

## 4. Criteria-based sorting plugin: documented model for PandA

The documented criteria-based sorter is the recommended architectural model for PandA.

### 4.1 How it is consumed

The routing sorter is consumed through the normal MFC action pipeline:

1. A PLC sends a 201 `DestinationRequest`.
2. ADS converts the PLC device to an MFC MP, applying the configured connector offset.
3. The MFC loads `MfcAction.json` for that MP.
4. Actions execute in sequence.
5. A custom action calculates or assigns a criterion.
6. `ProcessCriteriaWithLaneStatus` maps the criterion to a configured destination.
7. `SendRespToPlc` emits the result through the order’s `OriginPlcName`.
8. ADS converts the MFC destination back into a native 1201 response.

**Citation:** `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §3; `2026-08-04-routing-sorter-decision-tree.md`, Decisions.

### 4.2 The two-action pattern

The routing-sorter implementation deliberately uses two actions:

- **Sequence 2 — custom decision action:**  
  `CriteriaActionsCustom.RouteToteHandler` evaluates the complete decision tree and assigns exactly one routing criterion.

- **Sequence 3 — generic criteria action:**  
  Existing `ProcessCriteriaWithLaneStatus` maps that criterion through `CriteriaConfig.json` and lane status.

- **Sequence 1 — preceding data action:**  
  `WeightActions.ProcessWeight` may calculate `Weight=Fail` before the custom route action.

This separation keeps the custom action responsible for business decision-making and the generic action responsible for criterion-to-place mapping.

**Citation:** `2026-08-04-routing-sorter-decision-tree.md`, Context, Decisions, and Gate 2.

### 4.3 Inputs

The sorter consumes criteria from multiple sources:

- `SortCriteriaExtension` from DTC/WMS, e.g. store, tote type, order type, VAS.
- Criteria generated by prior MFC actions, e.g. weight failure.
- Lane status from persisted lane-status rows.
- Physical identity/state from the transport order.
- Recirculation state from a stable LPN key.

All tree inputs are represented as `SortCriteria` or existing transport-order state; the route handler does not introduce a new telegram field or separate DB lookup for the basic decision.

**Citation:** `2026-08-04-routing-sorter-decision-tree.md`, Decisions and “Verified: order unknown detection.”

### 4.4 Criteria and configuration

The key configuration artifacts are:

- **`MfcAction.json`** — action chain and criterion array for each MP.
- **`CriteriaConfig.json`** — criterion type/value to PlaceID mapping used by the MFC.
- **`CriteriaValues.json`** — UI-facing list of valid criterion values.
- **`LaneStatus` table/config** — enabled/full state for destinations.
- **`controllers.json`** — controller/schema/config-folder identity.
- **`PlaceAliases.json`** — human-readable names for MPs/places.

`CriteriaValues.json` is not used by the MFC routing engine. It is read by `LaneConfigurationService` for UI dropdowns. The routing engine consumes `CriteriaConfig.json` and `CriteriaData`.

**Citation:** `2026-08-04-routing-sorter-decision-tree.md`, “CriteriaValues.json porting”; `.project/vision/vision-plcsim-integration.md`.

### 4.5 DTC consumption

DTC carries business sorting intent in `AdditionalData`:

```json
{
  "SortCriteriaExtension": {
    "SortCriterias": [
      { "Type": "Store", "Value": "STR03" }
    ]
  }
}
```

The DTC handler:

- Joins on `HuName`/`TuId`.
- Parses the keyed extension envelope.
- Uses `ExtensionKeyRegistry`/`[ExtensionKey]`.
- Merges `SortCriteriaExtension` by criterion type.
- Preserves MFC-owned criteria such as `Exception` and `DestinationLane`.
- Skips unknown or individually malformed extensions while accepting the broader message.
- Does not set `FinalDestMp`.

**Citation:** `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §4; `2026-08-04-routing-sorter-decision-tree.md`.

### 4.6 Order-unknown behavior

A 201 creates the physical routing request. DTC enrichment is expected to already be attached or to arrive on the same persisted TO. If no `SortCriteriaExtension` exists, or it contains no criteria, the route handler treats the carton as order-unknown and assigns:

```text
Exception = Reject
```

That criterion is then mapped to the configured reject destination.

**Citation:** `2026-08-04-routing-sorter-decision-tree.md`, “Verified: order unknown detection.”

### 4.7 Output contract

The route handler emits exactly one criterion:

```text
Exception=Reject
```

or:

```text
DestinationLane=AutoCMC
DestinationLane=ManualPack
DestinationLane=ManualCMC
DestinationLane=Recirc
```

The generic criteria processor maps the selected criterion to a PlaceID. The MFC then writes `FinalDestMp`, and the ADS connector generates the 1201 response.

**Citation:** `2026-08-04-routing-sorter-decision-tree.md`, Gate 2.

### 4.8 Routing decision ordering

The documented routing tree locks the following order:

1. Reject conditions.
2. VAS branch.
3. Batched singles / Manual CMC.
4. Returnable / Manual Pack.
5. Auto CMC.
6. Overflow and recirculation.

The order matters. For example, batched singles must be evaluated before the returnable rule to avoid routing a returnable batched-single tote incorrectly.

**Citation:** Same, “Regroup resolution” and locked evaluation order.

### 4.9 Lane-status and overflow behavior

Overflow uses lane status for the relevant sorter:

- If target lane is available, route there.
- If target lane is full/disabled, fall through to the configured overflow destination.
- Recirculation has a threshold.
- Over-threshold behavior falls back to NI/exception behavior.
- Zero lane-status rows are fail-open, meaning overflow logic is inert; production must seed rows.

**Citation:** Same, Gate 2 and Gate 3; `2026-08-04-lane-status-missing-row-enabled.md`.

### 4.10 Testing model

The routing-sorter test model included:

- Data-driven decision matrix.
- Exact-one-criterion assertions.
- Criteria/config parity checks.
- No-criteria/order-unknown tests.
- DTC → persisted TO → 201 merge → action execution tests.
- Recirculation self-increment tests.
- Recirculation reset-on-success tests.
- Missing LPN-key behavior.
- Overflow/no-status behavior.
- Full-suite verification.

The final documented state reports 22 route-handler cases and 272/272 full-suite tests at Gate 3/DTC seam verification.

**Citation:** `2026-08-04-routing-sorter-decision-tree.md`, Gate 2, Gate 3, and “DTC → 201 → 1103 seam verified”; `.project/reviewer-log/2026-08-04-gate3-routing-sorter.md`.

### 4.11 PandA adaptation

PandA should preserve this shape:

```text
PLC trigger / scan
    ↓
MfcAction sequence
    ↓
PandA custom action:
  - resolve label template/data
  - print
  - apply
  - verify
  - assign Print/Apply/Verify criteria or exception
    ↓
generic criteria/lane mapping
    ↓
FinalDestMp
    ↓
1201 response through OriginPlcName
```

Recommended PandA criteria include project-defined values such as:

- `PrintStatus=Success|Failed`
- `ApplyStatus=Success|Failed`
- `VerifyStatus=Passed|Failed|NoRead`
- `LabelType=...`
- `Exception=Reject|Reprint|ManualIntervention`
- `DestinationLane=...`

The action should emit a single authoritative route criterion and preserve diagnostic status as extensions or criteria that downstream screens/event logs can inspect.

---

## 5. Harness and process conventions to mirror

### 5.1 Agent roles

The repository uses a gated Planner → Coder → Senior Coder → Reviewer → Learner flow.

| Role | Responsibility | Citation |
|---|---|---|
| **Orchestrator** | Classifies every request, enforces gates, routes work, prevents undocumented decisions, and controls review loops. | `.agent/roles/orchestrator.agent.md`; `.agent/agents.md` |
| **Planner** | Elicits requirements, asks unresolved questions, maintains planner tasks, and produces a user-approved specification. | `.agent/roles/planner.agent.md` |
| **Senior Coder** | Decides technical HOW, architecture, feasibility, tradeoffs, and deep review issues. | `.agent/roles/senior-coder.agent.md`; `.agent/model-config.md` |
| **Coder** | Implements the approved plan using TDD and project patterns. | `.agent/roles/coder.agent.md` |
| **Reviewer** | Runs tests, checks coverage, exercises the rendered UI, validates deployment/config behavior, and reports defects. | `.agent/roles/reviewer.agent.md`; `.agent/agents.md` |
| **Learner** | Captures reusable lessons, updates architecture/docs/skills candidates, and records what differed from plan. | `.agent/roles/learner.agent.md` |

### 5.2 Gated workflow

The workflow requires:

1. Request classification.
2. Planner requirements discovery.
3. Senior Coder consultation for technical decisions.
4. Explicit user approval of the specification.
5. Coder implementation with failing tests first.
6. Senior Coder review/triage.
7. Reviewer validation of code, tests, UI, and deployment assumptions.
8. Fix/review loop until clean.
9. Learner documentation and changelog update.
10. User approval before push/merge.

Ambiguities discovered during implementation pause the flow. The Planner asks the user, updates the specification, and resumes.

**Citation:** `.agent/agents.md`; `.agent/roles/orchestrator.agent.md`; `.agent/roles/planner.agent.md`.

### 5.3 TDD and test expectations

The Coder conventions require:

- Write a failing test first.
- Implement the smallest passing change.
- Refactor while keeping tests green.
- Keep tests near the code they exercise.
- Add component tests for UI behavior.
- Add integration tests for user flows.
- Do not treat a successful build as sufficient.
- Review full configuration domains, not only examples.

**Citation:** `.agent/roles/coder.agent.md`; `.agent/skills/senior-coder-checklist.md`.

For PandA, this means testing:

- PLC/telegram parsing.
- DTC extension merging.
- Print/apply/verify state transitions.
- Retry/idempotency behavior.
- Exact criteria output.
- Lane/destination mapping.
- Multi-host/schema selection.
- Blazor screen interaction.
- Real config-folder loading.
- Net9 host compatibility.
- Deployment package contents.

### 5.4 Taskboard conventions

Taskboard files are feature-level execution plans with scope, decisions, risks, gates, and acceptance criteria.

Important examples:

- `.project/taskboard/README.md`
- `.project/taskboard/003-multi-controller.md`
- `.project/taskboard/005-routing-sorter.md`

A PandA taskboard should separate at least:

1. Requirements and open questions.
2. Architecture/design decisions.
3. Config/data model.
4. MFC action chain.
5. PLC/eHub integration.
6. DTC contract and extension model.
7. Blazor screens.
8. Multi-controller/shared DB.
9. Test strategy.
10. Deployment and rollback.
11. Gate criteria and known risks.

Risks should be explicit, such as:

- Printer idempotency.
- Duplicate print after PLC retry.
- Label verification timing.
- Host/plugin TFM mismatch.
- Multi-host concurrent writes.
- Schema/config-folder mismatch.
- Missing lane-status/config rows.
- Printer-side state not represented in MFC.

### 5.5 Architecture-log conventions

Architecture records are decision documents, not informal notes. The repository uses:

- Date-prefixed files for feature decisions.
- ADR files for major accepted architectural choices.
- A README describing the log format.
- Explicit status, author/context, decision, consequences, open items, and verification.
- Cross-references to taskboard, spec, tests, and backlog.
- Post-review records for findings and fixes.

**Citation:** `.project/architecture-log/README.md`; `ADR-003-multi-controller-gate-1-5-decisions.md`; `2026-08-04-routing-sorter-decision-tree.md`.

PandA should log decisions for:

- Printer protocol and driver ownership.
- Print job identity/idempotency key.
- Label payload source.
- Apply mechanism and sensor/PLC handshake.
- Verify result ownership: printer, scanner, PLC, or MFC.
- Retry and reprint semantics.
- DTC extension schema.
- Criteria names and route ownership.
- Config persistence and hot reload.
- Shared DB concurrency.
- Per-controller screen isolation.
- Deployment artifact structure.
- Net9/net10 compatibility.

### 5.6 Learning/retro conventions

Each learning entry records:

- Date.
- Feature/context.
- Severity.
- Incident or friction.
- Root cause.
- General rule.
- Actionable guidance.
- Related skills/docs.

**Citation:** `.project/learnings/README.md`; `.project/learnings/retro-log.md`.

The PandA team should capture lessons for printer retry semantics, duplicate suppression, PLC reconnect behavior, config migration, screen render mode, and multi-host concurrency.

---

## 6. Open framework limitations and backlog items

### 6.1 No supported in-memory MFC controller test seam

The largest testing limitation is the absence of a clean in-memory `eController.Mfc.Library` host:

- `MfcCrudContext` is abstract.
- Runtime bootstrap generates/registers the concrete context.
- No test-accessible registration exists.
- Current tests use reflection/stub workarounds.
- Tests can match and resolve actions but cannot execute the real packaged action path end-to-end without host/database bootstrap.
- Proposed solution: `MfcControllerTestHost`, `InMemoryMfcCrudContext`, and explicit test registration.

**Citation:** `.project/backlog/mfc-library-inmemory-test-seam-request.md`.

PandA should not assume unit tests prove real host dispatch. Maintain a host-coupled integration test until this seam exists.

### 6.2 No distributed lock or proven atomic upsert

The shared DB flow does not currently provide:

- A unique `TuId` index guarantee.
- Atomic upsert across DTC and PLC creation paths.
- A distributed lock for multiple hosts/controllers processing the same carton.

Concurrent DTC/201 arrivals may therefore race.

**Citation:** `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §4 and “Known open threads.”

For PandA, add an explicit idempotency/concurrency design rather than relying on arrival order.

### 6.3 DB and JSON configuration are not atomic together

The documented write pattern commits DB state first and then writes JSON. A JSON failure after DB commit leaves the DB updated and restart configuration stale.

**Citation:** `ADR-003`, Decision 7.

PandA screens must show this condition clearly and provide reconciliation/reload tooling.

### 6.4 Lane-status fail-open behavior

No lane-status rows cause overflow checks to be bypassed. This is convenient for development but dangerous in production.

**Citation:** `2026-08-04-routing-sorter-decision-tree.md`, Gate 2; `2026-08-04-lane-status-missing-row-enabled.md`.

PandA deployment must validate all required lanes/status rows before enabling production.

### 6.5 DTC producer is external to this repository

The repository documents the DTC contract and MFC handler, but not necessarily the live WMS/eManager producer or deployment connector.

**Citation:** `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §2 and §4.

PandA must identify the real producer, message version, delivery guarantees, and contract package before implementation.

### 6.6 ADS reconnect/online-change limitations

Known limitations include:

- Vendor client idle disconnect behavior.
- TCP/ADS reconnect gaps.
- Online-change cleanup failures.
- PLC bounce/offline read error spam.
- Batch-level acknowledgement risks.
- Environment-specific AMS/TCP routing.

**Citation:** `.project/learnings/003-toolbox-tcp-no-retry-reconnect.md`; `005-ads-vendor-client-handshake-and-online-change.md`; `007-ads-ams-idle-connection-drop-nat-keepalive.md`; `009-ads-plc-bounce-crash-and-offline-read-errspam.md`.

### 6.7 Host/runtime/package compatibility is fragile

The host runtime, test runtime, NuGet TFM, JS assets, DTC contract version, and deployed package contents can all differ.

**Citation:** `.project/learnings/002-nuget-tfm-js-interop-compat.md`; `.project/learnings/006-ehub-connector-nupkg-deploy-and-dtc-pin.md`; `.agent/skills/exol-eplugin-dll-swap.md`.

PandA CI should inspect:

- Target frameworks.
- Runtime host version.
- Direct/transitive contract versions.
- Actual `.eplugin` contents.
- JS static assets.
- DLL hashes.
- Deployment package version ordering.

### 6.8 Retention requires app-side audit maintenance

The framework does not reliably maintain `ChangeDate`; plugins must touch it at every edit site. Retention also requires legacy-row handling and schedule windows.

**Citation:** `.project/learnings/010-mfc-retention-purge-and-changedate-invariant.md`.

### 6.9 Live DTC outbound surface is incomplete

The contract includes accepted/rejected/transmitted events and destination-update commands, but the current build primarily implements inbound `CreateMfcTransportOrderV1`.

**Citation:** `2026-08-21-system-topology-ehub-twincat-econtroller-dtc.md`, §4a.

PandA should define whether print/apply/verify results are MFC criteria, DTC events, transport-order extensions, or a combination.

### 6.10 Shared-schema EF model caching is framework-sensitive

The library provides `DynamicSchemaCacheKeyFactory`, and the documented spike verified schema isolation, but any PandA customization must preserve that registration and include a two-schema write test.

**Citation:** `ADR-003`, Decision 4.

---

## Recommended PandA starting checklist

- Define the PandA transport-order identity and idempotency key.
- Define PLC telegrams and whether PandA uses existing 201/1201 flow or new message types.
- Define DTC `AdditionalData` extensions for label/job/verification data.
- Decide which state is MFC-owned versus PLC/printer-owned.
- Implement a custom PandA action that emits one authoritative criterion.
- Reuse generic criteria/lane processing.
- Add `MfcAction`, `CriteriaConfig`, `CriteriaValues`, and controller-folder parity tests.
- Make all edits call `TouchChanged()`.
- Make Blazor screens interactive and content-root-relative.
- Make all screen services controller-explicit.
- Test both controller schemas and shared-host behavior.
- Add real-host integration coverage because the in-memory seam is missing.
- Verify NuGet TFMs and direct DTC package references.
- Ship additive, versioned eplugins with read-only verification and rollback.
- Record every unresolved technical decision in an architecture-log ADR before implementation.

