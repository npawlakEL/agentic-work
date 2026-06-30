export class SorterNotFoundError extends Error {
  constructor(sorterId) {
    super(`Sorter not found: ${sorterId}`);
    this.code = 'SORTER_NOT_FOUND';
  }
}

export class VersionConflictError extends Error {
  constructor(currentVersion) {
    super("Another user has updated this sorter's configuration");
    this.code = 'VERSION_CONFLICT';
    this.currentVersion = currentVersion;
  }
}

export class InvalidMappingPayloadError extends Error {
  constructor(message) {
    super(message);
    this.code = 'INVALID_MAPPING_PAYLOAD';
  }
}
