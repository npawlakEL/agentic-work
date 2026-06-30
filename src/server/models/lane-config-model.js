export const createLaneConfigModel = (adapter) => ({
  getSorters: () => adapter.getSorters(),
  getMappings: (sorterId) => adapter.getMappings(sorterId),
  saveMappings: (sorterId, payload) => adapter.saveMappings(sorterId, payload),
  deleteMappings: (sorterId) => adapter.deleteMappings(sorterId),
});
