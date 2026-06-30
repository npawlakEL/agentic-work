import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import SorterView from './SorterView.jsx';

const sorter = { id: 'sorter-a', name: 'Sorter A', laneCount: 4 };

describe('SorterView', () => {
  it('removes existing mappings and assigns values in paint mode', async () => {
    const user = userEvent.setup();
    const onAssignMapping = vi.fn();
    const onRemoveMapping = vi.fn();

    render(
      <SorterView
        activeBrush="S3"
        currentObjectType="ship-to"
        isPaintMode
        mappings={[{ laneId: 1, objectType: 'ship-to', objectValue: 'S1' }]}
        onAssignMapping={onAssignMapping}
        onRemoveMapping={onRemoveMapping}
        sorter={sorter}
        valueColorMap={{ S1: 'var(--color-accent-1)', S3: 'var(--color-accent-2)' }}
      />,
    );

    await user.click(screen.getByRole('button', { name: 'Remove S1 from Lane 1' }));
    await user.click(screen.getByRole('button', { name: 'Lane 3 drop zone' }));

    expect(onRemoveMapping).toHaveBeenCalledWith({
      laneId: 1,
      objectType: 'ship-to',
      objectValue: 'S1',
    });
    expect(onAssignMapping).toHaveBeenCalledWith({
      laneId: 3,
      objectType: 'ship-to',
      objectValue: 'S3',
    });
  });

  it('renders mapping chips with their configured colors', () => {
    render(
      <SorterView
        activeBrush={null}
        currentObjectType="ship-to"
        isPaintMode={false}
        mappings={[
          { laneId: 1, objectType: 'ship-to', objectValue: 'S1' },
          { laneId: 2, objectType: 'ship-to', objectValue: 'S3' },
        ]}
        onAssignMapping={vi.fn()}
        onRemoveMapping={vi.fn()}
        sorter={sorter}
        valueColorMap={{ S1: 'var(--color-accent-1)', S3: 'var(--color-accent-3)' }}
      />,
    );

    expect(screen.getByRole('button', { name: 'Remove S1 from Lane 1' }).closest('.mapping-chip')).toHaveStyle(
      '--chip-color: var(--color-accent-1)',
    );
    expect(screen.getByRole('button', { name: 'Remove S3 from Lane 2' }).closest('.mapping-chip')).toHaveStyle(
      '--chip-color: var(--color-accent-3)',
    );
  });
});
