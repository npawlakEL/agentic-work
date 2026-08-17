---
name: mutation-testing
description: >
  Use to measure TEST QUALITY (not coverage) by running mutation testing on a
  targeted module — it mutates production code and checks whether tests catch
  the change. Load during Finalize audits and when the Senior Coder wants proof
  that tests on shared/critical/high-risk code actually assert behavior.
load_when: >
  Finalize audit of test quality, or a request to verify tests are meaningful
  beyond coverage, or hardening high-risk/shared modules.
  Match keywords: mutation testing, Stryker, mutation score, test quality,
  surviving mutants, weak tests, coverage isn't enough.
upstream: true
---
# Skill: Mutation Testing

## Trigger
Run this when you need to know whether tests are *meaningful*, not just present:
- During a **Finalize** audit, on the critical/high-risk modules in scope.
- When the **Senior Coder** wants extra confidence on shared code (utilities,
  models, adapters, base classes) that many things depend on.
- When coverage looks high but bugs still slip through — a classic sign of tests
  that execute code without asserting its behavior.

## What it does
A mutation tool makes tiny changes ("mutants") to production code — flip `>` to
`>=`, `&&` to `||`, `+` to `-`, remove a statement, swap a return value — then
re-runs the tests against each mutant.
- Test **fails** → mutant **killed** ✅ (tests caught it — good).
- Tests still **pass** → mutant **survived** ❌ (a real test gap).
- **Mutation score** = killed / total. It's a far stronger signal than line coverage.

## Tooling by stack
| Stack | Tool | Invocation (targeted) |
|-------|------|-----------------------|
| .NET / C# / xUnit | **Stryker.NET** | `dotnet stryker --mutate "src/Module/**/*.cs"` |
| JS / TS | **StrykerJS** | `npx stryker run --mutate "src/module/**/*.js"` |
| Java | PIT | Maven/Gradle goal scoped to target classes |
| Python | mutmut / cosmic-ray | scoped to the changed package |

## Steps
1. **Scope it tightly.** Mutation runs are slow (they run the suite once per
   mutant). Target ONLY the changed/critical module — never the whole codebase.
   The Senior Coder names the module; use the tool's `--mutate` / include filter.
2. **Ensure the suite is green first.** Mutation testing is meaningless if normal
   tests already fail — fix those before running.
3. **Run the mutation tool** against the scoped files.
4. **Read the report.** Note the mutation score and, more importantly, each
   **surviving mutant** — its location and what change went undetected.
5. **Turn survivors into findings.** Each surviving mutant is a concrete
   test-quality gap: "test at `X` executes `foo()` but never asserts the boundary
   — mutant `>=`→`>` survived at `file:line`." Recommend the specific missing
   assertion.
6. **Route fixes through the workflow.** Adding/strengthening tests is real work —
   it goes through the normal gates (hot-path or full flow), not inline.
7. **Log it.** Record the score and survivors in `.project/architecture-log/`
   (or the Finalize report). Persistent low-scoring modules become backlog items.

## Notes
- **Coverage ≠ quality.** 100% line coverage can still have a poor mutation score.
  Mutation testing is the check that catches "lightly-coupled breakage" the
  regression guardrail (Constraint #21) cares about.
- **Not a per-save tool.** It's for audits and high-risk hardening, kept targeted
  and opt-in so it never bogs down the normal Coder ↔ Reviewer loop.
- **Don't chase 100%.** Some survived mutants are equivalent (behaviorally
  identical) or not worth killing. Prioritize survivors in critical logic; note
  equivalents and move on.
- **Baseline once, then diff.** For a large module, record a baseline score so
  future runs show whether test quality is improving or regressing.
