import { useDraggable } from '@dnd-kit/core';

const DraggableValueButton = ({
  color,
  isActive,
  objectTypeName,
  onBrushSelect,
  value,
}) => {
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: `value:${objectTypeName}:${value.id}`,
    data: {
      objectValue: value.id,
    },
  });

  const style = {
    '--chip-color': color,
    opacity: isDragging ? 0.7 : 1,
    transform: transform
      ? `translate3d(${transform.x}px, ${transform.y}px, 0)`
      : undefined,
  };

  return (
    <button
      {...attributes}
      {...listeners}
      aria-pressed={isActive}
      className={`value-chip ${isActive ? 'is-active' : ''}`}
      onClick={() => onBrushSelect(value.id)}
      ref={setNodeRef}
      style={style}
      type="button"
    >
      {objectTypeName} {value.label}
    </button>
  );
};

const MappingPanel = ({
  activeBrush,
  isPaintMode,
  objectType,
  onBrushSelect,
  onPaintModeToggle,
  valueColorMap,
  valueFilter,
}) => {
  if (!objectType) {
    return null;
  }

  const visibleValues = objectType.values.filter(
    (value) => valueFilter === 'all' || value.id === valueFilter,
  );

  return (
    <aside className="mapping-panel">
      <div className="panel-header">
        <h2>Drag to assign</h2>
        <button
          aria-pressed={isPaintMode}
          className={`button ${isPaintMode ? 'button-primary' : 'button-secondary'}`}
          onClick={() => onPaintModeToggle(!isPaintMode)}
          type="button"
        >
          Paint Mode
        </button>
      </div>
      <div className="value-list">
        {visibleValues.map((value) => (
          <DraggableValueButton
            color={valueColorMap?.[value.id]}
            isActive={activeBrush === value.id}
            key={value.id}
            objectTypeName={objectType.name}
            onBrushSelect={onBrushSelect}
            value={value}
          />
        ))}
      </div>
    </aside>
  );
};

export default MappingPanel;
