import { getDiagramComponent } from '../diagrams/index.js';

const SorterView = ({
  activeBrush,
  currentObjectType,
  isPaintMode,
  mappings,
  onAssignMapping,
  onRemoveMapping,
  sorter,
  valueColorMap,
}) => {
  if (!sorter) {
    return null;
  }

  const Diagram = getDiagramComponent(sorter.id);

  return (
    <section className="sorter-view">
      <div className="sorter-view__header">
        <h2>{sorter.name}</h2>
        <span>{sorter.laneCount} lanes</span>
      </div>
      <Diagram
        activeBrush={activeBrush}
        currentObjectType={currentObjectType}
        isPaintMode={isPaintMode}
        mappings={mappings}
        onAssignMapping={onAssignMapping}
        onRemoveMapping={onRemoveMapping}
        sorter={sorter}
        valueColorMap={valueColorMap}
      />
    </section>
  );
};

export default SorterView;
