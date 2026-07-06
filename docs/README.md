# Documentation

This folder contains project documentation produced by the **Learner Agent** after each development cycle.

## Structure

```
docs/
├── technical/    ← For Coders and Senior Coders
└── operator/     ← For human operators using the system
```

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
2. Technical docs focus on "how it works internally"
3. Operator docs focus on "how to use it as a human"
4. Never mix audiences — keep them separate
5. Docs are cumulative — new cycles add new files, old files remain
