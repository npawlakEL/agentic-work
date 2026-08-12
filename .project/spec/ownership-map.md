# File / Module Ownership Map

Purpose: make parallel Coder agents **safe** by assigning disjoint file ownership.
Two agents may run concurrently **only if their file sets do not overlap.**

## Legend
- 🟢 **New file** — created by exactly one feature; no collision risk.
- 🔴 **Shared/hot file** — edited by multiple features; edits MUST be serialized.

---

## Shared "hot" files (collision points — serialize edits)

These existing files are touched by many features. Edits to them cannot be parallelized;
they are the reason the port needs a **foundation-first** sequence before any fan-out.

| File | Touched by | Nature of change |
|------|-----------|------------------|
| 🔴 `TransportOrder.cs` | INBOUND, PROFSW, F15, F23, SETTINGS-2 | Add `WaveId`, `ProfileName`, `Bypass`, `VerifyEnabled`, `VerifyPassDest`, `VerifyFailDest`; `ResetForTrackingEvent`, `ForceMarkPrinted` |
| 🔴 `LineConfig.cs` | SETTINGS, F08, F12, LINECTRL, DYNAP, PROFSW | Add `Lanes`, `PrinterStatusSuffix`, `PlcZone`, `SorterPlcRecId`, `PlcDbName`, `EncoderResolution`, `DynamicPrintPoint`, `ProfileRegistry`, `DefaultProfile`, `FilterLabels` |
| 🔴 `InductService.cs` | F20, F10, F11, F12, PROFSW, DYNAP, F08, F21, F-LOG1, F16, SETTINGS-2 | Inject settings/sinks; quality classify; profile select; sanitize; suffix; routing; slot; run-history |
| 🔴 `CartonAdviceService.cs` | INBOUND, SETTINGS-2, PROFSW, F18, F23, F16 | Accept `AdviceMessage`; gate re-advice on reprint rules; populate xref + wave; store ProfileName |
| 🔴 `VerifyStationService.cs` | F22, F23, F16 | Emit reject records; wave auto-complete hot-path; event emission |
| 🔴 `InductResult.cs` | F08, F21 | Add `DivertLane`, `SlotIndex` |
| 🔴 `VerifyOutcome.cs` (Verification/) | F08 | Add `DivertLane` |
| 🔴 `PrinterConfig.cs` | DYNAP | Add `DefaultApplyDistance`, `LabelWidthInches` |
| 🔴 `LaneEvalService.cs` | LINECTRL, LOCK, F16 | Egress after evaluate; lock guard; events |

**Rule:** All 🔴 edits belong to the **foundation waves** (see dependency-graph.md), executed
sequentially by a single Coder (or a tightly-serialized pair) with Senior review, BEFORE leaf
features fan out. Leaf features then add only 🟢 new files plus, at most, a single well-known
insertion point in a 🔴 file that the foundation already prepared.

---

## Feature → new files (🟢 parallelizable once foundations land)

### Events / recovery / logging cluster
| Feature | New files |
|---------|-----------|
| **F16** logging (see **decision-005**) | Adopt `ILogger<T>` (Microsoft.Extensions.Logging.Abstractions) + structured templates in existing services (🔴 foundation edits); `PandA.Sim/FakeLogger.cs` (🟢). No bespoke sink/event types. |
| **F20** read-quality | `PandA.Core/Induct/InductQualityClassifier.cs`, `CartonStatus.cs`, `IMinGapProvider.cs`; `PandA.Sim/SimMinGapProvider.cs` |
| **F-LOG1** run-history | `PandA.Core/History/CartonRunRecord.cs`, `ICartonRunRepository.cs`; `PandA.Sim/InMemoryCartonRunRepository.cs` |
| **F15** PLC recovery | `PandA.Core/Plc/PlcEventCode.cs`, `PlcEvent.cs`, `PlcEventHandlerService.cs`, `IVerifyDeviceProvider.cs` |
| **F22** reject audit | `PandA.Core/Verification/VerifyReasonCode.cs`, `RejectRecord.cs`, `IRejectHistoryRepository.cs`; `PandA.Sim/InMemoryRejectHistoryRepository.cs` |

### Routing / status / control cluster
| Feature | New files |
|---------|-----------|
| **F08** lane routing | `PandA.Core/Routing/LaneRoutingService.cs`, `LaneDef.cs` |
| **LINECTRL** egress | `PandA.Core/Control/ILinePlcGateway.cs`, `LineControlCommand.cs`; `PandA.Sim/CapturingLinePlcGateway.cs` |
| **F13** status ingestion | `PandA.Core/Status/PrintEngineStatusParser.cs`, `PrintEngineStatusRecord.cs`, `PrinterStatusHandler.cs`, `ZoneStatusHandler.cs`, `IPrintEngineStatusStore.cs`; `PandA.Sim/Messaging` +2, `InMemoryPrintEngineStatusStore.cs` |
| **F12** `~HS` suffix | `PandA.Core/Zpl/ZplStatusSuffix.cs` |

### Labels / fire-point cluster
| Feature | New files |
|---------|-----------|
| **F11** ZPL sanitizer | `PandA.Core/Zpl/ZplSanitizer.cs` |
| **F10** exception labels | `PandA.Core/Labels/ExceptionType.cs`, `LabelTemplate.cs`, `ILabelTemplateRepository.cs`, `ExceptionLabelBuilder.cs`, `IExceptionLabelPolicy.cs`; `PandA.Sim/InMemoryLabelTemplateRepository.cs` |
| **PROFSW** profile switching | `PandA.Core/FirePoints/IProfileStore.cs`; edits to `LineConfig`, `TransportOrder`, `InductService` (foundation) |
| **DYNAP** dynamic apply-point | `PandA.Core/FirePoints/DynamicApplyPointCalculator.cs`, `DynamicApplyPointInput.cs`, `DynamicApplyPointResult.cs` |

### Platform / settings cluster
| Feature | New files |
|---------|-----------|
| **SETTINGS** | `PandA.Core/Settings/ISettingsProvider.cs`, `SettingsDescriptor.cs`, `KnownSettings.cs`; `PandA.Sim/InMemorySettingsProvider.cs` |
| **INBOUND** advice ingestion | `PandA.Core/Advice/AdviceMessage.cs`, `WaveIdParser.cs`, `IHostAdviceAcknowledger.cs` |
| **F18/F24** xref + oLPN | `PandA.Core/XRef/XRef.cs`, `BarcodeType.cs`, `XRefService.cs`, `IXRefStore.cs`; `PandA.Sim/InMemoryXRefStore.cs` |
| **F21** slot index | `PandA.Core/Slots/SlotIndexService.cs`, `ISlotIndexProvider.cs`; `PandA.Sim/InMemorySlotIndexProvider.cs` |
| **F17** purge | `PandA.Core/Purge/PurgeService.cs`, `PurgePolicy.cs`, `IPurgeStore.cs`; `PandA.Sim/InMemoryPurgeStore.cs` |
| **LOCK** concurrency | `PandA.Core/Concurrency/ILockProvider.cs`; `PandA.Sim/InMemoryLockProvider.cs` |
| **F23** wave auto-complete | `PandA.Core/Waves/Wave.cs`, `WaveStatus.cs`, `WaveService.cs`, `IWaveStore.cs`, `IDcmsWaveNotifier.cs`; `PandA.Sim/InMemoryWaveStore.cs` |
| **F14** MandA | `PandA.Core/Manda/MandaInductService.cs`, `MandaVerifyService.cs`, `MandaInductResult.cs`, `IWmsVerifyNotifier.cs` |

---

## Test-file ownership

Each feature owns its own `tests/PandA.Tests/<Feature>Tests.cs` (🟢 new). Existing test files
(`InductServiceTests.cs`, `VerificationServiceTests.cs`, `LifecyclePermutationTests.cs`) are 🔴 —
foundation-wave edits only, to keep the 231-green baseline intact via additive changes.
