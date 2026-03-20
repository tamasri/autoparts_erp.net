import { apiClient } from '../client';

export type LoginPayload = {
  userNameOrEmail: string;
  password: string;
};

export const authApi = {
  login: (payload: LoginPayload) => apiClient.post('/auth/login', payload),
  refresh: (refreshToken: string) => apiClient.post('/auth/refresh', { refreshToken }),
  logout: (refreshToken: string) => apiClient.post('/auth/logout', { refreshToken }),
  currentUser: () => apiClient.get('/auth/me'),
};
