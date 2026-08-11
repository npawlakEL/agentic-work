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
- **Orchestrator** — the ONLY agent that writes to `.agent/skills/`. It receives candidates from all agents, classifies them, and writes the final skill file.
- **Planner** — surfaces candidates about questioning techniques, delegation patterns, spec structuring, and communication approaches that worked
- **Senior Coder** — surfaces candidates continuously (architecture patterns, debugging techniques, code patterns)
- **Coder** — surfaces candidates during implementation (repeated patterns, setup sequences, testing strategies)
- **Reviewer** — surfaces candidates during review (bug pattern recognition, QA techniques, testing approaches)
- **Learner** — surfaces candidates during knowledge capture (process patterns, workflow optimizations, retrospective insights)

## Flow
```
Any Agent spots a pattern → surfaces candidate to Orchestrator →
Orchestrator classifies (Universal / Project-Specific / Learning / Dismissed) →
Orchestrator writes to appropriate location →
Universal skills pushed to agent-harness at cycle end
```
