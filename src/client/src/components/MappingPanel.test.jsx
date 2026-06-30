import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import MappingPanel from './MappingPanel.jsx';

const objectType = {
  id: 'ship-to',
  name: 'Ship To',
  values: [
    { id: 'S1', label: 'S1' },
    { id: 'S2', label: 'S2' },
  ],
};

describe('MappingPanel', () => {
  it('filters visible values and notifies when a brush is selected', async () => {
    const user = userEvent.setup();
    const onBrushSelect = vi.fn();

    render(
      <MappingPanel
        activeBrush="S2"
        isPaintMode={false}
        objectType={objectType}
        onBrushSelect={onBrushSelect}
        onPaintModeToggle={vi.fn()}
        valueColorMap={{ S1: 'var(--color-accent-1)', S2: 'var(--color-accent-2)' }}
        valueFilter="S2"
      />,
    );

    await user.click(screen.getByRole('button', { name: 'Ship To S2' }));

    expect(onBrushSelect).toHaveBeenCalledWith('S2');
    expect(screen.queryByRole('button', { name: 'Ship To S1' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ship To S2' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
  });

  it('toggles paint mode from the toolbar', async () => {
    const user = userEvent.setup();
    const onPaintModeToggle = vi.fn();

    render(
      <MappingPanel
        activeBrush={null}
        isPaintMode={false}
        objectType={objectType}
        onBrushSelect={vi.fn()}
        onPaintModeToggle={onPaintModeToggle}
        valueColorMap={{ S1: 'var(--color-accent-1)', S2: 'var(--color-accent-2)' }}
        valueFilter="all"
      />,
    );

    await user.click(screen.getByRole('button', { name: /paint mode/i }));

    expect(onPaintModeToggle).toHaveBeenCalledWith(true);
  });

  it('renders distinct chip colors for each object value', () => {
    render(
      <MappingPanel
        activeBrush={null}
        isPaintMode={false}
        objectType={objectType}
        onBrushSelect={vi.fn()}
        onPaintModeToggle={vi.fn()}
        valueColorMap={{ S1: 'var(--color-accent-1)', S2: 'var(--color-accent-2)' }}
        valueFilter="all"
      />,
    );

    expect(screen.getByRole('button', { name: 'Ship To S1' })).toHaveStyle(
      '--chip-color: var(--color-accent-1)',
    );
    expect(screen.getByRole('button', { name: 'Ship To S2' })).toHaveStyle(
      '--chip-color: var(--color-accent-2)',
    );
  });
});
