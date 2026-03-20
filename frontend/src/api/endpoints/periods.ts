import { apiClient } from '../client';

export const periodsApi = {
  getLocks: (year?: number, month?: number) => apiClient.get('/periods/locks', { params: { year, month } }),
  lockPeriod: (payload: { periodKey: string; moduleCode: string; reason: string }) =>
    apiClient.post('/periods/lock', payload),
  unlockPeriod: (payload: { periodKey: string; moduleCode: string; reason: string }) =>
    apiClient.post('/periods/unlock', payload),
};
