import { useDroppable } from '@dnd-kit/core';

export default function LaneSlot({ laneNumber, mappings, onRemoveMapping }) {
  const { isOver, setNodeRef } = useDroppable({
    id: `lane-${laneNumber}`,
    data: { laneNumber },
  });

  return (
    <div
      ref={setNodeRef}
      className={`lane-slot ${isOver ? 'lane-slot--over' : ''}`}
    >
      <div className="lane-slot__header">Lane {laneNumber}</div>
      <div className="lane-slot__mappings">
        {mappings.map((m) => (
          <div key={`${m.type}-${m.value}`} className="lane-slot__chip">
            <span>{m.value}</span>
            <button
              className="lane-slot__remove"
              onClick={() => onRemoveMapping(laneNumber, m)}
              title="Remove"
            >
              ×
            </button>
          </div>
        ))}
        {mappings.length === 0 && (
          <div className="lane-slot__empty">Drop here</div>
        )}
      </div>
    </div>
  );
}
