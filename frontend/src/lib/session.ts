import axios from 'axios';
import { useAuthStore, type SessionPayload } from '../stores/authStore';

/** Sent on every API call; the server requires it on refresh/logout, which a cross-site form cannot add. */
export const CSRF_HEADER = { 'X-Requested-With': 'XMLHttpRequest' } as const;

type RoleNode = string | { code?: string; name?: string };
type SessionResponse = {
  accessToken: string;
  user?: { id?: string; userName?: string; fullName?: string; firstName?: string; lastName?: string; roles?: RoleNode[] };
  permissions?: string[];
};

/** Maps the server's login/refresh answer to the in-memory session. */
export function toSession(raw: unknown, fallbackName = ''): SessionPayload {
  const body = raw as { data?: SessionResponse } & SessionResponse;
  const data = body.data ?? body;
  const u = data.user ?? {};
  const roles = (u.roles ?? []).map((r) => (typeof r === 'string' ? r : r.code ?? r.name ?? '')).filter(Boolean);
  const fullName = u.fullName || [u.firstName, u.lastName].filter(Boolean).join(' ') || u.userName || fallbackName;
  return {
    token: data.accessToken,
    user: { id: u.id ?? '', username: u.userName ?? fallbackName, fullName, roles },
    permissions: data.permissions ?? [],
  };
}

let inFlight: Promise<boolean> | null = null;

async function callRefresh(): Promise<boolean> {
  try {
    // Plain axios, not the API client: its 401 handler would call back into this function.
    const res = await axios.post('/api/v1/auth/refresh', null, { headers: CSRF_HEADER, timeout: 15000 });
    useAuthStore.getState().login(toSession(res.data));
    return true;
  } catch {
    useAuthStore.getState().logout();
    return false;
  }
}

/**
 * Gets a new access token from the HttpOnly refresh cookie. One call at a time in this tab, and one at a time across tabs
 * (Web Locks): refresh tokens are single-use, so two tabs refreshing with the same cookie at once would sign one of them out.
 * The cookie jar is shared, so a tab that waited simply uses the cookie the other tab just received.
 */
export function refreshSession(): Promise<boolean> {
  if (!inFlight) {
    const run = (async (): Promise<boolean> =>
      navigator.locks?.request ? await navigator.locks.request('erp-auth-refresh', callRefresh) : callRefresh())();
    inFlight = run.finally(() => { inFlight = null; });
  }
  return inFlight;
}

/** Ends the session on the server (revokes the refresh token, clears the cookie) and in this tab. */
export async function signOut(): Promise<void> {
  try { await axios.post('/api/v1/auth/logout', null, { headers: CSRF_HEADER, timeout: 10000 }); } catch { /* signed out locally anyway */ }
  useAuthStore.getState().logout();
}
