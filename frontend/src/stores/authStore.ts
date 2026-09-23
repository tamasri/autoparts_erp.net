import { create } from 'zustand';

type AuthUser = {
  id: string;
  username: string;
  fullName: string;
  roles: string[];
};

export type SessionPayload = {
  token: string;
  user: AuthUser;
  permissions?: string[];
};

/**
 * The signed-in session, kept in memory only. The access token is never written to localStorage (an injected script could read
 * it there); the refresh token is an HttpOnly cookie the page cannot see at all. After a reload the session is restored by one
 * call to /auth/refresh (see lib/session.ts). `checking` is true until that first call has answered.
 */
type AuthState = {
  token: string | null;
  user: AuthUser | null;
  permissions: string[];
  isAuthenticated: boolean;
  checking: boolean;
  login: (payload: SessionPayload) => void;
  logout: () => void;
  doneChecking: () => void;
};

// Sessions saved by earlier versions (tokens in localStorage) are removed on load.
try { localStorage.removeItem('autoparts-erp-auth'); } catch { /* storage unavailable */ }

export const useAuthStore = create<AuthState>()((set) => ({
  token: null,
  user: null,
  permissions: [],
  isAuthenticated: false,
  checking: true,
  login: (payload) =>
    set({
      token: payload.token,
      user: payload.user,
      permissions: payload.permissions ?? [],
      isAuthenticated: true,
      checking: false,
    }),
  logout: () =>
    set({
      token: null,
      user: null,
      permissions: [],
      isAuthenticated: false,
      checking: false,
    }),
  doneChecking: () => set({ checking: false }),
}));
