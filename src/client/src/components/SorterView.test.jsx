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
});
