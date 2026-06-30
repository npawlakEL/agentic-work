export const createDragEndHandler = ({
  currentObjectType,
  onAssignMapping,
}) => ({ active, over }) => {
  const objectValue = active?.data?.current?.objectValue;
  const laneId = over?.data?.current?.laneId;

  if (!objectValue || !laneId || !currentObjectType) {
    return;
  }

  onAssignMapping({
    laneId,
    objectType: currentObjectType,
    objectValue,
  });
};
