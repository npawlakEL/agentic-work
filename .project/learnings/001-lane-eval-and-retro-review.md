# 001 — Lane-eval + retro-review cycle

**Date:** 2026-08-12
**Context:** Lane-eval/spare/printer-state slice (arch-log 012, gap F09) followed by the first properly-gated
Gate-2 review + retro-review sweep across earlier slices, after the domain owner flagged that the `.agent/`
multi-agent workflow was not actually being run (roles were collapsed inline; `reviewer-log/` and `taskboard/`
were empty).

## Insights & actionable guidance

### 1. Run the real gated workflow — retro-review pays for itself
Collapsing Orchestrator→…→Reviewer into a single inline pass *felt* faster but skipped the review gate.
When a genuine `code-review` sub-agent was finally dispatched, it found **4 real verify-core divergences**
(one High) and a Medium advice re-arm hole — none of which the green test suite caught, because the tests
were *enshrining* the un-adjudicated behavior.
**Guidance:** dispatch an independent reviewer per slice; log every finding to `reviewer-log/` even when the
resolution is "no change". A passing suite is not a substitute for a review — tests encode decisions, and an
undecided decision encoded as a test is a latent bug.

### 2. Source SQL is a reference, not an authority — the domain owner adjudicates divergences
Three of four verify findings were "the port diverges from source." The correct resolution was **not**
"match source": for VF-1 and VF-2 the domain owner *kept* the stricter port behavior (source was too
lenient — same spirit as decision-003's reprint-hole correction); VF-3/VF-4 were refined/made configurable.
**Guidance:** when a port diverges from source in a way that changes PASS/FAIL or safety semantics, **escalate
to the domain owner with a concrete recommendation** rather than silently matching source or silently keeping
the port. Record the outcome as a decision-log entry (decision-004) so the divergence is intentional and
traceable.

### 3. Don't flip behavior that an existing test pins — it may be a deliberate decision
The advice re-arm "fix" looked obviously correct until it broke `DuplicateAdvice_BeforePrint_StartsNewGeneration`,
which deliberately encodes legitimate blind-label reuse. Reverting and asking surfaced the real rule
(reprint-rules-gated re-advice).
**Guidance:** if a "fix" breaks an existing green test, stop — the test may encode an intentional decision.
Read the test's intent, and if it conflicts with your change, that's an escalation, not a test to rewrite.

### 4. Per-orientation grouping + generalized degraded policy (lane-eval)
`PrinterType` turned out to mean apply orientation (Side/Top), and min-count/spare rules apply **per
orientation group independently** (confirmed from seed data, not assumed). The source's hard-coded "2 Printer
Rule" was generalized into a `PrinterGroupPolicy` (`OnlineMin`/`SlowLineFloor`/`AllowDegraded`) that
reproduces the 2-printer case as a strict superset.
**Guidance:** verify the *meaning* of an enum/column against seed data before modeling it; prefer generalizing
a hard-coded source rule into a policy object (reproduce the original as a config instance) over porting the
magic number.

### 5. Make genuinely-plausible either/or behaviors configurable, not decided
VF-4 (reset-on-trip vs stay-tripped) had two defensible answers; the resolution was a constructor flag
defaulting to current behavior, plus a backlog item to surface it as a setting.
**Guidance:** when both options are reasonable and low-cost to support, add a flag (default = current
behavior to avoid churn) and backlog the config surface — don't force a one-way decision.

## Reviewer-log / taskboard pattern analysis
- Across reviewer-logs 001–004, **zero Critical/High correctness bugs in shipped code**; the real value was in
  **coverage gaps** (untested fire-point resolve-on-print) and **un-adjudicated semantics** (verify/advice).
- Pattern: our TDD slices are algorithmically faithful but under-test the *wiring* paths (induct→PrintJob
  fire-point) and *policy* edges (bypass glitch, post-trip reset). Future slices should add an integration
  test for each wiring seam, not just unit tests for the algorithm.
