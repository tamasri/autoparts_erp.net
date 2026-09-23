/** Where the local record behind an ERPNext document lives in this application (by the sync-log entity type). */
export function localRoute(entityType?: string | null, id?: string | null): string | null {
  if (!entityType) return null;
  switch (entityType) {
    case 'Invoice': return id ? `/invoices/${id}` : '/invoices';
    case 'PurchaseInvoice': return '/purchasing';
    case 'Payment': return '/payments';
    case 'SupplierPayment': return '/purchasing?tab=payments';
    case 'JournalEntry': return '/accounting/entries';
    case 'StockAdjustment': return '/inventory/adjustments';
    case 'Party': return id ? `/parties/${id}/statement` : '/accounts';
    case 'Sku': return '/items';
    case 'SalesRep': return id ? `/sales-reps/${id}` : '/sales-reps';
    default: return null;
  }
}

export const DOCTYPE_LABEL: Record<string, string> = {
  Item: 'أصناف', Customer: 'زبائن', Supplier: 'موردون', 'Sales Invoice': 'فواتير مبيعات', 'Purchase Invoice': 'فواتير شراء',
  'Payment Entry': 'سندات دفع وقبض', 'Journal Entry': 'قيود يومية (يدوية، التكلفة، التسويات)', 'Sales Person': 'مندوبو المبيعات',
};
