import { API_BASE } from '../../../shared/types.js';

const fetchJson = async (path, options = {}) => {
  const response = await fetch(`${API_BASE}${path}`, {
    method: options.method ?? 'GET',
    headers: {
      'Content-Type': 'application/json',
      ...(options.headers ?? {}),
    },
    body: options.body ? JSON.stringify(options.body) : undefined,
  });

  const data = await response.json();

  return {
    ok: response.ok,
    status: response.status,
    data,
  };
};

export const laneConfigApi = {
  getSorters: async () => (await fetchJson('/sorters')).data,
  getObjectTypes: async () => (await fetchJson('/config/object-types')).data,
  getMappings: async (sorterId) => (await fetchJson(`/mappings/${sorterId}`)).data,
  saveMappings: async (sorterId, payload) => fetchJson(`/mappings/${sorterId}`, {
    method: 'POST',
    body: payload,
  }),
  clearMappings: async (sorterId) => fetchJson(`/mappings/${sorterId}`, {
    method: 'DELETE',
  }),
};
