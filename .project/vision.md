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

## Integration model (RESOLVED — see architecture-log/002 + decision-002)

PandA logic → C# classes implementing `IMfcAction`; entry points → methods wired to print-and-apply
**message points** via per-project `MfcAction.json`. **Architecture = ports-and-adapters** (decision-002):
- **`PandA.Core`** — pure domain + services + config model, zero econtroller dependency (compiles/tests here).
- **`PandA.Sim`** — simulated backend adapter for standalone end-to-end validation.
- **`PandA.EController`** — thin adapter (actions, config loader, outbox wiring, TU-extension mapping),
  written against real interfaces, compiled at integration.
- Carton = **`MfcTransportOrder`**; `TuId` = **blind label**; per-carton data via **TU extensions**
  (persisted through `DynamicField`). Config = **JSON files** via EffortlessConfiguration (NOT a config DB);
  DB only for runtime host data. Outbound via an `IPrinterGateway` port → `ITelegramOutbox` at integration.
  Actions stay thin; SP bodies → C# services.

## Phase 1 (LOCKED) — proof-of-pattern vertical slice (see decision-002 + spec.md)

**Scope:** **advice → induct → pick printer → send ZPL**, backend-only. The host provides ready ZPL, so
Phase 1 is a **pass-through** (label-template→ZPL engine is out of scope for now).
- **MP1 label-advice (host inbound):** blind label + typed ZPL set → creates TO shell (`TuId`=blind label,
  status `ADVISED`) + stores a `PandaLabelSet` extension (`[{ LabelType, Lpn, Zpl }]`).
- **MP2 induct scan:** blind label → match TO by `TuId` → pick printer(s) by matching each label's
  `LabelType` to the printer `LabelMap` → emit ZPL via `IPrinterGateway`. Selection is **load-balanced per
  label type** (least-recently-printed) with **same-carton collision→backup routing** so a multi-label
  carton spreads across distinct printers — the authoritative algorithm (incl. spare/min pool rules) is
  **architecture-log 005**.
- **Done bar:** in the Sim, advice + induct yields correct ZPL to the correctly-matched printer(s),
  proven by unit + integration tests (TDD).

**Config for Phase 1:** `PandaLine.json` (per-line: printers + LabelMap + load-balance) is what Phase 1
needs; labeling/profile config deferred (host sends ZPL).

**Bookmarked / deferred:** Wave/WaveRange + wave lifecycle; operator GUI; label-template→ZPL engine;
verify + lane routing; real TCP/PLC/host connectors; SiteBuilder UI.
**Architectural replacement (not ported as data):** SQL eventing plumbing → native logging/eventing;
synonyms/SynBuilder → transport/connector config.

## Status

Discovery + design pressure-testing COMPLETE (decision-002 accepted). Next: Planner Phase-1 spec (`spec.md`)
→ user approval (Gate 1) → TDD implementation of `PandA.Core` + `PandA.Sim`.
