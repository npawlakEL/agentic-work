# Vision

> **This is the whiteboard in the middle of the office.** Every agent checks this document when they
> have a question about what we're building, why, and how the user wants things done.

## What is this project?

Port **PandA** ("Print and Apply") — a warehouse print-and-apply line controller currently written
**entirely in SQL Server** (35 tables, 31 views, 6 UDFs, 57 synonyms, seed data, and **170 stored
procedures**) — into the **`Element-Logic/econtroller`** platform as an easily loadable
**.NET C# plug-in / library**.

PandA controls the print-and-apply line: a carton is scanned as it inducts onto a sorter, PandA looks
up the carton, picks a label printer, prints label(s), verifies the applied label, and routes the
carton to a pass/fail lane. It integrates with PLC / TCP printers / host (DCMS) systems and exposes an
operator GUI.

## Goals

- **End state:** ALL PandA logic is committed as **native .NET C# code** (no stored procedures) that can
  be **injected into eController easily**.
- The ported logic is consumed as **MFC "actions"** configured via the **MFC action JSON config file**,
  wired to **message points** relating to the print-and-apply line in a given project.
  - (Maps naturally to PandA's existing `sdisp_BP2PA_*` entry procs, which are already triggered at
    line message points, e.g. "induct msg 281".)
- Port **all** functionality eventually — but it does NOT have to be done at once. Phased delivery is
  expected and encouraged.
- **Nothing gets missed:** every source object is tracked and documented (see
  `.project/architecture-log/panda-object-inventory.csv`, all 322 objects).

## Non-Goals

_(To be confirmed with user — candidates:)_
- Preserving T-SQL stored procedures verbatim / SQL-Server-only execution (rejected: end state is C#).
- Porting the test/scratch harness (`sdisp_ScratchPad_*`) as production code (convert to tests instead).
- Site-specific `_CUSTOM_` procs as core (treat as per-project customization).

## Success Criteria

_(Draft — to refine after we scope Phase 1.)_
- PandA functionality is available in eController as C# and wireable via the MFC action JSON config at
  print-and-apply message points.
- A defined vertical slice (Phase 1) runs end-to-end and proves the action/config integration pattern.
- Coverage is tracked against the 322-object inventory so remaining work is always visible.

## User Preferences & Conventions

- **Workflow:** follow the repo agent harness (`.agent/agents.md`) strictly — Orchestrator routes
  everything, Senior Coder auto-engages on code, gates + documentation are enforced, no push without
  explicit approval.
- **Phased is fine:** deliver in steps; keep everything written down so nothing is lost.
- **Communication:** concise.
- **Target stack:** econtroller conventions — .NET 9 / C# 13, EF Core code-first, Blazor Server plugins,
  xUnit v3 tests. Prefer idiomatic econtroller patterns over reproducing SQL structure.

## Integration model (RESOLVED — see architecture-log/002)

PandA logic → C# classes implementing `IMfcAction`; each `sdisp_BP2PA_*` entry proc → a method. Wired to
print-and-apply **message points** via per-project `MfcAction.json` (Mp + TelegramType + TypeName +
MethodName + ServiceValues, sequenced). PandA tables via persistent-tables/EF; TU data via
`trans.ApplyExtension<PandaData>`; outbound (print/PLC/host) via `ITelegramOutbox<T>`. Business logic
(the SP bodies) → C# services; actions stay thin.

## Open objective questions (being resolved with user)

1. DB engine target — SQL Server only, or cross-DB (Postgres/SQLite) like the rest of econtroller?
2. Integration scope now vs later — how much of PLC / TCP-printer / DCMS-host channels in Phase 1?
3. Operator GUI — reimplement `sdisp_GUI_*` screens in Blazor, and when (backend-first?)?
4. Site/config tooling (`SiteBuilder`/`SynBuilder`) — port or replace with econtroller-native config?
5. Phase 1 vertical slice definition.
