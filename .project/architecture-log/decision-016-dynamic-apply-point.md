# Decision 016 — DynamicApplyPoint: port into Core as a carton-aware apply-point resolver

**Status:** Accepted
**Date:** 2026-08-12
**Basis:** Domain-owner grill (2026-08-12) over `sdisp_TOOL_CUSTOM_DynamicApplyPoint.sql`.
Resolves open questions C2, C3, C4, C5 and the incomplete top-orientation branch.

## What DynamicApplyPoint is

It converts a **human-readable apply point** (inch + edge, e.g. `1T`, `.4M`, `5.25L` — the
`ApplyFirePoint` from decision-011) into the **raw step-pulse integer** the PLC fires the TAMP
head at, **computed per carton at induct time** because it depends on carton dimensions. The
**print** fire point stays a static int (decision-011); **only the apply point is dynamic**
(resolves C2 — dynamic applies to apply, not print).

## Inputs

- **CartonLength / CartonHeight** — from the **PLC inbound scan message** (mapped to
  `PandaCartonList.CartonLength/CartonHeight`). CartonLength is in **step pulses**.
- **Apply point** — inch offset + edge letter (`L`=leading, `T`=trailing, `M`=middle), from the
  active fire-point profile (decision-011).
- **Per-printer config** (see below).

## Units (resolves C4)

- **EncoderResolution = 0.25 inches per pulse** (per-line/printer configurable; default 0.25).
  `pulsesPerInch = 1 / EncoderResolution` (= 4 at default).
- The source **divides inches by EncoderResolution — which is CORRECT** given in/pulse units
  (the earlier "inversion bug" hypothesis was wrong). The real trap is not mistaking
  CartonLength (already pulses) for inches.
- Work in **pulses** (PLC-native). Convert an inch value to pulses by `/ EncoderResolution`.

## Side apply — the carton-length cases (resolves C3)

`labelWidth` = **per-printer config, default 4 inches** (the source's hardcoded `4` was a bug).

```
Leading  (xL): fire = xInches / EncoderResolution                                  + DefaultApplyDistance
Trailing (xT): fire = CartonLengthPulses − (xInches + labelWidth)/EncoderResolution + DefaultApplyDistance
Middle   (xM): fire = CartonLengthPulses/2 − (labelWidth/2 ∓ xInches)/EncoderResolution + DefaultApplyDistance
```

Leading is carton-length-independent (leading edge is the tracking reference → simple/robust).
Trailing & Middle **require** carton length — these are the fragile cases the source got wrong.
`DefaultApplyDistance` = per-printer base offset (from source `PrinterDetails`).

## Top apply — kinematic correction (resolves C5, replaces the magic `0.113`)

A top TAMP head is mounted at fixed height `H_head` above the belt. On fire it extends **down**
`(H_head − H_carton)` at tamp speed `s_tamp`, taking `t = (H_head − H_carton)/s_tamp`. During
`t` the carton travels `v·t` at belt speed `v`, so we must **fire early** by that travel:

```
leadCorrection_inches = k · (H_head − H_carton),   where k = v / s_tamp  (beltSpeed / tampSpeed)
firePoint_top = firePoint_side(x, edge, cartonLen) − [ k · (H_head − H_carton) ] / EncoderResolution
```

Sign check: taller carton → smaller `(H_head − H_carton)` → smaller correction → fires later;
shorter carton → larger correction → fires earlier. Matches physical behaviour.

**Per-printer config for top-apply printers only** (derive everything from these three):
- `tampMountHeight` (H_head, inches)
- `beltSpeed` (v)
- `tampSpeed` (s_tamp)

`k` is derived (`beltSpeed / tampSpeed`); the empirical `0.113` was `k/EncoderResolution` with the
`H_head` reference folded into DefaultApplyDistance — retired.

## Target module & shape

- New Core service (e.g. `ApplyPointResolver` in `PandA.Core`) — pure, no econtroller dependency.
  Consumes an `ApplyPoint` (decision-011), carton dimensions, printer config; returns the raw
  pulse integer. Called during induct where the apply fire point is resolved onto the print job.
- Per-printer config extends the printer/profile config (labelWidth default 4; EncoderResolution
  default 0.25; DefaultApplyDistance; and the three top-apply params, present only for top printers).
- TDD: table-driven leading/trailing/middle side cases + top-apply cases across carton
  height/length permutations, including sign-direction assertions.

## Follow-ups / bookmarks

- **CartonHeight unit** — CartonLength is confirmed pulses; CartonHeight's unit (for the top
  kinematics, needs inches) needs one calibration confirmation from the inbound scan message.
  Bookmark a unit-calibration check before top-apply hardware trials.
- Top-apply Trailing/Middle now fully supported (source only did top-Leading).

## References
- Source: `8.0_CreateSP/sdisp_TOOL_CUSTOM_DynamicApplyPoint.sql`; table `PandaCartonList`
  (CartonLength/Width/Height/Weight/FrontGap). decision-011 (fire-point Map/Profile model;
  print=int static, apply=inch+edge).
