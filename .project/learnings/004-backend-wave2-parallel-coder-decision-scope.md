# 004 — Backend Wave 2: parallel-coder orchestration + decision-driven scope

**Date:** 2026-08-13
**Context:** Wave-2 backend full-port slices (FLOG1, F10, F18-A, F12, PROFSW, F15, F08) built with a
senior/coder workflow — background Coder agents authoring domain code in parallel while a single Senior
owned the build, test, review, and integration. Grew the suite 279 → 328 backend tests; nothing pushed.

## Insights & actionable guidance

### 1. Parallel coders share build artifacts — partition by files, not just by feature.
Multiple background agents each running `dotnet build`/`dotnet test` collide on the same
`PandA.Core.dll` / `PandA.Sim.dll` / `PandA.Tests.dll` (file locks, half-written outputs, flaky failures
that look like real bugs). The workflow that held: **coders author code + tests only and never invoke
`dotnet`; the Senior runs the one authoritative build + full suite after each batch.** And only launch
coders in parallel for **new-file-only** features. Any feature that edits a shared hot file
(`TransportOrder.cs`, `InductService.cs`, `LineConfig.cs`) must be **serialized** — here PROFSW, F15, F08,
and the F12 wiring all touched `TransportOrder`/`InductService` and were done one at a time by the Senior.
**Guidance:** before fanning out coders, build the collision map (which features touch which files); parallelize
the disjoint new-file set, serialize the hot-file set. Never let a background coder run the build.

### 2. A coder that never compiles will ship non-compiling code — the Senior's build gate is mandatory.
Because parallel coders can't run `dotnet` (insight 1), they can't catch their own syntax errors. One coder
emitted an invalid record constructor (`public XRef { throw ... }`) that never would have compiled. This is
structural, not a fluke: **any agent that doesn't build cannot self-verify.** The mitigation is not "trust
the coder more" — it's that the Senior's post-batch build+review is a hard gate, and coder output is treated
as a draft until it passes. **Guidance:** when using non-building sub-agents, budget for a Senior fix pass;
review every new file for faithfulness to spec *and* compilability before trusting a green count.

### 3. Build to the decision, not the spec text — specs get superseded and the source can be wrong.
Two Wave-2 features would have been built wrong from their cluster specs. F08's spec (LaneDef / round-robin /
`GetFinalLaneFromStatus`) is fully out of scope — decision-008 delegates lane selection to econtroller's
CriteriaBasedSorting, so F08 collapsed to a `{Type,Value}` routing criterion with **zero** lane logic. F15's
PandA source zeroes the print count on recovery (`Printed=0`), but decision-009 requires a **monotonic**
count for a truthful reprint audit — so the port deliberately *diverges from the source* and adds a
`TrackingRearmed` flag instead. **Guidance:** before porting a feature, reconcile the cluster spec against the
architecture-log decisions; where they conflict, the decision wins and the divergence gets a code comment +
a test asserting the decided behavior (not the source's). A faithful port is faithful to the *decision*.

### 4. Defer honestly: "domain-built" is not "wired", and say what blocks the wiring.
Four Batch-1 slices (FLOG1, F10, F18, F12) landed as pure domain code with `build_status=
'domain-built;integration-deferred'`. Only F12 could actually be wired into `InductService` this wave; the
rest are blocked on **Wave-0 foundations that don't exist yet** — FLOG1 + F18 need the PLC inbound-scan
payload (sorter/device/seq/dims/gap) plumbed through `InductAsync` (still the minimal `(lineId, blindLabel)`
seam), and F10 needs F20 read-quality classification (a stub seam). The value is in **naming the exact
blocker** in `plan.md` and the feature tracker, not quietly leaving a TODO. **Guidance:** track a two-stage
status (domain-built → integrated); when you defer integration, record the concrete foundation it waits on so
the next wave can sequence it, and never report a deferred slice as "done."

## Verification
- New: `RoutingCriterionTests` (F08, 9), `PlcEventHandlerTests` (F15, 11), `ProfileSwitchTests` (PROFSW, 6),
  Batch-1 slice tests (FLOG1/F10/F18/F12), F12 induct-wiring tests (2).
- Senior build gate caught 3 pre-commit issues (reviewer-log/007): invalid record ctor, lost new-folder file,
  spec-vs-decision scope drift on F08/F15.
- Full backend suite green: **328** (up from 279). Build clean. DemoHost re-verified (`/`, `/sim` → 200).
