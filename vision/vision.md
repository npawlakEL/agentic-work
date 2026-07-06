# Vision: Lane Configuration Screen

## Overview

Rebuild the legacy SQL-based Lane Configuration screen as a modern, isolated web application using an agentic development workflow. The Lane Config screen allows warehouse/distribution center operators to map criteria (such as Ship To destinations) to physical sorter lanes.

## Goals

### Product Goals
1. **Sorter Visualization** — Display a schematic/CAD-style diagram of the selected sorter with all lanes visible, showing current mappings immediately (no hover/click required). Support importing custom sorter graphics (PNG, SVG, etc.).
2. **Criteria-to-Lane Mapping** — Allow users to assign criteria (Ship To values, and future criteria types) to lanes via drag-and-drop or paint mode bulk assignment.
3. **Many-to-Many Support** — Multiple criteria can map to one lane; one criteria value can map to multiple lanes.
4. **Persistence** — Config file as durable storage, database as runtime cache. Dual-write on save, DB rebuilt from config on startup.
5. **Concurrent Users** — Support multiple users editing simultaneously with conflict detection (409 + Refresh popup).
6. **Extensibility** — Criteria types read from config (not hardcoded). Architecture ready for additional types beyond Ship To.
7. **Frontend Isolation** — UI is a self-contained, plug-and-play React component exportable as a library for embedding in any host application.
8. **Static Web Assets** — Frontend builds to static files served by backend (single deployable) or independently deployable to any web server.
9. **Documentation** — Comprehensive separated docs: UI guide, backend guide, controls reference, integration guide, API contract, theming guide, architecture overview.

### Agentic Workflow Goals
1. **Planner** — Gathers requirements, produces specification documents for downstream agents.
2. **Coder** — Implements the project based on planner's spec using strict TDD.
3. **Reviewer** — QA stand-in that debugs, tests, validates, and documents findings in `qa-log/`. Iterates with coder until quality bar is met.
4. **Learner** — Captures lessons learned, guardrails, and patterns discovered during the process. Outputs pushed as project artifacts.

## Success Criteria
- User can select a sorter from a dropdown, see its lanes graphically (default wireframe or imported image), and map/unmap criteria.
- Mappings persist via config file + database dual-write pattern.
- Architecture is pluggable (backend adapter pattern), extensible (config-driven criteria types), and isolated (library build for integration).
- Full documentation covers UI, backend, controls, integration, API contract, and theming.
- The agentic workflow (plan → code → review → learn) is documented, repeatable, and produces artifacts (qa-log, learnings).
