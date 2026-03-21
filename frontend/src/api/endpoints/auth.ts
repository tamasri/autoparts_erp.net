import { client } from '../client';

export const authApi = {
  login: (userNameOrEmail: string, password: string) =>
    client.post('/auth/login', { userNameOrEmail, password }),
  logout: () =>
    client.post('/auth/logout'),
  me: () =>
    client.get('/auth/me'),
  refresh: (refreshToken: string) =>
    client.post('/auth/refresh', { refreshToken }),
};
