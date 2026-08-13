# 008 — Backend serialized wiring wave: senior review (F20/F-LOG1/F18/DYNAP/F10)

**Found by:** Senior (single authoritative build+test after each feature)
**Iteration:** Serialized induct-wiring wave — five hot-file features landed one at a time
**Date:** 2026-08-14

This wave wired the five previously "domain-built;integration-deferred" slices into the `InductService`
induct path. Every feature edited the same hot files (`InductService.cs`, plus `TransportOrder.cs`,
`LineConfig.cs`, `InductResult.cs`, `IPrinterGateway.cs`), so — per learnings/004 insight 1 — the whole wave
was **serialized and Senior-owned**; no parallel coders. Each feature was built + full-suite tested +
committed before the next started. Final: **358 backend tests green** (up from 352 at wave start / 331 after
Wave 0), build clean, DemoHost re-verified, nothing pushed.

---

## Issue 1 — DYNAP: a test asserted a path the induct pipeline can't reach

- **Found by:** Senior, first full-suite run after the DYNAP wiring edits.
- **Severity:** Medium (test-only; false red, no production defect).
- **Description:** `Induct_TopApply_LeavesApplyPulseNull_UntilTampCommissioned` provisioned a **Top** printer
  and expected a dispatched job with a null `ApplyPulse`. It failed with `Sequence contains no matching
  element` — no job was ever produced. Root cause: `PrinterSelectionService` filters candidates by
  `p.PrinterType == orientation`, and the induct path hardcodes `orientation = Side` (architecture-log 005).
  A Top printer is therefore never selected in the induct path today, so the test's premise is unreachable.
- **Fix applied:** removed the unreachable test. Top-apply's "leave ApplyPulse null" behavior is already
  covered where it actually lives — the `ApplyPointResolver` unit tests and the DYNAP guard
  (`printer.PrinterType == ApplyOrientation.Side`) in the print loop. Kept the two reachable side-apply
  induct tests.
- **Verified:** yes — 354 green after removal; the side-apply dynamic-pulse assertion (`1L` @ ER 0.2 → 5
  pulses) still passes.
- **Lesson:** when writing an integration test, confirm the pipeline can actually produce the precondition.
  A provisioned-but-unwired dimension (here Top orientation) belongs in unit tests, not induct integration
  tests, until the pipeline reaches it.

## Issue 2 — F10 effective-flag: "global unset" isn't representable in the seeded settings provider

- **Found by:** Senior, during F10 design (before writing the emission path).
- **Severity:** Medium (design correctness — would have made per-line `PrintExceptionLabels` dead code).
- **Description:** decision-021 requires "a *defined* global `PrintExceptionLabels` overrides all lines;
  only when the global is unset do per-line values apply." But `InMemorySettingsProvider` seeds every
  `KnownSettings` entry with its default (`PrintExceptionLabels = false`), so a naive
  `GetAsync<bool>(...)` read always returns a *defined* `false` — the global is never "unset," and the
  per-line switch could never take effect.
- **Fix applied:** read the global as a **nullable** `GetAsync<bool?>(name, null)`, and model precedence
  with `ExceptionLabelPolicy.Effective(global, line) => global ?? line ?? false`. "Unset" is then genuinely
  representable: tests that want per-line behavior construct `new InMemorySettingsProvider(seedKnownSettings:
  false)` (key absent → `bool?` null). The default seeded provider yields a defined global `false`, which is
  exactly the source default (exceptions globally off) and preserves every existing test unchanged.
- **Verified:** yes — three precedence tests pin it: global-unset+line-true → emit; global-defined-false
  overrides line-true → no emit; both unset → no emit. 358 green.

## Issue 3 — F10 exception labels must NOT go through printer capability gating

- **Found by:** Senior, while choosing the printer for an exception label.
- **Severity:** Low (caught in design; avoided a silent "no exception printed" bug).
- **Description:** the exception label's `LabelType` is the sentinel `"Exception"`, which is not in any
  printer's configured label map. Routing it through the normal `PrinterConfig.CanPrint(type)` /
  `PrinterSelectionService` path would have found zero candidates and silently emitted nothing.
- **Fix applied:** the exception path selects the first printer of the line's apply orientation directly
  (`Printers.FirstOrDefault(p => p.PrinterType == orientation)`), bypassing label-capability gating — an
  exception label is a physical "something is wrong with this carton" marker, not a typed business label.
- **Verified:** yes — the NoRead test asserts the job dispatches to `Ship1` with `LabelType == "Exception"`.

---

## Cross-cutting: the optional-trailing-ctor-param pattern held for all five features

Every new `InductService` collaborator (`IMinGapProvider`, `ICartonRunRepository`, `IXRefStore`,
`IExceptionLabelSource`) was added as an **optional trailing constructor parameter before `logger`**, with a
null/no-op default. All 9 `new InductService(...)` call sites kept compiling untouched; features light up
only where a caller opts in by passing the collaborator. The one breakage across the whole wave was a single
`CS1503` in the DemoHost factory (F20) fixed by passing `logger:` as a named argument — the documented cost
of the pattern, not a surprise.
