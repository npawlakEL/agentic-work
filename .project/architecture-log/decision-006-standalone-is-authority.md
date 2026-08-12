# Decision 006 — This repo's standalone port + spec is the sole authority

**Status:** Accepted
**Date:** 2026-08-12
**Owner ruling:** domain owner (explicit)

## Context

During the full-port spec lockdown we discovered an existing org library,
`Element-Logic/econtroller-behaviors-panda` (`eController.Behaviors.PandA`). It is a
real, shipping MFC plug-in that:

- registers via `builder.AddPandA()` and reads a `PandAConfig.json`
  (`UseLoadBalancing`, `Lines[]` → `PandAId`, `VerifyThreshold`,
  `Printers[]` → `IPAddress`/`PortNumber`/`LabelMap`);
- implements a `RouteToPrinter` `IMfcAction`: looks up `LabelData` by
  `BlindLabel == TuId`, filters online printers via `PandAStatus`, load-balances
  through `LastAccessedPrinter`, matches label type → printer (`LabelMap`), writes a
  `PandARoutingExtension` onto the `MfcTransportOrder`, and sets routing error codes;
- persists entities (`LabelData`, `PandALine`, `PandAPrinter`, `PandAStatus`) via MFC
  `ITableProvider`, and logs through `ILogger<T>` (consistent with decision-005);
- covers **only** the routing/printer-selection slice — no verify/reprint lifecycle,
  fire-point profiles, lane-eval/spare, advice ingestion, exception labels, ZPL
  sanitizer, events/recovery, settings, waves, or purge.

## Decision

1. **This repository's standalone port (`PandA.Core` / `PandA.Sim`) and the
   `.project/spec/` documents are the SOLE authority** for PandA behavior and scope.
2. `econtroller-behaviors-panda` is treated as a **non-authoritative integration
   reference only** — it may be less fleshed out and possibly inaccurate. Consult it
   for *how* to wire into econtroller/MFC (builder extension, `IMfcAction`,
   `ITableProvider`, `MfcTransportOrder`, config binding) when we build the deferred
   `PandA.EController` adapter. Do **not** import its behavior as ground truth.
3. Where the two disagree on behavior, **our spec wins**. If the existing library
   reveals a genuinely better/necessary integration constraint, capture it as a new
   decision-log entry rather than silently conforming.

## Consequences

- The from-scratch port continues unchanged; no pivot, no retire.
- The eventual `PandA.EController` adapter will map our `PandA.Core` contracts onto MFC
  primitives, using `behaviors-panda` as a wiring cookbook (config shape, action
  registration, table provider usage) — not as a spec.
- Overlap in the routing slice (their `RouteToPrinter` vs our printer-selection +
  load-balancing) is expected; reconcile at adapter time, keeping our semantics.

## References

- `Element-Logic/econtroller-behaviors-panda` — `README.md`,
  `src/eController.Behaviors.PandA/Actions/PandAActions.cs`, `Config/PandASnapshot.cs`,
  `Entities/{LabelData,PandALine,PandAPrinter,PandAStatus}.cs`.
- decision-005 (ILogger) — corroborated by the existing library's logging style.
