import Database from 'better-sqlite3';
import { DatabaseAdapter } from './base.js';
import {
  InvalidMappingPayloadError,
  SorterNotFoundError,
  VersionConflictError,
} from './errors.js';

export class SqliteDatabaseAdapter extends DatabaseAdapter {
  constructor({ filename = ':memory:', sorters = [], objectTypes = [] } = {}) {
    super();
    this.db = new Database(filename);
    this.sorters = [...sorters];
    this.sorterIds = new Set(sorters.map((sorter) => sorter.id));
    this.sorterConfigById = new Map(sorters.map((sorter) => [sorter.id, sorter]));
    this.objectTypeValuesById = new Map(
      objectTypes.map((objectType) => [
        objectType.id,
        new Set((objectType.values ?? []).map((value) => value.id)),
      ]),
    );
    this.initialize();
  }

  initialize() {
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS mappings (
        sorter_id TEXT NOT NULL,
        lane_id INTEGER NOT NULL,
        object_type TEXT NOT NULL,
        object_value TEXT NOT NULL,
        PRIMARY KEY (sorter_id, lane_id, object_type, object_value)
      );

      CREATE TABLE IF NOT EXISTS mapping_versions (
        sorter_id TEXT PRIMARY KEY,
        version INTEGER NOT NULL DEFAULT 0
      );
    `);
  }

  assertSorterExists(sorterId) {
    if (!this.sorterIds.has(sorterId)) {
      throw new SorterNotFoundError(sorterId);
    }
  }

  validateMappings(sorterId, mappings) {
    if (!Array.isArray(mappings)) {
      throw new InvalidMappingPayloadError('Mappings payload must be an array');
    }

    const sorter = this.sorterConfigById.get(sorterId);

    mappings.forEach((mapping) => {
      if (!Number.isInteger(mapping.laneId) || mapping.laneId < 1 || mapping.laneId > sorter.laneCount) {
        throw new InvalidMappingPayloadError(`Invalid laneId for sorter ${sorterId}: ${mapping.laneId}`);
      }

      const validObjectValues = this.objectTypeValuesById.get(mapping.objectType);

      if (!validObjectValues) {
        throw new InvalidMappingPayloadError(`Invalid objectType: ${mapping.objectType}`);
      }

      if (!validObjectValues.has(mapping.objectValue)) {
        throw new InvalidMappingPayloadError(
          `Invalid objectValue for ${mapping.objectType}: ${mapping.objectValue}`,
        );
      }
    });
  }

  getCurrentVersion(sorterId) {
    const row = this.db
      .prepare('SELECT version FROM mapping_versions WHERE sorter_id = ?')
      .get(sorterId);

    return row?.version ?? 0;
  }

  async getSorters() {
    return [...this.sorters];
  }

  async getMappings(sorterId) {
    this.assertSorterExists(sorterId);

    const mappings = this.db
      .prepare(
        `SELECT lane_id, object_type, object_value
         FROM mappings
         WHERE sorter_id = ?
         ORDER BY lane_id, object_type, object_value`,
      )
      .all(sorterId)
      .map((row) => ({
        laneId: row.lane_id,
        objectType: row.object_type,
        objectValue: row.object_value,
      }));

    return {
      sorterId,
      version: this.getCurrentVersion(sorterId),
      mappings,
    };
  }

  async saveMappings(sorterId, { version, mappings }) {
    this.assertSorterExists(sorterId);
    this.validateMappings(sorterId, mappings);

    const transaction = this.db.transaction(() => {
      const currentVersion = this.getCurrentVersion(sorterId);

      if (version !== currentVersion) {
        throw new VersionConflictError(currentVersion);
      }

      this.db.prepare('DELETE FROM mappings WHERE sorter_id = ?').run(sorterId);

      const insert = this.db.prepare(
        `INSERT INTO mappings (sorter_id, lane_id, object_type, object_value)
         VALUES (?, ?, ?, ?)`,
      );

      mappings.forEach((mapping) => {
        insert.run(
          sorterId,
          mapping.laneId,
          mapping.objectType,
          mapping.objectValue,
        );
      });

      const nextVersion = currentVersion + 1;

      this.db
        .prepare(
          `INSERT INTO mapping_versions (sorter_id, version)
           VALUES (?, ?)
           ON CONFLICT(sorter_id) DO UPDATE SET version = excluded.version`,
        )
        .run(sorterId, nextVersion);

      return { sorterId, version: nextVersion };
    });

    return transaction();
  }

  async deleteMappings(sorterId) {
    this.assertSorterExists(sorterId);

    const transaction = this.db.transaction(() => {
      const currentVersion = this.getCurrentVersion(sorterId);
      this.db.prepare('DELETE FROM mappings WHERE sorter_id = ?').run(sorterId);

      const nextVersion = currentVersion + 1;
      this.db
        .prepare(
          `INSERT INTO mapping_versions (sorter_id, version)
           VALUES (?, ?)
           ON CONFLICT(sorter_id) DO UPDATE SET version = excluded.version`,
        )
        .run(sorterId, nextVersion);

      return { sorterId, version: nextVersion };
    });

    return transaction();
  }
}
