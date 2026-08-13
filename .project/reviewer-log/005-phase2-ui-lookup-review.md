# 005 — Phase-2 UI: Label Data Lookup screen review

**Slice:** `PandA.UI/Pages/LabelLookup.razor` (+ `LabelLookupTests`, `/lookup` e2e route). UI-LOOKUP tracker row.
**Found by:** Reviewer (code-review agent), Gate-2 review of commit `0d9c9a6`.
**Iteration:** 1 · **Date:** 2026-08-13
**Backs:** decision-019 standalone Blazor UI; contracts `ITransportOrderQuery`, `ICartonLabelDetailQuery`, `IReprintAuthorizationCommand`.

## Verdict
Screen largely sound. No blocking bugs. Two real robustness issues (both **applied**), one acknowledged gap (operator identity, backlogged).

## Confirmed clean (no action)
- MudTable `context!` aliasing is not a null-risk — `Items` is a non-null list of non-null records; `!` only suppresses the nullable-ref warning.
- Filter reload is correct: `Immediate="false"` + explicit **Apply** → `ReloadAsync` re-queries with current fuzzy/status/verify; no debounce/subscription/dispose concerns.
- Single-expansion model (`_expandedCartonId` + shared `_slots`) is internally consistent; `ChildRowContent` correctly gated by `IsExpanded(row)`.
- bUnit `Authorize_reprint_button_invokes_command` genuinely exercises the `IsHeld` branch (only held rows render the button) and asserts the command fired.

## Findings

| # | Sev | Finding | Resolution |
|---|-----|---------|------------|
| 1 | Warning | Async event handlers (`ReloadAsync`/`ToggleAsync`/`AuthorizeAsync`) called the backend interfaces with **no try/catch**. On Blazor Server an exception thrown from an event handler is **fatal to the circuit** — the user gets the blank error UI and loses page state instead of a Snackbar error. The Sim returns `CommandResult.Fail(...)` rather than throwing, so the demo never trips it; but a real SQL-backed adapter **will** throw on transient DB/network faults. | **FIXED (`db8c548`)** — wrapped all three handlers in `try/catch (Exception ex) when (ex is not OperationCanceledException)` surfacing faults via `Snackbar.Add(..., Severity.Error)`. Same guard pattern applied to every subsequent screen. |
| 2 | Info | Expanded slot data goes **stale after reload**: after `AuthorizeAsync`→`ReloadAsync`, the expanded row keeps rendering `_slots` fetched before the reprint (Sim mutates `Printed`/`PrintedCount`); and if the expanded carton drops out of the filtered set, `_slots` is never cleared. | **FIXED (`db8c548`)** — `ReloadAsync` now reconciles: if the expanded carton is still present, re-fetch its slots; otherwise collapse it and clear `_slots`. |
| 3 | Info | Operator hardcoded as the literal `"operator"` on every audited reprint. Fine for Wave 1, but the audit trail is not trustworthy until real identity is wired. | **BACKLOGGED** — see backlog "Operator identity for audited reprint actions". Confirmed high-confidence real gap; deferred to Wave 2 auth wiring. |
| — | Low | `IsExpanded` matches rows by `CartonId`; two rows with the same `CartonId` would both expand. Data model is one row per carton, so non-issue — flagged for awareness only. | No change. |

## Outcome
2 robustness fixes applied and re-gated (bUnit + e2e + 231 backend green). 1 gap backlogged. Reviewer had no blocking objections.
