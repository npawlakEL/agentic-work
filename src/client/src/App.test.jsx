import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App.jsx';
import {
  CONFLICT_MESSAGE,
  DEFAULT_OBJECT_TYPE_ID,
  LAST_SORTER_STORAGE_KEY,
} from '../../shared/types.js';

const sorters = [
  { id: 'sorter-a', name: 'Sorter A', laneCount: 12 },
  { id: 'sorter-b', name: 'Sorter B', laneCount: 16 },
];

const objectTypes = [
  {
    id: DEFAULT_OBJECT_TYPE_ID,
    name: 'Ship To',
    values: [
      { id: 'S1', label: 'S1' },
      { id: 'S2', label: 'S2' },
      { id: 'S3', label: 'S3' },
      { id: 'S4', label: 'S4' },
    ],
  },
];

const mappingsBySorter = {
  'sorter-a': {
    sorterId: 'sorter-a',
    version: 0,
    mappings: [{ laneId: 2, objectType: 'ship-to', objectValue: 'S2' }],
  },
  'sorter-b': {
    sorterId: 'sorter-b',
    version: 3,
    mappings: [{ laneId: 1, objectType: 'ship-to', objectValue: 'S4' }],
  },
};

const mockFetch = (overrides = {}) => {
  const saveHandler = overrides.saveHandler ?? (() => ({ sorterId: 'sorter-a', version: 1 }));
  const fetchMock = vi.fn(async (input, init = {}) => {
    const url = String(input);
    const method = init.method ?? 'GET';

    if (url.endsWith('/api/sorters')) {
      return { ok: true, json: async () => sorters };
    }

    if (url.endsWith('/api/config/object-types')) {
      return { ok: true, json: async () => objectTypes };
    }

    if (url.includes('/api/mappings/')) {
      const sorterId = url.split('/').at(-1);

      if (method === 'GET') {
        return { ok: true, json: async () => mappingsBySorter[sorterId] };
      }

      if (method === 'POST') {
        const result = saveHandler({ sorterId, body: JSON.parse(init.body) });
        return {
          ok: result.status ? result.status < 400 : true,
          status: result.status ?? 200,
          json: async () => result.body ?? result,
        };
      }
    }

    throw new Error(`Unhandled request: ${method} ${url}`);
  });

  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
};

describe('App', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.unstubAllGlobals();
  });

  it('loads config data, auto-selects the first sorter, and fetches mappings', async () => {
    mockFetch();

    render(<App />);

    expect(await screen.findByLabelText(/sorter/i)).toHaveValue('sorter-a');
    expect(await screen.findByText('Lane 2')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Remove S2 from Lane 2' })).toBeInTheDocument();
    expect(localStorage.getItem(LAST_SORTER_STORAGE_KEY)).toBe('sorter-a');
  });

  it('restores the last selected sorter from local storage', async () => {
    localStorage.setItem(LAST_SORTER_STORAGE_KEY, 'sorter-b');
    const fetchMock = mockFetch();

    render(<App />);

    expect(await screen.findByLabelText(/sorter/i)).toHaveValue('sorter-b');
    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/mappings/sorter-b'),
        expect.anything(),
      );
    });
    expect(screen.getByRole('button', { name: 'Remove S4 from Lane 1' })).toBeInTheDocument();
  });

  it('filters the mapping panel by object value', async () => {
    const user = userEvent.setup();
    mockFetch();

    render(<App />);

    const valueFilter = await screen.findByLabelText(/value/i);
    await user.selectOptions(valueFilter, 'S2');

    expect(screen.getByRole('button', { name: 'Ship To S2' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Ship To S1' })).not.toBeInTheDocument();
  });

  it('shows a conflict dialog and refreshes mappings after a stale save', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      saveHandler: () => ({
        status: 409,
        body: {
          error: 'VERSION_CONFLICT',
          message: CONFLICT_MESSAGE,
          currentVersion: 1,
        },
      }),
    });

    render(<App />);

    await user.click(await screen.findByRole('button', { name: /save/i }));

    expect(await screen.findByRole('dialog')).toHaveTextContent(CONFLICT_MESSAGE);

    await user.click(screen.getByRole('button', { name: /refresh/i }));

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/mappings/sorter-a'),
        expect.objectContaining({ method: 'GET' }),
      );
    });
  });
});
