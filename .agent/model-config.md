# Model Configuration

Per-agent model recommendations for the harness. Different agents have different cognitive loads — matching the model to the job saves cost and time without sacrificing quality where it matters.

> **Tuned for:** Claude Opus 4.8 (reasoning-tier agents). The enforcement language throughout this harness assumes a model that follows long, nuanced instruction sets. Older/smaller models may need more repetition and explicit gating.

## Recommended Model per Agent

| Agent | Recommended Model | Why |
|-------|------------------|-----|
| **Orchestrator** | **Opus 4.8** | The enforcement brain. Classifies requests, runs the self-check, tracks state, coordinates all agents, and catches its own slips. Runs every turn — must be sharp. Economical alternative: Sonnet-tier if cost is a concern, but quality of enforcement drops. |
| **Planner** | **Opus 4.8** | Requirements reasoning, proactive questioning, vision nuance, and conversational judgment. Needs to infer what the user *isn't* saying and probe gaps. High reasoning load. |
| **Senior Coder** | **Opus 4.8** (non-negotiable) | The hardest technical reasoning in the harness — architecture, feasibility, scope enforcement, tradeoff analysis, review triage. This is where model quality matters most. Never downgrade this one. |
| **Coder** | **Sonnet-tier** (4.6 / 5) | Mechanical TDD execution against clear taskboard stories and Senior Coder guidance. Fast and capable. The reasoning is already done upstream — the Coder executes. |
| **Reviewer** | **Sonnet-tier** (4.6 / 5) | Runs tests, checks coverage, exercises the UI, finds bugs. Capable mid-tier work. Doesn't need deep architectural reasoning — that's the Senior Coder's job during triage. |
| **Learner** | **Sonnet-tier** (or Haiku if cost-sensitive) | Documentation writing, knowledge capture, changelog updates. Prose-heavy but not reasoning-heavy. Sonnet gives better doc quality; Haiku works if budget matters. |

## The Rule of Thumb

- **Reasoning-heavy (WHAT + HOW decisions):** Orchestrator, Planner, Senior Coder → **Opus 4.8**
- **Execution-heavy (do the thing that was decided):** Coder, Reviewer, Learner → **Sonnet-tier**

The three reasoning agents set direction; the three execution agents carry it out. Spend your best model on the agents that make decisions.

## Cost/Quality Tradeoffs

- If minimizing cost: only Senior Coder strictly needs Opus 4.8. Planner and Orchestrator can run Sonnet-tier acceptably, though questioning and enforcement quality soften slightly.
- If maximizing quality: run Opus 4.8 across the board. The execution agents won't hurt from it — they just may be slower/costlier than necessary.
- **Never** run the Senior Coder below Opus-tier. Architectural mistakes are the most expensive to unwind.

## Notes

- These recommendations are current as of the Opus 4.8 era. Re-evaluate when new models ship.
- Model assignment is set by the user in the app when launching or configuring each agent session — the harness documents the recommendation but does not enforce it programmatically.
