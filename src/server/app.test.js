import request from 'supertest';
import { beforeEach, describe, expect, it } from 'vitest';
import { createApp } from './app.js';
import { SqliteDatabaseAdapter } from './adapters/sqlite.js';

const sorters = [
  { id: 'sorter-a', name: 'Sorter A', laneCount: 12 },
  { id: 'sorter-b', name: 'Sorter B', laneCount: 16 },
];

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

describe('lane configuration API', () => {
  let app;

  beforeEach(() => {
    const adapter = new SqliteDatabaseAdapter({
      filename: ':memory:',
      sorters,
    });

    app = createApp({
      adapter,
      objectTypes,
    });
  });

  it('lists configured sorters', async () => {
    const response = await request(app).get('/api/sorters');

    expect(response.status).toBe(200);
    expect(response.body).toEqual(sorters);
  });

  it('lists configured object types', async () => {
    const response = await request(app).get('/api/config/object-types');

    expect(response.status).toBe(200);
    expect(response.body).toEqual(objectTypes);
  });

  it('loads mappings for a sorter', async () => {
    const response = await request(app).get('/api/mappings/sorter-a');

    expect(response.status).toBe(200);
    expect(response.body).toEqual({
      sorterId: 'sorter-a',
      version: 0,
      mappings: [],
    });
  });

  it('saves mappings for a sorter', async () => {
    const saveResponse = await request(app)
      .post('/api/mappings/sorter-a')
      .send({
        version: 0,
        mappings: [
          { laneId: 1, objectType: 'ship-to', objectValue: 'S1' },
          { laneId: 2, objectType: 'ship-to', objectValue: 'S2' },
        ],
      });

    expect(saveResponse.status).toBe(200);
    expect(saveResponse.body).toEqual({
      sorterId: 'sorter-a',
      version: 1,
    });

    const loadResponse = await request(app).get('/api/mappings/sorter-a');
    expect(loadResponse.body).toEqual({
      sorterId: 'sorter-a',
      version: 1,
      mappings: [
        { laneId: 1, objectType: 'ship-to', objectValue: 'S1' },
        { laneId: 2, objectType: 'ship-to', objectValue: 'S2' },
      ],
    });
  });

  it('returns 409 when saving with a stale version', async () => {
    await request(app).post('/api/mappings/sorter-a').send({
      version: 0,
      mappings: [{ laneId: 1, objectType: 'ship-to', objectValue: 'S1' }],
    });

    const response = await request(app).post('/api/mappings/sorter-a').send({
      version: 0,
      mappings: [{ laneId: 2, objectType: 'ship-to', objectValue: 'S2' }],
    });

    expect(response.status).toBe(409);
    expect(response.body).toEqual({
      error: 'VERSION_CONFLICT',
      message: "Another user has updated this sorter's configuration",
      currentVersion: 1,
    });
  });

  it('deletes mappings for a sorter', async () => {
    await request(app).post('/api/mappings/sorter-a').send({
      version: 0,
      mappings: [{ laneId: 1, objectType: 'ship-to', objectValue: 'S1' }],
    });

    const deleteResponse = await request(app).delete('/api/mappings/sorter-a');

    expect(deleteResponse.status).toBe(200);
    expect(deleteResponse.body).toEqual({
      sorterId: 'sorter-a',
      version: 2,
    });

    const loadResponse = await request(app).get('/api/mappings/sorter-a');
    expect(loadResponse.body).toEqual({
      sorterId: 'sorter-a',
      version: 2,
      mappings: [],
    });
  });
});
