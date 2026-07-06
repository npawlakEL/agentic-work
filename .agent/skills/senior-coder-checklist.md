# Skill: Senior Coder Checklist

**Owner:** Senior Coder Agent
**Purpose:** A living checklist of things the Senior Coder always verifies. Grows over time as new patterns and anti-patterns are discovered.

## Before Feasibility Sign-off
- [ ] Read `architecture-log/current-architecture.md`
- [ ] Verify proposed changes don't violate existing patterns
- [ ] Check for dependency conflicts with in-progress work on other branches
- [ ] Confirm the proposed approach is the least-complex path to the goal
- [ ] Identify files that will be touched and assess blast radius

## During Spot-Checks
- [ ] Code follows established patterns (not reinventing)
- [ ] No unnecessary abstractions introduced
- [ ] Error handling is consistent with existing patterns
- [ ] No hardcoded values that should be configurable
- [ ] Tests are meaningful (not just coverage padding)

## Before Final Sign-off
- [ ] All acceptance criteria from taskboard are met
- [ ] No architectural drift from the approved approach
- [ ] Code is the most efficient approach (no over-engineering)
- [ ] No unused imports, dead code, or TODO comments without tickets
- [ ] Integration points are clean (no tight coupling)

## Review Triage (Reviewer Findings)
- [ ] Is this an architectural issue or a style/nitpick?
- [ ] Has this same issue appeared before? (check architecture-log)
- [ ] Does fixing this require structural changes or just local edits?
- [ ] Should this be logged as a pattern to prevent in future?

## Lessons Learned (grows over time)
<!-- Add entries here as projects reveal recurring issues -->
- Race conditions in concurrent edit scenarios — always check for optimistic locking
- CSS values that look hardcoded but are valid (100%, 1fr, color-mix percentages) — don't flag these
- Windows detached processes exit immediately for Node servers — use Start-Process workaround
- Squash-merge creates conflicts when additional commits exist on the same branch — use fresh temp branch
