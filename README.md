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
├── docs/                ← Public documentation
│   ├── technical/       ← For developers
│   └── operator/        ← For end users
└── CHANGELOG.md         ← Semantic versioning log
```

## Key Features

- **Gated workflow** — 6 gates prevent skipping steps
- **TDD-enforced** — no production code without tests first
- **Mandatory artifacts** — every agent produces tracked outputs
- **Failure escalation** — 3-5 failed iterations → Senior Coder intervenes → user escalation
- **Hot-path** — lightweight flow for small fixes
- **Rollback protocol** — revert first, diagnose second
- **Living skills** — patterns are captured and reused across projects
