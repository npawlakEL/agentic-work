import { act, render, screen, waitFor } from '@testing-library/react';
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

const createDeferred = () => {
  let resolve;
  const promise = new Promise((res) => {
    resolve = res;
  });

  return { promise, resolve };
};

const createJsonResponse = ({ body, ok = true, status = ok ? 200 : 500 }) => ({
  ok,
  status,
  json: async () => body,
});

const mockFetch = (overrides = {}) => {
  const saveHandler = overrides.saveHandler ?? (() => ({ sorterId: 'sorter-a', version: 1 }));
  const fetchMock = vi.fn(async (input, init = {}) => {
    const url = String(input);
    const method = init.method ?? 'GET';

    if (url.endsWith('/api/sorters')) {
      return createJsonResponse({ body: sorters });
    }

    if (url.endsWith('/api/config/object-types')) {
      return createJsonResponse({ body: objectTypes });
    }

    if (url.includes('/api/mappings/')) {
      const sorterId = url.split('/').at(-1);

      if (method === 'GET') {
        return createJsonResponse({
          body:
            overrides.getMappingsHandler?.({ sorterId }) ??
            mappingsBySorter[sorterId],
        });
      }

      if (method === 'POST') {
        const result = saveHandler({ sorterId, body: JSON.parse(init.body) });
        return createJsonResponse({
          ok: result.status ? result.status < 400 : true,
          status: result.status ?? 200,
          body: result.body ?? result,
        });
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

  it('reloads mappings when the selected sorter changes', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch();

    render(<App />);

    const sorterSelect = await screen.findByLabelText(/sorter/i);
    await user.selectOptions(sorterSelect, 'sorter-b');

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/mappings/sorter-b'),
        expect.anything(),
      );
    });
    expect(await screen.findByRole('button', { name: 'Remove S4 from Lane 1' })).toBeInTheDocument();
  });

  it('clears mappings, disables save while loading, and ignores stale sorter responses', async () => {
    const user = userEvent.setup();
    const sorterBLoad = createDeferred();
    const sorterAReload = createDeferred();
    let sorterALoadCount = 0;

    vi.stubGlobal(
      'fetch',
      vi.fn(async (input, init = {}) => {
        const url = String(input);
        const method = init.method ?? 'GET';

        if (url.endsWith('/api/sorters')) {
          return createJsonResponse({ body: sorters });
        }

        if (url.endsWith('/api/config/object-types')) {
          return createJsonResponse({ body: objectTypes });
        }

        if (url.includes('/api/mappings/') && method === 'GET') {
          const sorterId = url.split('/').at(-1);

          if (sorterId === 'sorter-a' && sorterALoadCount === 0) {
            sorterALoadCount += 1;
            return createJsonResponse({ body: mappingsBySorter['sorter-a'] });
          }

          if (sorterId === 'sorter-b') {
            return sorterBLoad.promise;
          }

          if (sorterId === 'sorter-a' && sorterALoadCount === 1) {
            sorterALoadCount += 1;
            return sorterAReload.promise;
          }
        }

        if (url.includes('/api/mappings/') && method === 'POST') {
          return createJsonResponse({ body: { sorterId: 'sorter-a', version: 99 } });
        }

        throw new Error(`Unhandled request: ${method} ${url}`);
      }),
    );

    render(<App />);

    expect(await screen.findByRole('button', { name: 'Remove S2 from Lane 2' })).toBeInTheDocument();

    const saveButton = screen.getByRole('button', { name: /save/i });
    const sorterSelect = screen.getByLabelText(/sorter/i);

    expect(saveButton).not.toBeDisabled();

    await user.selectOptions(sorterSelect, 'sorter-b');

    expect(screen.queryByRole('button', { name: 'Remove S2 from Lane 2' })).not.toBeInTheDocument();
    expect(saveButton).toBeDisabled();

    await user.selectOptions(sorterSelect, 'sorter-a');

    await act(async () => {
      sorterAReload.resolve(
        createJsonResponse({
          body: {
            sorterId: 'sorter-a',
            version: 4,
            mappings: [{ laneId: 4, objectType: 'ship-to', objectValue: 'S3' }],
          },
        }),
      );
    });

    expect(await screen.findByRole('button', { name: 'Remove S3 from Lane 4' })).toBeInTheDocument();

    await act(async () => {
      sorterBLoad.resolve(createJsonResponse({ body: mappingsBySorter['sorter-b'] }));
    });

    await waitFor(() => {
      expect(screen.getByLabelText(/sorter/i)).toHaveValue('sorter-a');
      expect(screen.queryByRole('button', { name: 'Remove S4 from Lane 1' })).not.toBeInTheDocument();
    });
    expect(saveButton).not.toBeDisabled();
  });

  it('updates the client version after a successful save', async () => {
    const user = userEvent.setup();
    const saveBodies = [];

    mockFetch({
      saveHandler: ({ body }) => {
        saveBodies.push(body);
        return { sorterId: 'sorter-a', version: saveBodies.length };
      },
    });

    render(<App />);

    const saveButton = await screen.findByRole('button', { name: /save/i });

    await user.click(saveButton);
    await user.click(saveButton);

    await waitFor(() => {
      expect(saveBodies).toHaveLength(2);
    });
    expect(saveBodies.map((body) => body.version)).toEqual([0, 1]);
  });

  it('does not update the client version after a failed save', async () => {
    const user = userEvent.setup();
    const alertMock = vi.fn();
    const saveBodies = [];

    vi.stubGlobal('alert', alertMock);
    mockFetch({
      saveHandler: ({ body }) => {
        saveBodies.push(body);

        if (saveBodies.length === 1) {
          return {
            status: 500,
            body: {
              error: 'INTERNAL_SERVER_ERROR',
              message: 'Save failed unexpectedly',
              version: 99,
            },
          };
        }

        return { sorterId: 'sorter-a', version: 1 };
      },
    });

    render(<App />);

    const saveButton = await screen.findByRole('button', { name: /save/i });

    await user.click(saveButton);
    await user.click(saveButton);

    await waitFor(() => {
      expect(saveBodies).toHaveLength(2);
    });
    expect(saveBodies.map((body) => body.version)).toEqual([0, 0]);
    expect(alertMock).toHaveBeenCalledWith('Save failed unexpectedly');
  });

  it('exits paint mode when Escape is pressed', async () => {
    const user = userEvent.setup();
    mockFetch();

    render(<App />);

    const paintModeButton = await screen.findByRole('button', { name: /paint mode/i });

    await user.click(paintModeButton);
    expect(paintModeButton).toHaveAttribute('aria-pressed', 'true');

    await user.keyboard('{Escape}');

    expect(paintModeButton).toHaveAttribute('aria-pressed', 'false');
  });
});
