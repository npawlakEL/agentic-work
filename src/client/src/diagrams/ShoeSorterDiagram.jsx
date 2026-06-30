import { useDroppable } from '@dnd-kit/core';
import { buildLaneArray } from '../../../shared/types.js';

const MappingChip = ({ laneId, mapping, color, onRemove }) => (
  <span className="mapping-chip" style={{ '--chip-color': color }}>
    <span>{mapping.objectValue}</span>
    <button
      aria-label={`Remove ${mapping.objectValue} from Lane ${laneId}`}
      className="chip-remove"
      onClick={() => onRemove(mapping)}
      type="button"
    >
      ×
    </button>
  </span>
);

const LaneDropZone = ({
  activeBrush,
  currentObjectType,
  isPaintMode,
  laneId,
  mappings,
  onAssignMapping,
  onRemoveMapping,
  valueColorMap,
}) => {
  const { isOver, setNodeRef } = useDroppable({
    id: `lane:${laneId}`,
    data: {
      laneId,
    },
  });

  const handleLaneClick = () => {
    if (!isPaintMode || !activeBrush || !currentObjectType) {
      return;
    }

    onAssignMapping({
      laneId,
      objectType: currentObjectType,
      objectValue: activeBrush,
    });
  };

  return (
    <div className="lane-slot">
      <div className="lane-slot__label">Lane {laneId}</div>
      <div
        aria-label={`Lane ${laneId} drop zone`}
        className={`lane-slot__target ${isOver ? 'is-over' : ''}`}
        onClick={handleLaneClick}
        ref={setNodeRef}
        role="button"
        tabIndex={0}
      >
        <span className="lane-slot__line" />
        <div className="lane-slot__chips">
          {mappings.map((mapping) => (
            <MappingChip
              color={valueColorMap[mapping.objectValue]}
              key={`${mapping.laneId}-${mapping.objectType}-${mapping.objectValue}`}
              laneId={laneId}
              mapping={mapping}
              onRemove={onRemoveMapping}
            />
          ))}
        </div>
      </div>
    </div>
  );
};

const ShoeSorterDiagram = ({
  activeBrush,
  currentObjectType,
  isPaintMode,
  mappings,
  onAssignMapping,
  onRemoveMapping,
  sorter,
  valueColorMap,
}) => {
  const lanes = buildLaneArray(sorter.laneCount);

  return (
    <div className="diagram">
      <div className="diagram__conveyor">Main Conveyor</div>
      <div className="diagram__lanes">
        {lanes.map((laneId) => (
          <LaneDropZone
            activeBrush={activeBrush}
            currentObjectType={currentObjectType}
            isPaintMode={isPaintMode}
            key={laneId}
            laneId={laneId}
            mappings={mappings.filter((mapping) => mapping.laneId === laneId)}
            onAssignMapping={onAssignMapping}
            onRemoveMapping={onRemoveMapping}
            valueColorMap={valueColorMap}
          />
        ))}
      </div>
    </div>
  );
};

export default ShoeSorterDiagram;
