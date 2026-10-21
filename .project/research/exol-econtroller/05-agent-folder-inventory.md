## `.agent` inventory

I reviewed the complete `.agent` tree: `agents.md`, `model-config.md`, all role files, `skills/README.md`, and every skill file. The PandA project should copy the harness structure, then adapt the Exol-specific operational skills listed below.

**Source root:**  
`C:\Users\NoahPawlak\.copilot\session-state\be5b169f-b89b-4f3c-9dee-ff9a3abeaede\files\exol\.agent`

---

### `.agent/agents.md`

**Purpose and rules**

- Defines the gated agent workflow.
- Every request is classified before work begins.
- Planner gathers requirements and produces the specification.
- Senior Coder decides technical approach and feasibility.
- Coder implements using TDD.
- Reviewer validates tests, UI, coverage, and behavior.
- Learner captures reusable knowledge and updates documentation.
- The Orchestrator enforces ordering and does not allow agents to bypass gates.
- Spec gaps discovered during coding or review stop the loop and return to Planner/user.
- User decisions are final and must be logged in `.project/architecture-log/`.
- UI work requires actual browser verification, not merely compilation or component tests.
- Agents must surface recurring patterns as skill/learning candidates.
- The harness requires documentation, review logs, architecture records, and learnings to be treated as project artifacts.

**PandA reuse**

Create the same six-role workflow. Add PandA-specific gates for:

- Printer/applicator state-machine correctness.
- Duplicate print/reprint prevention.
- PLC reconnect and handshake behavior.
- DTC/201 ordering and merge behavior.
- Shared DB/controller isolation.
- Net9 host/package compatibility.
- Operator screen and deployment verification.

**Citation:** `.agent/agents.md`

---

### `.agent/model-config.md`

**Purpose and rules**

Recommended model allocation:

- **Orchestrator:** Opus-tier; classification, enforcement, state tracking.
- **Planner:** Opus-tier; requirements reasoning and proactive questioning.
- **Senior Coder:** Opus-tier, non-negotiable; architecture and tradeoffs.
- **Coder:** Sonnet-tier; mechanical TDD execution.
- **Reviewer:** Sonnet-tier; tests, UI, QA.
- **Learner:** Sonnet-tier or Haiku; documentation and knowledge capture.

The central rule is to spend stronger reasoning models on decisions and lower-cost models on execution/documentation.

**PandA reuse**

Mirror the allocation. Do not downgrade the Senior Coder for:

- PLC/eHub integration decisions.
- DTC contract design.
- Multi-host concurrency.
- Printer idempotency.
- Deployment/runtime compatibility.

**Citation:** `.agent/model-config.md`

---

## `.agent/roles`

### `.agent/roles/coder.agent.md`

**Notes and rules**

- Implements only against the approved Planner specification.
- Strict TDD: Red → Green → Refactor.
- Every feature, endpoint, and component requires tests before production implementation.
- Must follow repository architecture and file structure.
- Must add comments to functions, complex logic, and non-obvious decisions.
- Must build, run, and pass all tests.
- For UI work, must start the server, open the browser, visually inspect every component, exercise all controls, test responsive behavior, and test error states.
- Must surface repeated patterns and non-trivial solutions as skill/learning candidates.
- Coder does not directly write skills; it reports candidates to Orchestrator.

**PandA reuse**

Add a Coder checklist for:

- Print-job lifecycle tests before printer integration code.
- Duplicate/retry tests before retry implementation.
- Fake printer and fake PLC seams.
- DTC extension serialization tests.
- Blazor print/apply/verify screen tests.
- Configuration parity tests across controller folders.

**Citation:** `.agent/roles/coder.agent.md`

---

### `.agent/roles/learner.agent.md`

**Notes and rules**

- Reviews the full planning, coding, and review cycle.
- Extracts successful patterns, pitfalls, guardrails, and recurring issues.
- Reads reviewer logs and taskboard outcomes.
- Updates architecture knowledge when architecture changes.
- Produces two documentation outputs:
  - Technical documentation in `.client-docs/technical/`.
  - Operator documentation in `.client-docs/operator/`.
- Always writes at least one `.project/learnings/` entry.
- Updates `CHANGELOG.md` every cycle with semantic versioning.
- Surfaces skill candidates to Orchestrator.
- Documentation, reviewer logs, learnings, and changelog changes must be committed.

**PandA reuse**

Every PandA feature cycle should produce:

- Technical printer/PLC/DTC architecture documentation.
- Operator documentation for configuring printers, labels, retries, rejects, and verification.
- At least one learning entry.
- Changelog entry.
- Taskboard retrospective comparing planned versus delivered scope.

**Citation:** `.agent/roles/learner.agent.md`

---

### `.agent/roles/orchestrator.agent.md`

**Notes and rules**

- Enforcement authority for the whole harness.
- Classifies every request and announces the classification.
- Prevents silent work and passive “got it” responses.
- Ensures Planner asks questions and does not skip requirements discovery.
- Enforces Planner → Coder → Senior Coder → Reviewer → Learner sequencing.
- Requires documentation; “document later” is not accepted.
- Ensures agents surface skills and learnings continuously.
- Prevents duplicate project folders and misplaced artifacts.
- Requires user approval before pushing or merging.
- Logs architecture decisions, resolved questions, and postmortems.
- Does not permit speed requests to skip mandatory gates.

**PandA reuse**

Make the Orchestrator enforce these PandA-specific stop conditions:

- No implementation until print/apply/verify ownership boundaries are decided.
- No PLC integration until telegram/handshake behavior is specified.
- No deployment until host TFM and eplugin contents are verified.
- No multi-host screen changes until controller/schema/config-folder behavior is explicit.
- No release until duplicate/retry/reprint semantics are tested.

**Citation:** `.agent/roles/orchestrator.agent.md`

---

### `.agent/roles/planner.agent.md`

**Notes and rules**

- Planner never writes production code.
- First requires a vision document under `.project/vision/`.
- Proactively asks clarifying questions after every user input.
- Must probe data, behavior, UI, persistence, failure handling, concurrency, integration, constraints, success criteria, and non-goals.
- Can enter “Grill Me” mode for broad, batched requirements interrogation.
- Grill Me covers purpose, users, data, behavior, edge cases, UX, constraints, integration, success, and non-goals.
- Regroup mode combines Planner and Senior Coder perspectives.
- Maintains `.project/planner-tasks.md` with:
  - Done
  - Open
  - Questions
  - Bookmarked
  - Needs Elaboration
- Only the user can approve the specification.
- Automatically consults Senior Coder on technical HOW questions.
- Sends business/WHAT questions to the user.
- All open questions must be resolved before Coder handoff.

**PandA reuse**

Planner must explicitly ask:

- Who owns print job identity?
- What happens after printer timeout?
- Is reprint allowed automatically, manually, or never?
- Is apply confirmation PLC-, sensor-, scanner-, or MFC-owned?
- What does “verify” mean and what evidence is persisted?
- What happens if the DB is unavailable?
- Can two hosts issue the same print/apply command?
- How are printer configurations scoped per controller?
- Which screens are operator versus administrator screens?
- What is the rollback behavior for label-template/config deployment?

**Citation:** `.agent/roles/planner.agent.md`

---

### `.agent/roles/reviewer.agent.md`

**Notes and rules**

- QA stand-in and debugger.
- Runs the full suite and checks meaningful coverage.
- Validates requirements, functionality, edge cases, and TDD compliance.
- Iterates with Coder until clean.
- Must create/update a reviewer-log entry on every review round.
- Every issue records severity, description, expected fix, and later resolution.
- UI reviews must open a browser and exercise every control, navigation path, layout, data state, and error state.
- A passing test suite is not sufficient for UI sign-off.
- Reviewer surfaces recurring bug patterns and testing techniques as candidates.
- Final sign-off requires no orphaned reviewer-log entries.

**PandA reuse**

Reviewer logs should include:

- Duplicate print scenario.
- Printer unavailable.
- Printer returns success but PLC never acknowledges.
- PLC resend after timeout.
- Apply sensor mismatch.
- Verify no-read.
- Operator retries.
- Concurrent controller edits.
- Wrong config-folder selection.
- Host reload/cache behavior.
- Browser UI error and empty states.

**Citation:** `.agent/roles/reviewer.agent.md`

---

### `.agent/roles/senior-coder.agent.md`

**Notes and rules**

- Architectural authority and implementation overseer.
- Consulted automatically by Planner for technical questions.
- Signs off technical feasibility before coding.
- Produces task breakdowns in `.project/taskboard/`.
- Performs implementation spot checks and scope-creep checks.
- Ensures Coder follows approved architecture.
- Reviews Coder before Reviewer begins.
- Triages Reviewer findings into architectural versus implementation issues.
- Maintains architecture logs and current architecture.
- Continuously surfaces skill candidates.
- Does not write production code.
- Must verify code comments.
- Must log every architectural decision.
- Must read the architecture baseline at the start of every engagement.

**Mandatory exhaustiveness gate**

For config/data-driven code, the Senior Coder must:

1. Enumerate the full source-of-truth domain.
2. Prove the classification is total and disjoint.
3. Add a static drift test that fails when the source grows.
4. Check sibling dimensions such as other telegram types, hooks, directions, or statuses.
5. Reject point fixes that cover only the reported example.

**PandA reuse**

Apply the exhaustiveness gate to:

- Every PandA PLC message point.
- Every label/print/apply/verify state.
- Every printer error/result code.
- Every configured line/controller/printer.
- Every destination lane.
- Every DTC extension type.
- Every screen authorization role.
- Every deployment environment.

**Citation:** `.agent/roles/senior-coder.agent.md`

---

# `.agent/skills`

### `.agent/skills/README.md`

**Notes and rules**

- Skills are reusable task patterns.
- Every skill should contain:
  - Name.
  - Trigger.
  - Steps.
  - Notes.
- Skills are classified as:
  - Project-specific.
  - Universal/upstream.
- Only Orchestrator writes final skill files.
- Planner, Coder, Senior Coder, Reviewer, and Learner surface candidates.
- Universal skills use `<!-- UPSTREAM: true -->` and are propagated to the shared harness.

**PandA reuse**

Create the same skill format. Keep PandA-specific skills separate from universal workflow skills.

**Citation:** `.agent/skills/README.md`

---

### `.agent/skills/bluepaw-201-telegram-mp-decoder.md`

**Notes and rules**

- Decodes ADS/BluePaw `201`, `203`, `204`, `205`, and `1201` telegrams.
- `Label1` is the LPN and becomes `TuId`.
- MP is derived, not read directly from the wire.
- CP1 offset is `100`; CP2 offset is `200`.
- 201 uses the 1000 prefix; 203 uses 2000; 204 uses 3000.
- 205 lane values are handled specially.
- Outbound 1201 reverses the destination calculation.
- Route explanation requires walking `PlaceAliases.json`, `MfcAction.json`, and `CriteriaConfig.json`.
- Handshake tags are `Ready`, `Ack`, `Sending`, and `Pending`.
- `Sending` can contain concatenated messages.
- Notifications are edge-triggered.
- Wrong offsets silently route messages to the wrong MP.

**PandA adaptation**

Create `panda-telegram-decoder.md` covering:

- PandA scan/print/apply/verify telegrams.
- Printer/job ID extraction.
- Controller/line offsets.
- Outbound acknowledgement/assignment messages.
- Handshake and batching.
- Route explanation through PandA action/config files.

**Citation:** `.agent/skills/bluepaw-201-telegram-mp-decoder.md`

---

### `.agent/skills/commit-and-push.md`

**Notes and rules**

- Planning/setup commits may be pushed immediately.
- During Coder ↔ Reviewer, commits remain local.
- Exactly one final push occurs after the full cycle.
- Explicit user approval is required before push.
- Never describe work as pushed if it is only committed.
- Verify tests and Reviewer sign-off before requesting push.
- Verify push success afterward.

**PandA reuse**

Use identical branch/push discipline. Particularly important for production deploy packages and configuration changes.

**Citation:** `.agent/skills/commit-and-push.md`

---

### `.agent/skills/exol-config-only-connector-deploy.md`

**Notes and rules**

- Used for JSON/config-only eHub fixes.
- Repository config mirror is the source of truth.
- Deployment package must include exact payload, installer, and one-screen README.
- Installer validates intended changes and all previous fixes.
- Refuses while the target host is running.
- Targets the exact environment folder.
- Backs up before replacement.
- Replaces atomically.
- New packages must start from the last deployed JSON, not stale repository state.
- User runs the installer; agent never does.

**PandA adaptation**

Port as `panda-config-only-deploy.md` for:

- Printer endpoint/config.
- Label-template settings.
- PLC tag names.
- Timeout/retry settings.
- Verify/apply thresholds.
- Controller-specific settings.

Add assertions that all previously deployed printer/PLC settings remain present.

**Citation:** `.agent/skills/exol-config-only-connector-deploy.md`

---

### `.agent/skills/exol-ehub-connector-nupkg-deploy.md`

**Notes and rules**

- Used for connector code/DLL changes.
- eHub loads the highest package version.
- Deploy additively; never overwrite existing packages.
- Target the exact environment and connector.
- Build from committed state.
- Record package hash, leaf DLL hash, and DTC dependency fingerprint.
- Verify script is read-only and must run before deployment.
- Deployment checks hashes, ownership, host state, and process locks.
- Clears only the target connector’s extracted Content directory.
- Supports rollback by removing only the new package.
- Directly reference contract packages to prevent transitive version shadowing.

**PandA adaptation**

Port as `panda-ehub-nupkg-deploy.md`, including:

- Printer connector package.
- PandA DTC contract package.
- Package/leaf-DLL hash verification.
- Host environment selection.
- Additive versioning.
- Printer connector ownership guard.
- Rollback and extracted-content verification.

**Citation:** `.agent/skills/exol-ehub-connector-nupkg-deploy.md`

---

### `.agent/skills/exol-eplugin-dll-swap.md`

**Notes and rules**

- Applies when prebuilt MFC/Screens DLLs must be inserted into existing eplugins.
- Host re-extracts DLLs from the packaged `@light` eplugin on startup.
- Copying directly into Content is temporary and silently reverted.
- Patch exact package entries, including PDB when present.
- Never touch `@deps`.
- Probe package and extracted hashes first.
- Use `-WhatIfDeploy`.
- Back up packages.
- Clear extracted DLL/cache.
- Verify the new DLL survives restart.
- MFC host is net9; tests may be net10.

**PandA adaptation**

Port as `panda-eplugin-dll-swap.md` for PandA MFC/UI plugins. Add printer-driver/native dependency checks if PandA includes printer libraries.

**Citation:** `.agent/skills/exol-eplugin-dll-swap.md`

---

### `.agent/skills/exol-mfc-autocmc-roundrobin.md`

**Notes and rules**

- Documents custom lane load balancing.
- Exception/no-read short-circuits to reject.
- Normal cartons round-robin over configured lanes.
- Candidate lanes are dynamically discovered from `CriteriaConfig`.
- Lane status is intentionally not consulted for this specific MP.
- Winner is ordered by `LastDiverted`, then PlaceID.
- Selection updates `LastDiverted` and `DivertCount`.
- Startup seeds `LocationStatus`.
- Generic criteria processing was bypassed because of unsuitable behavior.
- Configuration must remain byte-identical across Publish and Sim.
- On-box config may differ in incidental fields; clone the live sibling row rather than injecting repository-specific fields.
- Tests cover alternation, disabled/full lanes, and dynamic configuration.

**PandA adaptation**

Reuse the dynamic candidate-discovery and deterministic selection pattern if PandA supports:

- Multiple printers.
- Multiple applicators.
- Multiple verification stations.
- Load balancing by last-used timestamp.
- Per-line printer pools.

Do not blindly reuse the “ignore lane status” behavior; PandA printer availability should be an explicit product decision.

**Citation:** `.agent/skills/exol-mfc-autocmc-roundrobin.md`

---

### `.agent/skills/exol-mfc-lane-status.md`

**Notes and rules**

- PLC 205 status is the single source of truth for lane divertability.
- `status == 0` means enabled/divertable.
- Any non-zero status means blocked; non-zero is not merely “full.”
- Do not add a second operator/UI `IsEnabled` axis.
- Store raw PLC status.
- A zero 205 re-enables the lane.
- Unknown rows fail open.
- Duplicate rows fail closed if any duplicate is blocked.
- Persistence uses a dynamic JSON key-value blob, so no DDL migration is required.
- Auto-CMC has a known pre-existing possible infinite recirculation gap.

**PandA adaptation**

Define a similar single source of truth for printer/applicator availability:

- Decide whether PLC status, printer status, or a combined controller code owns availability.
- Do not create contradictory UI and PLC availability flags without an explicit model.
- Preserve raw status/error codes.
- Test unknown, blocked, reset, and duplicate state behavior.

**Citation:** `.agent/skills/exol-mfc-lane-status.md`

---

### `.agent/skills/exol-mfc-retention.md`

**Notes and rules**

- Covers retention and `ChangeDate` handling.
- Vendor MFC entities cannot be modified with clean EF interceptors.
- `CreationDate` is create-only.
- `ChangeDate` must be advanced at every application edit site.
- Transport-order identity can be reused; retention keys on status plus age, not only `TuId`.
- CP shells and physical orders share the same table.
- Effective age is `max(ChangeDate, CreationDate)`.
- Purge uses shared select/delete predicates to close races.
- Separate CP-shell and default TTL buckets.
- Purge is batched and throttled.
- Window and interval are independent.
- `controllers.json` enables/configures; sidecar hot-reloads per-field values.
- Missing/malformed/invalid sidecar values fail safe.
- Real SQLite is required for delete tests.
- Fresh-context reload is required to prove persistence.
- Cross-layer UI-writer/device-reader verification is currently incomplete.
- Known parallel-sensitive tests should be isolated before diagnosing regressions.

**PandA adaptation**

Port if PandA creates persistent print/apply/verify events or transient transport records. Add retention categories for:

- Successful print/apply records.
- Failed verification records.
- Reprint/audit records.
- Operator intervention records.
- Durable compliance/audit shells.

**Citation:** `.agent/skills/exol-mfc-retention.md`

---

### `.agent/skills/exol-mfc-screen-bunit-testing.md`

**Notes and rules**

- Provides bUnit setup for Blazor screens inheriting the host `FrameworkComponent`.
- Requires MudBlazor services.
- Requires substitutes for host messenger services.
- Uses loose JS interop.
- Avoids `Bunit.TestContext` versus xUnit `TestContext` ambiguity with an alias.
- Shows how to drive MudBlazor checkbox and button interactions.
- Uses NSubstitute to capture service calls and inject failures.
- Does not prove real browser behavior or live authorization/nav behavior.
- Recommends separate policy-pipeline tests.
- Warns against reflecting host net9 assemblies from standalone net10 scripts.

**PandA adaptation**

Port as `panda-screen-bunit-testing.md` and add fixtures for:

- Printer selection.
- Label-template editing.
- Print/reprint controls.
- Apply/verify status banners.
- Error and offline states.
- Controller selection.
- Unsaved-change guards.
- Authorization policies.

Retain browser verification as a separate mandatory review step.

**Citation:** `.agent/skills/exol-mfc-screen-bunit-testing.md`

---

### `.agent/skills/exol-parallel-feed-isolation.md`

**Notes and rules**

- Used when enriching a feed consumed by an untouchable external system.
- Never mutate the shared external feed.
- Add a separate endpoint, topic, transform, and export row.
- Keep the original endpoint/topic/transform byte-identical.
- Serialize the internal contract rather than sharing CLR types across version boundaries.
- Add byte-shape tests for unchanged output.
- Add endpoint/topic isolation tests.
- Emit enrichment only when present.
- Verify the correct operational path rather than resending old exports.
- Deployment is additive and independently removable.
- Branch state must be checked against `origin/develop`.

**PandA adaptation**

Use this if PandA needs richer print/apply metadata while preserving an existing WMS/ELWS feed:

- Add a PandA-specific endpoint/topic.
- Keep existing WMS output unchanged.
- Add a separate transform or adapter.
- Use serialized JSON as the internal PandA contract.
- Test byte-level preservation of the existing feed.

**Citation:** `.agent/skills/exol-parallel-feed-isolation.md`

---

### `.agent/skills/exol-plugin-deploy-host-config-guards.md`

**Notes and rules**

- Host detection must target the exact host, not the entire runtime root.
- Verify and Deploy must use byte-identical process predicates.
- MFC detection uses MFC-specific command-line/name patterns.
- eHub detection must match the intended `--environment`.
- Foreign environments must be listed but left untouched.
- Config verification must use fields the runtime actually resolves.
- Do not require incidental fields such as `PlcName` if the runtime ignores them.
- Clone a live sibling config row and change only the discriminating value.
- Test guards against fake process/config trees.
- Verify and Deploy should agree exactly.

**PandA adaptation**

Port as a general `panda-plugin-deploy-host-config-guards.md` and make these PandA release gates:

- Exact host/environment matching.
- Exact printer connector ownership.
- Exact controller folder/schema.
- No assumptions about optional fields.
- Clone live configuration conventions.
- Test no-target-running, sibling-host-running, blank/incidental config fields, and rollback.

**Citation:** `.agent/skills/exol-plugin-deploy-host-config-guards.md`

---

### `.agent/skills/exol-plugin-deploy.md`

**Notes and rules**

This is the general Exol plugin deployment guide. Its golden rules include:

- Identify the actual target host and environment.
- Do not touch sibling hosts or plugins.
- Verify current installed state before mutation.
- Use additive/versioned deployment where supported.
- Make deploy scripts self-validating.
- Refuse unsafe host/process states.
- Preserve rollback copies.
- Clear/rebuild extracted content when the host regenerates packages.
- Use the live box’s configuration conventions rather than blindly applying repository defaults.
- Clone existing rows for config patches.
- Verify both before and after deployment.
- Ensure Verify and Deploy predicates are identical.
- Never execute production deploy scripts as the agent.

**PandA adaptation**

This should become the base PandA deployment skill from which printer connector, MFC plugin, screen plugin, and config-only deployment skills inherit.

**Citation:** `.agent/skills/exol-plugin-deploy.md`

---

### `.agent/skills/exol-screens-eplugin-deploy.md`

**Notes and rules**

- Screens deploy as two eplugins: `@light` and `@deps`.
- The host is net9 even when tests are net10.
- `@light` contains the actual Screens DLL.
- `@deps` contains dependencies.
- Missing `@light` can make the tab silently absent.
- Missing `@deps` can prevent plugin discovery.
- Build requires `ProjectNetFramework=net9.0`.
- `ePluginPkgLight=true` and `ePluginPkgDependencies=true` are both required.
- The output-folder trailing slash is mandatory.
- Verify package hashes before deployment.
- Clear the unpacked Content cache.
- Restart the host and hard-refresh the browser.
- Prefer inline CSS over host-version-sensitive MudBlazor enum layout props.
- Verify the tab appears and renders after deployment.

**PandA adaptation**

Port as `panda-screens-eplugin-deploy.md` and add:

- Printer/applicator configuration screen checks.
- Label preview checks.
- Controller-selection checks.
- Verify/reprint authorization checks.
- Browser hard-refresh and stale cache handling.
- Net9 package assertion.

**Citation:** `.agent/skills/exol-screens-eplugin-deploy.md`

---

### `.agent/skills/exol-site-sql-scripts.md`

**Notes and rules**

- Every SQL object must be schema-qualified.
- Resolve unknown dynamic-table schemas through `sys.tables`/`sys.schemas`.
- Fail fast on query errors in loops.
- RDS clock is UTC; site-local log timestamps require measured conversion.
- Do not rely on `VIEW SERVER STATE`.
- Use light timed probe queries.
- Shared RDS has `READ_COMMITTED_SNAPSHOT` disabled; readers can block behind writers.
- Every SQL handed to an operator must also be persisted in the investigation/test pack.

**PandA adaptation**

Port as `panda-site-sql-scripts.md` and add:

- Printer/job tables.
- Print failure and reprint diagnostics.
- Controller/schema qualification.
- Lock/wait probes for shared print-job updates.
- UTC/local timestamp handling.
- Safe, low-load production probes.

**Citation:** `.agent/skills/exol-site-sql-scripts.md`

---

### `.agent/skills/harness-doc-merge.md`

**Classification:** Universal skill (`<!-- UPSTREAM: true -->`).

**Notes and rules**

- Applies when merging or rebasing branches with divergent harness documentation.
- Read both sides in full.
- Classify changes as disjoint, same-section, or superseding.
- Intuitively combine workstreams instead of selecting one side or blindly concatenating conflict hunks.
- Preserve all distinct decisions, bookmarks, citations, and context.
- Keep one coherent umbrella document.
- Remove all conflict markers.
- Sanity-read the merged document.
- Commit the resolved documentation.

**PandA reuse**

Copy unchanged. PandA will likely have parallel workstreams for:

- PLC integration.
- Printer integration.
- Screens.
- DTC.
- Deployment.
- Multi-host configuration.

**Citation:** `.agent/skills/harness-doc-merge.md`

---

### `.agent/skills/multi-ask-processing.md`

**Classification:** Universal skill (`<!-- UPSTREAM: true -->`).

**Notes and rules**

Triggered when:

- The user repeats a request.
- The user corrects or overrides an answer.
- A step is rerun because the previous artifact was wrong.
- A guard produces a false positive or false negative.
- The user signals lost trust or asks why something happened.

Required behavior:

1. Stop and identify the faulty assumption.
2. Classify the defect as behavioral, factual/environmental, process, or code/config.
3. Put the durable fix where it will be consulted next time.
4. Make Verify and Deploy predicates agree.
5. State the durable correction explicitly.

The skill rejects surface-only fixes, hardcoded examples, broad guards, and learnings that remain only in chat.

**PandA reuse**

Copy unchanged. It is especially important for:

- Printer behavior differing from simulation.
- Environment-specific config schemas.
- Host-detection false positives.
- Label-template differences.
- PLC status assumptions.
- Runtime/package version mismatches.

**Citation:** `.agent/skills/multi-ask-processing.md`

---

### `.agent/skills/open-deploy-package.md`

**Notes and rules**

- Used when a deployment artifact has been assembled and is ready for delivery.
- Opens the package location in Windows File Explorer.
- Delivery must include both:
  - A clickable `file:///` link.
  - A plain filesystem path.
- For zip delivery, Explorer should select the zip in its parent folder.
- For folder-only delivery, Explorer opens the folder directly.
- This applies to every delivery, including recuts or appended artifacts.

**PandA reuse**

Copy unchanged if PandA tooling delivers Windows deployment packages. Use it for:

- PandA eplugins.
- Printer connector packages.
- Config-only packages.
- SQL/test packs.
- Rollback packages.

**Citation:** `.agent/skills/open-deploy-package.md`

---

### `.agent/skills/senior-coder-checklist.md`

**Notes and rules**

Before feasibility sign-off:

- Read current architecture.
- Check existing patterns.
- Check branch/dependency conflicts.
- Choose the least-complex viable design.
- Identify touched files and blast radius.

During spot checks:

- Check established patterns.
- Reject unnecessary abstractions.
- Check error handling.
- Reject hardcoded values that should be configurable.
- Require meaningful tests.
- Require comments.

Before final sign-off:

- Verify every taskboard acceptance criterion.
- Check architectural drift.
- Check efficiency and lack of over-engineering.
- Remove dead code and untracked TODOs.
- Check integration boundaries.

The exhaustiveness gate requires full-domain enumeration, total/disjoint classification, source-of-truth drift tests, sibling-dimension checks, and regression rather than point-fix coverage.

The checklist records the 2026-08-13 config-domain incident as a permanent lesson.

**PandA reuse**

Copy and extend with explicit checks for:

- Printer state-machine completeness.
- Every PLC telegram/status code.
- Every printer response/error code.
- Every configured controller/line/printer.
- Every label-template variant.
- Every user role and authorization path.
- Every retry/reprint branch.
- Every deploy artifact and rollback path.

**Citation:** `.agent/skills/senior-coder-checklist.md`

---

## Skills to port directly versus adapt

### Copy unchanged

These are generally universal:

- `harness-doc-merge.md`
- `multi-ask-processing.md`
- `commit-and-push.md`
- `open-deploy-package.md`
- `skills/README.md`

### Copy and adapt for PandA

- `bluepaw-201-telegram-mp-decoder.md` → `panda-telegram-decoder.md`
- `exol-config-only-connector-deploy.md` → PandA config deploy
- `exol-ehub-connector-nupkg-deploy.md` → PandA connector package deploy
- `exol-eplugin-dll-swap.md` → PandA MFC/plugin swap
- `exol-plugin-deploy.md` → PandA deployment base
- `exol-plugin-deploy-host-config-guards.md` → PandA host/config guard rules
- `exol-screens-eplugin-deploy.md` → PandA Screens deployment
- `exol-mfc-screen-bunit-testing.md` → PandA UI tests
- `exol-site-sql-scripts.md` → PandA production SQL probes
- `exol-mfc-retention.md` → PandA retention if print/audit records are persisted
- `exol-parallel-feed-isolation.md` → PandA feed enrichment if existing WMS/ELWS output must remain unchanged
- `exol-mfc-lane-status.md` → PandA PLC/printer availability model
- `exol-mfc-autocmc-roundrobin.md` → PandA printer/applicator pool balancing, only if required
- `senior-coder-checklist.md` → extend with PandA completeness gates

### Do not copy as-is

The following contain Exol-specific domain assumptions and should be mined for patterns rather than copied literally:

- AutoCMC lane IDs and routing.
- CP1/CP2 offsets.
- BluePaw field positions.
- MFC-specific retention statuses.
- Exol service names and deployment paths.
- Exol DTC DLL byte lengths.
- AutoCMC-specific fail-open/round-robin rules.
- `LaneStatus` semantics unless PandA explicitly uses the same PLC 205 contract.

---

## PandA `.agent` folder recommended initial layout

```text
.agent/
├── agents.md
├── model-config.md
├── roles/
│   ├── coder.agent.md
│   ├── learner.agent.md
│   ├── orchestrator.agent.md
│   ├── planner.agent.md
│   ├── reviewer.agent.md
│   └── senior-coder.agent.md
└── skills/
    ├── README.md
    ├── commit-and-push.md
    ├── harness-doc-merge.md
    ├── multi-ask-processing.md
    ├── open-deploy-package.md
    ├── senior-coder-checklist.md
    ├── panda-telegram-decoder.md
    ├── panda-print-job-idempotency.md
    ├── panda-printer-connector-deploy.md
    ├── panda-config-only-deploy.md
    ├── panda-eplugin-deploy.md
    ├── panda-screens-bunit-testing.md
    ├── panda-screens-eplugin-deploy.md
    ├── panda-host-config-guards.md
    ├── panda-shared-db-concurrency.md
    ├── panda-site-sql-scripts.md
    └── panda-parallel-feed-isolation.md
```

The highest-value new PandA skill absent from Exol is **printer/job idempotency**: define a stable job key, persist lifecycle state, suppress duplicate physical prints after retries, distinguish safe retry from manual intervention, and test PLC resend, host retry, printer acknowledgement loss, and operator reprint independently.
