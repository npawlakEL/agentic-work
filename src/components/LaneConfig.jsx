import { useState, useCallback } from 'react';
import { DndContext, DragOverlay, pointerWithin } from '@dnd-kit/core';
import { sorters, objectTypes } from '../data/config';
import LaneSlot from './LaneSlot';
import DraggableItem from './DraggableItem';

export default function LaneConfig() {
  const [selectedSorterId, setSelectedSorterId] = useState(sorters[0].id);
  const [selectedTypeId, setSelectedTypeId] = useState(objectTypes[0].id);
  const [selectedValueId, setSelectedValueId] = useState('');
  const [activeId, setActiveId] = useState(null);

  // mappings: { [sorterId]: { [laneNumber]: [{ type, value }] } }
  const [mappings, setMappings] = useState({});

  const selectedSorter = sorters.find((s) => s.id === selectedSorterId);
  const selectedType = objectTypes.find((t) => t.id === selectedTypeId);
  const lanes = Array.from({ length: selectedSorter.laneCount }, (_, i) => i + 1);

  const getLaneMappings = (laneNumber) => {
    return mappings[selectedSorterId]?.[laneNumber] || [];
  };

  const handleDragStart = (event) => {
    setActiveId(event.active.id);
  };

  const handleDragEnd = (event) => {
    setActiveId(null);
    const { active, over } = event;
    if (!over) return;

    const laneNumber = over.data.current?.laneNumber;
    if (laneNumber == null) return;

    const [type, value] = active.id.split('::');
    const existing = getLaneMappings(laneNumber);
    if (existing.some((m) => m.type === type && m.value === value)) return;

    setMappings((prev) => {
      const sorterMappings = { ...(prev[selectedSorterId] || {}) };
      sorterMappings[laneNumber] = [...existing, { type, value }];
      return { ...prev, [selectedSorterId]: sorterMappings };
    });
  };

  const handleRemoveMapping = useCallback(
    (laneNumber, mapping) => {
      setMappings((prev) => {
        const sorterMappings = { ...(prev[selectedSorterId] || {}) };
        sorterMappings[laneNumber] = (sorterMappings[laneNumber] || []).filter(
          (m) => !(m.type === mapping.type && m.value === mapping.value)
        );
        return { ...prev, [selectedSorterId]: sorterMappings };
      });
    },
    [selectedSorterId]
  );

  const handleSave = () => {
    const data = JSON.stringify(mappings, null, 2);
    localStorage.setItem('laneConfigMappings', data);
    alert('Configuration saved!');
  };

  const handleLoad = () => {
    const data = localStorage.getItem('laneConfigMappings');
    if (data) {
      setMappings(JSON.parse(data));
    }
  };

  // Build draggable items based on selected type and value
  const draggableItems = selectedValueId
    ? [{ id: `${selectedTypeId}::${selectedValueId}`, label: selectedValueId }]
    : selectedType.values.map((v) => ({
        id: `${selectedTypeId}::${v.id}`,
        label: v.label,
      }));

  return (
    <div className="lane-config">
      <header className="lane-config__header">
        <h1>Lane Configuration</h1>
        <div className="lane-config__actions">
          <button onClick={handleLoad} className="btn btn--secondary">
            Load
          </button>
          <button onClick={handleSave} className="btn btn--primary">
            Save
          </button>
        </div>
      </header>

      <div className="lane-config__controls">
        <div className="control-group">
          <label htmlFor="sorter-select">Sorter</label>
          <select
            id="sorter-select"
            value={selectedSorterId}
            onChange={(e) => setSelectedSorterId(e.target.value)}
          >
            {sorters.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name} ({s.laneCount} lanes)
              </option>
            ))}
          </select>
        </div>

        <div className="control-group">
          <label htmlFor="type-select">Object Type</label>
          <select
            id="type-select"
            value={selectedTypeId}
            onChange={(e) => {
              setSelectedTypeId(e.target.value);
              setSelectedValueId('');
            }}
          >
            {objectTypes.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </select>
        </div>

        <div className="control-group">
          <label htmlFor="value-select">Value</label>
          <select
            id="value-select"
            value={selectedValueId}
            onChange={(e) => setSelectedValueId(e.target.value)}
          >
            <option value="">All values</option>
            {selectedType.values.map((v) => (
              <option key={v.id} value={v.id}>
                {v.label}
              </option>
            ))}
          </select>
        </div>
      </div>

      <DndContext
        collisionDetection={pointerWithin}
        onDragStart={handleDragStart}
        onDragEnd={handleDragEnd}
      >
        <div className="lane-config__body">
          <aside className="lane-config__sidebar">
            <h3>Drag to assign</h3>
            <div className="draggable-list">
              {draggableItems.map((item) => (
                <DraggableItem key={item.id} id={item.id} label={item.label} />
              ))}
            </div>
          </aside>

          <main className="lane-config__sorter">
            <h2>{selectedSorter.name}</h2>
            <div className="lanes-container">
              {lanes.map((laneNum) => (
                <LaneSlot
                  key={laneNum}
                  laneNumber={laneNum}
                  mappings={getLaneMappings(laneNum)}
                  onRemoveMapping={handleRemoveMapping}
                />
              ))}
            </div>
          </main>
        </div>

        <DragOverlay>
          {activeId ? (
            <div className="draggable-item draggable-item--overlay">
              {activeId.split('::')[1]}
            </div>
          ) : null}
        </DragOverlay>
      </DndContext>
    </div>
  );
}
