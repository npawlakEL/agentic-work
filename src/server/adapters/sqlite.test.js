import { describe, expect, it } from 'vitest';
import { SqliteDatabaseAdapter } from './sqlite.js';

const createMapping = (laneId, objectValue) => ({
  laneId,
  objectType: 'ship-to',
  objectValue,
});

const objectTypes = [
  {
    id: 'ship-to',
    name: 'Ship To',
    values: [
      { id: 'S1', label: 'S1' },
      { id: 'S2', label: 'S2' },
      { id: 'S3', label: 'S3' },
      { id: 'S4', label: 'S4' },
    ],
  },
];

describe('SqliteDatabaseAdapter', () => {
  it('returns sorters from config', async () => {
    const adapter = new SqliteDatabaseAdapter({
      filename: ':memory:',
      sorters: [{ id: 'sorter-a', name: 'Sorter A', laneCount: 12 }],
      objectTypes,
    });

    await expect(adapter.getSorters()).resolves.toEqual([
      { id: 'sorter-a', name: 'Sorter A', laneCount: 12 },
    ]);
  });

  it('persists mappings and increments version', async () => {
    const adapter = new SqliteDatabaseAdapter({
      filename: ':memory:',
      sorters: [{ id: 'sorter-a', name: 'Sorter A', laneCount: 12 }],
      objectTypes,
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
      objectTypes,
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
      objectTypes,
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

  it.each([
    {
      name: 'lane outside sorter range',
      mapping: { laneId: 99, objectType: 'ship-to', objectValue: 'S1' },
      message: 'Invalid laneId for sorter sorter-a: 99',
    },
    {
      name: 'unknown object type',
      mapping: { laneId: 1, objectType: 'unknown', objectValue: 'S1' },
      message: 'Invalid objectType: unknown',
    },
    {
      name: 'unknown object value',
      mapping: { laneId: 1, objectType: 'ship-to', objectValue: 'BAD' },
      message: 'Invalid objectValue for ship-to: BAD',
    },
  ])('rejects invalid mappings for $name', async ({ mapping, message }) => {
    const adapter = new SqliteDatabaseAdapter({
      filename: ':memory:',
      sorters: [{ id: 'sorter-a', name: 'Sorter A', laneCount: 12 }],
      objectTypes,
    });

    await expect(
      adapter.saveMappings('sorter-a', {
        version: 0,
        mappings: [mapping],
      }),
    ).rejects.toMatchObject({
      code: 'INVALID_MAPPING_PAYLOAD',
      message,
    });
  });
});
