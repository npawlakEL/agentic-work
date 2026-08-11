# Client Documentation

This folder contains project documentation produced collaboratively after each development cycle.

## Structure

```
.client-docs/
├── technical/    ← For Coders and Senior Coders
└── operator/     ← For human operators using the system
```

## Collaboration Model

Documentation is NOT a solo effort. Different agents contribute based on doc type:

| Doc Type | Primary Author | Collaborators |
|----------|---------------|---------------|
| **Technical** | Learner | Senior Coder (architecture accuracy, patterns) |
| **Operator** | Learner | Planner (user workflows, UX context) |

- **Senior Coder** reviews technical docs for architectural correctness and adds implementation details the Learner may miss
- **Planner** reviews operator docs for user-facing accuracy — ensuring instructions match how the user actually interacts with the system
- **Learner** drives the writing and owns the final output

## Technical Docs (`technical/`)

**Audience:** Coders, Senior Coders, developers integrating with this system.

Contains:
- Architecture changes per feature
- New patterns and how to use them
- API changes, data model changes
- File structure changes
- Integration guidance
- Testing patterns
- Known limitations / tech debt

## Operator Docs (`operator/`)

**Audience:** Human operators interacting with the UI.

Contains:
- What's new (plain language)
- Button/control documentation (what it does, how to use it)
- Step-by-step workflows
- Screen descriptions
- Troubleshooting

## Naming Convention

```
{cycle-number}-{feature-name}.md
```

Example: `002-import-sorter-graphic.md`

## Rules

1. The Learner writes BOTH docs after every cycle — no exceptions
2. Senior Coder reviews technical docs before they're finalized
3. Planner reviews operator docs before they're finalized
4. Technical docs focus on "how it works internally"
5. Operator docs focus on "how to use it as a human"
6. Never mix audiences — keep them separate
7. Docs are cumulative — new cycles add new files, old files remain
