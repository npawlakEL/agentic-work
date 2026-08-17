---
name: retro
description: >
  Use to run a process retrospective that TRAINS the harness: mine reviewer/
  architecture logs, learnings, and this session's user corrections; then
  propose NEW/UPDATED/STALE skill changes, get user sign-off, and curate the
  skill set (add sharp skills, prune sprawl, upstream universal ones).
load_when: >
  A "retro" request, end-of-cycle/milestone review of the process (not the code),
  or promoting captured corrections/log patterns into durable skills.
  Match keywords: retro, retrospective, train the harness, curate skills,
  promote learnings, prune skills.
upstream: true
---
# Skill: Retro (Process Retrospective — Train the Harness)

## Trigger
- The user says "retro."
- End of a cycle or a milestone, when scattered feedback should become memory.
- The process-side complement to Finalize (Finalize audits the code; Retro audits
  the harness's memory).

## What it does
Turns feedback data into curated, auto-loading skills so the same mistakes stop
recurring. This is the main mechanism for "training" the harness over time.

## Steps
1. **Mine the feedback data:**
   - `reviewer-log/` + `architecture-log/` — issues that recur 2+ times, repeated
     triage decisions, the same bug in different clothes.
   - `learnings/` — lessons captured but never promoted into skills.
   - **This session's user corrections** — every override/redirect (see
     Correction-Capture reflex, Constraint #23). These are the richest signal.
   - Hygiene candidates surfaced by Finalize/Nightwatch (unpromoted patterns,
     never-fired or conflicting skills).
2. **Propose changes as a concrete list:**
   - **NEW** — recurring patterns → new skills. Write a sharp `description`/
     `load_when` so they actually FIRE, and cite the incident under Evidence.
   - **UPDATED** — skills that were vague, wrong, or incomplete → fix them.
   - **STALE** — skills that never fired, are duplicated, or now contradict each
     other → prune or merge. Pruning is as valuable as adding.
3. **Get user sign-off** on the proposed set.
4. **Write the approved skills** (the Orchestrator is the only skill-writer),
   set `upstream: true` on universal ones, and push universal ones to
   `agent-harness`.
5. **Log the retro** to `.project/learnings/` (dated) so the training history is
   itself recorded.

## Report format
```
🔁 RETRO — <date>
Mined: <n> log entries, <n> corrections, <n> learnings
➕ NEW skills:      <name> — <why> (evidence: <ref>)
✏️  UPDATED skills:  <name> — <what changed>
🗑️  STALE skills:    <name> — prune/merge (<reason>)
⬆️  Upstream:        <universal skills pushed to agent-harness>
```

## Notes
- Curated memory, NOT sprawl — quality over count. A small sharp skill set beats
  a large fuzzy one (the agent matches against every skill's frontmatter).
- Prefer **evidence-backed** skills — cite the log entry or correction that
  motivated each one; unbacked skills get culled first.
- Don't wait for Retro on repeated corrections: the Correction-Capture reflex
  promotes a skill immediately on the SECOND occurrence. Retro is the bulk,
  deliberate curation pass.

## Evidence
Motivated by user request to close the learning loop so corrections and log data
become durable training rather than one-off fixes (2026-08-17).
