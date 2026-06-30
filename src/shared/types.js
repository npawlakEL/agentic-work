export const API_BASE = '/api';
export const CONFLICT_MESSAGE =
  "Another user has updated this sorter's configuration";
export const DEFAULT_OBJECT_TYPE_ID = 'ship-to';
export const LAST_SORTER_STORAGE_KEY = 'lane-config:last-sorter';
export const VALUE_FILTER_ALL = 'all';

export const buildLaneArray = (laneCount) =>
  Array.from({ length: laneCount }, (_value, index) => index + 1);
