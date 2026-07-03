import { apiClient } from '../client';

export type CreateFxRate = {
  buyRate: number;
  sellRate: number;
  midRate: number;
  rateDate: string;
};

export const fxRatesApi = {
  getList: (page = 1, pageSize = 30) =>
    apiClient.get('/fx-rates', { params: { page, pageSize } }),
  getLatest: () => apiClient.get('/fx-rates/latest'),
  create: (body: CreateFxRate) => apiClient.post('/fx-rates', body),
};
