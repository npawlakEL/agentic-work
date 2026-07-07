# Planning Sessions

Captured Q&A sessions between the **Planner**, **Senior Coder**, and the **User** during requirements gathering.

## Purpose

Every time the Planner/Senior Coder goes back and forth with the user asking questions, clarifying requirements, or discussing architecture — that conversation is logged here. This preserves:
- Decisions made and WHY
- Questions asked and answers given
- Context that informed the spec
- Technical feasibility discussions

## File Naming

```
{number}-{feature-or-topic}.md
```

Example: `001-lane-config-requirements.md`, `002-auth-architecture.md`

## Format

Each file captures the session as a structured Q&A:

```markdown
# Planning Session: {Topic}
**Date:** YYYY-MM-DD
**Participants:** Planner, Senior Coder, User

## Questions & Answers

### Q1: {Question from Planner/Senior Coder}
**A:** {User's answer}
**Decision:** {What was decided}

### Q2: ...

## Outcomes
- {What was added to spec}
- {What was deferred}
- {Open items remaining}
```

## Rules

1. The Orchestrator captures these AUTOMATICALLY during planning conversations
2. Every planning session that involves Q&A gets logged — no exceptions
3. Files are committed alongside spec updates
4. The Learner references these for "how did we arrive at this decision?" context
