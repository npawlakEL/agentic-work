import { describe, expect, it } from 'vitest';
import { SqliteDatabaseAdapter } from './sqlite.js';

const createMapping = (laneId, objectValue) => ({
  laneId,
  objectType: 'ship-to',
  objectValue,
});

describe('SqliteDatabaseAdapter', () => {
  it('returns sorters from config', async () => {
    const adapter = new SqliteDatabaseAdapter({
      filename: ':memory:',
      sorters: [{ id: 'sorter-a', name: 'Sorter A', laneCount: 12 }],
    });

    await expect(adapter.getSorters()).resolves.toEqual([
      { id: 'sorter-a', name: 'Sorter A', laneCount: 12 },
    ]);
  });

  it('persists mappings and increments version', async () => {
    const adapter = new SqliteDatabaseAdapter({
      filename: ':memory:',
      sorters: [{ id: 'sorter-a', name: 'Sorter A', laneCount: 12 }],
    });

    await expect(adapter.getMappings('sorter-a')).resolves.toEqual({
      sorterId: 'sorter-a',
      version: 0,
      mappings: [],
    });

    const saveResult = await adapter.saveMappings('sorter-a', {
      version: 0,
      mappings: [createMapping(1, 'S1'), createMapping(2, 'S2')],
    });

    expect(saveResult).toEqual({ sorterId: 'sorter-a', version: 1 });

    await expect(adapter.getMappings('sorter-a')).resolves.toEqual({
      sorterId: 'sorter-a',
      version: 1,
      mappings: [createMapping(1, 'S1'), createMapping(2, 'S2')],
    });
  });

  it('rejects stale mapping saves with a version conflict', async () => {
    const adapter = new SqliteDatabaseAdapter({
      filename: ':memory:',
      sorters: [{ id: 'sorter-a', name: 'Sorter A', laneCount: 12 }],
    });

    await adapter.saveMappings('sorter-a', {
      version: 0,
      mappings: [createMapping(1, 'S1')],
    });

    await expect(
      adapter.saveMappings('sorter-a', {
        version: 0,
        mappings: [createMapping(2, 'S2')],
      }),
    ).rejects.toMatchObject({
      code: 'VERSION_CONFLICT',
      currentVersion: 1,
    });
  });

  it('clears mappings and increments version on delete', async () => {
    const adapter = new SqliteDatabaseAdapter({
      filename: ':memory:',
      sorters: [{ id: 'sorter-a', name: 'Sorter A', laneCount: 12 }],
    });

    await adapter.saveMappings('sorter-a', {
      version: 0,
      mappings: [createMapping(1, 'S1')],
    });

    await expect(adapter.deleteMappings('sorter-a')).resolves.toEqual({
      sorterId: 'sorter-a',
      version: 2,
    });

    await expect(adapter.getMappings('sorter-a')).resolves.toEqual({
      sorterId: 'sorter-a',
      version: 2,
      mappings: [],
    });
  });
});
