import { mkdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createDatabaseAdapter } from './adapters/index.js';
import { createApp } from './app.js';
import { loadObjectTypes, loadSorters } from './config/index.js';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const port = Number(process.env.PORT ?? 3001);
const databaseFile =
  process.env.DB_FILE ?? path.join(__dirname, 'data', 'lane-config.db');
mkdirSync(path.dirname(databaseFile), { recursive: true });
const sorters = loadSorters();
const objectTypes = loadObjectTypes();

const adapter = createDatabaseAdapter({
  type: process.env.DB_ADAPTER ?? 'sqlite',
  filename: databaseFile,
  sorters,
  objectTypes,
});

const app = createApp({
  adapter,
  objectTypes,
});

app.listen(port, () => {
  console.log(`Lane configuration API listening on port ${port}`);
});
