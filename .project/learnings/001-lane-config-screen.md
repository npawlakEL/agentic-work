# 001 Lane Config Screen

- **Date**: 2026-06-30
- **Context**: Lane Configuration Screen project cycle covering the React/Vite UI, Node/Express API, SQLite-backed adapter layer, mock data mode, and review-driven hardening before merge.

## Insights
- Paint Mode was the right bulk-assignment interaction: treating a selected object as a reusable brush reduced drag repetition while still preserving precise lane-by-lane control.
- SVG-based sorter diagrams plus a registry pattern gave the team a practical v1 CAD wireframe without coupling mapping logic to a single physical layout.
- A mock data layer kept frontend work moving independently of backend availability and made UI iteration faster.
- Optimistic concurrency needs sorter-aware state boundaries. Switching sorters while async loads/saves are in flight can apply stale mappings or versions to the wrong sorter unless requests are scoped and ignored when outdated.
- Version state must only advance on successful saves. Reusing failed save responses to mutate version data creates client-side corruption that is harder to notice than a visible error.
- Adapter abstractions only help if route handlers depend on adapter contracts, not storage-specific imports.
- Design-token rules need enforcement during implementation, not just in the spec; hardcoded CSS slips in easily during UI polish.

## Actionable Guidance
- For editable multi-entity screens, model request state per entity and drop late async responses when the selected entity changes.
- Treat 409 conflict handling separately from generic save failures: refresh only on confirmed version conflicts, and preserve local version state for all other errors.
- Validate mapping payloads at the API boundary even if the UI constrains normal input.
- Keep adapters pure by exposing all persistence operations through the adapter interface before wiring routes.
- Add review checklists or lightweight searches for raw hex values, px literals, and direct storage imports before sign-off.
- Preserve a mock repository/data provider in early full-stack work so frontend progress is never blocked by backend startup or schema churn.

## Reviewer Findings
- The reviewer caught stale sorter save/load races, version corruption on non-409 failures, missing backend validation, SQLite leakage past the adapter boundary, hardcoded theme values, and gaps in interaction coverage.
- Prevent repeats by adding tests for sorter switching during async work, failed-save recovery, API validation rejection paths, adapter-only route imports, token-only styling, Paint Mode escape behavior, drag-drop assignment, and mapping chip color rendering.
