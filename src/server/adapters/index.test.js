import { describe, expect, it } from 'vitest';
import { createDatabaseAdapter } from './index.js';

describe('createDatabaseAdapter', () => {
  it('creates the configured sqlite adapter', () => {
    const adapter = createDatabaseAdapter({
      type: 'sqlite',
      filename: ':memory:',
      sorters: [{ id: 'sorter-a', name: 'Sorter A', laneCount: 12 }],
    });

    expect(adapter.constructor.name).toBe('SqliteDatabaseAdapter');
  });
});
