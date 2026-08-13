# 007 — Backend Wave 2: senior review of parallel-coder full-port slices

**Found by:** Senior (single authoritative build+test after each batch)
**Iteration:** Wave-2 backend, Batch 1 (4 parallel coders) + serialized hot-file features
**Date:** 2026-08-13

Wave-2 backend ran a senior/coder workflow: background Coder agents authored **new-file-only** domain
slices in parallel; the Senior owned the single build + full-suite run, review, and integration. This log
captures what the Senior caught before trusting coder output. Final: **328 backend tests green**, build
clean, nothing pushed.

---

## Issue 1 — Coder emitted non-compiling C# (invalid record constructor)

- **Found by:** Senior, first build after Batch 1.
- **Severity:** High (build-breaking) — would have failed the whole batch if trusted.
- **Description:** coder-f18's `XRef.cs` declared a validating constructor as
  `public XRef { if (...) throw new ArgumentException(...); }` — a property-like block on a positional
  record, which does not compile. The coder never built (by design — parallel coders must not run `dotnet`
  to avoid shared-DLL lock races), so it shipped invalid syntax.
- **Fix applied:** Senior rewrote `XRef` as a simple positional record (validation deferred to the service
  layer, consistent with the other Batch-1 slices). `dotnet build PandA.slnx` → 0 errors afterward.
- **Verified:** yes — 300 tests green after the fix; `XRefService`/`InMemoryXRefStore` reviewed as faithful
  to decision-017 Facet A.

## Issue 2 — First-file-in-new-folder create silently lost

- **Found by:** Senior, build error after F15 first pass (`PlcEventCode` type not found).
- **Severity:** Medium (self-inflicted tooling gotcha, not a code defect).
- **Description:** the `create` tool fails when the parent directory does not yet exist. `PlcEventCode.cs`
  was the first file in a new `src/PandA.Core/Plc/` folder; the initial create failed and was not retried,
  so the F15 handler referenced a non-existent enum.
- **Fix applied:** create the directory first (`New-Item -ItemType Directory -Force`), re-create the file,
  and **verify the file exists on disk** before building.
- **Verified:** yes — 317 tests green after re-adding the file.

## Issue 3 — Spec acceptance criteria superseded by decisions (scope drift risk)

- **Found by:** Senior, before writing F08 and F15.
- **Severity:** Medium (would have built the wrong thing at real cost).
- **Description:** the raw cluster specs for **F08** (lane routing) and **F15** (recovery) describe behavior
  the architecture decisions later **overruled**. F08's spec (LaneDef/round-robin/`GetFinalLaneFromStatus`)
  is entirely out of scope per **decision-008** (lane selection delegated to econtroller
  CriteriaBasedSorting). F15's source zeroes `Printed=0` on recovery, but **decision-009** mandates a
  monotonic `PrintCount`.
- **Fix applied:** built to the **decisions**, not the spec text. F08 shrank to a `{Type,Value}`
  `RoutingCriterion` on the verify result (no lane logic). F15 keeps `PrintCount` monotonic and adds a
  `TrackingRearmed` flag instead of resetting to 0.
- **Verified:** yes — F08 (9 tests) and F15 (11 tests) assert the decision semantics, not the source's.

---

## Outcome

- No defect escaped to the committed suite; every issue was caught by the Senior's build+test gate before
  the batch was trusted.
- Confirmed the parallelization rule: **only new-file-only features run as parallel coders**; every
  hot-file feature (PROFSW, F15, F08, F12-wiring — all touch `TransportOrder`/`InductService`) was
  serialized by the Senior.
- Commits: `00b9147` (Batch 1), `9fd5437` (PROFSW), `75b7144` (F15), `97ce2e8` (F08), `1c17768` (F12 wiring).
