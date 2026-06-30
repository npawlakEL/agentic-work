import { DatabaseAdapter } from './base.js';
import { SqliteDatabaseAdapter } from './sqlite.js';

export const createDatabaseAdapter = ({ type = 'sqlite', ...options }) => {
  if (type === 'sqlite') {
    return new SqliteDatabaseAdapter(options);
  }

  throw new Error(`Unsupported database adapter: ${type}`);
};

export { DatabaseAdapter };
