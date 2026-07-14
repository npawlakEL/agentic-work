# Skills

This folder contains reusable task patterns discovered during development. Skills are recurring tasks that agents can reference to avoid repeating work.

## Format

Each skill file should include:
- **Name** — short descriptive name
- **Trigger** — when to use this skill
- **Steps** — the sequence of actions
- **Notes** — any caveats or variations

## Skill Classification

Skills are classified into two types:

### Project-Specific Skills
Only relevant to this project's stack, codebase, or domain. These stay in the project repo only.

### Universal Skills (Upstream)
Reusable across ANY project. These are flagged for upstream push to the `agent-harness` branch (source of truth).

**To mark a skill as universal**, add this comment at the top of the skill file:
```markdown
<!-- UPSTREAM: true -->
```

The Orchestrator collects all `UPSTREAM: true` skills at cycle end and pushes them to the `agent-harness` branch so all future projects inherit them.

## Who Creates Skills
- **Senior Coder** — continuously discovers skills throughout the workflow (primary skill author)
- **Coder** — captures implementation patterns it uses repeatedly
- **Learner** — captures process/workflow patterns from learnings
