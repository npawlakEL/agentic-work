# 009 — PLC ↔ PandA Message Protocol (reverse-engineered)

**Purpose:** authoritative reference for the wire protocol between the PLC layer and the PandA
application DB, reconstructed from the PLC-side SQL. This is the seam the `PandA.EController`
adapter will implement. It also names the concepts the current C# Core does **not** yet model.

**Source repo:** `Element-Logic-FL/ItmSort-AI` (commit `7074eb71`, captured 2024-12-04).
**Primary snapshot:** `databases/PLC/lab-2k19-tl22.vm_SDI_PLC_SRTRK_CP3_20241204/`.
All three plant snapshots (lab-2k19-tl22, GEODIS `172.20.32.5`, Hanes-Perris) share identical SHAs
for every file cited — **zero per-plant divergence** in the 281–286 layer.

> Note: the PandA application DB (`SDI_PandA`) itself is **not** in this repo. The `sdisp_BP2PA_*`
> and `sdisp_TOOL_PA_GetPrinterFirePoints` procedures exist here only as synonyms pointing at
> `[SDI_PandA].[dbo].*`. Their internals are inferred from call sites (marked *inferred* below).

---

## 1. Inbound framing (PLC → DB)

- The PLC writes a raw `VARCHAR(4000)` buffer into a Service Broker queue.
- A buffer **may contain multiple back-to-back messages with no separator**:
  `<281,1,1,1,1,8,0,0154006001,0,0,0,0,0,0,24092,118><283,1,1,2,0>`
- **STX/ETX = `<` / `>`.** Content between the brackets is a flat comma-separated field list, no
  trailing delimiter.
- `sdisp_VLC2DB_Msgs_PreProcess` (SHA `9dd130cf`) loops `WHILE LEN(@VLCBuffer) > 0`:
  1. `<` must be at position 1 (else log EventID 18/19 and advance)
  2. `>` must exist (else EventID 20, abort)
  3. reject nested `<` inside the pair (EventID 21)
  4. extract `@ProcessBuffer` including brackets
  5. read the leading integer code (pos 2 → first comma)
  6. route by range, then chop the consumed message off the front and continue

### Code-range routing

| Range | Target SP |
|-------|-----------|
| 50–99 | `sdisp_VLC2DB_Msgs_Process_Conv` |
| 200–279 | `sdisp_VLC2DB_Msgs_Process_Ctn` |
| **280–299** | **`sdisp_VLC2DB_Msgs_Process_PA`** ← PandA |
| 300–399 | `sdisp_VLC2DB_Msgs_Process_Mdr` |
| 400–499 | `sdisp_VLC2DB_Msgs_Process_Itm` |
| 500–599 | `sdisp_VLC2DB_Msgs_Process_Sf` |
| 600–699 | `sdisp_VLC2DB_Msgs_Process_Stat` |
| else | `sdisp_VLC2DB_Msgs_Process_Scp` |

Inside `sdisp_VLC2DB_Msgs_Process_PA` (SHA `80f1ed17`) the body is parsed with a T-SQL cursor
`StrParser_Cur` (positional `FETCH NEXT` per field), not CHARINDEX loops.

---

## 2. Message codes 281–286

### 281 — PANDA_SCAN_INBOUND (induct **and** verify, discriminated by DeviceID)

| # | CSV | Type | SP name | Meaning |
|---|-----|------|---------|---------|
| 01 | [0] | INT | code | `281` |
| 02 | [1] | INT | `@SourceMode` | app-DB selector (→ §4) |
| 03 | [2] | INT | `@SorterNumber` | sorter / lane number |
| 04 | [3] | INT | `@SorterMode` | 1=normal, 2=aim sort, 3=alignment, 4=round robin |
| 05 | [4] | INT | `@DeviceID` | **1 = induct**, **>1 = verify** at a downstream device |
| 06 | [5] | INT | `@SeqNum` | rolling carton-slot counter, 0–2000 then wraps |
| 07 | [6] | INT | `@LabelStatus` | 0=good, 1=lowboy, 2=no data, 3=data error / multi-scan |
| 08–13 | [7]–[12] | VARCHAR | `@Label1..@Label6` | raw barcode reads from up to 6 scanners |
| 14 | [13] | INT | `@Weight` | inline-scale weight |
| 15 | [14] | INT | `@Gap` | gap to preceding carton (encoder units) |
| 16 | [15] | INT | `@Length` | carton length (encoder units) |

Example: `<281,1,1,1,1,8,0,0154006001,0,0,0,0,0,0,24092,118>` → SourceMode 1, Sorter 1,
SorterMode 1 (normal), DeviceID 1 (induct), Seq 8, LabelStatus 0 (good), Label1 `0154006001`,
Wt 24092, Len 118. `?` in a label = no-read sentinel.

Routing: `IF @ScannerID NOT LIKE '%VERIFY'` (≡ `DeviceID = 1`) → `sdisp_BP2PA_Scan_Induct`;
else → `sdisp_BP2PA_Scan_Verify`.

### 282 — PANDA_PRINT
`EXEC sdisp_BP2PA_Print @DbName, @CartonListID, @PrinterID, @ErrorCode OUTPUT`.
Fields (inferred): `<282, SourceMode, SorterNumber, CartonID, PrinterNumber>`. Out-of-band explicit
print trigger for a known carton.

### 283 — PANDA_PRINTER_STATUS
`EXEC sdisp_BP2PA_Status_Printer @DBName, @PrinterID, @PrinterStatus, @SorterNumber`.
Fields (inferred): `<283, SourceMode, SorterNumber, PrinterNumber, PrinterStatus>`. Printer
hardware online/offline/fault; PandA suppresses jobs for a faulted printer.

### 284 — PANDA_ZONE_STATUS
`EXEC sdisp_BP2PA_Status_Zone`. Conveyor print-and-apply zone enabled/disabled.

### 285 — PANDA_LATE_ASSIGN
PLC-initiated late assignment. Triggers `sdisp_PA_Response_CartonAssign` which writes the
`vCtn.Assign[i].*` tag bundle (§3B).

### 286 — PANDA_SCAN_VERIFY (explicit verify variant)

| # | CSV | Name | Meaning |
|---|-----|------|---------|
| 01 | [0] | code | `286` |
| 02 | [1] | SourceMode | DB selector |
| 03 | [2] | SorterNumber | sorter |
| 04 | [3] | SorterMode | 1–4 |
| 05 | [4] | DeviceID | verify device |
| 06 | [5] | SeqNum | carton slot |
| 07 | [6] | `@LabelBuffer` | **pipe-delimited** scanner reads, e.g. `123456798\|0154006001` |
| 08 | [7] | trailing | extra/seq |

Example `<286,1,1,1,5,8,123456798|0154006001,136>`. The `|` buffer is arbitrated to `@FinalLabel`
inside the PandA DB (scoring logic not in this repo). Then
`EXEC sdisp_BP2PA_Scan_Verify @DbName, @SorterNumber, @SorterMode, @DeviceID, @SeqNum, @Label1=@FinalLabel`.

---

## 3. Outbound framing (DB → PLC)

Mechanism: `EXEC sdisp_DB2VLC_Send @SorterID='CSS', @TagName, @TagValue` (SHA `b84e8b5a`).
Tries `CAST(@TagValue AS INT)`; numeric → `TagValueNum`, else `TagValueString`. Looks up
`CCSorterOffset` from `SorterDef` (1–6) and INSERTs into `DB2VLC_CE[offset]`. A separate OPC bridge
polls those tables and writes Allen-Bradley PLC tag memory.

### 3A — 281 induct response: `vPA.Assign[i].*` (fire points)

**File:** `sdisp_PA_Response_FirePoints.sql` (SHA `ebd60d98`). Per printer assigned to the carton,
in order:

| # | Tag | Source | Meaning |
|---|-----|--------|---------|
| 1 | `vPA.Assign[i].RecID` | `@CartonID` | DB carton PK |
| 2 | `vPA.Assign[i].Seq` | `@SeqNum` | PLC slot (echoes inbound SeqNum) |
| 3 | `vPA.Assign[i].Sorter` | `@SorterNumber` | sorter lane |
| 4 | `vPA.Assign[i].Dest` | `@LaneID` | sort **destination lane** |
| 5 | `vPA.Assign[i].PrintPointDevice[p]` | `@PrintTrackingDevice` | encoder segment of the **print** station |
| 6 | `vPA.Assign[i].PrintPoint[p]` | `@PrintFirePoint` | encoder distance to **fire the printer** |
| 7 | `vPA.Assign[i].ApplyPointDevice[p]` | `@ApplyTrackingDevice` | encoder segment of the **apply** station |
| 8 | `vPA.Assign[i].ApplyPoint[p]` | `@ApplyFirePoint` | encoder distance to **fire the applicator** |

Written directly from `Process_PA` (not the FirePoints SP):

| # | Tag | Value | Meaning |
|---|-----|-------|---------|
| 9 | `vPA.Assign[i].SourceMode` | `@SourceMode` | mode echo-back |
| 10 | `vPA.Assign[i].MsgRdy` | `1` | **commit handshake — always the last write** |

`i` = PLC tag-array slot index; `p` = printer ID (multiple printers per carton keyed within the
slot). The PLC must treat the slot as invalid until `MsgRdy = 1`.

### 3B — 285 late-assign response: `vCtn.Assign[i].*`

**File:** `sdisp_PA_Response_CartonAssign.sql` (SHA `9dcd414c`). Signature
`(@Index, @PrinterID, @SeqNum, @CartonID, @LaneID, @SorterNumber, @SourceMode)`. Writes, in order:
`Seq`, `RecID`, `Dest`, `Sorter`, `MsgRdy=1`.

> **Two distinct tag arrays.** `vPA.Assign[i]` (PandA namespace) carries full fire-point/printer
> data for 281 induct. `vCtn.Assign[i]` (conveyor/carton namespace) carries simplified
> lane/destination routing for 285 late-assign. The adapter must pick the right array per message.

### 3C — `sdisp_TOOL_PA_GetPrinterFirePoints` (SHA `057d2d37`, synonym → PandA DB)

Called from `Process_PA` **before** `Response_FirePoints`:
`EXEC ... @PandaID, @PrinterID OUTPUT, @PrintTrackingDevice OUTPUT` (and apply equivalents).
Takes the PandA assignment ID; outputs the assigned physical printer plus the tracking-device
encoder chain for its print (and apply) station.

**Physical model:** a *tracking device* is a conveyor segment with its own encoder. A *fire point*
is an encoder distance: when the PLC's position tracker for the item reaches it, the PLC fires the
printer or the applicator — precise timing with no real-time DB call. **Print station** = where the
label is produced; **apply station** = downstream, where it is pressed onto the carton. They may sit
on different tracking devices.

---

## 4. SourceMode → application DB routing

`Itm_SourceMode2DB.sql` (SHA `961e923c`, lab-2k19-tl22): 1→`SDI_ItmSort_AIM`, 2→`_STR`, 3→`_ORD`,
4→`_RTN`, 5→`_EXT`, 6→`_CTN`. `Process_PA` resolves `@SourceMode` → `@DBName`. Two synonym layers:
`sdisp_TOOL_SynBuilder_PA` builds generic `[SDI_PandA].[dbo].*` synonyms (single-DB runtime);
`sdisp_TOOL_SynBuilder_Panda` builds per-SourceMode `_SM[n]` synonyms pointing at the right
`SDI_ItmSort_*` DB. This is the multi-tenancy seam.

---

## 5. Inferred `sdisp_BP2PA_*` contracts (internals not in repo)

- **`sdisp_BP2PA_Scan_Induct`** — in: `@DbName, @SorterNumber, @SorterMode, @BarcodeID`
  (best-of-6 arbitrated label), `@SeqNum, @LabelStatus`. out: `@PrinterID`, `@PandaID`
  (assignment record ID, feeds GetPrinterFirePoints), `@ErrorCode` (0=ok). Looks up the barcode,
  finds carton/wave assignment, picks the printer for this device+lane, records the assignment.
- **`sdisp_BP2PA_Scan_Verify`** — in: `@DbName, @SorterNumber, @SorterMode, @DeviceID, @SeqNum,
  @Label1` (`@FinalLabel` from pipe-buffer arbitration). Confirms the carton at the verify scanner
  matches the slot assigned at induct.

---

## 6. Message ↔ Core mapping

| PLC message | Code | DeviceID | Core equivalent |
|-------------|------|----------|-----------------|
| PANDA_SCAN_INBOUND (induct) | 281 | =1 | induct scan → lookup, assignment, printer selection |
| PANDA_SCAN_INBOUND (verify) | 281 | >1 | verify scan at downstream scanner |
| PANDA_SCAN_VERIFY | 286 | any | verify scan (pipe-buffer variant) |
| PANDA_PRINT | 282 | — | explicit print trigger |
| PANDA_PRINTER_STATUS | 283 | — | printer health event |
| PANDA_ZONE_STATUS | 284 | — | zone enable/disable |
| PANDA_LATE_ASSIGN | 285 | — | late assignment (destination) |

Current harness implements: **281 induct** and **286 verify** framing. Everything below §7 is not
yet modeled.

---

## 7. Concepts NOT yet in the C# Core (adapter seams / backlog)

🔴 **Fire points / tracking devices** — the whole `PrintPointDevice/PrintPoint/ApplyPointDevice/
ApplyPoint` bundle. The 281 response is fire-point coordination, **not** ZPL dispatch. Highest gap.
🔴 **Print-vs-apply split** — two physically separate fire operations at two encoder positions,
possibly on different tracking devices. Core models print-and-apply as one action.
🔴 **`vPA` vs `vCtn` tag namespaces** — adapter must choose per message type.
🔴 **SourceMode multi-DB routing** — field 2 of every message selects the app context (up to 6).
🟡 **SorterMode 1–4** (normal/aim/alignment/round-robin) — affects lane assignment; pass-through.
🟡 **Weight / Gap / Length** — physical measurements available to assignment; unused in Core.
🟡 **LabelStatus=1 (lowboy)** — may need different print/apply params.
🟡 **SeqNum roll-over at ~2000** — slot lookup must expect reuse (modulo), not treat wrap as error.
🟡 **6-scanner label arbitration (281)** and **pipe-buffer arbitration (286)** — the best-label
scoring lives in the PandA DB; adapter must replicate in C#. The harness currently stands this in
with positional/order-based typing.
🟢 **`Settings_LabelBufferOrder`** — searched exhaustively; **not present** in this repo. Buffer→type
mapping is either in the PandA DB or implicitly positional.

---

## Citation index

| File | SHA | Key facts |
|------|-----|-----------|
| `…/8.0_CreateSP/sdisp_VLC2DB_Msgs_PreProcess.sql` | `9dd130cf` | STX/ETX framing, multi-message buffer, range routing |
| `…/8.0_CreateSP/sdisp_VLC2DB_Msgs_Process_PA.sql` | `80f1ed17` | 281–286 field lists, StrParser_Cur, EXEC signatures, vPA writes |
| `…/8.0_CreateSP/sdisp_PA_Response_FirePoints.sql` | `ebd60d98` | full `vPA.Assign[i].*` set with PrintPoint/ApplyPoint |
| `…/8.0_CreateSP/sdisp_PA_Response_CartonAssign.sql` | `9dcd414c` | `vCtn.Assign[i].*` set for 285 |
| `…/8.0_CreateSP/sdisp_TOOL_SynBuilder_PA.sql` | `79203c77` | canonical PA synonym list → `[SDI_PandA].[dbo].*` |
| `…/8.0_CreateSP/sdisp_TOOL_SynBuilder_Panda.sql` | `44903eb6` | per-SourceMode synonym builder |
| `…/6.0_PopulateTables/Itm_SourceMode2DB.sql` | `961e923c` | SourceMode 1–6 → `SDI_ItmSort_*` |
| `…/4.0_CreateSynonyms/sdisp_BP2PA_Scan_Induct.sql` | `a9f166b9` | synonym → PandA induct |
| `…/4.0_CreateSynonyms/sdisp_BP2PA_Scan_Verify.sql` | `eb285fdf` | synonym → PandA verify |
| `…/4.0_CreateSynonyms/sdisp_TOOL_PA_GetPrinterFirePoints.sql` | `057d2d37` | synonym → fire-point tool |
| `databases/ItmSort/8.2/8.0_CreateSP/sdisp_DB2VLC_Send.sql` | `b84e8b5a` | outbound tag write, DB2VLC_CE[1-6] |
