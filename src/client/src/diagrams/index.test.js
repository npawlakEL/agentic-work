import { describe, expect, it } from 'vitest';
import ShoeSorterDiagram from './ShoeSorterDiagram.jsx';
import { getDiagramComponent } from './index.js';

describe('diagram registry', () => {
  it('selects the registered component for known sorters and falls back for unknown sorters', () => {
    expect(getDiagramComponent('sorter-a')).toBe(ShoeSorterDiagram);
    expect(getDiagramComponent('sorter-b')).toBe(ShoeSorterDiagram);
    expect(getDiagramComponent('unknown-sorter')).toBe(ShoeSorterDiagram);
  });
});
