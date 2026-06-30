import { DndContext, PointerSensor, useSensor, useSensors } from '@dnd-kit/core';
import { useEffect, useMemo, useRef, useState } from 'react';
import MappingPanel from './components/MappingPanel.jsx';
import SorterView from './components/SorterView.jsx';
import ConflictDialog from './components/ConflictDialog.jsx';
import { createDragEndHandler } from './hooks/useDragAndDrop.js';
import { laneConfigApi } from './services/api.js';
import {
  CONFLICT_MESSAGE,
  DEFAULT_OBJECT_TYPE_ID,
  LAST_SORTER_STORAGE_KEY,
  VALUE_FILTER_ALL,
} from '../../shared/types.js';

const createValueColorMap = (objectType) =>
  Object.fromEntries(
    (objectType?.values ?? []).map((value, index) => [
      value.id,
      `var(--color-accent-${(index % 4) + 1})`,
    ]),
  );

const hasMapping = (mappings, nextMapping) =>
  mappings.some(
    (mapping) =>
      mapping.laneId === nextMapping.laneId &&
      mapping.objectType === nextMapping.objectType &&
      mapping.objectValue === nextMapping.objectValue,
  );

const App = () => {
  const sensors = useSensors(useSensor(PointerSensor));
  const [sorters, setSorters] = useState([]);
  const [objectTypes, setObjectTypes] = useState([]);
  const [selectedSorterId, setSelectedSorterId] = useState('');
  const [selectedObjectTypeId, setSelectedObjectTypeId] =
    useState(DEFAULT_OBJECT_TYPE_ID);
  const [valueFilter, setValueFilter] = useState(VALUE_FILTER_ALL);
  const [mappings, setMappings] = useState([]);
  const [version, setVersion] = useState(0);
  const [activeBrush, setActiveBrush] = useState(null);
  const [isPaintMode, setIsPaintMode] = useState(false);
  const [conflictOpen, setConflictOpen] = useState(false);
  const [isMappingsLoading, setIsMappingsLoading] = useState(false);
  const latestMappingsRequestId = useRef(0);
  const selectedSorterIdRef = useRef(selectedSorterId);

  const selectedSorter = useMemo(
    () => sorters.find((sorter) => sorter.id === selectedSorterId) ?? null,
    [selectedSorterId, sorters],
  );
  const selectedObjectType = useMemo(
    () =>
      objectTypes.find((objectType) => objectType.id === selectedObjectTypeId) ??
      objectTypes[0] ??
      null,
    [objectTypes, selectedObjectTypeId],
  );
  const valueColorMap = useMemo(
    () => createValueColorMap(selectedObjectType),
    [selectedObjectType],
  );

  useEffect(() => {
    selectedSorterIdRef.current = selectedSorterId;
  }, [selectedSorterId]);

  const loadMappings = async (sorterId) => {
    if (!sorterId) {
      setMappings([]);
      setVersion(0);
      setConflictOpen(false);
      setIsMappingsLoading(false);
      return;
    }

    const requestId = latestMappingsRequestId.current + 1;
    latestMappingsRequestId.current = requestId;
    setMappings([]);
    setVersion(0);
    setConflictOpen(false);
    setIsMappingsLoading(true);

    try {
      const mappingResponse = await laneConfigApi.getMappings(sorterId);

      if (
        requestId !== latestMappingsRequestId.current ||
        sorterId !== selectedSorterIdRef.current
      ) {
        return;
      }

      setMappings(mappingResponse.mappings);
      setVersion(mappingResponse.version);
      setConflictOpen(false);
    } finally {
      if (
        requestId === latestMappingsRequestId.current &&
        sorterId === selectedSorterIdRef.current
      ) {
        setIsMappingsLoading(false);
      }
    }
  };

  useEffect(() => {
    const loadConfig = async () => {
      const [loadedSorters, loadedObjectTypes] = await Promise.all([
        laneConfigApi.getSorters(),
        laneConfigApi.getObjectTypes(),
      ]);

      setSorters(loadedSorters);
      setObjectTypes(loadedObjectTypes);

      const storedSorterId = localStorage.getItem(LAST_SORTER_STORAGE_KEY);
      const nextSorterId =
        loadedSorters.find((sorter) => sorter.id === storedSorterId)?.id ??
        loadedSorters[0]?.id ??
        '';

      const nextObjectTypeId =
        loadedObjectTypes.find(
          (objectType) => objectType.id === DEFAULT_OBJECT_TYPE_ID,
        )?.id ??
        loadedObjectTypes[0]?.id ??
        '';

      setSelectedObjectTypeId(nextObjectTypeId);
      setSelectedSorterId(nextSorterId);
    };

    loadConfig();
  }, []);

  useEffect(() => {
    if (!selectedSorterId) {
      return;
    }

    localStorage.setItem(LAST_SORTER_STORAGE_KEY, selectedSorterId);
    loadMappings(selectedSorterId);
  }, [selectedSorterId]);

  useEffect(() => {
    const handleKeydown = (event) => {
      if (event.key === 'Escape') {
        setIsPaintMode(false);
      }
    };

    window.addEventListener('keydown', handleKeydown);
    return () => window.removeEventListener('keydown', handleKeydown);
  }, []);

  const assignMapping = (nextMapping) => {
    setMappings((currentMappings) =>
      hasMapping(currentMappings, nextMapping)
        ? currentMappings
        : [...currentMappings, nextMapping],
    );
  };

  const handleDragEnd = createDragEndHandler({
    currentObjectType: selectedObjectType?.id,
    onAssignMapping: assignMapping,
  });

  const handleSave = async () => {
    const response = await laneConfigApi.saveMappings(selectedSorterId, {
      version,
      mappings,
    });

    if (response.status === 409) {
      setConflictOpen(true);
      return;
    }

    if (!response.ok) {
      alert(response.data.message ?? 'Unable to save mappings.');
      return;
    }

    setVersion(response.data.version);
    setConflictOpen(false);
  };

  const handleRefresh = async () => {
    await loadMappings(selectedSorterId);
  };

  return (
    <DndContext onDragEnd={handleDragEnd} sensors={sensors}>
      <div className="app-shell">
        <header className="page-header">
          <div>
            <h1>Lane Configuration</h1>
          </div>
          <div className="page-actions">
            <button className="button button-secondary" onClick={handleRefresh} type="button">
              Load
            </button>
            <button
              className="button button-primary"
              disabled={isMappingsLoading || !selectedSorterId}
              onClick={handleSave}
              type="button"
            >
              Save
            </button>
          </div>
        </header>

        <section className="toolbar">
          <label>
            <span>Sorter</span>
            <select
              aria-label="Sorter"
              onChange={(event) => setSelectedSorterId(event.target.value)}
              value={selectedSorterId}
            >
              {sorters.map((sorter) => (
                <option key={sorter.id} value={sorter.id}>
                  {sorter.name}
                </option>
              ))}
            </select>
          </label>
          <label>
            <span>Object Type</span>
            <select
              aria-label="Object Type"
              onChange={(event) => setSelectedObjectTypeId(event.target.value)}
              value={selectedObjectType?.id ?? ''}
            >
              {objectTypes.map((objectType) => (
                <option key={objectType.id} value={objectType.id}>
                  {objectType.name}
                </option>
              ))}
            </select>
          </label>
          <label>
            <span>Value</span>
            <select
              aria-label="Value"
              onChange={(event) => setValueFilter(event.target.value)}
              value={valueFilter}
            >
              <option value={VALUE_FILTER_ALL}>All</option>
              {(selectedObjectType?.values ?? []).map((value) => (
                <option key={value.id} value={value.id}>
                  {value.label}
                </option>
              ))}
            </select>
          </label>
        </section>

        <main className="layout">
          <MappingPanel
            activeBrush={activeBrush}
            isPaintMode={isPaintMode}
            objectType={selectedObjectType}
            onBrushSelect={(valueId) => {
              setActiveBrush(valueId);
              setIsPaintMode(true);
            }}
            onPaintModeToggle={setIsPaintMode}
            valueColorMap={valueColorMap}
            valueFilter={valueFilter}
          />
          <SorterView
            activeBrush={activeBrush}
            currentObjectType={selectedObjectType?.id ?? null}
            isPaintMode={isPaintMode}
            mappings={mappings}
            onAssignMapping={assignMapping}
            onRemoveMapping={(mappingToRemove) => {
              setMappings((currentMappings) =>
                currentMappings.filter(
                  (mapping) =>
                    !(
                      mapping.laneId === mappingToRemove.laneId &&
                      mapping.objectType === mappingToRemove.objectType &&
                      mapping.objectValue === mappingToRemove.objectValue
                    ),
                ),
              );
            }}
            sorter={selectedSorter}
            valueColorMap={valueColorMap}
          />
        </main>
        <ConflictDialog
          isOpen={conflictOpen}
          message={CONFLICT_MESSAGE}
          onRefresh={handleRefresh}
        />
      </div>
    </DndContext>
  );
};

export default App;
