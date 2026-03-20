import { create } from 'zustand';
import { persist } from 'zustand/middleware';

type AuthState = {
  accessToken: string | null;
  refreshToken: string | null;
  permissions: string[];
  setSession: (accessToken: string, refreshToken: string, permissions: string[]) => void;
  clearSession: () => void;
};

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      accessToken: null,
      refreshToken: null,
      permissions: [],
      setSession: (accessToken, refreshToken, permissions) =>
        set({ accessToken, refreshToken, permissions }),
      clearSession: () =>
        set({
          accessToken: null,
          refreshToken: null,
          permissions: [],
        }),
    }),
    {
      name: 'autoparts-erp-auth',
    },
  ),
);
