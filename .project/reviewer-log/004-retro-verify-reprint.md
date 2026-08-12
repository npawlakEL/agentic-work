# 004 — Retro-review: verify model + reprint / print-count lifecycle

**Slices:** `VerificationService`, `VerifyStationService`, `VerifyThresholdTracker`, verify models/enums; `TransportOrder` / `LabelPrintState` reprint lifecycle.
**Found by:** Reviewer (code-review agent), retroactive Gate 2 review.
**Iteration:** 1 (retro) · **Date:** 2026-08-12
**Source ported:** `sdisp_TOOL_PA_VerifyLabel`, `sdisp_PA_VerifyCarton`, `sdisp_PA_VerifyThreshold_Update` (+ `_Add`/`_Refresh`), decision-003.

## Verdict
**Reprint / print-count lifecycle (decision-003): confirmed faithful & well-tested — CLEAN.**
**Verify-core comparison DIVERGES from source in 4 places; all four were adjudicated by the domain owner
(decision-004).** VF-1/VF-2 are deliberate divergences (kept strict — no change); VF-3/VF-4 were implemented.

## Confirmed clean (no action)
- decision-003 reprint hole correctly avoided: `MarkVerifyFailed → HeldForIntervention` (no auto re-arm), monotonic `PrintCount`, `CanPrint` gate, `AuthorizeReprint` consumed on next `CompletePrintRun`, partial runs don't count. Well covered.
- Classification ordering (`?`/`!`/`~`/`0`/`#`), content-toggle filtering, Orientation/`-`/empty drops, first-failure short-circuit, PASS path — all match `sdisp_TOOL_PA_VerifyLabel` + doc 006 §A–C.
- Threshold post-increment `>= threshold` compare — matches source `_Add`; no off-by-one.
- VerifyPass→enum abstraction acceptable (per-label `Reason`+`LabelType` preserve remap to host codes).
- No ZPL-strip issue: source compares raw scanned barcode strings; port's exact-string `Acceptable.Contains` matches.

## Findings (all ESCALATED — pending user decision)

| # | Sev | Finding | Source ground truth | Recommendation |
|---|-----|---------|---------------------|----------------|
| 1 | High | A scanned label whose type has **no expected barcode** currently returns `Fail`/`NoRead` and short-circuits. This false-FAILs three real cases: (a) an extra label not in advice data, (b) a **duplicate read of a valid label** (first consumes the slot, second finds none → false fail), (c) cartons with fewer types than the scanner presents. False fails also feed the consecutive-fail counter and can force-pause a healthy line. | `sdisp_TOOL_PA_VerifyLabel.sql:271-278`: `IF @CurrentVerifyLabel='-'` ⇒ **ignore** (do nothing, continue cursor) ⇒ carton still PASSES if all expected matched. **Verified: the later "extra label ⇒ code 14" block (lines 343-350) is dead/unreachable** because line 275 catches every `'-'`. | Change: when no expected slot matches a scanned label, **skip & continue** (ignore) instead of fail; only leftover *expected* labels FAIL. Then fix tests `ScannedLabelNotInData_YieldsNoRead`, `Extra_ScannedTypeNotExpected_NoRead`, `EmptyExpected_ButAScanArrives_NoRead`, and strike doc 006 §B table row for code 14. |
| 2 | Medium | Duplicate same-type expected labels modeled as ordered required slots (first-unconsumed match) rather than acceptable-value alternates. `Expected(("Shipping","A"),("Shipping","B"))` scanned `B,A` FAILs (pinned by `DuplicateExpected_ReversedOrder_Fails`). | `sdisp_TOOL_PA_VerifyLabel.sql:354-366`: multiple rows for one `LabelName` are the xref set — scanned value IN any ⇒ PASS (order-independent), then all rows deleted. Doc 006 §B "Xref rescue". | Treat multiple same-type expected rows as one acceptable-value union; fix the test. (Lower real-world likelihood — alternates normally arrive via the `LabelXref` param, which the port already handles.) |
| 3 | Medium | Bypass (`VerifyPass=3`) is folded into `proceed` and registered as `pass:true`, which **resets** the consecutive-fail streak. | `sdisp_PA_VerifyCarton.sql:297-301` calls threshold update unconditionally; `_Update.sql:87` `IF @VerifyPass<>1 ⇒ increment`. So bypass **increments** the streak. Doc 006 §D: only `VerifyPass=1` resets. | Either register bypass as non-pass (`pass: Outcome==Pass`) to match source/doc, OR record an explicit decision that bypass intentionally clears the streak (may be a deliberate improvement). |
| 4 | Low | Threshold tracker never resets after tripping the pause; returns `PausePrinter:true` on every subsequent fail (pinned by `StaysPaused_WhileFailsContinue`). After un-pause, the next single fail immediately re-pauses. | `sdisp_PA_VerifyThreshold_Update.sql:173` calls `_Refresh` (→ count=0) when the threshold fires. Doc 006 §D: "force-pause … then refresh/reset." | Reset the line's count to 0 when `Register` returns `PausePrinter:true`, OR doc-log that post-trip reset is deferred to the pause-egress connector. |

## Open decisions surfaced to user — RESOLVED (decision-004)
- **VF-1 (Finding 1):** **KEEP strict fail** (deliberate divergence, like decision-003). A scanned label
  outside the carton's required barcode array (extra/unexpected read, or duplicate read of a matched label)
  is a verify FAIL. Domain owner: a label with no barcode to verify against can't be verified; anything
  outside the required set isn't a valid pass. **No code change.**
- **VF-2 (Finding 2):** **KEEP ordered slots.** Slot numbering matters — a repeated LPN is expected scanned
  twice in slot order. True alternate-value xref goes through the `LabelXref` param (already handled).
  **No code change.**
- **VF-3 (Finding 3):** **Bypass does not count UNLESS no-read/no-data.** A clean bypass proceeds and clears
  the streak; a bypass that read `?`/`!`/`~`/`0` fails, holds, and counts. **Implemented** in
  `VerificationService` + tests.
- **VF-4 (Finding 4):** **Configurable.** `VerifyThresholdTracker(resetOnTrip)` — `true` refreshes to 0 on
  trip (source), `false` (default) stays tripped. **Implemented** + test. Surfacing as a setting → backlog
  F-VF4CFG.

## Post-fix state
- Code changed: VF-3 (bypass glitch fails+counts), VF-4 (configurable post-trip reset). VF-1/VF-2 confirmed
  correct-as-intended (no change).
- Tests: 231 green (added bypass-glitch + reset-on-trip coverage).
