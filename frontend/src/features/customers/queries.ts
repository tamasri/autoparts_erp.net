/**
 * features/customers/queries.ts — AutoPartsERP
 *
 * TanStack Query hooks for Customers.
 * Replaces usePagedList + manual useState/useEffect in Customers.tsx.
 *
 * Key design decisions:
 * - Query keys are structured: ['customers', 'list', params] so mutations
 *   can invalidate all list variants with queryKey: ['customers', 'list'].
 * - `placeholderData: keepPreviousData` prevents table flash when paginating.
 * - staleTime inherits the global 30s default set in QueryClient (main.tsx).
 * - `useSaveCustomer` and `useDeactivateCustomer` auto-invalidate the list
 *   cache on success so the UI stays in sync without a manual reload().
 *
 * Phase 2 — feat(frontend): add TanStack Query customer hooks (phase2)
 */
import { useMutation, useQuery, useQueryClient, keepPreviousData } from '@tanstack/react-query';
import { apiGet, apiPost, apiPut, apiDelete } from '../../lib/apiClient';
import type { PagedResponse } from '../../lib/apiClient';
import type { CustomerForm, UpdateCustomerForm } from './schema';

// ── Types ────────────────────────────────────────────────────────────────────

export type Customer = {
  id: string;
  code: string;
  name: string;
  type: string;
  phone?: string;
  phone2?: string;
  address?: string;
  city?: string;
  creditLimitSyp?: number;
  creditLimitUsd?: number;
  paymentTermsDays?: number;
  balanceSyp?: number;
  notes?: string;
  isActive?: boolean;
  assignedSalesRep?: string | null;
};

export type CustomersParams = {
  page: number;
  pageSize: number;
  search: string;
  type?: string;
  isActive?: boolean;
};

// ── Query keys ───────────────────────────────────────────────────────────────

export const customerKeys = {
  all:    ['customers'] as const,
  lists:  () => [...customerKeys.all, 'list'] as const,
  list:   (p: CustomersParams) => [...customerKeys.lists(), p] as const,
  detail: (id: string) => [...customerKeys.all, 'detail', id] as const,
} as const;

// ── Hooks ────────────────────────────────────────────────────────────────────

/**
 * Fetches a server-paged, searched list of customers.
 * `keepPreviousData` keeps the previous page visible while the next loads —
 * satisfies the acceptance criterion: "no spinner flash between pages".
 */
export function useCustomerList(params: CustomersParams) {
  return useQuery({
    queryKey: customerKeys.list(params),
    queryFn: () =>
      apiGet<PagedResponse<Customer>>('/customers', {
        page:       params.page,
        pageSize:   params.pageSize,
        searchTerm: params.search || undefined,
        type:       params.type,
        isActive:   params.isActive,
      }),
    placeholderData: keepPreviousData,
    // staleTime inherits 30_000 ms from QueryClient default
  });
}

/**
 * Fetches a single customer by ID (used by CustomerDetail).
 */
export function useCustomerById(id: string) {
  return useQuery({
    queryKey: customerKeys.detail(id),
    queryFn: () => apiGet<Customer>(`/customers/${id}`),
    enabled: !!id,
  });
}

/**
 * Creates or updates a customer.
 * On success, invalidates all customer list queries so the list refreshes.
 *
 * Acceptance criterion: "no full-page reload on edit" — achieved because
 * invalidation only refetches the affected query key, not the page.
 */
export function useSaveCustomer(editId: string | null) {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: (form: CustomerForm | UpdateCustomerForm) => {
      if (editId) {
        // Update: omit `code` (immutable)
        const { code: _code, ...updatePayload } = form as CustomerForm;
        void _code; // suppress lint
        return apiPut<Customer>(`/customers/${editId}`, updatePayload);
      }
      return apiPost<Customer>('/customers', form);
    },
    onSuccess: () => {
      // Invalidate all list variants — next focus/mount will refetch
      void qc.invalidateQueries({ queryKey: customerKeys.lists() });
    },
  });
}

/**
 * Deactivates a customer with a reason string.
 * Replaces the window.prompt flow.
 */
export function useDeactivateCustomer() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      apiDelete<void>(`/customers/${id}`, { params: { reason } }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: customerKeys.lists() });
    },
  });
}
