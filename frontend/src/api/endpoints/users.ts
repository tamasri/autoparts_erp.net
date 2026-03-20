import { apiClient } from '../client';

export const usersApi = {
  getUsers: (page = 1, pageSize = 20) => apiClient.get('/users', { params: { page, pageSize } }),
  getUserById: (userId: string) => apiClient.get(`/users/${userId}`),
  createUser: (payload: unknown) => apiClient.post('/users', payload),
  updateUser: (userId: string, payload: unknown) => apiClient.put(`/users/${userId}`, payload),
  deactivateUser: (userId: string, payload: { reason: string; reasonCode?: string }) =>
    apiClient.post(`/users/${userId}/deactivate`, payload),
  assignRoles: (userId: string, payload: { roleIds: string[] }) =>
    apiClient.post(`/users/${userId}/roles`, payload),
};
