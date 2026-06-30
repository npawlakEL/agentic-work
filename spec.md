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

**Future Vision:** Developers will import actual CAD SVGs from engineering software, register them in the diagram registry, and the system will overlay the interactive lane drop targets onto the imported drawing.

### 3.3 Behavior
1. **Page Load:** Auto-select the first sorter (or cached selection from last session via localStorage).
2. **Sorter Dropdown:** Switching sorter reloads the diagram and mappings for that sorter (loads the registered diagram component).
3. **Object Type Dropdown:** Selects the category (e.g., "Ship To"). Determines what values appear in the value dropdown and the drag panel.
4. **Value Dropdown:** Optionally filter to a single value or show "All values" in the drag panel.
5. **Sorter Diagram:** CAD wireframe renders the physical sorter layout. Mappings displayed directly on each lane branch. No hover or click required to see mappings.
6. **Mapping:**
   - Drag-and-drop from sidebar onto lane targets in the diagram
   - Also provide a bulk assignment UI (e.g., select multiple lanes, then assign an object to all of them)
7. **Unmapping:** Click a remove button (×) on a mapping chip to remove it.
8. **Save:** Saves the current sorter's mappings to the backend. Each sorter saved individually.
9. **Load:** Fetches current mappings from the backend on sorter selection.
10. **Color Coding:** Each object value gets a distinct color for visual differentiation.

### 3.3 Constraints
- Lanes can be left empty (no validation requiring all lanes mapped).
- No max objects per lane.
- Ship To's do not have to be assigned to any lane.
- Support concurrent users (optimistic updates or last-write-wins for v1).

---

## 4. Architecture

### 4.1 Tech Stack
- **Frontend:** React + Vite
- **Drag-and-Drop:** @dnd-kit/core
- **Styling:** CSS variables-based theming layer (see 4.6 below)
- **Backend:** Node.js + Express
- **Database:** SQLite (via adapter pattern — pluggable)
- **Concurrency:** Basic support (last-write-wins for v1, can upgrade to conflict resolution later)

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
| GET | /api/config/object-types | List all object types and their values |
| GET | /api/mappings/:sorterId | Get all mappings for a sorter |
| POST | /api/mappings/:sorterId | Save/overwrite mappings for a sorter |
| DELETE | /api/mappings/:sorterId | Clear all mappings for a sorter |

### 4.5 Config Files
Object types and sorter definitions are stored in JSON config files on the server, loaded at startup. This makes them easy to edit without code changes.

---

## 5. Out of Scope (v1)
- Sorter/lane configuration editing UI (separate project)
- Priority ordering of objects within a lane
- User authentication/authorization
- Real-time sync (WebSocket push) — using polling or manual refresh for now
- Lane names/descriptions (just numbers for now)

---

## 6. Future Considerations
- Additional object types (Customer, Route, etc.) — config-driven, just add to config file
- Priority/ordering within lanes
- Real-time collaboration via WebSockets
- Sorter config admin screen
- Undo/redo for mapping changes
