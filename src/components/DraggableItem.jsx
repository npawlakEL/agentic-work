import { useDraggable } from '@dnd-kit/core';

export default function DraggableItem({ id, label }) {
  const { attributes, listeners, setNodeRef, transform, isDragging } =
    useDraggable({ id, data: { label } });

  const style = transform
    ? { transform: `translate(${transform.x}px, ${transform.y}px)` }
    : undefined;

  return (
    <div
      ref={setNodeRef}
      className={`draggable-item ${isDragging ? 'draggable-item--dragging' : ''}`}
      style={style}
      {...listeners}
      {...attributes}
    >
      {label}
    </div>
  );
}
