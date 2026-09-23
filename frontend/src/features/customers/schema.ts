/**
 * features/customers/schema.ts — AutoPartsERP
 *
 * Zod schema for Customer create/update.
 * This is the single source of truth for form validation and TypeScript types.
 * The inferred type `CustomerForm` replaces both the old `FormState` and the
 * separate `CreateCustomer`/`UpdateCustomer` payload types.
 *
 * Phase 3 — feat(frontend): add Zod customer schema (phase3)
 */
import { z } from 'zod';

const phoneRegex = /^$|^\+?[\d\s\-().]{6,20}$/;

export const customerSchema = z.object({
  code: z
    .string()
    .min(1, 'الكود مطلوب')
    .max(50, 'الكود طويل جداً')
    .regex(/^[A-Za-z0-9_-]+$/, 'الكود يقبل حروف وأرقام وشرطة فقط'),

  name: z
    .string()
    .min(1, 'الاسم مطلوب')
    .max(200, 'الاسم طويل جداً'),

  type: z.enum(['RETAIL', 'WORKSHOP', 'WHOLESALE'], {
    errorMap: () => ({ message: 'يرجى اختيار نوع الزبون' }),
  }),

  phone: z
    .string()
    .regex(phoneRegex, 'رقم الهاتف غير صالح')
    .optional()
    .or(z.literal('')),

  phone2: z
    .string()
    .regex(phoneRegex, 'رقم الهاتف غير صالح')
    .optional()
    .or(z.literal('')),

  address: z.string().max(500).optional().or(z.literal('')),
  city:    z.string().max(100).optional().or(z.literal('')),

  /** Not entered any more (the limit is in dollars); kept at 0 so the API contract is unchanged. */
  creditLimitSyp: z.coerce.number().default(0),

  creditLimitUsd: z.coerce
    .number({ invalid_type_error: 'يجب أن يكون رقماً' })
    .min(0, 'لا يمكن أن يكون سالباً')
    .default(0),

  paymentTermsDays: z.coerce
    .number({ invalid_type_error: 'يجب أن يكون رقماً' })
    .int('يجب أن يكون عدداً صحيحاً')
    .min(0)
    .max(365, 'لا يمكن أن يتجاوز 365 يوماً')
    .default(0),

  notes: z.string().max(2000, 'الملاحظات طويلة جداً').optional().or(z.literal('')),

  /** '' = no rep (sent as the empty guid); undefined = leave as it is. */
  assignedSalesRep: z.string().optional(),
});

/** Full inferred type — used for form values AND API payload (create). */
export type CustomerForm = z.infer<typeof customerSchema>;

/** Update payload: same schema but `code` is excluded (immutable after create). */
export const updateCustomerSchema = customerSchema.omit({ code: true });
export type UpdateCustomerForm = z.infer<typeof updateCustomerSchema>;

/** Deactivate-reason schema for the confirm dialog. */
export const deactivateReasonSchema = z.object({
  reason: z.string().min(1, 'سبب إلغاء التفعيل مطلوب').max(500),
});
export type DeactivateReasonForm = z.infer<typeof deactivateReasonSchema>;
