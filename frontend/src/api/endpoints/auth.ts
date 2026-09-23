import { client } from '../client';

// Refresh and logout live in lib/session.ts: they work from the HttpOnly cookie and must bypass the client's 401 retry.
export const authApi = {
  login: (userNameOrEmail: string, password: string) =>
    client.post('/auth/login', { userNameOrEmail, password }),
  me: () =>
    client.get('/auth/me'),
};
