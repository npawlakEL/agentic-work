import { describe, expect, it, vi } from 'vitest';
import { createDragEndHandler } from './useDragAndDrop.js';

describe('createDragEndHandler', () => {
  it('converts a dnd-kit drag result into a lane assignment', () => {
    const onAssignMapping = vi.fn();
    const handleDragEnd = createDragEndHandler({
      currentObjectType: 'ship-to',
      onAssignMapping,
    });

    handleDragEnd({
      active: {
        data: {
          current: {
            objectValue: 'S2',
          },
        },
      },
      over: {
        data: {
          current: {
            laneId: 4,
          },
        },
      },
    });

    expect(onAssignMapping).toHaveBeenCalledWith({
      laneId: 4,
      objectType: 'ship-to',
      objectValue: 'S2',
    });
  });
});
