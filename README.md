# Agent Harness

A reusable agentic development framework for software projects. Clone this into any repo to get a full agent-driven workflow with gates, enforcement, and artifact tracking.

## Quick Start

1. Clone or copy this repo's contents into your project
2. The Orchestrator (your AI assistant) will prompt you to create a vision document
3. From there, the full workflow kicks in automatically

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
│   ├── skills/          ← Reusable skills (grows over time)
│   └── vision/          ← Product vision (co-created with user)
├── .project/            ← Project tracking & logs
│   ├── spec.md          ← Requirements specification
│   ├── planner-tasks.md ← Planning task tracker
│   ├── planning-sessions/ ← Q&A session logs
│   ├── taskboard/       ← Story breakdowns (Senior Coder)
│   ├── architecture-log/← Architectural decisions & issues
│   ├── reviewer-log/    ← Review findings & accountability
│   └── learnings/       ← Post-project learnings
├── .client-docs/                ← Public documentation
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
