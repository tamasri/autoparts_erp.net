import { create } from 'zustand';
import { persist } from 'zustand/middleware';

type AuthUser = {
  id: string;
  username: string;
  fullName: string;
  roles: string[];
};

type LoginPayload = {
  token: string;
  refreshToken: string;
  user: AuthUser;
  permissions?: string[];
};

type AuthState = {
  token: string | null;
  refreshToken: string | null;
  user: AuthUser | null;
  permissions: string[];
  isAuthenticated: boolean;
  login: (payload: LoginPayload) => void;
  logout: () => void;
};

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      token: null,
      refreshToken: null,
      user: null,
      permissions: [],
      isAuthenticated: false,
      login: (payload) =>
        set({
          token: payload.token,
          refreshToken: payload.refreshToken,
          user: payload.user,
          permissions: payload.permissions ?? [],
          isAuthenticated: payload.token !== null,
        }),
      logout: () =>
        set({
          token: null,
          refreshToken: null,
          user: null,
          permissions: [],
          isAuthenticated: false,
        }),
    }),
    {
      name: 'autoparts-erp-auth',
    },
  ),
);
