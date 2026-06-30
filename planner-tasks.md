# Planner Task List

## Done
- [x] Data model for lane mappings (sorter_id, lane_id, object_type, object_value)
- [x] Sorter visualization approach (CAD wireframe, pluggable diagram registry)
- [x] Object type architecture (config-driven, extensible beyond Ship To)
- [x] Adaptive UI theming layer (CSS custom properties, swappable theme files)
- [x] Backend adapter pattern (pluggable DB layer — SQLite now, swappable later)
- [x] Many-to-many mapping support confirmed
- [x] Save behavior (per-sorter, user-triggered)
- [x] Tech stack (React + Vite frontend, Node/Express backend)
- [x] Agentic workflow structure (planner, coder, reviewer, learner)
- [x] Git flow branching strategy
- [x] TDD requirement for coder agent
- [x] Single-push rule and PR merge gate

## Open
(none)

## Questions
(all resolved)

## Bookmarked (Deferred)
- Sorter/lane configuration editing UI (separate project)
- Priority ordering of objects within a lane
- User authentication/authorization
- Real-time sync via WebSockets (using polling/manual refresh for now)
- Lane names/descriptions (just numbers for now)
- Additional object types beyond Ship To (architecture supports it, values TBD)

## Needs Elaboration
- CAD import workflow for future — how will developers register new sorter SVGs? (documented in spec but not detailed)

## Resolved
- Bulk assignment UI: "Paint mode" — user selects an object value (becomes the active brush), then clicks/taps multiple lanes to assign. A toggle button or Escape exits paint mode. Most fluid for repetitive operator workflows.
- Concurrent edit conflict UX: Popup notification window with a "Refresh" button when another user has edited the same sorter's mappings. No silent overwrite — user is always informed.
