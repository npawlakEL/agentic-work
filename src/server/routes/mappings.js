import { Router } from 'express';
import {
  InvalidMappingPayloadError,
  SorterNotFoundError,
  VersionConflictError,
} from '../adapters/errors.js';

export const createMappingsRouter = (model) => {
  const router = Router();

  router.get('/:sorterId', async (request, response, next) => {
    try {
      response.json(await model.getMappings(request.params.sorterId));
    } catch (error) {
      next(error);
    }
  });

  router.post('/:sorterId', async (request, response, next) => {
    try {
      response.json(
        await model.saveMappings(request.params.sorterId, request.body),
      );
    } catch (error) {
      next(error);
    }
  });

  router.delete('/:sorterId', async (request, response, next) => {
    try {
      response.json(await model.deleteMappings(request.params.sorterId));
    } catch (error) {
      next(error);
    }
  });

  router.use((error, _request, response, next) => {
    if (error instanceof VersionConflictError) {
      response.status(409).json({
        error: error.code,
        message: error.message,
        currentVersion: error.currentVersion,
      });
      return;
    }

    if (error instanceof SorterNotFoundError) {
      response.status(404).json({
        error: error.code,
        message: error.message,
      });
      return;
    }

    if (error instanceof InvalidMappingPayloadError) {
      response.status(400).json({
        error: error.code,
        message: error.message,
      });
      return;
    }

    next(error);
  });

  return router;
};
