# Skills

This folder contains reusable task patterns discovered during development. Skills are recurring tasks that agents can reference to avoid repeating work.

## Format

**Every skill file MUST begin with a YAML frontmatter block** so agents can auto-load it by matching the task at hand against the `description`. The frontmatter is the machine-readable trigger; the body is the human-readable procedure.

```markdown
---
name: commit-and-push
description: >
  Use when committing or pushing code/docs to a branch, or when deciding
  whether a push is allowed under the current gate. Covers push timing,
  the single-push-per-cycle rule, and Gate 2.5 approval.
load_when: >
  Any task that commits, pushes, or reasons about when to push.
  Match keywords: commit, push, remote, branch, Gate 2.5.
upstream: true   # true = universal (push to agent-harness); false = project-specific
---
```

Frontmatter fields:
- **name** — short kebab-case identifier (matches the filename)
- **description** — what the skill does AND when it applies, written so an agent can decide relevance by reading this line alone
- **load_when** — the explicit trigger: the task types / keywords that mean "load me now"
- **upstream** — `true` for universal skills (collected and pushed to `agent-harness`), `false` for project-specific. This replaces the old `<!-- UPSTREAM: true -->` comment; if you see that comment on an older skill, migrate it to the `upstream:` field.

After the frontmatter, the body should include:
- **Trigger** — human-readable restatement of when to use this skill
- **Steps** — the sequence of actions
- **Notes** — any caveats or variations
- **Evidence** (recommended) — cite the incident that motivated the skill (e.g. `reviewer-log/007`, `architecture-log/2026-08-11`, or "user correction on 2026-08-17"). Evidence-backed skills are trusted more and survive pruning during Retro; skills with no rationale are the first to be culled.

## Auto-Loading (how agents use skills)

Skills are **self-loading by description match** — an agent does NOT wait to be told to use a skill:
1. At the START of every invocation, each agent scans the frontmatter (`name` + `description` + `load_when`) of every skill file in `.agent/skills/` — **excluding this `README.md`**, which documents the format and is not itself a loadable skill. Reading just the frontmatter is cheap.
2. For each skill whose `description`/`load_when` matches the task the agent is about to do, the agent **loads the full skill body and follows it** — no reinventing, no asking permission.
3. If multiple skills match, all matching skills are loaded. If none match, the agent proceeds normally.
4. Skills are mandatory once matched: an agent may not do a task a matching skill covers while ignoring that skill.

This is why the `description`/`load_when` must be written for MATCHING, not just for humans — a vague description means the skill won't fire when it should.

## Skill Classification

Skills are classified into two types:

### Project-Specific Skills
Only relevant to this project's stack, codebase, or domain. These stay in the project repo only.

### Universal Skills (Upstream)
Reusable across ANY project. These are flagged for upstream push to the `agent-harness` branch (source of truth).

**To mark a skill as universal**, set `upstream: true` in the frontmatter block (see Format above). The Orchestrator collects all `upstream: true` skills at cycle end and pushes them to the `agent-harness` branch so all future projects inherit them.

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
