# Decision 022 — Config-model remodel: user-defined orientations, ownership move, screen restructure

Status: Accepted (2026-08-17). Supersedes the field ownership in decision-011 (fire-point/map)
and decision-016 (dynamic apply) for the **UI config contracts** only. Core domain types
(`PrinterConfig`, `LineConfig`, `FirePointProfile`, `ApplyOrientation`) are unchanged; all changes
live in `PandA.UI.Contracts.Config` and its Demo mapping/UI layers.

## Why
Operator feedback: the config screens mixed concerns and duplicated data. Orientation was a
hard-coded Side/Top string; label width/encoder/belt/online-minimum sat on the wrong entity;
the Maps & Fire Points screen was needlessly gated behind a lane dropdown.

## Rulings

### R1 — Orientation becomes a first-class, user-defined entity
New `OrientationDto(OrientationId, Name, MotionKind)` with `ApplyMotionKind { Side, Top, Front }`.
Users create orientations (e.g. "Front Apply"), then attach them to printers. Seed: `Side`→Side,
`Top`→Top. `MotionKind` tells the sim how to animate the applicator; `Front` maps to the closest
existing motion for now — a genuine front-apply applicator motion is a **follow-up** (not this pass).

### R2 — Label definition = identity only
`LabelDefDto(LabelDefId, Name, Description, LabelWidthInches)`. Orientation and print position are
**removed** from label definitions (they live on printer/fire point). Label width **moves here** from
the printer (fixes the printer-owned width duplication).

### R3 — Printer owns hardware placement
`PrinterDto` gains `OrientationId` (ref, replaces the `Orientation` string), `PrintDevice`,
`ApplyDevice`, `PrintPoint`, `DynamicApply`. It **loses** `LabelWidthInches` (→ label def),
`EncoderResolutionInchesPerPulse` and `BeltSpeedInchesPerSecond` (→ line). Tamp kinematics
(`TampMountHeightInches`, `TampSpeedInchesPerSecond`) stay. Print point + device and dynamic apply
are per-printer, with a **Line-page bulk action** to push a print point/device onto every printer.

### R4 — Fire point = mapping + apply point only
`FirePointDto(FirePointId, PrinterId, LabelDefId, ApplyEdge, ApplyInches)`. Selecting the printer
auto-fills orientation + print/apply device + print point + dynamic apply (all read-only, sourced
from the printer). The only fire-point-specific value is the **apply point**, entered as an **edge**
(Leading/Middle/Trailing) + a typed integer, composed to source notation (`1L`, `0M`, `1T`).
`LabelType` string is replaced by `LabelDefId`; the factory resolves it to the label-def Name for the
`FirePointProfile` key and carton buffer order.

### R5 — Line owns belt/encoder + per-orientation minimums, not tracking devices
`LineDto` **loses** `TrackingDevices` (the sim infers eyes from print device + apply device + verify
scanner). The single `OnlineMinimum` int is replaced by **per-orientation minimums**
(`OrientationMinimum(OrientationId, Minimum)`), seeded from the label mapping and editable only under
`ControlPolicy = OnlineMinimum`. Line **gains** `EncoderResolutionInchesPerPulse` and
`BeltSpeedInchesPerSecond`. Online minimums are config-only today (Core lane-eval does not yet consume
them, matching current behavior).

### R6 — Settings are global; encoder propagates to lines
Settings tab is explicitly labeled global. Global `EncoderResolutionInchesPerPulse` seeds new lines
and, when changed, propagates to existing lines' encoder resolution.

### R7 — Maps & Fire Points screen is flat
No lane dropdown. Maps = one flat list; each map is expandable to a table of its fire-point names and
shows an "Active on {line}" badge (derived from any `LineDto.ActiveMapId`). Fire Points = a flat
glossary of all fire points below the maps, with no map grouping.

## Blast radius (no Core changes)
`ConfigDtos.cs`, `IConfigEditors.cs` (add `IOrientationEditor`), `DemoConfigServices.cs`,
`DemoDataStore.cs` (seed), `LineSimulationFactory.cs` (mapping), `ConfigExplorer.razor` (UI),
`ConfigExplorerTests.cs` + any DTO-constructing tests.
