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
- [x] Import sorter graphic feature spec (formats, workflow, placement, persistence)
- [x] Lane number visibility fix (always above/beside drop zones)
- [x] Config file architecture (single JSON → DB on startup → dual-write on save)
- [x] Terminology: "object type/value" → "criteria type/value"
- [x] Frontend isolation — plug-and-play component with props API, library build, no global side effects
- [x] UI documentation requirement — separated docs (ui-guide, backend-guide, controls-reference, integration, api-contract, theming, architecture)
- [x] Minimal backend API — thin CRUD layer, no business logic, replaceable
- [x] Static web assets — frontend builds to static files, backend serves them by default, also independently deployable
- [x] Frontend contract definitions — formal adapter interface in src/client/src/contracts/, single source of truth for backend integration

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
- Admin UI for creating new criteria types/values (user-defined criteria)

## Needs Elaboration
(none — all items elaborated)

## Resolved
- Bulk assignment UI: "Paint mode" — user selects an object value (becomes the active brush), then clicks/taps multiple lanes to assign. A toggle button or Escape exits paint mode. Most fluid for repetitive operator workflows.
- Concurrent edit conflict UX: Popup notification window with a "Refresh" button when another user has edited the same sorter's mappings. No silent overwrite — user is always informed.
