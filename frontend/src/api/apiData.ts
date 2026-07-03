/**
 * Shared response-envelope unwrapping helpers.
 *
 * The backend wraps every payload in the standard `ApiResponse` envelope:
 *   { isSuccess, isPending, message, data, error }
 * List endpoints additionally page their results as:
 *   data: { items: T[], page, pageSize, totalCount }
 *
 * These helpers are the single source of truth for reading that envelope on the
 * frontend, replacing the per-page `getRows` / `node` copies that previously probed
 * multiple shapes defensively.
 */

type Envelope<T> = { data?: T };
type Paged<T> = { items?: T[]; data?: T[] };

/** Unwrap a list payload into a plain array, tolerating envelope + pagination shapes. */
export function unwrapList<T>(payload: unknown): T[] {
  const root = payload as Envelope<unknown> | undefined;
  const data = (root?.data ?? payload) as Paged<T> | T[] | undefined;
  if (Array.isArray(data)) return data;
  if (Array.isArray(data?.items)) return data.items as T[];
  if (Array.isArray(data?.data)) return data.data as T[];
  return [];
}

/** Unwrap a single-object payload from the envelope, or null when absent. */
export function unwrapNode<T>(payload: unknown): T | null {
  const root = payload as Envelope<T> | undefined;
  const data = (root?.data ?? payload) as T | undefined;
  return (data ?? null) as T | null;
}
