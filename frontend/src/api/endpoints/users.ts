import { apiClient } from '../client';

export type UserRole = { roleId: string; code: string; name: string };
export type User = {
  id: string; userName: string; email: string; firstName: string; lastName: string; isActive: boolean; isLockedOut: boolean;
  roles: UserRole[]; createdAtUtc?: string; lastLoginAtUtc?: string | null;
};
export type CreateUserBody = { userName: string; email: string; firstName: string; lastName: string; password: string; roleIds: string[] };
export type UpdateUserBody = { email: string; firstName: string; lastName: string };

export const usersApi = {
  getUsers: (page = 1, pageSize = 20, search?: string, isActive?: boolean) =>
    apiClient.get('/users', { params: { page, pageSize, search: search || undefined, isActive } }),
  getUserById: (userId: string) => apiClient.get(`/users/${userId}`),
  createUser: (payload: CreateUserBody) => apiClient.post('/users', payload, { headers: { 'Idempotency-Key': crypto.randomUUID() } }),
  /** The profile only; roles and (de)activation have their own calls because they are governed. */
  updateUser: (userId: string, payload: UpdateUserBody) => apiClient.put(`/users/${userId}`, payload),
  deactivateUser: (userId: string, payload: { reason: string; reasonCode?: string }) =>
    apiClient.post(`/users/${userId}/deactivate`, payload, { headers: { 'Idempotency-Key': crypto.randomUUID() } }),
  activateUser: (userId: string) => apiClient.post(`/users/${userId}/activate`),
  resetPassword: (userId: string, newPassword: string) => apiClient.post(`/users/${userId}/password`, { newPassword }),
  assignRoles: (userId: string, payload: { roleIds: string[] }) =>
    apiClient.post(`/users/${userId}/roles`, payload, { headers: { 'Idempotency-Key': crypto.randomUUID() } }),
};
