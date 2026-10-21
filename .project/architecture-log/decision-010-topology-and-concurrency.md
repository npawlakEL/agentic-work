# Decision 010 — Deployment topology & concurrency model (multi-host, shared eController DB)

**Status:** Accepted — **supersedes** the 2026-08-12 single-process ruling (see §History).
**Date:** 2026-10-21
**Owner ruling (2026-10-21):** PandA runs as **multiple hosts/processes, serving multiple lanes, all
sharing ONE database — the eController platform database** (`MfcDbContext`; runtime data through the
`PersistantTable` layer). The legacy PandA SQL Server database is **not** used. eController has **no
built-in distributed lock**. The host→line mapping is **undecided**, so the design must be correct for
**any host processing any line at any time**; we optimize later.

## Context

The SQL source coordinated every caller through one database:
- `sp_getapplock` session locks (`sdisp_PA_Lock`);
- the `CartonAssignSeq` SEQUENCE (1..300 CYCLE);
- identity `CartonListID`s;
- row-level status tables.

The first C# port assumed one process (2026-08-12 ruling). It used in-process `SemaphoreSlim` locks,
per-process counters and live in-memory `PrinterState` objects, and saved `TransportOrder` by last-wins
upsert. With several hosts on one database that model:
- double-prints a carton inducted on two lanes;
- loses updates;
- splits printer/spare state between hosts;
- mints colliding run, exception and slot ids.

The audit hazards were C1–C4, H1–H3 and M1–M3.

Constraints:
- The only shared primitive is the database through `PersistantTable`, so the design uses portable
  **conditional updates** (`Update().Set(...).Where(...)`, success = *affected rows == 1*).
- No SQL-Server-only features (`sp_getapplock`, `SEQUENCE`, `SqlClient`).
- No econtroller package reference in this repo.

## Decision

1. **Every shared fact lives in the shared database behind a Core port. No Core correctness relies on
   per-process state.**
   - Ports document their multi-host semantics in XML docs.
   - PandA.Sim provides in-memory adapters that enforce the same semantics.
   - One `SimSharedDatabase` instance plays the shared database. Every simulated host/line composed over
     it shares the same tables. DemoHost registers one singleton for all line simulations.
2. **Mutual exclusion = leases** (`ILockProvider` → `ILease`).
   - `AcquireAsync(resource, timeout, ct)` returns a lease with a unique `OwnerId` and an `ExpiresUtc`.
     Defaults are a 60 s lease and a 60 s acquire timeout (`LockDefaults`).
   - Leases are **non-reentrant**, and `RenewAsync` extends them. `DisposeAsync` releases the lease only
     if this owner still holds it.
   - A crashed holder's lease **expires**, so another host can proceed.
   - Before an irreversible action, a holder **fences**: it renews every lease it holds and aborts if any
     renew fails.
   - `ILockProvider` is a **required** constructor dependency everywhere. Every silent in-process fallback
     is removed: the `InductService` fallback to `InProcessLockProvider.Shared`, the static `LineMonitors`
     in `LaneEvalService`, and the `InProcessLockProvider`, `CyclicSlotIndexProvider` and Sim
     `InMemoryLockProvider` types themselves.
   - PandA.Sim ships `InMemoryLeaseLockProvider`: a shared lease table, `ForHost(id)` views, expiry driven
     by `TimeProvider`.
3. **Canonical lease names** (`LockNames`):
   - `Carton_{TUID}` is taken by **every** `TransportOrder` writer: induct, verify, PLC tracking recovery,
     advice, reprint authorization and exception-carton creation.
   - `PrinterState_{LINEID}` is taken by every lane evaluation / printer-status ingestion that decides
     spare/promote/demote.
4. **Optimistic concurrency on `TransportOrder`** (`ITransportOrderStore`).
   - `TransportOrder.Version` is a long; 0 means never persisted.
   - `InsertAsync` requires version 0 and enforces **one active order per TuId**
     (`DuplicateTransportOrderException`).
   - `UpdateAsync` is a version CAS: `WHERE Id = @id AND Version = @expected`, `SET Version =
     @expected + 1`. If it affects 0 rows it raises `ConcurrencyConflictException`.
   - Reads return **detached copies**.
   - `SaveWithRetryAsync(order, mutation)` reloads, re-applies the *intent* to the fresh row and retries
     **once**. Induct replays its print run (labels printed, print count) onto the latest row and never
     re-dispatches.
   - The lease makes conflicts exceptional. The version check catches a lease-bypassing writer or an
     expired lease, so a whole-row last-wins overwrite can never happen.
5. **Printer runtime state is shared** (`IPrinterStateStore`).
   - It holds online/engine status, spare and `LastPrinted`.
   - Reads are fresh detached snapshots per operation.
   - Mutations are single atomic row updates: `SetStatus`, `SetSpare` and `StampLastPrinted`.
   - `ILineProvider` returns configuration only and never live mutable state.
   - `PrinterSelectionService` and `LaneEvalService` work on snapshots and write back through the port.
   - Verify-fail streaks (per printer) live in `IVerifyFailCounterStore`, with atomic
     increment-with-reset and reset. It backs `IVerifyThresholdTracker`, which is now async.
6. **Ids are store-generated and unique across hosts.**
   - Run ids (`CartonListID`) come from `ICartonRunRepository` (`CreateRunAsync` / `ReserveRunIdAsync`).
   - Exception carton ids **derive from a reserved run id** (source `<CartonID> = CartonListID`). The
     per-process clock-seeded counter in `ExceptionLabelPolicy` is removed.
   - Slots come from `ISlotIndexProvider`, a shared atomic 1..300 cycling counter **per controller (PLC)**
     (owner ruling 2026-10-21, R2). `NextSlotAsync(controllerKey)` takes the key from
     `SlotControllerKey.For(line)`: `PLC:{LineConfig.PlcNumber}`, or `LINE:{LineId}` when the line has no PLC
     number. The model has no controller entity, so the PLC number *is* the controller identity: lines that
     share a PLC number share a counter, and different PLCs count independently.
7. **Configuration is read-through.**
   - `ISettingsProvider` and `ILineProvider` reflect the shared database on every call.
   - An adapter may cache only with a short bounded TTL or a version check, never unbounded.
   - `IMinGapProvider` is a synchronous static override for tests and Sim. Production leaves it unset, so
     MinGap comes from the read-through `MinGap` setting.

### Lease timeout behavior per call site

The source logs a `sp_getapplock` timeout and **continues unlocked**. That is unsafe across hosts, so
each call site picks a fail-safe:

| Call site | Lease | On timeout | Why |
|---|---|---|---|
| Induct (`InductService`) | `Carton_{tu}` (+ the resolved TuId on an xref hit) | `InductStatus.LockTimeout`; **nothing printed**; the PLC slot is still assigned | Never risk a double print; the carton routes on without a label. |
| Exception carton creation | `Carton_{exceptionId}` | Propagates; the id is freshly reserved and unique, so contention cannot happen in practice | — |
| Lane eval / printer status (`LaneEvalService`) | `PrinterState_{line}` | Log **Warning**, return `LaneEvalResult.SkippedResult()`; no state written (incl. the status signal) | The next status event re-evaluates. |
| PLC tracking recovery (`PlcEventHandlerService`) | `Carton_{tu}` | Log Warning, skip the recovery | Best-effort recovery; the carton is being processed by the lease owner. |
| Verify (`VerifyStationService`) | `Carton_{tu}` | `LockTimeoutException` propagates to the station adapter | The adapter answers the PLC with its no-decision/reject default. |
| Advice (`CartonAdviceService`) | `Carton_{tu}` | `LockTimeoutException` propagates | The message transport redelivers or alarms. |
| Reprint authorization (`ReprintAuthorizationService`) | `Carton_{tu}` | Returns `false`; logged | The operator retries. |

Induct also **fences**: immediately before dispatch it calls `RenewAsync` on every held lease. If a
lease was lost (expired, maybe taken by another host), nothing is printed.

### Known, accepted gaps (optimize later)

- `LastPrinted` stamps and verify-fail increments are lock-free atomic single-row operations, not
  lease-guarded. Load-balancing rotation may jitter across hosts; correctness does not depend on it.
- There is no pre-print "claim" write. The carton lease plus fencing gives exactly-once printing
  between live hosts. A host that crashes *after* dispatch but *before* persisting leaves `PrintCount`
  unchanged. The carton may then print again on its next induct, which is the source's behavior after a
  crash too.
- Non-printed induct outcomes do not persist the induct-scan stamps. This preserves the existing write
  counts.
- The slot counter is keyed by the line's PLC number (see 6); there is no first-class controller entity yet.
- The DemoHost's reprint-authorization page still runs over demo data, not over
  `ReprintAuthorizationService`. The dashboard's "last printed" column still reads DemoDataStore
  runtime.
- The DemoHost message console uses a fresh `SimSharedDatabase` per run, so traces stay isolated.

## eController PersistantTable adapter guide

Implement each port in an eController adapter project; it is not part of this repo.
- Everything uses `ITableRepository<T>` builders.
- The pattern **conditional update ⇒ check affected rows == 1** is the CAS primitive.
- Use UTC timestamps from one clock source: the DB server time if PersistantTable exposes it, otherwise
  each host's NTP-synced UTC with the lease TTL sized well above the possible clock skew.

### 1. `ILockProvider` → table `PandaLease`

Columns: `Resource` (string, **unique key**), `Owner` (string), `ExpiresUtc` (datetime).

```
acquire(resource, timeout):
  owner = "{hostId}:{guid}"                         -- new per acquisition → non-reentrant
  loop until timeout:
    now = utcNow; exp = now + leaseDuration
    rows = Update<PandaLease>().Set(Owner = owner, ExpiresUtc = exp)
             .Where(Resource == resource && ExpiresUtc < now).ExecuteAsync()    -- free or expired → take it
    if rows == 1: return lease(owner, exp)
    if Query().Where(Resource == resource) is empty:
      try Create(new PandaLease { Resource, Owner = owner, ExpiresUtc = exp }) -- first use of this name
          return lease(owner, exp)
      catch unique-key violation: (another host created it) fall through
    delay(poll 25–100 ms, jittered)
  throw LockTimeoutException(resource, timeout)

renew:   rows = Update.Set(ExpiresUtc = now + leaseDuration)
                  .Where(Resource == r && Owner == me && ExpiresUtc >= now)     -- false ⇒ lease lost: stop
release: Update.Set(ExpiresUtc = DateTime.MinValue).Where(Resource == r && Owner == me)
```

- Release and renew are conditional on `Owner == me`, so a revived stale holder can never extend or free
  its successor's lease.
- Release by expiring the row rather than deleting it; this keeps the row for the next acquire and avoids
  create races.
- `Owner == me` is deliberately **not** in the acquire predicate: a second acquire by the same logical
  caller must wait (non-reentrant, matching the Sim).

### 2. `ITransportOrderStore` → `TransportOrder` table (+ `Version`, `ActiveKey`)

- **`ActiveKey`** holds `TuId` while the order is active and `"~closed~{Id}"` once closed, with a
  **unique index on `ActiveKey`**. This enforces "one active order per TuId" portably, without
  backend-specific filtered or NULL-unique indexes.
- **`InsertAsync`**: require `Version == 0`, then `Create(row with Version = 1)`. On a unique-key
  violation, throw `DuplicateTransportOrderException`. Stamp `Version = 1` on the caller's instance.
- **`UpdateAsync`**:
  ```
  rows = Update<TransportOrder>().Set(...every mutable column..., Version = expected + 1)
           .Where(Id == id && Version == expected).ExecuteAsync()
  if rows != 1: throw ConcurrencyConflictException(tuId, expected, actualVersionReadBack)
  order.StampVersion(expected + 1)
  ```
- **`FindActiveByTuIdAsync`**: `Query().Where(ActiveKey == tuId)`, mapped to a new detached domain
  object with `StampVersion(row.Version)`. Never cache.
- **Child data** (label set, per-label print states, scan measurements) goes in child rows or a JSON
  column, written in the same transaction as the parent's conditional update. The parent's version CAS
  guards all of it.

### 3. `IPrinterStateStore` → table `PandaPrinterState`

Columns: `PrinterId` (key), `PlcOnline`, `EngineOnline`, `IsSpare`, `LastStatusUpdate`, `LastPrinted`.

- Every mutation is one single-row `Update().Set(...).Where(PrinterId == id)`.
- `SetStatusAsync` creates the row when absent: create, and on a unique-key violation update instead.
- Decisions that need consistency (spare/promote/demote) run under the `PrinterState_{line}` lease, so
  these single-column writes need no CAS.
- `GetSnapshotAsync` is a plain read on every call.

### 4. `IVerifyFailCounterStore` → table `PandaCounter` (`Key`, `Value`, `Version`)

`IncrementAsync(key, resetAtOrAbove)` is a CAS loop:

```
loop:
  row = Query().Where(Key == key).FirstOrDefault()
  if row is null: try Create(Key, Value 0, Version 0) (unique violation → ignore); continue
  next   = row.Value + 1
  stored = (resetAtOrAbove > 0 && next >= resetAtOrAbove) ? 0 : next
  rows   = Update.Set(Value = stored, Version = row.Version + 1).Where(Key == key && Version == row.Version)
  if rows == 1: return next
```

`ResetAsync` is an unconditional `Set(Value = 0, Version = Version + 1)`.

### 5. `ISlotIndexProvider` → `PandaCounter` row `Key = "CartonAssignSeq:{controllerKey}"`

- Use the same CAS loop as §4, with `following = current >= 300 ? 1 : current + 1`. Store `following`
  and return `current`, the value claimed.
- This reproduces the source SEQUENCE 1..300 CYCLE across all hosts.
- One row per controller key (`PLC:{n}` / `LINE:{id}`, owner ruling 2026-10-21 R2; open-questions E1). Rows
  are created lazily on first draw; each key is an independent CAS counter.

### 6. `ICartonRunRepository` → `CartonRun` table

- **Store-generated** key. EF Core identity/autoincrement works on both SQL Server and SQLite.
  `CreateRunAsync` with `RunId == 0` does a `Create` and reads back the generated id.
- **`ReserveRunIdAsync`** is used for exception carton ids, which must be known before the run row is
  written. Either:
  - (a) insert a placeholder run row and return its generated id, after which `CreateRunAsync(RunId > 0)`
    updates that row; or
  - (b) draw **all** run ids from a `PandaCounter` row `Key = "CartonRunId"` with the §4 CAS loop. Seed
    it above any existing id so the two sources never collide.
- Run ids go to the PLC and come back in PLC events, so they must be globally unique — never minted
  per process.

### 7. Settings / line configuration

- Read `Settings` and line/printer configuration through PersistantTable or EffortlessConfiguration on
  each call.
- If profiling shows the read cost matters, cache with a **short TTL (≤ 5 s)**, or key the cache on a
  config-version row that every config write bumps.

## History

- **2026-08-12 (superseded):** "single process with multiple lines/PLCs". It used in-memory
  `SemaphoreSlim` locks, per-line in-memory counters and "hard error on lock timeout" (E3). This decision
  replaces it. E3 is refined by the per-call-site timeout table above.

## References

- Specs:
  - `.project/spec/clusters/platform-settings.md` §LOCK-1, §F21
  - `.project/spec/open-questions.md` E1–E3
- Architecture log:
  - `.project/architecture-log/011-source-coverage-gap-analysis.md` (F13)
  - `.project/architecture-log/012-lane-eval-spare-state.md` (concurrency)
- Code:
  - `PandA.Core/Concurrency/ILockProvider.cs`, `ConcurrencyExceptions.cs`
  - `PandA.Core/ITransportOrderStore.cs`, `IPrinterStateStore.cs`, `ICartonRunRepository.cs`
  - `PandA.Core/Slots/ISlotIndexProvider.cs`
  - `PandA.Core/Verification/IVerifyFailCounterStore.cs`
  - `PandA.Sim/SimSharedDatabase.cs`
- Tests:
  - `tests/PandA.Tests/MultiHostTests.cs` (two hosts over one `SimSharedDatabase`)
  - `LockProviderTests.cs`
