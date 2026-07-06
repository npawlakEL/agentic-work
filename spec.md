# Lane Configuration Screen — Specification

**Version:** 1.0
**Status:** Ready for implementation
**Author:** Planner Agent
**Date:** 2026-06-30

---

## 1. Overview

A web application that allows warehouse operators to map objects (Ship To destinations, and future object types) to physical sorter lanes. The UI displays a schematic view of a sorter with its lanes and shows all current mappings at a glance.

---

## 2. Data Model

### 2.1 Sorter Configuration (read from config)
| Field | Type | Description |
|-------|------|-------------|
| id | string | Unique sorter identifier |
| name | string | Display name |
| laneCount | number | Number of lanes on this sorter |

### 2.2 Object Types (read from config)
| Field | Type | Description |
|-------|------|-------------|
| id | string | Object type identifier (e.g., "ship-to") |
| name | string | Display name (e.g., "Ship To") |
| values | array | List of `{ id, label }` representing possible values |

### 2.3 Lane Mappings (persisted to backend)
| Field | Type | Description |
|-------|------|-------------|
| sorter_id | string | FK to sorter |
| lane_id | number | Lane number on the sorter |
| object_type | string | Type of object mapped (e.g., "ship-to") |
| object_value | string | Value ID of the mapped object (e.g., "S1") |

- One row per mapping.
- Many-to-many: multiple objects per lane, same object on multiple lanes.
- No priority ordering required for v1.

### 2.4 Mapping Version (concurrency control)
| Field | Type | Description |
|-------|------|-------------|
| sorter_id | string | FK to sorter |
| version | number | Auto-incrementing version, bumped on each save |

- Returned with GET /api/mappings/:sorterId.
- Client sends version on POST. Server rejects if version doesn't match (409 Conflict). Client shows "Refresh" popup.

### 2.4 Initial Config Data
**Sorters:**
- Sorter A: 12 lanes
- Sorter B: 16 lanes
- Sorter C: 10 lanes

**Object Types:**
- Ship To: values S1, S2, S3, S4

---

## 3. UI Requirements

### 3.1 Layout
```
┌─────────────────────────────────────────────────────────────┐
│  Lane Configuration                          [Load] [Save]  │
├─────────────────────────────────────────────────────────────┤
│  Sorter: [▼ Sorter A (12 lanes)]                            │
│  Object Type: [▼ Ship To]   Value: [▼ All / S1..S4]        │
├──────────┬──────────────────────────────────────────────────┤
│ Drag to  │                                                  │
│ assign:  │         Lane 1 ──── [S1][S3]                     │
│          │              │                                    │
│  [S1]    │         Lane 2 ──── [  ]                         │
│  [S2]    │              │                                    │
│  [S3]    │  ════════════════════════════ ← Main Conveyor    │
│  [S4]    │              │                                    │
│          │         Lane 3 ──── [S2]                          │
│          │              │                                    │
│          │         Lane 4 ──── [S1][S4]                      │
│          │                                                   │
│          │   (CAD Wireframe Diagram - swappable per sorter)  │
└──────────┴──────────────────────────────────────────────────┘
```

### 3.2 Sorter Diagram (CAD Wireframe Layer)

The centerpiece of the UI is a **CAD-style wireframe diagram** of the selected sorter. This is NOT a simple row of boxes — it's a schematic representation of the physical sorter hardware.

**Architecture: Pluggable Diagram Renderer**

Each sorter has an associated diagram component that renders its physical layout. This is designed as an **adaptive/swappable layer** so that:
1. Different sorters can have completely different visual layouts (shoe sorter, tilt-tray, crossbelt, etc.)
2. In the future, actual CAD drawings (SVGs exported from engineering tools) can be imported and used directly.
3. Swapping a sorter's diagram requires only changing the diagram component/SVG registered for that sorter — no core logic changes.

**Diagram Registry Pattern:**
```
src/client/src/diagrams/
├── index.js                  # Registry: maps sorter ID → diagram component
├── ShoeSorterDiagram.jsx     # Default: straight conveyor with branching lanes
├── TiltTrayDiagram.jsx       # Future: circular tilt-tray layout
└── imported/                 # Future: imported SVG/CAD files converted to components
```

Each diagram component:
- Receives `lanes` (array of lane numbers) and `mappings` (current mapping data) as props
- Renders an SVG or canvas schematic of the physical sorter
- Each lane in the diagram is an interactive drop target
- Mappings are displayed directly on/near each lane in the diagram (colored labels/chips)
- The diagram is responsible for its own layout (conveyor path, lane positions, angles)

**Default Diagram (v1):** A shoe sorter / roller divert schematic:
- A horizontal main conveyor belt (the "spine")
- Lanes branch off at angles (alternating left/right or all to one side)
- Each lane is labeled with its number
- Lane drop zones are visually distinct and highlight on drag-over
- Mapped objects appear as colored chips at each lane's branch point

```
         Lane 1 ─────┐
                      │
         Lane 2 ─────┤
                      │
═══════════════════════════════════  ← Main Conveyor
                      │
         Lane 3 ─────┤
                      │
         Lane 4 ─────┘
```

**Future Vision:** ~~Developers will import actual CAD SVGs from engineering software, register them in the diagram registry, and the system will overlay the interactive lane drop targets onto the imported drawing.~~ — **NOW IMPLEMENTED** (see 3.4 below).

### 3.3 Behavior
1. **Page Load:** Auto-select the first sorter (or cached selection from last session via localStorage).
2. **Sorter Dropdown:** Switching sorter reloads the diagram and mappings for that sorter (loads the registered diagram component).
3. **Object Type Dropdown:** Selects the category (e.g., "Ship To"). Determines what values appear in the value dropdown and the drag panel.
4. **Value Dropdown:** Optionally filter to a single value or show "All values" in the drag panel.
5. **Sorter Diagram:** CAD wireframe renders the physical sorter layout. Mappings displayed directly on each lane branch. No hover or click required to see mappings.
6. **Mapping:**
   - Drag-and-drop from sidebar onto lane targets in the diagram
   - **Bulk Assignment ("Paint Mode"):** User selects an object value from the sidebar (it becomes the active "brush," visually highlighted). While in paint mode, clicking any lane in the diagram assigns that object to the lane. User exits paint mode via a toggle button, pressing Escape, or selecting a different object. This enables rapid multi-lane assignment without repeated drag operations.
7. **Unmapping:** Click a remove button (×) on a mapping chip to remove it.
8. **Save:** Saves the current sorter's mappings to the backend. Each sorter saved individually.
9. **Load:** Fetches current mappings from the backend on sorter selection.
10. **Color Coding:** Each object value gets a distinct color for visual differentiation.
11. **Concurrent Edit Notification:** If another user has saved changes to the same sorter since the current user loaded, a popup dialog appears with the message "Another user has updated this sorter's configuration" and a "Refresh" button. The user must refresh to see the latest state before saving. Detection via a version/timestamp field returned by the API.

### 3.4 Import Sorter Graphic

Users can import a custom image to replace the default SVG wireframe diagram for any sorter.

**UI:**
- "Import Graphic" button in the toolbar area (next to Save/Load buttons)
- Clicking opens a file picker dialog

**Supported Formats:** Any browser-renderable image — PNG, JPG, SVG, WebP, GIF

**Workflow:**
1. User clicks "Import Graphic"
2. File picker opens; user selects an image file
3. The imported image replaces the default SVG wireframe as the sorter's diagram background
4. The UI enters "lane placement mode" — user clicks on the image to position each lane target (drop zone)
5. Lane targets are numbered and positioned wherever the user clicks
6. Once all lanes are placed, the user exits placement mode (e.g., via a "Done Placing" button)
7. The layout (image + lane positions) is saved per-sorter and persists across sessions

**Lane Placement Mode:**
- Visual indicator showing "Click to place Lane N" where N is the next lane number
- Each placed lane target shows its number prominently
- Ability to drag/reposition an already-placed lane target
- "Reset Placement" option to start over
- Lane count comes from the sorter's config (e.g., 12 lanes for Sorter A)

**Persistence:**
- The imported image is stored (as a data URL or uploaded file reference) per sorter
- Lane target positions (x, y coordinates as percentages of image dimensions) are stored per sorter
- On next load, if a custom graphic exists for the selected sorter, it renders the image with overlaid lane targets at their saved positions

**Fallback:**
- If no custom graphic has been imported for a sorter, the default SVG wireframe diagram is shown
- User can "Remove Graphic" to revert to the default wireframe

### 3.5 Lane Number Visibility

Lane numbers MUST always be visible regardless of how many mapping chips are displayed.

**Rules:**
- Lane number labels are positioned ABOVE or BESIDE the drop zone area, never underneath mapping chips
- Lane numbers use a fixed position that does not shift when chips are added/removed
- In the SVG wireframe: lane labels sit at the end of the branch line, clearly separated from the chip overlay area
- In imported graphics mode: lane numbers display as a bold badge on each placed lane target

### 3.3 Constraints
- Lanes can be left empty (no validation requiring all lanes mapped).
- No max objects per lane.
- Ship To's do not have to be assigned to any lane.
- Support concurrent users (optimistic updates or last-write-wins for v1).
- On save, if the server detects a version mismatch (another user saved since this user loaded), the save is rejected and a popup dialog tells the user to refresh.

---

## 4. Architecture

### 4.1 Tech Stack
- **Frontend:** React + Vite
- **Drag-and-Drop:** @dnd-kit/core
- **Styling:** CSS variables-based theming layer (see 4.6 below)
- **Backend:** Node.js + Express
- **Database:** SQLite (via adapter pattern — pluggable)
- **Concurrency:** Basic support (last-write-wins for v1, can upgrade to conflict resolution later)

### 4.1.1 Frontend Isolation (Plug-and-Play Architecture)

The frontend UI MUST be fully isolated and self-contained so it can be embedded into any host application without modification.

**Requirements:**
1. **Single entry point** — The entire Lane Configuration UI is exported as one React component (`<LaneConfigScreen />`) that can be imported and rendered by any host app.
2. **Props-based configuration** — All external dependencies are injected via props:
   - `apiBaseUrl` — where to fetch data (allows host app to route through its own backend)
   - `sorters` — optional: inject sorter list directly instead of fetching
   - `onSave(mappings)` — callback when user saves (host app handles persistence if desired)
   - `onError(error)` — callback for error handling
   - `theme` — optional theme override (CSS custom properties object)
3. **No global side effects** — No global CSS leakage (all styles scoped or namespaced), no global state, no direct DOM manipulation outside its container.
4. **Self-contained dependencies** — All npm packages bundled; host app doesn't need to install @dnd-kit or other internals.
5. **Build output** — Produces both:
   - A standalone app (`npm run dev` / `npm run build`) for development/preview
   - A library build (`npm run build:lib`) that exports the component for integration
6. **No backend coupling** — The UI works with a mock data layer OR real API. The API layer is injectable/configurable.

**Integration example (for host app):**
```jsx
import { LaneConfigScreen } from '@element-logic/lane-config';
import '@element-logic/lane-config/styles.css';

<LaneConfigScreen
  apiBaseUrl="/api/lane-config"
  onSave={(mappings) => console.log('Saved:', mappings)}
  onError={(err) => toast.error(err.message)}
/>
```

**Package structure:**
```
dist/
├── lane-config.es.js     # ESM library build
├── lane-config.umd.js    # UMD build (for script tag or legacy bundlers)
├── styles.css            # All styles (scoped, token-based)
└── index.d.ts            # TypeScript declarations (future)
```

### 4.1.2 Documentation Structure

The project MUST include comprehensive, separated documentation in a `docs/` folder:

```
docs/
├── ui-guide.md              # Frontend UI documentation
├── backend-guide.md         # Backend/API implementation documentation
├── controls-reference.md    # All buttons, dropdowns, and interactive elements
├── integration-guide.md     # How to embed the UI in a host application
├── api-contract.md          # Formal API contract (request/response schemas)
├── theming-guide.md         # How to customize visual design
└── architecture.md          # System overview, data flow, component diagrams
```

**docs/ui-guide.md — Frontend UI Documentation:**
- Component tree and hierarchy
- State management approach (what state lives where)
- Data flow (props down, events up)
- Each screen/view and what it displays
- User workflows (step-by-step with diagrams)
- How the mock data layer works vs. real API mode

**docs/backend-guide.md — Backend Implementation Documentation:**
- Server startup lifecycle (config file → DB population)
- Database schema and table structure
- Config file format and location
- Dual-write mechanism (DB + config file on save)
- Error handling patterns
- How to add new criteria types
- Environment variables and configuration options

**docs/controls-reference.md — Buttons & Interactive Elements:**
Every interactive control documented with:
- **Name** — what the control is called
- **Location** — where it appears in the UI
- **Type** — button, dropdown, drag target, toggle, etc.
- **Behavior** — exactly what happens when activated
- **States** — enabled, disabled, loading, active, etc.
- **Keyboard shortcuts** — if any (e.g., Escape exits paint mode)

Controls to document:
| Control | Type | Behavior |
|---------|------|----------|
| Sorter dropdown | select | Switches active sorter, reloads diagram + mappings |
| Object/Criteria Type dropdown | select | Filters which criteria values appear in sidebar |
| Value dropdown | select | Filters sidebar to single value or "All" |
| Load button | button | Fetches current mappings from backend |
| Save button | button | Persists current mappings (disabled while loading) |
| Import Graphic button | button | Opens file picker for sorter image import |
| Value chips (sidebar) | draggable + clickable | Drag to assign OR click to enter paint mode |
| Paint Mode toggle | toggle | Activates/deactivates paint mode brush |
| Lane drop zones | drop target + click target | Accepts drops; in paint mode, click assigns active brush |
| Mapping chip (×) button | button | Removes that mapping from the lane |
| Refresh button (conflict dialog) | button | Reloads latest mappings after version conflict |
| Done Placing button | button | Exits lane placement mode (import graphic flow) |
| Reset Placement button | button | Clears all placed lane targets, restart placement |
| Remove Graphic button | button | Reverts sorter to default SVG wireframe |
| Escape key | keyboard | Exits paint mode |

**docs/integration-guide.md — Plug-and-Play Integration:**
- Installation steps
- Import and render (`<LaneConfigScreen />`)
- Full props API reference with types and defaults
- Event callbacks (onSave, onError, onMappingChange)
- How to provide a custom API adapter
- How to inject theme overrides
- Example: embedding in a Next.js app
- Example: embedding in an existing Express app

**docs/api-contract.md — Formal API Contract:**
- Every endpoint with method, path, description
- Request body schemas (JSON Schema or TypeScript types)
- Response body schemas
- Error response shapes (400, 409, 500)
- Versioning/conflict detection protocol
- Example requests and responses for each endpoint

**docs/theming-guide.md — Visual Customization:**
- List of all CSS custom properties (tokens)
- How to create a new theme file
- How to override tokens at runtime via props
- Color palette reference
- Spacing/typography scale

**docs/architecture.md — System Overview:**
- High-level architecture diagram
- Frontend ↔ Backend communication flow
- Config file lifecycle diagram
- Component dependency graph
- Technology decisions and rationale

### 4.1.3 Minimal Backend API

The backend MUST be as minimal as possible — a thin layer that reads/writes the config file and maintains the runtime DB cache. No business logic beyond validation lives in the backend.

**Principles:**
- CRUD only — no computed endpoints, no aggregation
- Stateless request handling (no sessions)
- All intelligence lives in the frontend (sorting, filtering, UI state)
- Backend is replaceable — any system that implements the API contract can serve the frontend

**Static Web Assets (Default Deployment):**
- The backend serves the built frontend as static files by default (`express.static('dist')`)
- `npm run build` in the client produces a `dist/` folder with HTML, CSS, JS — no runtime dependencies
- Single deployable: one server process serves both the API and the webpage
- The frontend is ALSO independently deployable — the static assets work on any web server (Nginx, CDN, S3, another app's `public/` folder) by pointing the API base URL to wherever the backend runs
- This gives maximum integration flexibility with zero extra effort

### 4.1.4 Frontend Contract Definitions

The frontend MUST define a formal contract (TypeScript interface or JSDoc) for the API it expects. This contract lives in the source code as the authoritative definition of what the UI needs from any backend.

**Location:** `src/client/src/contracts/`

```
src/client/src/contracts/
├── index.js              # Exports all contract types
├── api-adapter.js        # Interface: what methods the API layer must provide
├── sorter.js             # Shape: Sorter object
├── mapping.js            # Shape: Mapping object (sorterId, laneId, criteriaType, criteriaValue)
├── criteria.js           # Shape: CriteriaType, CriteriaValue
└── responses.js          # Shape: API response envelopes (success, error, conflict)
```

**API Adapter Interface (the contract):**
```javascript
/**
 * Any backend that serves the Lane Config UI must implement this interface.
 * The frontend is decoupled from HTTP — it calls these methods,
 * which can be backed by fetch(), mock data, WebSocket, or anything else.
 */
const ApiAdapter = {
  /** @returns {Promise<Sorter[]>} */
  getSorters: async () => {},

  /** @returns {Promise<CriteriaType[]>} */
  getCriteriaTypes: async () => {},

  /** @returns {Promise<{ mappings: Mapping[], version: number }>} */
  getMappings: async (sorterId) => {},

  /** @returns {Promise<{ ok: boolean, status: number, data: any }>} */
  saveMappings: async (sorterId, { version, mappings }) => {},

  /** @returns {Promise<{ ok: boolean }>} */
  clearMappings: async (sorterId) => {},
};
```

**Rules:**
- The frontend NEVER uses `fetch()` directly — always goes through the adapter
- The mock data layer implements this same interface (for standalone mode)
- Host apps can provide their own adapter implementation via props
- The contract is the single source of truth for backend integration

### 4.6 UI Styling Architecture (Adaptive Theme Layer)

The UI must be **modern and sleek** but also **easily re-themeable** without touching component code.

**Approach: CSS Custom Properties (Design Tokens) + Theme Files**

```
src/client/src/styles/
├── tokens/
│   ├── theme-default.css    # Default dark modern theme
│   ├── theme-light.css      # Light variant
│   └── theme-corporate.css  # Example: branded/corporate template
├── base.css                 # Reset, typography, layout primitives
├── components/              # Component-specific styles (reference tokens only)
│   ├── sorter-view.css
│   ├── lane-slot.css
│   └── mapping-panel.css
└── index.css                # Imports base + active theme + components
```

**Rules:**
1. **No hardcoded colors, spacing, or font sizes in component CSS.** All visual values reference CSS custom properties (e.g., `var(--color-primary)`, `var(--spacing-md)`, `var(--radius-lg)`).
2. **Theme swap = swap one CSS file.** Changing the import in `index.css` from `theme-default.css` to `theme-corporate.css` reskins the entire app.
3. **Component styles are structural only.** They define layout (flex, grid, positioning) and reference tokens for all visual styling.
4. **Design tokens include:** colors (primary, secondary, surface, text, accent, success, warning, error), spacing scale, border radii, shadows, font family/sizes, transition durations.

**Example token file:**
```css
:root {
  /* Colors */
  --color-bg: #1a1a2e;
  --color-surface: #16213e;
  --color-primary: #4361ee;
  --color-text: #eeeeee;
  --color-text-muted: #aaaaaa;
  --color-border: #333333;
  --color-accent-1: #4361ee;
  --color-accent-2: #7209b7;
  --color-accent-3: #f72585;
  --color-accent-4: #4cc9f0;

  /* Spacing */
  --spacing-xs: 4px;
  --spacing-sm: 8px;
  --spacing-md: 16px;
  --spacing-lg: 24px;
  --spacing-xl: 32px;

  /* Typography */
  --font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
  --font-size-sm: 0.8rem;
  --font-size-base: 0.95rem;
  --font-size-lg: 1.2rem;
  --font-size-xl: 1.8rem;

  /* Borders & Shadows */
  --radius-sm: 4px;
  --radius-md: 8px;
  --radius-lg: 12px;
  --shadow-sm: 0 2px 8px rgba(0,0,0,0.2);
  --shadow-md: 0 4px 16px rgba(0,0,0,0.3);
  --shadow-lg: 0 8px 32px rgba(0,0,0,0.4);

  /* Transitions */
  --transition-fast: 0.1s ease;
  --transition-normal: 0.2s ease;
}
```

This ensures the entire visual identity can be swapped by replacing the theme token file — no component rewrites needed.

### 4.2 File Structure
```
src/
├── client/                  # React frontend
│   ├── src/
│   │   ├── components/      # SorterView, LaneSlot, MappingPanel, BulkAssign, etc.
│   │   ├── diagrams/        # Pluggable sorter diagram components
│   │   │   ├── index.js     # Registry: sorter ID → diagram component
│   │   │   ├── ShoeSorterDiagram.jsx  # Default conveyor + branching lanes
│   │   │   └── imported/    # Future: CAD SVG imports
│   │   ├── hooks/           # useMappings, useSorterConfig, useDragAndDrop
│   │   ├── services/        # API client (fetch wrapper)
│   │   ├── config/          # Object type definitions loaded from server
│   │   └── styles/          # CSS files (token-based theming)
│   ├── index.html
│   ├── vite.config.js
│   └── package.json
├── server/                  # Express backend
│   ├── index.js             # Server entry point
│   ├── routes/              # API route handlers
│   │   ├── sorters.js       # GET /api/sorters
│   │   ├── mappings.js      # GET/POST/DELETE /api/mappings
│   │   └── config.js        # GET /api/config (object types)
│   ├── adapters/            # Database adapter layer
│   │   ├── index.js         # Adapter interface/factory
│   │   └── sqlite.js        # SQLite implementation
│   ├── models/              # Data access layer
│   └── package.json
└── shared/                  # Shared types/constants
    └── types.js
```

### 4.3 Backend Adapter Pattern
```javascript
// adapters/index.js — interface
class DatabaseAdapter {
  async getSorters() {}
  async getMappings(sorterId) {}
  async saveMappings(sorterId, mappings) {}
  async deleteMappings(sorterId, mappings) {}
}

// adapters/sqlite.js — SQLite implementation
// Future: adapters/postgres.js, adapters/mysql.js, etc.
```

The active adapter is determined by config (environment variable or config file). Swapping backends requires only writing a new adapter file and changing the config.

### 4.4 API Endpoints
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | /api/sorters | List all sorters |
| GET | /api/config/criteria-types | List all criteria types and their available values (derived from config) |
| GET | /api/mappings/:sorterId | Get all mappings for a sorter |
| POST | /api/mappings/:sorterId | Save/overwrite mappings for a sorter (writes to DB + config file) |
| DELETE | /api/mappings/:sorterId | Clear all mappings for a sorter |

### 4.5 Config File Architecture (Backend)

**Single config file** contains all lane-to-criteria mappings for the entire system. This is the persistent source of truth.

**File location:** `src/server/config/lane-mappings.json` (or configurable via env var)

**Structure:**
```json
{
  "mappings": [
    { "sorterId": "sorter-a", "laneId": 1, "criteriaType": "ship-to", "criteriaValue": "ST1" },
    { "sorterId": "sorter-a", "laneId": 1, "criteriaType": "ship-to", "criteriaValue": "ST3" },
    { "sorterId": "sorter-a", "laneId": 4, "criteriaType": "ship-to", "criteriaValue": "ST2" },
    { "sorterId": "sorter-b", "laneId": 5, "criteriaType": "ship-to", "criteriaValue": "ST2" }
  ]
}
```

**Terminology change:** What was previously called "object type" / "object value" is now **criteria type** / **criteria value** to match domain language.

**Data flow:**
```
┌────────────────┐     startup      ┌──────────────┐     reads      ┌──────────┐
│ Config File    │ ────────────────▶ │  Database    │ ◀──────────── │ Frontend │
│ (JSON)         │                   │  (SQLite)    │               │          │
│ source of      │ ◀──── write ──── │  runtime     │ ◀── save ──── │          │
│ truth on disk  │    (on save)      │  cache       │   (on save)   │          │
└────────────────┘                   └──────────────┘               └──────────┘
```

**Lifecycle:**
1. **App startup:** Database table is created (or rebuilt) empty. Config file is read and all mappings are inserted into the DB.
2. **Page load / API read:** Frontend fetches from DB via API (fast).
3. **User saves:** API writes to DB AND updates the config file atomically. Both stay in sync.
4. **App restart:** DB is rebuilt from config file (no data loss).

**Deriving available criteria:**
The dropdown options ("Ship To", "Customer", etc.) and their possible values are inferred from the config file at startup:
- All unique `criteriaType` values → criteria type dropdown
- All unique `criteriaValue` per type → value dropdown options

**Why this pattern:**
- The database is ephemeral (dynamic table, rebuilt on startup) — great for fast reads and queries
- The config file is the durable store — survives restarts, can be version-controlled, manually edited, or synced
- No migration headaches — the config file IS the schema

### 4.5.1 Config File Update Rules
- Writes are atomic (write to temp file, then rename)
- File is formatted/pretty-printed JSON for readability
- Concurrent write protection: file lock or write-through-DB-only pattern

---

## 5. Out of Scope (v1)
- Sorter/lane configuration editing UI (separate project)
- Priority ordering of objects within a lane
- User authentication/authorization
- Real-time sync (WebSocket push) — using polling or manual refresh for now
- Lane names/descriptions (just numbers for now)
- Admin UI for creating new criteria types/values (users define custom criteria — future feature)

---

## 6. Future Considerations
- **Criteria admin screen** — users can create/edit criteria types and their possible values, saved back to config
- Additional criteria types (Customer, Route, etc.) — just add to config file
- Priority/ordering within lanes
- Real-time collaboration via WebSockets
- Sorter config admin screen
- Undo/redo for mapping changes
