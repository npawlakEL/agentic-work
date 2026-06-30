import { Router } from 'express';

export const createSortersRouter = (model) => {
  const router = Router();

  router.get('/', async (_request, response, next) => {
    try {
      response.json(await model.getSorters());
    } catch (error) {
      next(error);
    }
  });

  return router;
};
