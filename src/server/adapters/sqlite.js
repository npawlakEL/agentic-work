import Database from 'better-sqlite3';
import { DatabaseAdapter } from './index.js';

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

export class SqliteDatabaseAdapter extends DatabaseAdapter {
  constructor({ filename = ':memory:', sorters = [] } = {}) {
    super();
    this.db = new Database(filename);
    this.sorters = [...sorters];
    this.sorterIds = new Set(sorters.map((sorter) => sorter.id));
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
