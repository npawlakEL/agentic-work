# Agent Harness

A reusable agentic development framework for software projects. Clone this into any repo to get a full agent-driven workflow with gates, enforcement, and artifact tracking.

## Quick Start

Drop the harness into your repo and let the Orchestrator ingest it:

1. **Copy the harness in** — clone this repo, or copy `.agent/`, `.project/`, `.client-docs/`, and `CHANGELOG.md` into your existing project.
2. **Run `boot` first** — tell your AI assistant (the Orchestrator) `boot`. It deep-reads the entire harness, detects the repo type, runs an integrity check, and replies with a Boot Report plus an explicit commitment to follow the workflow. **Always do this before anything else** — dropping the files in does not guarantee the workflow gets followed; `boot` is what makes it stick.
3. **Create the vision** — the Planner walks you through `.project/vision.md` (the whiteboard).
4. **Work the flow** — describe what you want; the gated workflow (Planner → Senior Coder → Coder → Reviewer → Learner) kicks in automatically.

> Re-run `boot` anytime the workflow feels like it's slipping — it re-anchors the Orchestrator.

## Agent Roster

| Agent | Role |
|-------|------|
| **Orchestrator** | User-facing coordinator, gate manager, artifact enforcement |
| **Planner** | Requirements gathering, spec writing, vision co-creation |
| **Senior Coder** | Architecture authority, task breakdowns, feasibility, code oversight |
| **Coder** | TDD implementation, follows taskboard stories |
| **Reviewer** | QA, blunt accountability, visual verification |
| **Learner** | Knowledge capture, dual docs (technical + operator), changelog |

## Structure

```
├── .agent/              ← Agent framework (portable)
│   ├── agents.md        ← Master workflow (gates, constraints, protocols)
│   ├── model-config.md  ← Per-agent model recommendations
│   ├── roles/           ← Agent definitions with personalities
│   ├── skills/          ← Reusable skills (auto-load by description match)
│   └── tools/           ← Harness utilities (e.g. integrity check)
├── .project/            ← Project tracking & logs
│   ├── vision.md        ← Product vision / whiteboard (co-created with user)
│   ├── spec.md          ← Requirements specification
│   ├── planner-tasks.md ← Planning task tracker
│   ├── STATE.md         ← Live "where are we" snapshot (Orchestrator-maintained)
│   ├── planning-sessions/ ← Q&A session logs
│   ├── taskboard/       ← Story breakdowns (Senior Coder)
│   ├── architecture-log/← Architectural decisions & issues
│   ├── reviewer-log/    ← Review findings & accountability
│   ├── backlog/         ← Out-of-scope ideas (auto-captured)
│   └── learnings/       ← Post-project learnings
├── .client-docs/        ← Public documentation
│   ├── technical/       ← For developers
│   └── operator/        ← For end users
└── CHANGELOG.md         ← Semantic versioning log
```

## Model Recommendations

The harness is **tuned for Claude Opus 4.8** on the reasoning-tier agents. Match the model to the cognitive load:

| Agent | Recommended Model |
|-------|------------------|
| Orchestrator | Opus 4.8 |
| Planner | Opus 4.8 |
| Senior Coder | Opus 4.8 (non-negotiable) |
| Coder | Sonnet-tier |
| Reviewer | Sonnet-tier |
| Learner | Sonnet-tier (or Haiku) |

**Rule of thumb:** reasoning agents (Orchestrator, Planner, Senior Coder) get the best model; execution agents (Coder, Reviewer, Learner) run mid-tier. Full rationale in `.agent/model-config.md`.

## Key Features

- **Gated workflow** — 6 gates prevent skipping steps
- **TDD-enforced** — no production code without tests first
- **Mandatory artifacts** — every agent produces tracked outputs
- **Failure escalation** — 3-5 failed iterations → Senior Coder intervenes → user escalation
- **Hot-path** — lightweight flow for small fixes
- **Rollback protocol** — revert first, diagnose second
- **Living skills** — patterns are captured and reused across projects

## User Commands

Say these keywords to trigger special modes:

| Command | Who runs it | What it does |
|---------|-------------|--------------|
| **boot** | Orchestrator | Run FIRST after cloning the harness into a repo — deep-dive read of all harness docs + project state + codebase, then a self-verifying Boot Report and a commitment to follow the workflow. Re-runnable anytime the workflow feels like it's slipping |
| **grill me** | Planner | Systematically interrogates you with deep requirement questions before any work begins |
| **regroup** | Planner + Senior Coder | Joint review of the current state to surface additional questions, risks, and concerns |
| **finalize** | Senior Coder(s) | Read-only parallel codebase audit — bugs, security, quality, optimizations, open questions, doc gaps → prioritized report |
| **run nightwatch** | Senior Coder + Coder + Reviewer | Scheduled (nightly) trunk guardian — full suite + mutation testing on unchanged `master`, drafts fixes for real regressions (never merges, never weakens a test), posts a morning digest |
| **retro** | Orchestrator + Learner | Process retrospective that trains the harness — mines logs + your corrections, then curates skills (add sharp ones, prune sprawl, upstream universal ones) |

## Automatic Behaviors (no command needed)

The Orchestrator does these on its own — you never have to ask:

- **Fleet auto-scaling** — for large *and* shardable work (codebase deep-dive, big Finalize, broad refactor/migration, test backfill, multi-repo), the Orchestrator automatically spins up **N parallel Coder ↔ Reviewer loops** with exclusive file ownership and draft-PR-only rails. Small or tightly-coupled work stays single-track. Coupling, not size, decides. (`.agent/skills/fleet.md`, Constraint #25)
- **Auto-engage Senior Coder** — anything touching code pulls in the Senior Coder automatically. (Constraint #20)
- **Correction capture** — when you correct the agents, it's captured and promoted into a durable skill. (Constraint #23)
