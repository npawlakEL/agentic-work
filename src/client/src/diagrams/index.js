import ShoeSorterDiagram from './ShoeSorterDiagram.jsx';

const diagramRegistry = {
  'sorter-a': ShoeSorterDiagram,
  'sorter-b': ShoeSorterDiagram,
  'sorter-c': ShoeSorterDiagram,
};

export const getDiagramComponent = (sorterId) =>
  diagramRegistry[sorterId] ?? ShoeSorterDiagram;
