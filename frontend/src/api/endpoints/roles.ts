import { apiClient } from '../client';

export const rolesApi = {
  getRoles: () => apiClient.get('/roles'),
  getRoleById: (roleId: string) => apiClient.get(`/roles/${roleId}`),
  getAllPermissions: () => apiClient.get('/roles/permissions/all'),
  createRole: (payload: unknown) => apiClient.post('/roles', payload),
  grantPermission: (roleId: string, permissionCode: string) =>
    apiClient.post(`/roles/${roleId}/permissions/grant`, { permissionCode }),
  revokePermission: (roleId: string, permissionCode: string) =>
    apiClient.post(`/roles/${roleId}/permissions/revoke`, { permissionCode }),
};
