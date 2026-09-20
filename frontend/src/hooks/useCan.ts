import { useAuthStore } from '../stores/authStore';

/** Whether the signed-in user holds a permission (the server enforces it too; this only hides buttons that would be refused). */
export function useCan(permission: string): boolean {
  return useAuthStore((s) => s.permissions.includes(permission));
}

export const ACCOUNTING = {
  read: 'accounting:read',
  manageAccounts: 'accounting:manage_accounts',
  postEntries: 'accounting:post_entries',
  reconcile: 'accounting:reconcile',
} as const;
