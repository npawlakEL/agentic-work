# Vision: Lane Configuration Screen

## Overview

Rebuild the legacy SQL-based Lane Configuration screen as a modern web application using an agentic development workflow. The Lane Config screen allows warehouse/distribution center operators to map objects (such as Ship To destinations) to physical sorter lanes.

## Goals

### Product Goals
1. **Sorter Visualization** — Display a schematic/CAD-style diagram of the selected sorter with all lanes visible, showing current mappings immediately (no hover/click required).
2. **Object-to-Lane Mapping** — Allow users to assign objects (Ship To values, and future object types) to lanes via drag-and-drop or a bulk assignment UI.
3. **Many-to-Many Support** — Multiple objects can map to one lane; one object can map to multiple lanes.
4. **Persistence** — Save per-sorter with a pluggable backend adapter (currently SQLite, designed for swap-ability).
5. **Concurrent Users** — Support multiple users editing simultaneously.
6. **Extensibility** — Object types read from config (not hardcoded). Architecture ready for additional types beyond Ship To.

### Agentic Workflow Goals
1. **Planner** — Gathers requirements, produces specification documents for downstream agents.
2. **Coder** — Implements the project based on planner's spec.
3. **Reviewer** — QA stand-in that debugs, tests, and validates the coder's output. Iterates with coder until quality bar is met.
4. **Learner** — Captures lessons learned, guardrails, and patterns discovered during the process. Dumps findings into the `learnings/` folder.

## Success Criteria
- User can select a sorter from a dropdown, see its lanes graphically, and map/unmap objects.
- Mappings persist across page loads via backend API.
- Architecture is pluggable (backend adapter pattern) and extensible (config-driven object types).
- The agentic workflow (plan → code → review → learn) is documented and repeatable.
