# 006 — Phase-2 UI: MandA station + Config Explorer review

**Slices:** `PandA.UI/Pages/MandaStationScreen.razor` (UI-MANDA), `Pages/ConfigExplorer.razor` + `Pages/ConfigTreeNodeView.razor` (UI-CFG), plus their bUnit tests.
**Found by:** Reviewer (code-review agent), Gate-2 review of commits `d30a2a8` (MandA) and `d02b23f` (Config).
**Iteration:** 1 · **Date:** 2026-08-13
**Backs:** decision-019; contracts `IMandaStation*`/`IConfigTreeQuery`/`IConfigEditor<T>` family + `ISettingsEditor`.

## Verdict
No blocking bugs. Draft↔DTO mappings across all 7 config entity types round-trip **every** field correctly (verified against `ConfigDtos.cs`), null-id create semantics and the delete path are correct, and the MandA scan/print/verify guards and optimistic state are sound. Three lower-severity findings; the two actionable ones **applied**.

## Confirmed clean (no action)
- All 7 draft classes (`SettingsDraft`/`LabelDraft`/`StationDraft`/`LineDraft`/`PrinterDraft`/`FirePointDraft`/`MapDraft`) carry every DTO field, including non-editable `PrinterId`/`LineId` held silently for the round-trip.
- Create-vs-update: null identifier ⇒ create is honored by the editors; delete path clears `_selected` before reload.
- MandA optimistic slot updates (`Printed`/`Verified`) stay consistent with the command results; the "Print selected" disabled-guard and the verify no-active-carton / not-found cases are handled.

## Findings

| # | Sev | Finding | Source ground truth | Resolution |
|---|-----|---------|---------------------|------------|
| 1 | Warning | `Split` used `StringSplitOptions.RemoveEmptyEntries`. For **positional** collections — `LineDto.BufferOrder` ("per-position buffer order") and `TrackingDevices` ("in order") — typing `a,,c` silently collapses to `["a","c"]`, shifting every later position by one. A silent corruption on save, not a visible validation error. | `ConfigDtos.cs:44-45` documents these fields as ordered/positional. `RemoveEmptyEntries` deletes gaps rather than preserving them. | **FIXED (`61b4af3`)** — `Split` now preserves interior empty positions (trims each token, drops only trailing empties to tolerate a trailing comma). |
| 2 | Info | `_selected` node goes **stale after a successful Save**: `SaveAsync`→`ReloadTreeAsync` rebuilds `_root` with fresh node instances, but `_selected` still points at a node from the previous tree, so `_selected.Label` in the detail title is stale (e.g. after a rename). If the entity were removed server-side, `_selected` would dangle. | — | **FIXED (`61b4af3`)** — after reload, `SaveAsync` re-resolves `_selected` against the rebuilt tree by `(Kind, EntityId)` via a new `FindNode` walk. |
| 3 | Info | bUnit tests were render/smoke gates only — MandA never exercised print / optimistic slot state / selection; Config asserted only a Settings-save **call count**, not the saved payload, leaving the draft↔DTO/CSV round-trip (Finding 1) untested. | — | **FIXED (`61b4af3`)** — Config test now asserts the saved `SettingsDto` contents (round-trip integrity); new MandA test drives slot-select→print and asserts the printed slots + optimistic "Printed" chip. |

## Learner note (recurring)
Same lesson as learnings/001 §"under-test the wiring paths": these UI slices were algorithmically fine but the gate under-tested the *behavioral* seam (payload mapping, optimistic state). The fix strengthened one behavioral assertion per screen rather than adding render smoke.

## Outcome
2 logic fixes + 2 test-strength improvements applied and re-gated (11 bUnit, 6 e2e, 231 backend green). No blocking objections.
