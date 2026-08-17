#!/usr/bin/env node
/*
 * Harness integrity check — no dependencies, cross-platform.
 *
 * Verifies the structural invariants the Boot integrity step (and Nightwatch)
 * rely on, instead of checking them by hand:
 *   1. Mode parity   — every mode with a section in agents.md is referenced in
 *                       orchestrator.agent.md (routing/orchestration).
 *   2. Skill frontmatter — every skill (not README) has name/description/
 *                       load_when/upstream, and name matches its filename.
 *   3. Path existence — every `.agent/…`, `.project/…`, `.client-docs/…` path
 *                       referenced in the docs resolves on disk.
 *   4. Constraint numbering — the Constraints list in agents.md is contiguous.
 *
 * Usage:  node .agent/tools/harness-check.mjs
 * Exit:   0 = all good, 1 = one or more gaps (details printed).
 */
import { readFileSync, existsSync, readdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const rd = (p) => readFileSync(join(ROOT, p), 'utf8');
const gaps = [];
const fail = (cat, msg) => gaps.push(`[${cat}] ${msg}`);

// ---------- 1. Mode parity ----------
const agents = rd('.agent/agents.md');
const orch = rd('.agent/roles/orchestrator.agent.md');
const modeNames = [...agents.matchAll(/^##.*"([A-Za-z][\w-]*)"\s+Mode/gm)].map((m) => m[1]);
for (const mode of [...new Set(modeNames)]) {
  if (!new RegExp(mode, 'i').test(orch)) {
    fail('mode-parity', `"${mode}" mode has a section in agents.md but is not referenced in orchestrator.agent.md`);
  }
}

// ---------- 2. Skill frontmatter ----------
const REQUIRED = ['name', 'description', 'load_when', 'upstream'];
for (const file of readdirSync(join(ROOT, '.agent/skills')).filter((f) => f.endsWith('.md') && f !== 'README.md')) {
  const body = rd(join('.agent/skills', file));
  const fm = body.match(/^---\r?\n([\s\S]*?)\r?\n---/);
  if (!fm) { fail('frontmatter', `${file}: missing YAML frontmatter block`); continue; }
  const block = fm[1];
  for (const key of REQUIRED) {
    if (!new RegExp(`^${key}:`, 'm').test(block)) fail('frontmatter', `${file}: missing "${key}"`);
  }
  const nameMatch = block.match(/^name:\s*(.+)$/m);
  const name = nameMatch ? nameMatch[1].trim() : '';
  if (name && name !== file.replace(/\.md$/, '')) {
    fail('frontmatter', `${file}: name "${name}" does not match filename`);
  }
}

// ---------- 3. Path existence ----------
// CHANGELOG is excluded: it documents historical states (old paths pre-refactor).
const docs = ['.agent/agents.md', 'README.md']
  .concat(readdirSync(join(ROOT, '.agent/roles')).map((f) => join('.agent/roles', f)))
  .concat(readdirSync(join(ROOT, '.agent/skills')).map((f) => join('.agent/skills', f)));
const checked = new Set();
for (const doc of docs) {
  const text = rd(doc);
  for (const m of text.matchAll(/`(\.(?:agent|project|client-docs)\/[^`]+)`/g)) {
    if (m[1].includes('*')) continue; // skip glob patterns, not literal paths
    // Check the first two path segments (folder or top-level file); deeper files
    // may be created later in a cycle, so we only assert the container exists.
    const parts = m[1].replace(/\/$/, '').split('/');
    const probe = parts.slice(0, 2).join('/');
    if (checked.has(probe)) continue;
    checked.add(probe);
    if (!existsSync(join(ROOT, probe))) fail('path', `referenced path does not exist: ${probe}  (from ${doc})`);
  }
}

// ---------- 4. Constraint numbering ----------
const cSection = agents.split(/^## Constraints & Guardrails/m)[1]?.split(/^## /m)[0] ?? '';
const nums = [...cSection.matchAll(/^(\d+)\.\s+\*\*/gm)].map((m) => Number(m[1]));
nums.forEach((n, i) => {
  if (n !== i + 1) fail('constraints', `numbering breaks at #${n} (expected #${i + 1})`);
});

// ---------- Report ----------
if (gaps.length === 0) {
  console.log(`✅ harness integrity OK — ${new Set(modeNames).size} modes, ${checked.size} paths, ${nums.length} constraints`);
  process.exit(0);
}
console.error(`❌ harness integrity: ${gaps.length} gap(s)`);
for (const g of gaps) console.error('  - ' + g);
process.exit(1);
