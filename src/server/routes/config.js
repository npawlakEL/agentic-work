import { Router } from 'express';

export const createConfigRouter = ({ objectTypes }) => {
  const router = Router();

  router.get('/object-types', (_request, response) => {
    response.json(objectTypes);
  });

  return router;
};
