# Changelog

All notable changes to this project are documented here.

## Versioning Scheme

**Format:** `MAJOR.MINOR.PATCH` (e.g., `1.2.3`)

| Position | When to increment | Example |
|----------|-------------------|---------|
| **PATCH** (0.0.X) | Minor revision, bug fix, hot-patch, small change | `0.0.1` → `0.0.2` |
| **MINOR** (0.X.0) | Major feature addition, significant new functionality | `0.1.0` → `0.2.0` |
| **MAJOR** (X.0.0) | Full version release — a component of all accumulated changes, milestone delivery | `0.2.3` → `1.0.0` |

## Ownership

- The **Learner** updates this file after every cycle (Gate 3)
- The **Orchestrator** approves version number increments
- Version bumps are committed alongside learnings and docs

---

## [0.1.0] — 2026-06-30

### Added
- Lane Configuration Screen — full drag-and-drop UI for mapping criteria to sorter lanes
- CAD wireframe SVG diagram (shoe sorter with branching lanes)
- Axon/Element Logic theme integration (light theme, Indigo primary, Roboto)
- Mock data layer (frontend works standalone without backend)
- TDD test suite (34 tests passing)

### Technical
- React + Vite frontend, Node/Express backend, SQLite adapter pattern
- @dnd-kit/core for drag-and-drop
- CSS custom properties theming system
- Pluggable diagram registry pattern

---

## [0.0.1] — 2026-07-06

### Added
- Agent framework: Orchestrator, Planner, Senior Coder, Coder, Reviewer, Learner
- Repo structure: `.agent/`, `.project/`, `docs/`, `src/`
- Taskboard system for story breakdowns
- Architecture log for Senior Coder decisions
- Failure Escalation Protocol (3-5 iterations → escalate)
- Dual documentation system (technical + operator)
- Agent personalities defined
- Versioning and changelog system (this file)

### Changed
- Reorganized flat file structure into grouped `.agent/` and `.project/` directories
- All internal path references updated
