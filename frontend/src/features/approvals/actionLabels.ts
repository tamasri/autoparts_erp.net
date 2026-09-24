/** What each governed action is, in words (the code name is shown for anything not listed). Used by the approvals screen and live notices. */
export const APPROVAL_ACTIONS: Record<string, string> = {
  ShipTransferOrderCommand: 'شحن أمر تحويل', TransferStockCommand: 'تحويل مخزون مباشر', CreateTransferRequestCommand: 'طلب تحويل',
  PostJournalEntryCommand: 'ترحيل قيد', VoidJournalEntryCommand: 'إلغاء قيد', PostInvoiceCommand: 'ترحيل فاتورة', VoidInvoiceCommand: 'إلغاء فاتورة',
  AssignRolesToUserCommand: 'تعديل أدوار مستخدم', DeactivateUserCommand: 'إيقاف مستخدم', SetUserWarehousesCommand: 'تعيين مستودعات مستخدم',
  LockPeriodCommand: 'إقفال فترة', UnlockPeriodCommand: 'فتح فترة',
};

export const approvalActionLabel = (code?: string | null): string => (code ? APPROVAL_ACTIONS[code] ?? code : '');
