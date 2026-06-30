import { SqliteDatabaseAdapter } from './sqlite.js';

export class DatabaseAdapter {
  async getSorters() {
    throw new Error('getSorters not implemented');
  }

  async getMappings() {
    throw new Error('getMappings not implemented');
  }

  async saveMappings() {
    throw new Error('saveMappings not implemented');
  }

  async deleteMappings() {
    throw new Error('deleteMappings not implemented');
  }
}

export const createDatabaseAdapter = ({ type = 'sqlite', ...options }) => {
  if (type === 'sqlite') {
    return new SqliteDatabaseAdapter(options);
  }

  throw new Error(`Unsupported database adapter: ${type}`);
};
