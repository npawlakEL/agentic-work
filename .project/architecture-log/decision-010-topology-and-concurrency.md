# Decision 010 — Deployment topology & concurrency model

**Status:** Accepted
**Date:** 2026-08-12
**Owner ruling:** explicit — "ideally a single process with multiple panda lines. however …
multiple PLCs can be connected to this single process. so a project can have 2 PLCs, each
with 2 lines, and all of this lives in this single process."

## Context

The SQL source coordinated concurrent access through the database (`sp_getapplock`-style
locks, DB counters) because every caller shared one DB. The port needs an explicit
concurrency model to size the LOCK (F21 counter / assign-array, general critical sections)
and settings/counter storage.

## Decision

1. **PandA runs as a SINGLE process per site.** That one process hosts **multiple PandA
   lines** and **multiple PLC connections** (e.g. 2 PLCs × 2 lines = 4 lines in one process).
2. **Concurrency is in-process, not cross-process.** Multiple PLC connections drive events
   concurrently on their own threads into the shared host. Therefore:
   - **Locks are in-memory and thread-safe** (`SemaphoreSlim` / `lock` / `Channel`), NOT
     `sp_getapplock`. No DB-level distributed lock is required.
   - **Counters (F21 PLC assign-slot index) are in-memory**, thread-safe (`Interlocked` /
     guarded), **scoped per line** (each line/PLC owns its own assign array and index).
3. **Locks must be keyed to preserve line independence.** Critical sections are scoped as
   narrowly as correctness allows — **per-line** (and per-printer where relevant) — so work
   on one line/PLC never blocks another. A global lock is used only for genuinely global
   shared state.
4. **Keep the lock/counter behind an interface** (`IPandaLock` / counter provider) so a
   DB-backed implementation could be introduced later if the topology ever changes, without
   touching Core logic. Default/only shipped impl today is the in-memory one.

## Resolves / informs open questions
- **E2 (LOCK):** in-memory locks suffice (single process). ✅
- **E1 (F21):** the assign counter is **per-line** (not shared across lines). Array size
  defaults to **300** but is **configurable per line** (`LineConfig`), since it is a
  PLC/site parameter. ✅
- **E3 (LOCK):** the decision-007 "hard error on lock timeout" stands; with short in-process
  critical sections a 60s wait that times out indicates a real deadlock/bug and should fail
  fast. ✅

## Port shape
- `PandA.Core`: `IPandaLock` (async acquire with timeout → hard error), `ISlotCounter`
  (per-line, thread-safe, wraps at configured size). `PandA.Sim`: in-memory impls used by
  tests. No econtroller dependency.

## References
- `.project/spec/clusters/platform-settings.md` §LOCK, §F21.
- decision-007 (E3 lock-timeout = hard error).
