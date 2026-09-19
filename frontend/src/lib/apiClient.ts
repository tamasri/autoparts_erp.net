/**
 * lib/apiClient.ts — AutoPartsERP
 *
 * Thin wrapper around the shared axios client (`api/client.ts`) that:
 *   1. Unwraps the standard ApiResponse envelope:
 *        { isSuccess, isPending, message, data, error }
 *      producing the inner `data` directly, so TanStack Query `queryFn`s
 *      never need to write `res.data?.data ?? res.data` again.
 *   2. Throws a typed `ApiError` on HTTP errors so TanStack Query's
 *      `error` state carries a structured message.
 *   3. Exposes the raw axios client for edge cases (file uploads, custom headers).
 *
 * Usage:
 *   import { apiGet, apiPost, apiPut, apiDelete } from '../lib/apiClient';
 *   const customers = await apiGet<PagedResponse<Customer>>('/customers', { page: 1, pageSize: 20 });
 *
 * Phase 2 — feat(frontend): add typed apiClient envelope unwrapper (phase2)
 */
import { client } from '../api/client';
import type { AxiosRequestConfig } from 'axios';

// ── Typed error ─────────────────────────────────────────────────────────────

export class ApiError extends Error {
  constructor(
    public readonly statusCode: number,
    message: string,
    public readonly detail?: string,
    public readonly code?: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

// ── Envelope shape returned by the .NET backend ─────────────────────────────

interface ApiEnvelope<T> {
  isSuccess?: boolean;
  isPending?: boolean;
  message?: string;
  data?: T;
  error?: {
    code?: string;
    message?: string;
    detail?: string;
  };
}

// ── Unwrap helper ────────────────────────────────────────────────────────────

function unwrap<T>(envelope: ApiEnvelope<T> | T | undefined): T {
  const env = envelope as ApiEnvelope<T> | undefined;
  // If the response has an `isSuccess` flag, it's our ApiResponse envelope
  if (env && typeof env === 'object' && 'isSuccess' in env) {
    if (!env.isSuccess && env.error) {
      throw new ApiError(
        422,
        env.error.message ?? env.message ?? 'حدث خطأ غير متوقع',
        env.error.detail,
        env.error.code,
      );
    }
    // data may be nested once: { data: { items, totalCount, … } }
    return (env.data ?? env) as T;
  }
  // Raw array or object — already unwrapped (some list endpoints return arrays directly)
  return envelope as T;
}

// ── Public API ───────────────────────────────────────────────────────────────

/** GET and unwrap. `params` become query-string via axios. */
export async function apiGet<T>(
  url: string,
  params?: Record<string, unknown>,
  config?: AxiosRequestConfig,
): Promise<T> {
  const res = await client.get<ApiEnvelope<T>>(url, { params, ...config });
  return unwrap<T>(res.data);
}

/** POST and unwrap. Returns the created / updated entity. */
export async function apiPost<T>(
  url: string,
  body?: unknown,
  config?: AxiosRequestConfig,
): Promise<T> {
  const res = await client.post<ApiEnvelope<T>>(url, body, config);
  return unwrap<T>(res.data);
}

/** PUT and unwrap. */
export async function apiPut<T>(
  url: string,
  body?: unknown,
  config?: AxiosRequestConfig,
): Promise<T> {
  const res = await client.put<ApiEnvelope<T>>(url, body, config);
  return unwrap<T>(res.data);
}

/** DELETE and unwrap. */
export async function apiDelete<T = void>(
  url: string,
  config?: AxiosRequestConfig,
): Promise<T> {
  const res = await client.delete<ApiEnvelope<T>>(url, config);
  return unwrap<T>(res.data);
}

/** Raw axios client for edge cases (file upload, custom headers, streaming). */
export { client as rawClient };

// ── Paged response type ──────────────────────────────────────────────────────

export interface PagedResponse<T> {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
}
