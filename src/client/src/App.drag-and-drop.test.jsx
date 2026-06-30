import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App.jsx';
import { DEFAULT_OBJECT_TYPE_ID } from '../../shared/types.js';

vi.mock('@dnd-kit/core', () => ({
  DndContext: ({ children, onDragEnd }) => (
    <div>
      <button
        onClick={() =>
          onDragEnd({
            active: { data: { current: { objectValue: 'S3' } } },
            over: { data: { current: { laneId: 3 } } },
          })
        }
        type="button"
      >
        Trigger drag end
      </button>
      {children}
    </div>
  ),
  PointerSensor: function PointerSensor() {},
  useDraggable: () => ({
    attributes: {},
    listeners: {},
    setNodeRef: () => {},
    transform: null,
    isDragging: false,
  }),
  useDroppable: () => ({
    isOver: false,
    setNodeRef: () => {},
  }),
  useSensor: () => ({}),
  useSensors: (...sensors) => sensors,
}));

const sorters = [{ id: 'sorter-a', name: 'Sorter A', laneCount: 12 }];
const objectTypes = [
  {
    id: DEFAULT_OBJECT_TYPE_ID,
    name: 'Ship To',
    values: [{ id: 'S3', label: 'S3' }],
  },
];

describe('App drag-and-drop integration', () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
  });

  it('assigns a mapping when drag end drops a value onto a lane', async () => {
    const user = userEvent.setup();

    vi.stubGlobal(
      'fetch',
      vi.fn(async (input) => {
        const url = String(input);

        if (url.endsWith('/api/sorters')) {
          return { ok: true, status: 200, json: async () => sorters };
        }

        if (url.endsWith('/api/config/object-types')) {
          return { ok: true, status: 200, json: async () => objectTypes };
        }

        if (url.includes('/api/mappings/sorter-a')) {
          return {
            ok: true,
            status: 200,
            json: async () => ({ sorterId: 'sorter-a', version: 0, mappings: [] }),
          };
        }

        throw new Error(`Unhandled request: ${url}`);
      }),
    );

    render(<App />);

    await screen.findByLabelText(/sorter/i);
    await user.click(screen.getByRole('button', { name: /trigger drag end/i }));

    expect(await screen.findByRole('button', { name: 'Remove S3 from Lane 3' })).toBeInTheDocument();
  });
});
