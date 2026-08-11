# 010 — Fire-point profiles (print + apply firing points per printer per label)

Status: **accepted (happy-path slice)** · Supersedes the fire-point gap flagged in `009 §7`.

## 1. Purpose

Every label a PandA line applies has two physical firing events, in a fixed order:

1. **Print** — the printer emits (prints) the label.
2. **Apply** — the PLC tells the printer's **TAMP head** to fire and stick the label onto the carton.

Each event is anchored to a **tracking device** (a physical photo-eye in the line) and a
**fire point** (how far past that eye the event fires). This doc defines the C# model for
that data, ported from the PandA source, and the happy-path slice we build now.

**Print always precedes apply** — you cannot apply a label that has not been printed. This
is a hard invariant of the model.

## 2. Source model (what the SQL does)

### Tables

| Table | Role |
|---|---|
| `PrinterFirePoints` | atomic fire-point row, **per printer per label**: `(PrinterRecID, LabelDefRecID, PrintTrackingDevice, PrintFirePoint, ApplyTrackingDevice, ApplyFirePoint)` |
| `LabelProfileHeader` | a **named** profile: `(RecID, Active, ProfileName, ProfileDescription)` |
| `LabelProfileDetail` | join: `(LabelHeaderRecID, PrinterFirepointRecID)` — a profile = a **set of** `PrinterFirePoints` rows |
| `LabelProfileMap` | the **map**: `(PrinterRecID, LabelDefRecID)` — which printers are wired to which label types on the line |
| `LabelPrintLocations` | edge codes: `L`=Leading, `T`=Trailing, `M`=Middle |

### Column meanings (confirmed with the domain owner)

- **`PrintTrackingDevice` / `ApplyTrackingDevice`** — the id of the physical **tracking photo-eye**
  the PLC starts that process at. Print eye = where the label is printed; apply eye = where the
  PLC fires the TAMP head. Written down so the PLC knows which eye anchors each event.
- **`PrintFirePoint`** — a raw **encoder/device count** (e.g. `800`). Usually **static**;
  edited by the client through the GUI. Kept as an int; not inch notation.
- **`ApplyFirePoint`** — human **inch + edge** notation, e.g. `1T`, `1L`, `0M`, `.4M`, `-.4M`,
  `5.25L`, `-1M`. Chosen because customers speak in inches from an edge
  ("1 inch from the trailing edge"). `<signed-decimal><edge-letter>`.

### How map and profiles relate

`LabelProfileMap` lays down the printer+label **slots** for the line. A **profile** fills those
slots with a fire-point variant: `sdisp_GUI_LabelProfile_Insert` takes a string like
`2:L:Shipping,2:T:Content` (`value:edge:label`), walks the map to find the printer (and its
`PrintPoint`/`PrintDevice`/`ApplyDevice` from `sdivw_printerdefs`) for each label, writes one
`PrinterFirePoints` row, and links it into the header via a detail row. Multiple named profiles
ride on top of the same map — "the map hosts a group of profiles" — but the link is **implicit**
through the shared `(PrinterRecID, LabelDefRecID)` keys; there is no map→profile FK.

At runtime the active profile is picked purely by **`ProfileName`** (`PandaData.ProfileName`),
filtered to `LabelProfileHeader.Active = 1`. The map is not consulted to *choose* the profile.

### Resolution (`sdisp_PA2BP_SendPrinterFirePoints`)

Inputs `(PandaID, PrinterRecID, LabelDefRecID, Index, CartonSize, PrinterOrientation, ProfileName)`.

1. `CurrentProfile` CTE = `LabelProfileHeader (Active=1, ProfileName=@ProfileName)` ⋈
   `LabelProfileDetail (Active=1)` → the set of `PrinterFirepointRecID`s in that profile.
2. `CurrentFirepoints` CTE = `PrinterFirePoints` filtered to that set **and** matching
   `@PrinterRecID + @LabelDefRecID` → the one fire point.
3. Optionally adjust `ApplyFirePoint` via `sdisp_TOOL_CUSTOM_DynamicApplyPoint` (carton
   size / orientation). **Deferred — backlog.**
4. Write `vPA.Assign[i].PrintPointDevice / PrintPoint / ApplyPointDevice / ApplyPoint` tags.
   **Tag transport is owned by the ADS BluePaw connector — not our port (009 §2).**

### Guards (ported)

- `PrintTrackingDevice = 0` **or** `ApplyTrackingDevice = 0` → error "no tracking devices".
- `PrintFirePoint LIKE '0%'` → "neglect print" (apply-only; do not print).
- Insert-time (`sdisp_GUI_LabelProfile_Insert`): edge `<> 'M'` **and** value `< 0` →
  "Improper value for Leading/Trailing Edge". i.e. **negative inches are only legal for Middle.**

### Seed evidence (`PrinterFirePoints.sql`)

`PrintTrackingDevice=2`, `PrintFirePoint=800` everywhere; `ApplyTrackingDevice ∈ {3,4,5}`;
`ApplyFirePoint ∈ {.4M, -.4M, 1L, 5.25L, 1T, 0L, 2L, 3L, 4L, 2T, -1M, -3M}`. Confirms:
decimals, leading `.`, negative only on `M`, `0` legal on `L`.

## 3. C# model (this slice)

Namespace `PandA.Core` (fire points are core domain, adapter-agnostic).

```
enum Edge { Leading, Trailing, Middle }           // L / T / M

record ApplyPoint(decimal Inches, Edge Edge)
    static ApplyPoint Parse(string)               // "1T" / ".4M" / "-.4M" / "0L"
    string ToString()                             // round-trips to source notation
    // guard: Inches < 0 allowed only when Edge == Middle

record FirePoint(
    int PrintTrackingDevice,                      // print photo-eye id  (> 0)
    int PrintFirePoint,                           // encoder count; 0/"0…" = neglect print
    int ApplyTrackingDevice,                      // apply photo-eye id  (> 0)
    ApplyPoint ApplyFirePoint)
    bool NeglectPrint => PrintFirePoint <= 0      // "0%" neglect-print rule
    // guard: tracking devices > 0

record FirePointProfile(string Name, IReadOnlyDictionary<(string PrinterId, string LabelType), FirePoint>)
    bool TryResolve(printerId, labelType, out FirePoint)

class FirePointResolver
    FirePointResolution Resolve(profile, printerId, labelType)
    // ports CurrentProfile ⋈ CurrentFirepoints + the tracking-device guard
```

- `ApplyPoint`/`FirePoint` are structural + self-validating; the **sign/edge** rule lives on
  `ApplyPoint`, the **tracking-device** rule on `FirePoint`.
- `FirePointProfile` is keyed by `(printerId, labelType)` — the exact grain of
  `PrinterFirePoints`. Case-insensitive on both keys (matches the rest of Core).
- `FirePointResolver.Resolve` returns a small result (`Resolved` | `NotInProfile` | error text)
  rather than throwing on a miss, so the caller can decide (mirrors the SP's `@ErrorCode`).

### Line wiring

`LineConfig` gains an optional `FirePointProfile? activeProfile` (the **static generic
profile** for the happy path). One profile per line, always active. No switching yet.

### Surfacing

The resolved `FirePoint` is attached to the print job / print result so the eventual
`PandA.EController` adapter (ADS connector) can write the `vPA` tags. The Sim harness logs it.

## 4. Scope of this slice

**In:** `Edge`, `ApplyPoint` (parse/format/guard), `FirePoint` (guards + neglect-print),
`FirePointProfile`, `FirePointResolver`, static `LineConfig.ActiveProfile`, resolve on print,
harness log line, unit tests.

**Backlog (documented, not built):**

- **Profile switching** — operator/GUI selects the active profile (or group) per line.
- **Host-driven `ProfileName`** — pattern-match the profile from `PandaData.ProfileName` per carton.
- **`DynamicApplyPoint`** — carton-size / orientation adjustment of `ApplyFirePoint`.
- **Explicit map→profile grouping** container (if we later want named maps owning profile groups).
- **BluePaw `vPA` tag write** — owned by the ADS connector in the eController adapter.
