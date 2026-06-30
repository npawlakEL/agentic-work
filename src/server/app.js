import cors from 'cors';
import express from 'express';
import { createLaneConfigModel } from './models/lane-config-model.js';
import { createConfigRouter } from './routes/config.js';
import { createMappingsRouter } from './routes/mappings.js';
import { createSortersRouter } from './routes/sorters.js';

export const createApp = ({ adapter, objectTypes }) => {
  const app = express();
  const model = createLaneConfigModel(adapter);

  app.use(cors());
  app.use(express.json());

  app.use('/api/sorters', createSortersRouter(model));
  app.use('/api/config', createConfigRouter({ objectTypes }));
  app.use('/api/mappings', createMappingsRouter(model));

  app.use((error, _request, response, _next) => {
    response.status(500).json({
      error: 'INTERNAL_SERVER_ERROR',
      message: error.message,
    });
  });

  return app;
};
