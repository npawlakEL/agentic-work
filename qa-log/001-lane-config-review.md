# QA Log — Lane Configuration Screen

**Date:** 2026-06-30
**Feature Branch:** `npawlakel-feature-lane-config-screen-np`
**Reviewer Rounds:** 3 (sign-off on round 3)

---

## Issue #1 — Stale mappings after sorter switch

- **Found by:** Reviewer
- **Iteration:** Round 1
- **Description:** Switching sorters kept old mappings/version in state until async load finished. Race condition: Save could POST the previous sorter's mappings to the new sorter. Out-of-order responses could overwrite state.
- **Severity:** Critical
- **Failing Test Written:** Test verifying mappings clear on switch, Save disabled while loading, stale responses ignored
- **Fix Applied:** Added `latestMappingsRequestId` ref to ignore stale responses, clear mappings on switch, disable Save button during load
- **Verified:** Yes — Round 2

---

## Issue #2 — Non-409 save failures corrupt version state

- **Found by:** Reviewer
- **Iteration:** Round 1
- **Description:** Any failed save (e.g., 500) still ran `setVersion(response.data.version)`, setting `undefined` and causing false conflicts later.
- **Severity:** High
- **Failing Test Written:** Test that a 500 response does NOT update version state
- **Fix Applied:** Gated success handling on `response.ok`; handle non-409 errors with alert
- **Verified:** Yes — Round 2

---

## Issue #3 — Backend accepts invalid mappings

- **Found by:** Reviewer
- **Iteration:** Round 1
- **Description:** No validation of `laneId`, `objectType`, or `objectValue` against config. API accepted bogus data (e.g., `{ laneId: 999, objectType: 'bogus' }`).
- **Severity:** High
- **Failing Test Written:** Backend tests POSTing invalid lane, object type, and value expecting 400 responses
- **Fix Applied:** Added validation in route handler against sorter config (valid lanes) and object-type config (valid types/values); returns 400 with descriptive message
- **Verified:** Yes — Round 2

---

## Issue #4 — Adapter abstraction broken by SQLite-specific imports

- **Found by:** Reviewer
- **Iteration:** Round 1
- **Description:** `src/server/routes/mappings.js` imported `SorterNotFoundError`/`VersionConflictError` directly from `sqlite.js`, coupling routes to a specific adapter.
- **Severity:** Medium
- **Failing Test Written:** Code-level verification (routes no longer import from sqlite.js)
- **Fix Applied:** Moved shared error classes to `src/server/adapters/errors.js`; both adapter and routes import from there
- **Verified:** Yes — Round 2

---

## Issue #5 — Hardcoded color in component CSS

- **Found by:** Reviewer
- **Iteration:** Round 1
- **Description:** `src/client/src/styles/components/app.css:72` had `background: rgba(15, 23, 42, 0.5)` bypassing theme tokens. Spec section 4.6 requires all visual values use CSS custom properties.
- **Severity:** Low
- **Failing Test Written:** N/A (CSS inspection)
- **Fix Applied:** Added `--color-backdrop` token in `theme-default.css`, referenced via `var(--color-backdrop)` in app.css
- **Verified:** Yes — Round 2

---

## Issue #6 — Missing frontend test coverage

- **Found by:** Reviewer
- **Iteration:** Round 1
- **Description:** Tests missing for: successful Save POST updates version, sorter-switch reload, Escape exits paint mode, drag-and-drop integration, diagram registry selection, color-coded chip rendering.
- **Severity:** Medium
- **Failing Test Written:** Added behavior-level tests for each missing scenario
- **Fix Applied:** 15 new frontend tests covering all required behaviors
- **Verified:** Yes — Round 2

---

## Issue #7 — Hardcoded size/border literals in component CSS

- **Found by:** Reviewer
- **Iteration:** Round 2
- **Description:** Component CSS still contained hardcoded `12rem`, `16rem`, `7rem`, `4rem`, `1px`, `2px` instead of CSS variable tokens.
- **Severity:** Low
- **Failing Test Written:** N/A (CSS inspection)
- **Fix Applied:** Added size/border tokens to `theme-default.css`; replaced all hardcoded values with `var()` references. Structural values (`100vh`, `1fr`, `border: 0`) kept as-is.
- **Verified:** Yes — Round 3

---

## Summary

| Round | Issues Found | Issues Fixed | Tests Added |
|-------|-------------|-------------|-------------|
| 1     | 6           | 6           | 15          |
| 2     | 1           | 1           | 0           |
| 3     | 0 (sign-off)| —           | —           |

**Total tests at end:** 34 (server: 11, client: 23)
**Final verdict:** REVIEWER SIGN-OFF
