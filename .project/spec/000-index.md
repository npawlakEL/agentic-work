# Full-Port Spec — Master Index

**Status:** Spec lockdown (Phase 1) — 21 features specced across 4 clusters.
**Baseline:** v0.5.0, 231 tests green. Scope: full backend engine EXCLUDING the Blazor
operator GUI / SiteBuilder CRUD and the `PandA.EController` adapter (both deferred).

This directory is the authoritative source-coverage spec for the remaining PandA port.
It was produced by four parallel Senior/research passes over the SQL source
(`panda-src/127.0.0.1_SDI_PandA_20260811`), one per cluster.

## Documents

| Doc | Contents |
|-----|----------|
| [`clusters/events-recovery-logging.md`](clusters/events-recovery-logging.md) | F16 logging, F20 read-quality, F-LOG1 run-history, F15 PLC recovery, F22 reject audit |
| [`clusters/routing-status-control.md`](clusters/routing-status-control.md) | F08 lane routing, LINECTRL shut/slow egress, F13 status ingestion, F12 `~HS` suffix |
| [`clusters/labels-firepoint.md`](clusters/labels-firepoint.md) | F10 exception labels, F11 ZPL sanitizer, DYNAP dynamic apply-point, PROFSW profile switching |
| [`clusters/platform-settings.md`](clusters/platform-settings.md) | SETTINGS, INBOUND advice ingestion, F18/F24 xref+oLPN, F21 slot index, F17 purge, LOCK, F23 wave, F14 MandA |
| [`ownership-map.md`](ownership-map.md) | **File/module ownership** — the disjoint-ownership map that makes parallel Coders safe |
| [`dependency-graph.md`](dependency-graph.md) | Build order, foundation-first sequencing, and parallelizable waves |
| [`open-questions.md`](open-questions.md) | ~40 domain-owner questions surfaced by the spec pass, batched for review |

## Each feature spec includes

- Source file + line references (`SQL:<file>:L<n>`)
- Precise behavior rules
- "Already built?" gap assessment against current C#
- Target module/class layout
- Dependencies
- Acceptance criteria (TDD-ready)
- Open questions for the domain owner

## Cross-cutting contracts (define once, centrally)

- **`CartonStatus`** enum — drives lane routing, exception labels, run-history, GUI display
- **`VerifyReasonCode`** enum — the 20 DCMS reject codes; persisted in RejectHistory, exposed to GUI
- **`ISettingsProvider`** — runtime-mutable operator settings (distinct from static JSON commissioning config)
- **`IPandaEventSink`** / `PandaEvent` / `PandaEventLevel` — structured event logging retrofitted into every service
- **`TransportOrder` field additions** — `WaveId`, `ProfileName`, `Bypass`, `VerifyEnabled`, `VerifyPassDest`, `VerifyFailDest`
