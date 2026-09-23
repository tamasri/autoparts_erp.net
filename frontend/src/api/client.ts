import axios, { type InternalAxiosRequestConfig } from 'axios';
import { useAuthStore } from '../stores/authStore';
import { CSRF_HEADER, refreshSession } from '../lib/session';

export const client = axios.create({
  baseURL: '/api/v1',
  timeout: 30000,
  headers: { 'Content-Type': 'application/json', ...CSRF_HEADER },
});

export const apiClient = client;

client.interceptors.request.use((config) => {
  const token = useAuthStore.getState().token;
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// The access token is short-lived: on a 401 get a new one from the refresh cookie and repeat the request once.
// Only when the refresh itself fails is the user signed out (the router then shows the login page).
client.interceptors.response.use(
  (response) => response,
  async (error) => {
    const original = error?.config as (InternalAxiosRequestConfig & { _retried?: boolean }) | undefined;
    const isAuthCall = typeof original?.url === 'string' && original.url.startsWith('/auth/');
    if (error?.response?.status === 401 && original && !original._retried && !isAuthCall) {
      original._retried = true;
      if (await refreshSession()) {
        original.headers.Authorization = `Bearer ${useAuthStore.getState().token}`;
        return client(original);
      }
    }
    return Promise.reject(error);
  },
);
