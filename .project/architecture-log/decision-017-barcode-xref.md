# Decision 017 — Barcode cross-reference (xref): lightweight resolver in Core; oLPN/waves tabled

**Status:** Accepted
**Date:** 2026-08-12
**Basis:** Domain-owner grill (2026-08-12) over `PandaDataXRef` / `sdivw_PandaDataXRef` and
`sdisp_TOOL_CUSTOM_LPNxRef.sql`. Resolves D12/F18; tables F24 indefinitely.

## Two facets, split on scope

The source `PandaDataXRef` table wears two hats via `BarcodeDescription` (BL/UPC/GTIN/EAN/ItemID/oLPN):

1. **Multi-barcode carton identity (A)** — IN SCOPE (this decision).
2. **oLPN outbound association (B)** — `sdisp_TOOL_CUSTOM_LPNxRef` — **TABLED INDEFINITELY.**
   It is ULW-custom, wave-coupled (requires a non-COMPLETED wave + WaveID), itmsort/RF-driven,
   with a one-oLPN-per-order guard + disassociate proc. Waves are tabled; revisit only when a
   concrete need appears.

## Facet A — cross-reference resolver (in scope)

### Use case
A carton is scanned by a label, but the host gave us data for a label that isn't literally that
blind label — it has an **associative** barcode that matches. We must resolve the scanned value
against **alternate identifiers**, not just the blind label.

### Model (NOT a DB table — a lightweight in-memory resolver)
- Each carton has its **primary blind label** plus a set of **cross-reference barcodes**.
- **Matching = EXACT** value match against the cross-reference values (no wildcards/patterns).
- **Resolution granularity = BOTH:** a cross-ref may resolve to the **whole carton** (transport
  order) OR to a **specific label slot** within the carton. So a cross-ref entry carries an
  optional label-slot target. Resolution returns the carton, the matched label slot (if the
  cross-ref was label-scoped), and the matched barcode value (for logging/verify-result).
- **Two ingestion paths (either order):**
  1. **Inline** — cross-refs bundled with the labels in the advice message.
  2. **Separate association message** — the customer sends cross-refs independently; we hold
     them and associate ourselves. The association may arrive **before or after** the carton
     advice, so associations are held in a store keyed by the primary/blind label and applied
     whenever the carton is present.

### Core contract (target module: `PandA.Core`)
- An `IBarcodeXRef` port (or `BarcodeXRefResolver` service):
  - `Associate(primaryBlindLabel, crossRefBarcode, optional labelSlot)` — from inline advice or a
    separate association message.
  - `Resolve(scannedBarcode)` → `XRefMatch { carton, matchedLabelSlot?, matchedBarcode }` or none.
- Held/unmatched associations persist keyed by blind label until the carton exists; no wave coupling.
- `PandA.Sim` provides an in-memory implementation; scan/verify (`VerifyStationService`, induct)
  consults primary-label match first, then the xref resolver.
- TDD: exact-match hit/miss; carton-level vs label-level resolution; association-before-carton and
  association-after-carton ordering; inline vs separate-message ingestion; matched-barcode recorded.

## Explicitly out
- No `PandaDataXRef` table port, no PIVOT view, no Priority column semantics (source-specific).
- No oLPN association, no wave dependency, no itmsort/RF integration (facet B, tabled).

## References
- Source: `5.0_CreateTables/PandaDataXRef.sql`, `7.0_CreateViews/sdivw_PandaDataXRef.sql`
  (PIVOT of BL/UPC/GTIN/EAN/ItemID/oLPN), `8.0_CreateSP/sdisp_TOOL_CUSTOM_LPNxRef.sql` (facet B).
- decision-012 (waves/xref tabling that this supersedes for facet A), decision-004 (verify semantics).
