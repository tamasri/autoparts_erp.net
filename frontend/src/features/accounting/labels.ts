/** Arabic names for the accounting vocabulary (ERPNext's own words stay as the keys). */
import type { EntryKind, SyncStatus } from '../../api/endpoints/accounting';

export const ROOT_LABEL: Record<string, string> = { Asset: 'أصول', Liability: 'التزامات', Equity: 'حقوق ملكية', Income: 'إيرادات', Expense: 'مصروفات' };
export const ROOT_COLOR: Record<string, 'primary' | 'warning' | 'secondary' | 'success' | 'error'> = { Asset: 'primary', Liability: 'warning', Equity: 'secondary', Income: 'success', Expense: 'error' };

export const ACCOUNT_TYPE_LABEL: Record<string, string> = {
  Bank: 'مصرف', Cash: 'صندوق', Receivable: 'ذمم مدينة (زبائن)', Payable: 'ذمم دائنة (موردون)', Stock: 'مخزون', Tax: 'ضريبة',
  'Income Account': 'حساب إيراد', 'Expense Account': 'حساب مصروف', 'Cost of Goods Sold': 'تكلفة البضاعة المباعة', 'Fixed Asset': 'أصل ثابت',
  Equity: 'حقوق ملكية', 'Current Asset': 'أصل متداول', 'Current Liability': 'التزام متداول', Liability: 'التزام', 'Direct Expense': 'مصروف مباشر',
  'Indirect Expense': 'مصروف غير مباشر', 'Direct Income': 'إيراد مباشر', 'Indirect Income': 'إيراد غير مباشر', 'Round Off': 'فروق تقريب',
  'Stock Adjustment': 'تسوية مخزون', Temporary: 'مؤقت', Depreciation: 'إهلاك', 'Accumulated Depreciation': 'مجمع الإهلاك',
};
export const accountTypeLabel = (t: string | null | undefined): string => (t ? ACCOUNT_TYPE_LABEL[t] ?? t : '');

export const KIND_LABEL: Record<EntryKind, string> = {
  RECEIPT: 'سند قبض', PAYMENT: 'سند دفع', CONTRA: 'مناقلة', JOURNAL: 'قيد يومية', OPENING: 'قيد افتتاحي', DEBIT_NOTE: 'إشعار مدين', CREDIT_NOTE: 'إشعار دائن',
};

/** A short line under the entry form saying how each kind is meant to be used. */
export const KIND_HINT: Record<EntryKind, string> = {
  RECEIPT: 'سند قبض: اجعل الصندوق أو المصرف مديناً، والحساب المقابل (إيراد، زبون، ...) دائناً.',
  PAYMENT: 'سند دفع: اجعل الحساب المقابل (مصروف، مورّد، ...) مديناً، والصندوق أو المصرف دائناً.',
  CONTRA: 'مناقلة: نقل مبلغ بين الصندوق والمصارف — بلا أطراف.',
  JOURNAL: 'قيد يومية: أي عدد من الأسطر بشرط تساوي المدين والدائن.',
  OPENING: 'قيد افتتاحي: أرصدة أول المدة.',
  DEBIT_NOTE: 'إشعار مدين لحساب.',
  CREDIT_NOTE: 'إشعار دائن لحساب.',
};

export const SYNC_LABEL: Record<SyncStatus, { label: string; color: 'default' | 'success' | 'warning' | 'error' | 'info' }> = {
  NONE: { label: '—', color: 'default' }, PENDING: { label: 'بانتظار الإرسال', color: 'info' }, SYNCED: { label: 'في دفتر الأستاذ', color: 'success' },
  FAILED: { label: 'فشل الإرسال', color: 'error' }, SKIPPED: { label: 'ERPNext غير مفعّل', color: 'warning' }, CANCELLED: { label: 'أُلغي في الدفتر', color: 'default' },
};

export const VOUCHER_LABEL: Record<string, string> = {
  'Sales Invoice': 'فاتورة مبيعات', 'Purchase Invoice': 'فاتورة شراء', 'Payment Entry': 'سند دفع/قبض', 'Journal Entry': 'قيد يومية',
};

/** Where a ledger line's source document opens in this application (null when it has no screen of its own). */
export function sourceRoute(localEntityType: string | null, localEntityId: string | null): string | null {
  if (!localEntityType || !localEntityId) return null;
  switch (localEntityType) {
    case 'Invoice': return `/invoices/${localEntityId}`;
    case 'JournalEntry': return `/accounting/entries?open=${localEntityId}`;
    case 'PurchaseInvoice': return '/purchasing';
    case 'Payment': return '/payments';
    default: return null;
  }
}

/** Account types that need a customer/supplier on every line, and which kind of party they take. */
export const PARTY_ACCOUNT_TYPES: Record<string, 'CUSTOMER' | 'VENDOR'> = { Receivable: 'CUSTOMER', Payable: 'VENDOR' };
