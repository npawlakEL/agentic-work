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
┌─────────────────────────────────────────────────────────┐
│  Lane Configuration                        [Load] [Save]│
├─────────────────────────────────────────────────────────┤
│  Sorter: [▼ Sorter A (12 lanes)]                        │
│  Object Type: [▼ Ship To]   Value: [▼ All / S1..S4]    │
├──────────┬──────────────────────────────────────────────┤
│ Drag to  │                                              │
│ assign:  │   ┌───┐ ┌───┐ ┌───┐ ┌───┐ ┌───┐ ...       │
│          │   │ 1 │ │ 2 │ │ 3 │ │ 4 │ │ 5 │            │
│  [S1]    │   │S1 │ │   │ │S2 │ │S1 │ │   │            │
│  [S2]    │   │S3 │ │   │ │   │ │   │ │   │            │
│  [S3]    │   └───┘ └───┘ └───┘ └───┘ └───┘            │
│  [S4]    │                                              │
│          │   Sorter Schematic Diagram                   │
└──────────┴──────────────────────────────────────────────┘
```

### 3.2 Behavior
1. **Page Load:** Auto-select the first sorter (or cached selection from last session via localStorage).
2. **Sorter Dropdown:** Switching sorter reloads the lane graphic and mappings for that sorter.
3. **Object Type Dropdown:** Selects the category (e.g., "Ship To"). Determines what values appear in the value dropdown and the drag panel.
4. **Value Dropdown:** Optionally filter to a single value or show "All values" in the drag panel.
5. **Sorter Graphic:** Schematic/CAD-style diagram showing all lanes as numbered slots in a horizontal row. Mappings displayed directly on each lane (colored chips/labels). No hover or click required to see mappings.
6. **Mapping:**
   - Drag-and-drop from sidebar onto lane slots
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
- **Styling:** CSS (clean, dark theme — polished look)
- **Backend:** Node.js + Express
- **Database:** SQLite (via adapter pattern — pluggable)
- **Concurrency:** Basic support (last-write-wins for v1, can upgrade to conflict resolution later)

### 4.2 File Structure
```
src/
├── client/                  # React frontend
│   ├── src/
│   │   ├── components/      # SorterView, LaneSlot, MappingPanel, BulkAssign, etc.
│   │   ├── hooks/           # useMappings, useSorterConfig, useDragAndDrop
│   │   ├── services/        # API client (fetch wrapper)
│   │   ├── config/          # Object type definitions loaded from server
│   │   └── styles/          # CSS files
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
