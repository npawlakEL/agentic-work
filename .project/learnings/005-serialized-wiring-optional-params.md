# 005 — Serialized hot-file wiring: optional-param seams, decision-faithful integration

**Date:** 2026-08-14
**Context:** Serialized induct-wiring wave — took five "domain-built;integration-deferred" slices
(F20 read-quality, F-LOG1 run history, F18 barcode xref, DYNAP apply-point, F10 exception labels) and wired
each into the `InductService` induct path. All five edit the same hot files, so the wave was serialized and
Senior-owned (no parallel coders). Grew the suite 352 → **358 backend tests**; nothing pushed.

## Insights & actionable guidance

### 1. Wire optional collaborators as trailing ctor params before `logger` — one seam, zero call-site churn.
Each feature needed a new dependency on `InductService` (`IMinGapProvider`, `ICartonRunRepository`,
`IXRefStore`, `IExceptionLabelSource`). Adding each as an **optional trailing parameter with a null/no-op
default, placed immediately before the existing `ILogger?`**, let all 9 construction sites keep compiling
untouched — the feature activates only where a caller opts in by passing the collaborator. This is what made
five hot-file features landable one-per-commit without a big-bang refactor. The single cost: existing
positional calls that passed `logger` must switch to the named `logger:` argument (one `CS1503` in the
DemoHost factory the whole wave). **Guidance:** for incremental integration into a widely-constructed
service, prefer optional trailing params over a new required dependency or a parameter object; keep `logger`
last and insert new seams just before it, and expect to name-qualify `logger:` at the call sites.

### 2. Integration tests must be reachable by the pipeline — provisioned ≠ wired.
A DYNAP test provisioned a **Top** printer and asserted its dispatched job left `ApplyPulse` null. It failed
because the induct path hardcodes `orientation = Side` (architecture-log 005), so `PrinterSelectionService`
never selects a Top printer — the test's precondition can't occur. The behavior it meant to check
(top-apply is null pending tamp commissioning) already lives correctly in the `ApplyPointResolver` unit
tests and the `PrinterType == Side` guard. **Guidance:** before writing an integration test, trace that the
pipeline can actually produce the precondition. Behavior of a provisioned-but-unwired dimension belongs in
unit tests until the pipeline reaches it; asserting it end-to-end just produces a false red.

### 3. "Unset" must be representable, or a precedence rule becomes dead code.
decision-021's "defined global overrides per-line; unset global falls back to per-line" is meaningless if
the settings store can't express *unset*. The seeded `InMemorySettingsProvider` gives every known setting a
concrete default, so a `bool` read of `PrintExceptionLabels` is always "defined false" and the per-line
switch could never fire. Reading it as **`bool?` with a null default** (`global ?? line ?? false`), plus a
`seedKnownSettings: false` provider in tests, made unset genuinely representable — and the default seeded
`false` conveniently *is* the source's "exceptions globally off," preserving every prior test. **Guidance:**
when a spec's precedence hinges on presence/absence, model the absent state explicitly (nullable read, tri-
state, or `TryGet`); don't collapse "unset" and "false" into one bool, or one branch of the rule dies
silently.

### 4. Build to the decision: F10 synthesizes identity and reroutes, diverging from a naive "no match → drop."
The source's instinct on an unreadable/unknown carton is to do nothing useful. decision-021 instead requires
a *positive* outcome: synthesize a human-readable id (`{Reason}-{seq}`), print a local exception label, mark
it printed (run-history), and route it to reject via the F08 `{Type,Value}` criterion — treating the carton
as a verify **Fail**. The Core also stays adapter-agnostic: the DCMS/eHub exception source is bookmarked as
an `IExceptionLabelSource` port (`LocalTemplateSource` is the in-process default), so `DCMSExceptions = 1`
is honored purely by which source the adapter injects — Core never branches on it. **Guidance:** continue
reconciling each feature against its architecture-log decision before wiring; when the decision prescribes a
richer behavior than the source, encode it with a comment + a test that pins the decided behavior, and keep
external-system variability behind a port the adapter fills.

## Verification
- New tests: F20 induct classify (+2), F-LOG1 run create/update (+2), F18 xref induct resolution (+3, incl.
  `XRefInductResolutionTests`), DYNAP side-apply pulse (+2), F10 exception emission + precedence (+4).
- Senior build gate caught: an unreachable Top-apply integration test (removed), the nullable-global
  effective-flag design, and the exception-label capability-gating bypass. See reviewer-log/008.
- Full backend suite green: **358** (up from 352). Build clean. DemoHost re-verified (`/`, `/sim` → 200).
- Feature tracker: F20/F-LOG1/F18/DYNAP/F10 → `built`.
