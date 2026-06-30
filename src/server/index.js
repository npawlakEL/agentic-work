import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createDatabaseAdapter } from './adapters/index.js';
import { createApp } from './app.js';
import { loadObjectTypes, loadSorters } from './config/index.js';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const port = Number(process.env.PORT ?? 3001);
const adapter = createDatabaseAdapter({
  type: process.env.DB_ADAPTER ?? 'sqlite',
  filename: process.env.DB_FILE ?? path.join(__dirname, 'data', 'lane-config.db'),
  sorters: loadSorters(),
});

const app = createApp({
  adapter,
  objectTypes: loadObjectTypes(),
});

app.listen(port, () => {
  console.log(`Lane configuration API listening on port ${port}`);
});
