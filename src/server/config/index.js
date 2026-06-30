import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const configDirectory = path.dirname(fileURLToPath(import.meta.url));

const loadJson = (fileName) =>
  JSON.parse(readFileSync(path.join(configDirectory, fileName), 'utf8'));

export const loadSorters = () => loadJson('sorters.json');
export const loadObjectTypes = () => loadJson('object-types.json');
