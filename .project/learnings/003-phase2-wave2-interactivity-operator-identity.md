# 003 — Phase-2 Wave 2: the static-SSR interactivity trap + operator identity

**Date:** 2026-08-13
**Context:** Wave-2 integration hardening of the standalone Blazor UI module. Two outcomes: wiring real
operator identity through the audited reprint path (replacing the hardcoded `"operator"`), and — while
building whole-app e2e flows — discovering that **the entire DemoHost was rendering as static SSR with zero
interactivity**. Every button in the browser was dead; only the compile + render gates were green because
bUnit forces interactivity and the screen-load gate never asserted a real interaction took effect.

## Insights & actionable guidance

### 1. "Renders + no console error" is NOT "works". Assert a real interaction changes real state.
The screen-load gate loaded every route (2xx, heading present, no `#blazor-error-ui`, zero console errors)
and even *clicked* the theme toggle — and still passed against a completely non-interactive app, because a
click on a static button simply does nothing (no error, no effect). bUnit passed too, because bUnit renders
components with interactivity always on. The gap was only caught by asserting a **post-interaction state
change in the real browser**: click the theme toggle → the layout root's `data-dark` attribute must flip to
`true`. That single assertion is the difference between "the button exists" and "the button works."
**Guidance:** every UI stack must have at least one e2e test that performs an action and asserts an
*observable consequence* in the real browser (attribute/text/DOM change), not just that the element is
present and clickable. Expose a small `data-*` state hook on a stable root element for exactly this. bUnit
interaction tests are necessary but **cannot** prove the deployed app is interactive — only a real-browser
consequence check can.

### 2. The Blazor Web App static-SSR trap: a global render mode is not the same as an applied one.
`Program.cs` had `AddInteractiveServerComponents()` + `.AddInteractiveServerRenderMode()`, which only makes
the interactive mode *available*. Interactivity is off until a render mode is **applied** to a component.
Our `App.razor` rendered `<Routes />` and `<HeadOutlet />` with no `@rendermode`, so the whole tree was
static SSR. Fix: `<Routes @rendermode="InteractiveServer" />` (and the same on `<HeadOutlet />`) for global
interactivity. Symptoms that pointed here: no WebSocket is ever opened (Playwright `RunAndWaitForWebSocket`
times out), and `onclick`/`ValueChanged` never fire though the elements are present and enabled.
**Guidance:** in any .NET 8/9 Blazor Web App, confirm a render mode is actually applied (on `Routes`/`App`
or per-component), not merely registered. Treat "no circuit/WebSocket established" as the tell.

### 3. Interactive vs static changes how tests must wait — but the right fix is interactivity, not retries.
Before finding the root cause, the symptom (a dropped first click) looked like a circuit-connection race,
tempting a poll-and-retry-click loop. That would have masked the real bug (the app was never interactive at
all) and shipped a dead UI behind a "green" flaky-looking test. **Guidance:** when an interaction has zero
effect, first prove the app is interactive at all (is a circuit established?) before adding waits/retries.
Retries paper over connection races; they must never paper over a missing render mode.

### 4. Operator identity belongs behind a port, injected — never a literal at the call site.
Audited actions (reprint authorization) hardcoded `"operator"` as the actor. Replaced with an injected
`IOperatorContext { CurrentOperator; SetOperator(name); OperatorChanged }` in `PandA.UI.Contracts`. The RCL
screens read `Operator.CurrentOperator` at action time; the app-bar hosts a selector bound to the same port;
the demo backs it with a scoped-per-circuit `DemoOperatorContext`, and a real host backs it with its auth
session. This keeps the audit trail truthful and the module host-agnostic (no RBAC yet — identity for
attribution only, per decision-019). **Guidance:** any value that ends up in an audit record must come from
an injected identity/context port, verified by a test that asserts the *captured* actor equals the current
context (not that the call merely happened).

## Verification
- New: `PandA.E2E.Tests/AppFlowTests` — cross-screen nav to every route, theme toggle flips `data-dark`
  (real interactivity guard), operator identity persists across navigation.
- New: `PandA.UI.Tests/PandaLayoutTests` — shell chrome present, operator selector drives the context and
  reflects context changes, theme toggle flips the layout state.
- Strengthened: Lookup/Reject bUnit tests assert the reprint captures the *current operator* from context.
- Full suite green: 231 backend + 16 bUnit + 9 e2e.
