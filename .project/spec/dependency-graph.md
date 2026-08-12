# Dependency Graph & Build Order

The port has a **foundation-first** shape: a handful of cross-cutting pieces edit the shared
"hot" files (see ownership-map.md) and unblock everything else. Those run **sequentially**.
Once they land, the remaining leaf features fan out into **parallel waves** by disjoint file
ownership.

## The two cross-cutting foundations

```
SETTINGS-1 (ISettingsProvider + KnownSettings)
    unblocks → SETTINGS-2 (reprint gate), F17 purge, F10 exception gate,
               F11 FilterLabels gate, F12 suffix gate, F20 MinGap, DYNAP DynamicPrintPoint

F16 (IPandaEventSink + PandaEvent + PandaEventLevel)
    retrofitted into → InductService, VerifyStationService, CartonAdviceService, LaneEvalService
    required by → F15, F20, F22, F-LOG1, F17 (all emit events)
```

Both are near-leaf in their own code (few dependencies) but wide in blast radius, so they go first.

## Shared enums (contracts — define once, before consumers)

```
CartonStatus      → F20 defines; consumed by F10, F-LOG1, F08, INBOUND
VerifyReasonCode  → F22 defines; consumed by VerifyStationService, GUI (later)
```

## Full dependency edges

```
SETTINGS-1 ──┬─→ SETTINGS-2 ──→ F-ADV1 (re-advice gate, backlog)
             ├─→ F17 (purge)
             ├─→ F10 (exception label gate)
             ├─→ F11 (filter gate)
             ├─→ F12 (suffix gate)
             └─→ INBOUND

INBOUND ─────┬─→ F18/F24 (xref+oLPN) ──→ (F19 profile validation)
             ├─→ F23 (wave auto-complete) ──→ F14 (MandA verify triggers wave)
             ├─→ PROFSW (per-carton ProfileName) ──→ DYNAP (needs resolved ApplyPoint)
             └─→ F08 (needs VerifyPassDest/FailDest on TransportOrder)

F16 ─────────┬─→ F15 (PLC recovery) ── also needs F-LOG1
             ├─→ F22 (reject audit)
             └─→ F-LOG1 (run history) ── also needs F20

F20 ─────────┬─→ F10 (ExceptionType input)
             └─→ F-LOG1 (StatusAtInduct)

LOCK ────────┬─→ F23 (auto-complete race guard)
             └─→ F13 (concurrent status serialization, later)

LINECTRL ────→ F13 (status → lane-eval → egress)

F21 (slot index)  — standalone; feeds F08 fire-point PLC bundle
F11 (sanitizer)   — standalone pure function
F12 (suffix)      — needs SETTINGS only; pairs operationally with F13c
```

## Recommended build waves

**Wave 0 — Foundations (sequential, single Coder + Senior; edits 🔴 hot files):**
1. **SETTINGS-1** — `ISettingsProvider`, `KnownSettings`, `InMemorySettingsProvider`; inject into `InductService` + `CartonAdviceService` (no behavior change yet).
2. **F16** — `IPandaEventSink` + event types; inject `NullPandaEventSink` default into all four services.
3. **INBOUND fields** — add `WaveId`, `ProfileName`, `Bypass`, `VerifyEnabled`, `VerifyPassDest`, `VerifyFailDest` to `TransportOrder`; add `AdviceMessage` + `WaveIdParser`; extend `CartonAdviceService`.
4. **LineConfig omnibus** — add all new config fields (`Lanes`, `PrinterStatusSuffix`, PLC fields, `EncoderResolution`, `DynamicPrintPoint`, `ProfileRegistry`/`DefaultProfile`, `FilterLabels`) in one pass, defaulted for backward-compat.

**Wave 1 — Standalone leaves (parallel; 🟢 new files only):**
- **F11** ZPL sanitizer · **F20** quality classifier + `CartonStatus` · **F21** slot index · **LOCK** lock provider · **F22** reject audit · **LINECTRL** egress port

**Wave 2 — Depend on Wave 0/1 (parallel; disjoint files):**
- **F-LOG1** (F20+F16) · **F15** (F16+F-LOG1 → serialize after F-LOG1) · **F10** (F20+F11+SETTINGS) · **F18/F24** (INBOUND) · **F08** (INBOUND) · **F12** (SETTINGS) · **PROFSW** (INBOUND)

**Wave 3 — Depend on Wave 2 (parallel where disjoint):**
- **F13** (LINECTRL) · **DYNAP** (PROFSW) · **F23** (INBOUND+LOCK) · **F17** (SETTINGS+F16)

**Wave 4 — Top of stack:**
- **F14** MandA (needs F23) · **F19** profile validation (folds into PROFSW)

## Parallelism caveat
Within a wave, two features that both need to touch the same 🔴 file (e.g. both wiring a new
call into `InductService.InductAsync`) must still serialize that specific edit. Foundation waves
should pre-carve clearly-labelled insertion points in the hot files to minimize this.
