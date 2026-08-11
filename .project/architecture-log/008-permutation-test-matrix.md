# 008 — Permutation test matrix (durable driver tests)

Goal: make the test suite **durable** by covering the full combinatorial space of configurations, inputs,
and lifecycle states with data-driven `[Theory]` "driver" tests, each asserting the correct **end result**.
This doc is the authoritative enumeration of dimensions and expected outcomes; the driver tests encode it.

The dimensions below are drawn directly from the current Core surface (not speculation):
`LineConfig`, `PrinterConfig`, `PrinterState`, `PandaLabelSet`/`Label`, `TransportOrder` (decision-003),
`VerifyOptions`, `VerificationService`, `VerifyThresholdTracker`.

---

## A. Dimensions (the axes of the matrix)

### A1. Line config (`LineConfig`)
| Axis | Values | Effect |
|---|---|---|
| `LoadBalance` | true / false | true = least-recently-printed; false = first-configured (ConfigOrder) |
| Printer count | 0 / 1 / N | 0 ⇒ nothing eligible; N enables collision + rotation |

### A2. Printer config (`PrinterConfig`)
| Axis | Values | Effect |
|---|---|---|
| `LabelMap` | covers-type / not-covers / overlapping across printers | eligibility per label type |
| `PrinterType` (orientation) | Side / Top | must equal the selection orientation to be eligible |
| `ConfigOrder` | 0..N | load-balance tie-break |

### A3. Printer runtime state (`PrinterState`)
| Axis | Values | Effect on `IsAvailable` |
|---|---|---|
| `PlcOnline` | true / false | false ⇒ unavailable |
| `EngineOnline` | true / false | false ⇒ unavailable |
| `IsSpare` | true / false | true ⇒ unavailable (held as spare) |
| `LastPrinted` | null / t | null = never printed = oldest = picked first (load-balance) |

`IsAvailable = PlcOnline && EngineOnline && !IsSpare`. **8 boolean combos** collapse to available (1,1,0) vs
unavailable (all others). Spare is the load-balancing "spare printer" rule from architecture-log 005.

### A4. Carton label set (advice → `PandaLabelSet`)
| Axis | Values | Effect |
|---|---|---|
| Label count | 0 / 1 / N | 0 ⇒ `InductStatus.NoData` |
| Types present | Shipping / Content / Parcel / Exception / Orientation | routing + verify filtering |
| Duplicate types | yes / no | distinct-type selection; per-label print states keyed by type |
| `Lpn` = "" or "-" | yes / no | dropped from **verify** expected set (not from print) |
| `Orientation` type | present / absent | dropped from verify expected set |

### A5. Reprint lifecycle (`TransportOrder`, decision-003)
| Axis | Values | Effect on `CanPrint` / induct |
|---|---|---|
| `PrintCount` | 0 / ≥1 | 0 + Advised ⇒ printable; ≥1 needs authorization |
| `Status` | Advised / Printed / Verified / HeldForIntervention / ReprintAuthorized | gate |
| Authorized | yes / no | ReprintAuthorized ⇒ exactly one more full run, then consumed |
| Run completeness | full / partial | only a **full** run increments `PrintCount` + advances status |

### A6. Verify options (`VerifyOptions`)
| Axis | Values | Effect |
|---|---|---|
| `VerifyEnabled` | true / false | false ⇒ Bypass decides Ignore vs Fail |
| `Bypass` | true / false | (disabled) true ⇒ Ignore; false ⇒ Fail |
| `VerifyContentLabel` | true / false | false ⇒ only Shipping/Exception verified/filtered |

### A7. Scanned vs expected (`VerificationService`)
| Case | Scanned value | Expected outcome |
|---|---|---|
| Exact match | == expected Lpn | Pass |
| Xref backup | == an xref barcode for the type | Pass (matched) |
| Plain mismatch | different, no sentinel | Fail (Mismatch) |
| No-read | contains `?` | NoRead |
| No-data | contains `!` or `~`, or == `"0"` | NoData |
| Conflict | contains `#` | Conflict |
| Missing | expected type never scanned | Fail (Missing) |
| Extra | scanned type not in expected | NoRead (Extra) |
| Order | first failing label short-circuits | outcome = first failure's |

### A8. Verify threshold (`VerifyThresholdTracker`)
| Axis | Values | Effect |
|---|---|---|
| `failThreshold` | ≤0 / 1 / N | ≤0 disables pause |
| Sequence | pass/fail stream | pass resets to 0; fail increments; pause when count ≥ threshold |
| Line isolation | same/different lineId | streaks are per-line |

---

## B. Expected end results (the assertions)

### B1. Printer selection (`PrinterSelectionService.Select`) → per-type `LabelSelectionStatus`
| Scenario | End result |
|---|---|
| One eligible printer covers type | Assigned to it |
| No eligible printer (no map / offline / spare / wrong orientation / 0 printers) | NoPrinter for that type |
| Two types share primary, **same** backup | **both** move to that backup (no second pass) |
| Two types share primary, only one has a backup | that type → backup, other stays on primary |
| Two types share primary, neither has a backup | both stay on primary |
| Two types, different rankings landing on the same backup printer | both use that printer |
| LoadBalance on, one printer never printed | picks the null-LastPrinted one |
| LoadBalance on, tie on LastPrinted | lowest ConfigOrder wins |
| LoadBalance off | lowest ConfigOrder wins regardless of LastPrinted |

### B2. Induct (`InductService`) → `InductStatus`
| Precondition | End result |
|---|---|
| No order for blind | NoActiveOrder |
| Order, 0 labels | NoData |
| Order printable, all labels get a printer | Printed, PrintCount+1, Status=Printed, jobs=labelcount |
| Order printable, some labels no printer | PartiallyPrinted, PrintCount **unchanged**, per-label state persisted |
| Order printable, no labels get a printer | NoPrinter, no upsert, PrintCount unchanged |
| Order already printed, not authorized | NoReprint, no jobs |
| Order authorized | Printed, PrintCount+1, authorization consumed |

### B3. Verify station (`VerifyStationService`) → `VerifyStationStatus` (+ PrinterPaused)
| Verify outcome | failThreshold / streak | End result |
|---|---|---|
| Pass | — | Verified, streak reset, VerifiedAt set |
| Ignore (bypass) | — | Verified, streak reset |
| Any fail | streak < threshold | HeldForIntervention, PrinterPaused=false, VerifyFailedAt set |
| Any fail | streak ≥ threshold (>0) | HeldForIntervention, PrinterPaused=true |
| No order for blind | — | NoActiveOrder, Verify=null |

### B4. Reprint/counter invariants (`TransportOrder`)
- `PrintCount` is monotonic **within one advice generation**; only `CompletePrintRun` increments it.
  `OverwriteAdvice` (duplicate advice) starts a new generation and **resets** PrintCount + all timestamps +
  authorization + Status→Advised (last-wins, spec §6a).
- Verify fail never re-arms; `MarkVerifyFailed` → HeldForIntervention only.
- `AuthorizeReprint` is the **only** transition back to printable; resets per-label state, not the counter.
- `CanPrint == (PrintCount==0 && Advised) || Status==ReprintAuthorized`.

---

## C. Impossible / out-of-scope combos (documented, not tested)
- `PrinterType=Top` selection path — provisioned but Phase-1 selection always requests Side (005). Covered
  as a **negative** eligibility case (Top printer ⇒ not eligible for a Side request) rather than a Top run.
- Multi-line concurrency races on the print gate (senior-flagged) — deferred; InMemory Sim is single-threaded.
- Raw scanner buffer-order string parsing — backlog; Phase-2 accepts pre-typed `ScannedLabel`.

---

## D. Test file plan (driver tests)
| File | Drives | Style |
|---|---|---|
| `PrinterSelectionMatrixTests` | B1 | `[Theory]` `[MemberData]` scenario rows |
| `VerificationMatrixTests` | A7 × A6 | `[Theory]` scanned/options → outcome |
| `ReprintLifecycleMatrixTests` | A5 → B2/B4 | `[Theory]` state → CanPrint/induct status |
| `ThresholdMatrixTests` | A8 → B3 | `[Theory]` sequence → pause |
| `LifecyclePermutationTests` | A4×A6×A7 end-to-end | `[Theory]` full run → final status/counter |

Each row carries a human-readable `Name`/`Because` so failures are self-describing.

---

## E. Senior review corrections (applied)

Key corrections from senior review (matrix-senior) folded into the plan above and the tests:

1. **Counter reset (B4):** duplicate advice via `OverwriteAdvice` resets `PrintCount`/timestamps/auth/status.
   Add a driver row: overwrite after Printed/Held/Verified → everything reset, Status=Advised.
2. **Collision (B1):** there is no second-pass "spread"; **every** type sharing a primary independently moves
   to its own backup. Same primary + same backup ⇒ **both** land on the backup. Four exact shapes enumerated.
3. **Ineligible backup is impossible:** backup is drawn from already-eligible candidates, so an offline/spare/
   wrong-orientation/unmapped second printer yields `Backup=null` (fallback to primary), never a non-null
   ineligible backup. Model it as "configured alternative filtered out → primary".
4. **Content-label toggle filters BOTH expected and scanned.** So with the toggle off, filtered Content/Parcel
   scans cannot be Extra and filtered expected cannot be Missing. Rows added:
   - Content expected + wrong Content scan, toggle off → **Pass**
   - Content expected + empty scans, toggle off → **Pass** (not Missing)
   - Shipping expected + extra Content scan, toggle off → **Pass**
   - Shipping expected + extra Exception scan (both retained) → **NoRead/Extra**
5. **Empty expected-set = Pass (intended, documented):** Orientation-only, all-empty/`"-"` Lpn, or content-only
   with toggle off ⇒ no expected labels. Empty retained scans → **Pass** (carton Verified, streak reset); any
   retained scan → **NoRead/Extra**. This is an accepted product behavior for the port.
6. **Induct persistence rows:** `PartiallyPrinted` always upserts; `NoPrinter` never upserts; authorized+partial
   stays `ReprintAuthorized` (auth NOT consumed); authorized+no-printer not upserted. Assert with a **spy store**
   (count upserts) — the in-memory store mutates by reference and hides missing upserts.
7. **Duplicate label types:** jobs == physical label count; duplicates of a type share status/printer; two
   duplicates of ONE type can never be `PartiallyPrinted` (both assigned or both NoPrinter); duplicates don't
   create a collision (collision counts distinct types); `_printStates` has one entry per type.
8. **Duplicate expected verify is order-sensitive:** slots consumed first-unconsumed; reversed scan order of
   two same-type values fails even though multisets match; excess duplicate scan → Extra. Use ordered inputs.
9. **Threshold/station reset rows must start from a non-zero streak:** Fail,Fail,Pass→0; Fail,Fail,Ignore→0;
   Fail,Fail,disabled-no-bypass→3; threshold≤0 accumulates but never pauses; pause stays true past threshold;
   changing threshold reuses the count; `Reset`/`CurrentCount` covered; NoActiveOrder doesn't reset the streak.
10. **Determinism:** unique `ConfigOrder` is a config invariant (else list-order tie-break). Verify details are
    order-dependent — assert the processed prefix only, never as a set. Address print states via
    `PrintStateFor(type)`, never by `PrintStates` enumeration order.

**Contract tests (separate from the cross-product, senior-flagged):**
- Induct with an order but no line config → `InvalidOperationException`.
- `InductResult.FromAssignments([])` returns `Printed` (empty-run quirk) — pin as a unit contract.
- Top orientation: one **direct** `Select(..., Top)` eligibility row; excluded from end-to-end Induct.

**Excluded as impossible (do not cross-product):** VerifyDisabled × {Missing/Extra/sentinel/xref};
Bypass affecting enabled verify; toggle-off filtered types producing Missing/Extra; Orientation/empty-Lpn
producing Missing; NoData × printer config; NoReprint × printer health; PartiallyPrinted with a single label
or only duplicates of one type; collision with a single distinct type.
