/** The application menu. One place to add, rename or move a screen. */
export type NavItem = { to: string; label: string; icon: string; end?: boolean };
export type NavGroup = { title: string; items: NavItem[] };

export const NAV_GROUPS: NavGroup[] = [
  {
    title: 'الإدارة العليا',
    items: [
      { to: '/', label: 'لوحة التحكم', icon: '⊞', end: true },
      { to: '/kpi', label: 'مؤشرات الأداء', icon: '↗' },
    ],
  },
  {
    title: 'المبيعات والمحاسبة',
    items: [
      { to: '/accounts', label: 'الحسابات', icon: '◎' },
      { to: '/invoices', label: 'الفواتير', icon: '☰' },
      { to: '/payments', label: 'الدفعات والقبض', icon: '＄' },
      { to: '/fx-rates', label: 'أسعار الصرف', icon: '⇄' },
      { to: '/accounting/chart', label: 'شجرة الحسابات', icon: '⌥' },
      { to: '/accounting/erp-documents', label: 'مستندات ERPNext', icon: '▤' },
    ],
  },
  {
    title: 'المشتريات والمخازن',
    items: [
      { to: '/purchasing', label: 'المشتريات', icon: '🛒' },
      { to: '/items', label: 'الأصناف', icon: '◧' },
      { to: '/inventory', label: 'المخزون', icon: '▦', end: true },
      { to: '/inventory/warehouses', label: 'المستودعات', icon: '⌂' },
      { to: '/inventory/movements', label: 'حركة الأصناف', icon: '⇅' },
      { to: '/inventory/receiving', label: 'الاستلام', icon: '↙' },
      { to: '/inventory/transfers', label: 'التحويلات', icon: '⇌' },
      { to: '/inventory/issue-orders', label: 'أوامر الصرف', icon: '↗' },
      { to: '/inventory/cycle-counts', label: 'الجرد الدوري', icon: '↻' },
      { to: '/inventory/adjustments', label: 'التسويات', icon: '⇔' },
      { to: '/inventory/alerts', label: 'التنبيهات', icon: '◬' },
    ],
  },
  {
    title: 'الإدارة والرقابة',
    items: [
      { to: '/approvals', label: 'الموافقات', icon: '✓' },
      { to: '/audit', label: 'سجل التدقيق', icon: '◎' },
      { to: '/accounting/sync', label: 'مزامنة المحاسبة', icon: '⇄' },
      { to: '/periods', label: 'إقفال الفترات', icon: '⊝' },
      { to: '/users', label: 'المستخدمون', icon: '◉' },
      { to: '/roles', label: 'الأدوار', icon: '◈' },
    ],
  },
];

/** "＋ إضافة جديد" shortcuts in the top bar. */
export const QUICK_ACTIONS: Array<{ to: string; label: string }> = [
  { to: '/invoices/new', label: 'فاتورة مبيعات' },
  { to: '/payments', label: 'سند قبض' },
  { to: '/purchasing', label: 'فاتورة شراء' },
  { to: '/inventory/receiving', label: 'استلام بضاعة' },
  { to: '/inventory/transfers', label: 'تحويل مخزون' },
  { to: '/accounts', label: 'حساب جديد' },
];
