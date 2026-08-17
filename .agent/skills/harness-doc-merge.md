---
name: harness-doc-merge
description: >
  Use when merging or rebasing two branches whose divergent edits land in
  agent-harness documentation (.project/** or .agent/**) rather than code —
  including when the user asks to "intuitively merge/combine" harness docs.
  Combines parallel workstreams into one coherent doc instead of dropping a
  side or dumping raw conflict hunks.
load_when: >
  Any merge/rebase touching .project/** or .agent/** docs, or a request to
  combine/reconcile spec/vision/backlog/taskboard/log differences.
  Match keywords: merge, rebase, conflict, combine docs, spec, vision.
upstream: true
---
# Skill: Intuitive Harness-Doc Merge

## Trigger
Any time two branches are merged (or rebased) and the conflicts — or simply the
divergent edits — land in **agent-harness documentation** rather than code. This
covers everything under `.project/` (e.g. `spec.md`, `vision/vision.md`, backlog,
taskboard, architecture-log, learnings, planning-sessions, reviewer-log) and
`.agent/` (roles, skills). It also applies when the user says "do intuitive
merging / combining" for harness differences.

## Principle
Harness docs describe **workstreams**, not code. When two branches each advanced
the same doc for *different* features, the sides are almost never in true
conflict — they are two parallel stories that both belong in the merged result.
**Do NOT pick one side and drop the other, and do NOT blindly concatenate raw
conflict hunks.** Intuitively combine them so the merged doc reads as one coherent
document that carries *both* workstreams.

## Steps
1. **Detect.** After a merge/rebase, list conflicted or divergent files. Separate
   harness docs (`.project/**`, `.agent/**`) from code — this skill governs the
   docs; resolve code conflicts normally.
2. **Read both sides in full.** For each doc, view `ours` and `theirs` completely
   (`git show :2:<path>` / `:3:<path>` or the branch refs). Identify what feature
   cycle each side represents.
3. **Classify the relationship:**
   - **Disjoint workstreams** (most common) → combine as sibling sections.
   - **Same section edited** → merge sentence-level, keeping every distinct fact
     from both; never lose a decision, bookmark, or citation.
   - **One supersedes the other** → keep the newer, but preserve any unique
     context from the old as a short historical note if it's still referenced.
4. **Assemble intuitively:**
   - Keep a single umbrella title (`# ...`).
   - Give each workstream its own clearly-labeled section
     (e.g. `## Part A — <feature>`, `## Part B — <feature>`).
   - Demote each side's internal headings by one level so they nest cleanly under
     their part (an incoming `#` becomes `##`, `##` becomes `###`, etc.).
   - Preserve ordered lists, tables, citations (`file:line`), and BOOKMARK/decision
     markers verbatim from both sides.
5. **Remove every conflict marker** (`<<<<<<<`, `=======`, `>>>>>>>`) and verify
   none remain anywhere: `git grep -nE "^(<<<<<<<|=======|>>>>>>>)"`.
6. **Sanity-read** the combined doc top-to-bottom — it must read as one document,
   not two stapled together, with no duplicated boilerplate.
7. **Stage and commit** the resolved docs with the merge.

## Notes
- Bias to **inclusion**: if unsure whether a line matters, keep it. Harness docs
  are cheap to carry and expensive to lose (they hold decisions and citations).
- When a straight fast-forward merge occurs (one branch already contains the
  other), there are no harness differences to combine — skip this skill.
- If two sides make **genuinely contradictory decisions** (not just different
  topics) about the same thing, do NOT silently pick one — flag it to the user
  with both options and a recommendation.
- This is display/spec text only; it never changes code behavior. Still, always
  leave the doc self-consistent — no dangling "see section X" that no longer exists.
