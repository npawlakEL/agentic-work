import { useDroppable } from '@dnd-kit/core';
import { buildLaneArray } from '../../../shared/types.js';

const MappingChip = ({ mapping, color, onRemove, laneId }) => (
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

const LaneChipsOverlay = ({
  activeBrush,
  currentObjectType,
  isPaintMode,
  laneId,
  mappings,
  onAssignMapping,
  onRemoveMapping,
  valueColorMap,
  x,
  y,
}) => {
  const { isOver, setNodeRef } = useDroppable({
    id: `lane:${laneId}`,
    data: { laneId },
  });

  const handleLaneClick = () => {
    if (!isPaintMode || !activeBrush || !currentObjectType) return;
    onAssignMapping({ laneId, objectType: currentObjectType, objectValue: activeBrush });
  };

  return (
    <foreignObject x={x} y={y - 16} width="220" height="60" overflow="visible">
      <div
        xmlns="http://www.w3.org/1999/xhtml"
        ref={setNodeRef}
        className={`cad-lane-chips ${isOver ? 'is-over' : ''}`}
        onClick={handleLaneClick}
        role="button"
        tabIndex={0}
        aria-label={`Lane ${laneId} drop zone`}
      >
        {mappings.map((mapping) => (
          <MappingChip
            key={`${mapping.laneId}-${mapping.objectType}-${mapping.objectValue}`}
            mapping={mapping}
            color={valueColorMap[mapping.objectValue]}
            onRemove={onRemoveMapping}
            laneId={laneId}
          />
        ))}
        {mappings.length === 0 && <span className="cad-lane-empty">Drop here</span>}
      </div>
    </foreignObject>
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
  const laneSpacing = 70;
  const conveyorX = 300;
  const branchLength = 180;
  const svgHeight = lanes.length * laneSpacing + 80;
  const svgWidth = 700;

  return (
    <div className="cad-diagram-wrapper">
      <svg
        className="cad-diagram"
        viewBox={`0 0 ${svgWidth} ${svgHeight}`}
        width="100%"
        height={svgHeight}
        xmlns="http://www.w3.org/2000/svg"
      >
        {/* Main conveyor spine */}
        <line
          x1={conveyorX}
          y1={20}
          x2={conveyorX}
          y2={svgHeight - 20}
          className="cad-conveyor-line"
        />
        <text x={conveyorX} y={14} className="cad-conveyor-label" textAnchor="middle">
          MAIN CONVEYOR
        </text>

        {/* Conveyor arrows */}
        <polygon
          points={`${conveyorX - 6},${svgHeight - 30} ${conveyorX + 6},${svgHeight - 30} ${conveyorX},${svgHeight - 18}`}
          className="cad-arrow"
        />

        {lanes.map((laneId, index) => {
          const y = 50 + index * laneSpacing;
          const isLeft = index % 2 === 0;
          const branchEndX = isLeft ? conveyorX - branchLength : conveyorX + branchLength;
          const divertAngleX = isLeft ? conveyorX - 30 : conveyorX + 30;

          return (
            <g key={laneId} className="cad-lane-group">
              {/* Divert branch line */}
              <line
                x1={conveyorX}
                y1={y}
                x2={divertAngleX}
                y2={y - 15}
                className="cad-branch-line"
              />
              <line
                x1={divertAngleX}
                y1={y - 15}
                x2={branchEndX}
                y2={y - 15}
                className="cad-branch-line"
              />

              {/* Lane endpoint (roller) */}
              <circle
                cx={branchEndX}
                cy={y - 15}
                r="6"
                className="cad-lane-endpoint"
              />

              {/* Lane label */}
              <text
                x={isLeft ? branchEndX - 16 : branchEndX + 16}
                y={y - 11}
                className="cad-lane-label"
                textAnchor={isLeft ? 'end' : 'start'}
              >
                Lane {laneId}
              </text>

              {/* Divert shoe indicator */}
              <rect
                x={conveyorX - 4}
                y={y - 4}
                width="8"
                height="8"
                className="cad-divert-shoe"
                transform={`rotate(${isLeft ? -30 : 30}, ${conveyorX}, ${y})`}
              />

              {/* Mapping chips overlay */}
              <LaneChipsOverlay
                activeBrush={activeBrush}
                currentObjectType={currentObjectType}
                isPaintMode={isPaintMode}
                laneId={laneId}
                mappings={mappings.filter((m) => m.laneId === laneId)}
                onAssignMapping={onAssignMapping}
                onRemoveMapping={onRemoveMapping}
                valueColorMap={valueColorMap}
                x={isLeft ? branchEndX - 230 : branchEndX + 20}
                y={y - 15}
              />
            </g>
          );
        })}

        {/* Conveyor side rails */}
        <line
          x1={conveyorX - 12}
          y1={20}
          x2={conveyorX - 12}
          y2={svgHeight - 20}
          className="cad-rail"
        />
        <line
          x1={conveyorX + 12}
          y1={20}
          x2={conveyorX + 12}
          y2={svgHeight - 20}
          className="cad-rail"
        />
      </svg>
    </div>
  );
};

export default ShoeSorterDiagram;
