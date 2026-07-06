# Taskboard

Owned by the **Senior Coder Agent**. This folder contains structured task breakdowns for each development cycle.

## Purpose

Before the Coder begins work, the Senior Coder produces a task breakdown here. This serves as:
- **Coder's work plan** — discrete stories to implement in order
- **Reviewer's checklist** — acceptance criteria to validate against
- **Learner's input** — post-project analysis of planned vs. actual delivery

## File Naming

```
{cycle-number}-{feature-name}.md
```

Example: `002-import-sorter-graphic.md`

## Story Format

Each story in a taskboard file follows this structure:

```markdown
### STORY-{N}: {Title}

**Status:** 🔴 Not Started | 🟡 In Progress | 🟢 Done | 🔵 Blocked
**Assigned to:** Coder
**Depends on:** STORY-{X} (if applicable)
**Estimated complexity:** S / M / L / XL

**Description:**
What needs to be built, in concrete terms.

**Acceptance Criteria:**
- [ ] Criterion 1 (testable, specific)
- [ ] Criterion 2
- [ ] Criterion 3

**Technical Notes (from Senior Coder):**
Architecture guidance, patterns to follow, files to touch.
```

## How Each Agent Uses This

| Agent | Usage |
|-------|-------|
| **Senior Coder** | Creates the breakdown, updates statuses, adds technical notes |
| **Coder** | Reads stories in dependency order, implements against acceptance criteria |
| **Reviewer** | Validates each story's acceptance criteria are met |
| **Learner** | Compares planned vs. actual, identifies scope accuracy, recommends improvements to Planner |
| **Planner** | Reads Learner's recommendations to improve future spec quality |

## Rules

1. Taskboard is created BEFORE the Coder starts (part of Gate 1.5)
2. Stories must have testable acceptance criteria — no vague "it should work well"
3. Dependencies must be explicit — if STORY-3 needs STORY-1, say so
4. Senior Coder updates statuses as the cycle progresses
5. Taskboard files are never deleted — they're historical records
6. The Learner MUST reference the taskboard in post-project analysis
