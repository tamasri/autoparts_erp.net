/** The application menu. One place to add, rename or move a screen. */
export type NavTab = { to: string; label: string; end?: boolean };
/** A menu entry. With `tabs` it stands for a whole section: the entry stays lit on any of its tabs and the tabs are shown above the page. */
export type NavItem = { to: string; label: string; icon: string; end?: boolean; tabs?: NavTab[] };
export type NavGroup = { title: string; items: NavItem[] };

export const NAV_GROUPS: NavGroup[] = [
  {
    title: 'الإدارة العليا',
    items: [
      { to: '/', label: 'لوحة التحكم', icon: '⊞', end: true },
    ],
  },
  {
    title: 'المبيعات',
    items: [
      { to: '/accounts', label: 'الحسابات', icon: '◎' },
      {
        to: '/invoices', label: 'الفواتير والقبض', icon: '☰',
        tabs: [{ to: '/invoices', label: 'فواتير المبيعات' }, { to: '/payments', label: 'الدفعات والقبض' }],
      },
      { to: '/sales-reps', label: 'المندوبون', icon: '☺' },
    ],
  },
  {
    title: 'المحاسبة',
    items: [
      { to: '/accounting/chart', label: 'شجرة الحسابات', icon: '⌥' },
      { to: '/accounting/entries', label: 'القيود', icon: '✎' },
      { to: '/accounting/reconciliation', label: 'تسوية الحسابات', icon: '⇋' },
      { to: '/accounting/balances', label: 'الذمم', icon: '⇵' },
      { to: '/accounting/reports', label: 'التقارير المالية', icon: '▤' },
      { to: '/fx-rates', label: 'أسعار الصرف', icon: '⇄' },
    ],
  },
  {
    title: 'المشتريات والمخازن',
    items: [
      { to: '/purchasing', label: 'المشتريات', icon: '🛒' },
      { to: '/items', label: 'الأصناف', icon: '◧' },
      {
        to: '/inventory', label: 'المخزون', icon: '▦',
        tabs: [
          { to: '/inventory', label: 'الأرصدة', end: true }, { to: '/inventory/movements', label: 'حركة الأصناف' },
          { to: '/inventory/warehouses', label: 'المستودعات' }, { to: '/inventory/alerts', label: 'التنبيهات' },
        ],
      },
      {
        to: '/inventory/receiving', label: 'عمليات المستودع', icon: '⇌',
        tabs: [
          { to: '/inventory/receiving', label: 'الاستلام' }, { to: '/inventory/transfers', label: 'التحويلات' }, { to: '/inventory/issue-orders', label: 'أوامر الصرف' },
          { to: '/inventory/cycle-counts', label: 'الجرد الدوري' }, { to: '/inventory/adjustments', label: 'التسويات' },
        ],
      },
    ],
  },
  {
    title: 'الإدارة والرقابة',
    items: [
      { to: '/approvals', label: 'الموافقات', icon: '✓' },
      {
        to: '/audit', label: 'سجل التدقيق', icon: '◎',
        tabs: [{ to: '/audit', label: 'سجل التدقيق', end: true }, { to: '/audit/numbering', label: 'الترقيم والمحذوفات' }],
      },
      { to: '/periods', label: 'إقفال الفترات', icon: '⊝' },
      { to: '/accounting/sync', label: 'مزامنة ERPNext', icon: '⇄' },
      { to: '/users', label: 'المستخدمون والأدوار', icon: '◉', tabs: [{ to: '/users', label: 'المستخدمون' }, { to: '/roles', label: 'الأدوار والصلاحيات' }] },
      { to: '/assistant', label: 'مساعد واتساب', icon: '✆' },
    ],
  },
];

/** "＋ إضافة جديد" shortcuts in the top bar. */
export const QUICK_ACTIONS: Array<{ to: string; label: string }> = [
  { to: '/invoices/new', label: 'فاتورة مبيعات' },
  { to: '/payments', label: 'سند قبض' },
  { to: '/accounting/entries', label: 'قيد محاسبي' },
  { to: '/purchasing', label: 'فاتورة شراء' },
  { to: '/purchasing?tab=landed-costs', label: 'قيد رسملة مصاريف شراء' },
  { to: '/inventory/receiving', label: 'استلام بضاعة' },
  { to: '/inventory/transfers', label: 'تحويل مخزون' },
  { to: '/accounts', label: 'حساب جديد' },
];
