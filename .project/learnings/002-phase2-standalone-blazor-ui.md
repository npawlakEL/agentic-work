# 002 — Phase-2 standalone Blazor UI cycle

**Date:** 2026-08-13
**Context:** Wave-0 + Wave-1 of the standalone, pluggable Blazor UI module (decision-019): the portable
`PandA.UI.Contracts` interface layer, Sim-backed `PandA.UI.DemoHost`, the Axon/MudBlazor shell, and all
five operator screens (Status Dashboard, Label Data Lookup, Reject Cartons, MandA Station, Config Explorer).
Built under an explicit domain-owner requirement that **everything be tested/touched** — screens load,
buttons work, it compiles — enforced by automated gates. Two Gate-2 code-review passes ran (reviewer-logs
005, 006).

## Insights & actionable guidance

### 1. Automated runtime gates are what actually satisfy "it must work" — write them first
The owner's pain was the classic one: "web screens won't load, buttons won't work, it won't compile."
The answer that held was three cheap, always-on gates established in Wave 0 **before** any screen:
solution build under `TreatWarningsAsErrors`+nullable, a Playwright screen-load test per route (2xx, no
visible `#blazor-error-ui`, heading present, **zero console errors**, theme toggle clicks), and a bUnit
render+interaction test per screen. Every screen landed green on all three or it didn't land.
**Guidance:** for UI work, stand up the compile + headless-load + component-interaction gates as step one,
add one route/one test per screen as you go, and treat a red gate as a blocked landing. A screen with no
screen-load test is unverified regardless of how it looks locally.

### 2. The view-model interface seam is what makes "it throws in prod but not in the demo" invisible — guard the handlers
The whole module depends only on `PandA.UI.Contracts`; the Sim implementations return
`CommandResult.Fail(...)` and never throw. That is exactly why the first review (005) flagged that
un-try/catch'd event handlers were a latent circuit-killer: on Blazor Server an exception from an event
handler tears down the SignalR circuit (blank error UI, lost state), and the **real** SQL-backed adapter
*will* throw on transient faults even though the Sim never does.
**Guidance:** when a UI is written against a stubbed/Sim backend, assume the real implementation throws.
Wrap every awaited backend call in `try/catch (Exception ex) when (ex is not OperationCanceledException)`
and surface it via Snackbar. Don't let the demo's happy-path politeness lull the error handling.

### 3. State that mirrors backend data goes stale on reload — reconcile, don't just re-query
Both reviews found the same shape of bug independently: the Lookup screen kept stale expanded-slot data
after an authorize+reload, and the Config screen kept a stale `_selected` node after save rebuilt the tree.
Re-fetching the list is not enough when other view state *references* the old data.
**Guidance:** after any reload that rebuilds a collection, explicitly reconcile dependent state — re-resolve
the selected item by identity, re-fetch or collapse expanded detail, clear anything that pointed into the
old snapshot. "Reload the list" and "refresh the screen" are not the same operation.

### 4. Render gates pass while behavioral bugs hide — assert the payload, not the call count
The as-written bUnit tests rendered and clicked but asserted almost nothing about behavior (Config asserted
a save *count*; MandA never printed). That let a real positional-CSV corruption bug (Split dropping empty
positions) sit behind a green suite — the exact "tests enshrine un-adjudicated behavior" trap from
learnings/001. Strengthening one assertion per screen (saved-DTO contents; post-print optimistic slot
state) closed it.
**Guidance:** every screen deserves at least one test that asserts the **effect** of an interaction (the DTO
handed to the command, the resulting visible state), not just that a handler ran. Prefer one deep
behavioral assertion over three render smokes.

### 5. Axon + MudBlazor share type names — alias once, centrally, and expect the trickle
`Axon` and `MudBlazor` both define `Color`, `Typo`, `Size`, `Icons`, `Edge`, `MaxWidth`, `Variant`,
`Severity`. Each new screen surfaced another ambiguity (`Variant`/`Severity` only appeared once forms/
snackbars were used). The fix is alias usings in `_Imports.razor` (`@using Variant = MudBlazor.Variant`
…) — but they must exist in **both** the RCL and the DemoHost `_Imports`, and code-behind `.cs` blocks need
fully-qualified names since razor usings don't apply there. Meziantou.Analyzer also rides in transitively
through Axon and, under warnings-as-errors, its style rules (MA0004/0006/0016/0048/0051/0123) become build
failures — silenced centrally in root `.editorconfig`.
**Guidance:** when adopting a component library layered on another (Axon→MudBlazor), set up the alias-using
block and the transitive-analyzer `.editorconfig` suppressions up front as shared infrastructure; add the
next alias the moment a new ambiguity compiles-errors rather than fighting it per-file. Also: a manual
`RenderTreeBuilder` fragment trips MA0123 (non-constant sequence numbers) — prefer a small self-recursive
component (`ConfigTreeNodeView`) over hand-built render fragments.

### 6. Test hooks: MudBlazor wrappers don't forward `data-testid` to the inner input
`data-testid` on `MudTextField`/`MudCheckBox` lands on the wrapper, not the `<input>`, so
`Find("[data-testid=x] input")` fails and `MudSelect`/`MudPopover` needs a `MudPopoverProvider` rendered in
the test. Wrapping the control in a plain `<div data-testid="…">` gives a stable hook for both bUnit and
Playwright.
**Guidance:** for reliable UI test selectors, put `data-testid` on a plain wrapper element you control, not
on the third-party component; render `MudPopoverProvider` in bUnit tests that use Select/menu popovers.

## Process note
This cycle finally ran the intended Coder→Reviewer loop on UI work: implement screen → gate → dispatch
`code-review` agent → apply findings → re-gate → commit, one screen at a time in the same project (sequential,
to avoid obj/bin contention). Reviewer value was again in robustness/coverage (circuit safety, stale state,
weak assertions), not shipped-code correctness bugs — consistent with learnings/001's pattern.
