// Mock data layer — no backend required.
// Replace this file with the real API client when the backend is ready.

const MOCK_SORTERS = [
  { id: 'sorter-a', name: 'Sorter A', laneCount: 12 },
  { id: 'sorter-b', name: 'Sorter B', laneCount: 16 },
  { id: 'sorter-c', name: 'Sorter C', laneCount: 10 },
];

const MOCK_OBJECT_TYPES = [
  {
    id: 'ship-to',
    name: 'Ship To',
    values: [
      { id: 'S1', label: 'S1 – East Coast Hub' },
      { id: 'S2', label: 'S2 – West Coast Hub' },
      { id: 'S3', label: 'S3 – Midwest DC' },
      { id: 'S4', label: 'S4 – Southeast DC' },
    ],
  },
];

// Pre-populated test mappings so the UI isn't empty on first load
const MOCK_INITIAL_MAPPINGS = {
  'sorter-a': [
    { laneId: 1, objectType: 'ship-to', objectValue: 'S1' },
    { laneId: 1, objectType: 'ship-to', objectValue: 'S3' },
    { laneId: 3, objectType: 'ship-to', objectValue: 'S2' },
    { laneId: 4, objectType: 'ship-to', objectValue: 'S1' },
    { laneId: 4, objectType: 'ship-to', objectValue: 'S4' },
    { laneId: 7, objectType: 'ship-to', objectValue: 'S3' },
    { laneId: 10, objectType: 'ship-to', objectValue: 'S2' },
    { laneId: 10, objectType: 'ship-to', objectValue: 'S4' },
  ],
  'sorter-b': [
    { laneId: 2, objectType: 'ship-to', objectValue: 'S1' },
    { laneId: 5, objectType: 'ship-to', objectValue: 'S2' },
    { laneId: 8, objectType: 'ship-to', objectValue: 'S3' },
    { laneId: 12, objectType: 'ship-to', objectValue: 'S4' },
  ],
  'sorter-c': [],
};

// In-memory store (persists during session, resets on refresh)
const store = {
  mappings: JSON.parse(JSON.stringify(MOCK_INITIAL_MAPPINGS)),
  versions: { 'sorter-a': 1, 'sorter-b': 1, 'sorter-c': 1 },
};

export const laneConfigApi = {
  getSorters: async () => MOCK_SORTERS,

  getObjectTypes: async () => MOCK_OBJECT_TYPES,

  getMappings: async (sorterId) => ({
    mappings: store.mappings[sorterId] ?? [],
    version: store.versions[sorterId] ?? 0,
  }),

  saveMappings: async (sorterId, payload) => {
    const currentVersion = store.versions[sorterId] ?? 0;

    if (payload.version !== currentVersion) {
      return { ok: false, status: 409, data: { message: "Another user has updated this sorter's configuration" } };
    }

    store.mappings[sorterId] = [...payload.mappings];
    store.versions[sorterId] = currentVersion + 1;

    return { ok: true, status: 200, data: { version: store.versions[sorterId] } };
  },

  clearMappings: async (sorterId) => {
    store.mappings[sorterId] = [];
    store.versions[sorterId] = (store.versions[sorterId] ?? 0) + 1;
    return { ok: true, status: 200, data: { version: store.versions[sorterId] } };
  },
};
