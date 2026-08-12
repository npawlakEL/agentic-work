# 012 — Line control & printer status (operator)

**Audience:** Operators and commissioning engineers running a PandA line.
**What's new:** the line now decides on its own how many printers to keep printing, holds the rest
as **spares**, and automatically **slows** or **stops** the line when it doesn't have enough
working printers — per apply orientation (Side vs Top).

> Today these controls are exercised through the **simulation harness** (a text console). The same
> behavior will drive the real line once the printer/engine/zone messages are wired in. Nothing
> here is a physical button yet — it's the logic that will sit behind the operator screens.

## Concepts in plain language

- **Orientation (Side / Top)** — which side of the carton a printer applies to. The line keeps a
  separate count for each. Losing a Side printer never affects the Top printers.
- **Active vs Spare** — an *active* printer is in rotation and receives labels. A *spare* is healthy
  but held in reserve. The line parks extra printers as spares so there's always a backup ready.
- **Minimum online** — how many working printers each orientation needs to run at full speed.
- **Slow line (degraded)** — if a printer group drops below its minimum but still has at least one
  working printer, and the group is allowed to run degraded, the line keeps going **slowly** so the
  remaining printer(s) can label every carton, instead of stopping.
- **Shut line / shut zone** — if a group can't meet its minimum and has no spare to fall back on,
  the line stops. If the conveyor **zone** itself goes down, the whole line stops regardless.

## What the line does automatically

- A printer comes back online and there are already enough running → it's **parked as a spare**.
- An active printer fails → a **spare is promoted** back into rotation to cover it.
- No spare left and below minimum → the line **runs slow** (if degraded is allowed) or **stops**.
- The conveyor zone goes down → the line **stops** immediately.

You don't do any of this by hand — the line re-evaluates every time a printer, print engine, or
zone changes state.

## Using the simulation harness

Start it and you'll see the command list. Relevant commands:

| Command | What it does |
|---|---|
| `s` | **Status** — shows each printer's orientation, PLC/engine health, and role (active / spare / offline), plus whether the zone is up. |
| `p <printerId> up` / `p <printerId> down` | Bring a printer online / take it offline, then re-evaluate the line. |
| `z up` / `z down` | Bring the conveyor zone up / down, then re-evaluate. |

After a `p` or `z` command the harness prints what happened, for example:

```
>> IN  printer-status  Ship1 → OFFLINE
   Cont1 promoted (spare→active)
   lane-eval ⇒ Balanced  (All printer groups balanced.)
```

or, when it can't keep full speed:

```
>> IN  printer-status  Cont1 → OFFLINE
   lane-eval ⇒ SlowLine  (Side: running slow (1/2 usable, degraded mode).)
<< OUT bluepaw  [stubbed egress: SlowLine]
```

The `<< OUT bluepaw [stubbed egress: ...]` line is where the real stop/slow signal to the PLC will
go later — for now it's just shown so you can see the decision.

## Try it (example walk-through)

The seeded line has 3 Side printers, keeps **2** running, and is allowed to run slow.

1. `s` — all three show **active**.
2. `p Cont1 down` then `s` — still **Balanced**; two remain, which is the minimum.
3. `p Cont1 up` then `s` — Cont1 becomes a **spare** (surplus parked in reserve).
4. `p Ship1 down` — the spare (**Cont1**) is **promoted** back to active; line stays Balanced.
5. `p Ship1 down`, `p Cont1 down` — only one printer left → **SlowLine**.
6. `p Par1 down` — no printers left → **ShutLine**.
7. `z down` — **ShutZone** (whole line stopped, regardless of printers).

## Troubleshooting

- **Line unexpectedly slow?** Run `s`. If a group shows only one active printer, a printer is
  offline and the line is running degraded. Bring the printer back with `p <id> up`.
- **Line stopped (ShutLine)?** A printer group has no working printers left. Check `s` for offline
  printers and restore them.
- **Line stopped (ShutZone)?** The conveyor zone is down (`zone=OFFLINE` in `s`). Bring it back with
  `z up`.
- **A healthy printer shows as `spare`?** That's normal — it's a reserve. It will be promoted
  automatically the moment an active printer in the same orientation fails.

## What changed from before

Previously printer online/offline state was fixed and the line had no automatic slow/stop or spare
handling. Now the line reacts to printer, print-engine, and zone status and manages spares and line
speed on its own.
