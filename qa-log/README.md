# QA Log

This folder contains a chronological record of every issue found by the Reviewer and how the Coder resolved it during the Coder ↔ Reviewer iteration loop (Gate 2).

## Purpose
- Full transparency into what broke and how it was fixed
- Input for the Learner agent to analyze patterns and extract future learnings
- Audit trail for debugging recurring issues across projects

## Format

Each file is named: `{number}-{short-description}.md` (e.g., `001-missing-null-check.md`)

Each entry should include:
- **Issue #** — sequential number
- **Found by:** Reviewer
- **Iteration:** Which coder/reviewer loop pass this was found in
- **Description** — What the Reviewer found (expected vs. actual behavior)
- **Severity** — Critical / High / Medium / Low
- **Failing Test Written** — The test the Coder wrote to capture this issue (TDD)
- **Fix Applied** — What the Coder did to resolve it
- **Verified** — Reviewer confirmed the fix (yes/no + notes)
