/** What each accounting screen prints or exports (PDF, Excel, CSV) — described once, rendered by the shared export engine. */
import type {
  BalanceSheet, JournalEntryDetail, LedgerStatement, PartyBalances, ProfitLoss, ReconciliationStatement, StatementLine, TrialBalance,
} from '../../api/endpoints/accounting';
import { num, ymd, type ExportDocument } from '../../lib/exportClient';
import { KIND_LABEL, ROOT_LABEL, VOUCHER_LABEL } from './labels';

const indent = (depth: number, text: string): string => `${'    '.repeat(depth)}${text}`;
const STATUS = { DRAFT: 'مسودة', POSTED: 'مرحّل', VOID: 'ملغى' } as const;
const plain = (name: string): string => name.replace(/ - [A-Z0-9]{1,8}$/, '');

export function entryDocument(d: JournalEntryDetail): ExportDocument {
  const e = d.entry;
  return {
    title: `${e.typeNameAr} ${e.entryNumber}`, subtitle: STATUS[e.status], fileName: `entry-${e.entryNumber}`,
    fields: [
      { label: 'النوع', value: `${e.typeNameAr} (${KIND_LABEL[e.kind]})` }, { label: 'التاريخ', value: ymd(e.entryDate) }, { label: 'المرجع', value: d.referenceNumber },
      { label: 'رقم القيد في ERPNext', value: e.erpNextName }, { label: 'البيان', value: e.narration }, { label: 'وسوم', value: e.tags.map((t) => t.name).join('، ') },
    ],
    tables: [{
      columns: ['#', 'الحساب', 'الزبون / المورّد', 'مدين ($)', 'دائن ($)', 'بيان'],
      rows: d.lines.map((l) => [String(l.lineNumber), plain(l.account), l.partyName ?? '', l.debit ? num(l.debit) : '', l.credit ? num(l.credit) : '', l.narration ?? '']),
      totals: ['', 'الإجمالي', '', num(d.lines.reduce((s, l) => s + l.debit, 0)), num(d.lines.reduce((s, l) => s + l.credit, 0)), ''], numericColumns: [3, 4],
    }],
    footer: d.voidReason ? `سبب الإلغاء: ${d.voidReason}` : undefined,
  };
}

export function trialBalanceDocument(tb: TrialBalance): ExportDocument {
  const dr = (v: number): string => (v > 0 ? num(v) : '');
  const cr = (v: number): string => (v < 0 ? num(-v) : '');
  return {
    title: 'ميزان المراجعة', subtitle: `من ${ymd(tb.from)} إلى ${ymd(tb.to)}`, fileName: 'trial-balance', fields: [{ label: 'التوازن', value: tb.isBalanced ? 'متوازن' : 'غير متوازن' }],
    tables: [{
      columns: ['الحساب', 'الفئة', 'الافتتاحي مدين', 'الافتتاحي دائن', 'حركة مدين', 'حركة دائن', 'الختامي مدين', 'الختامي دائن'],
      rows: tb.lines.map((l) => [indent(l.depth, l.accountName), ROOT_LABEL[l.rootType ?? ''] ?? '', dr(l.opening), cr(l.opening), num(l.debit || null), num(l.credit || null), dr(l.closing), cr(l.closing)]),
      totals: ['الإجمالي', '', '', '', '', '', num(tb.totalDebit), num(tb.totalCredit)], numericColumns: [2, 3, 4, 5, 6, 7],
    }],
  };
}

const section = (title: string, lines: StatementLine[], total: number, label: string) => ({
  title, columns: ['الحساب', 'المبلغ ($)'], rows: lines.map((l) => [indent(l.depth, l.accountName), num(l.amount)]), totals: [label, num(total)], numericColumns: [1],
});

export function balanceSheetDocument(b: BalanceSheet): ExportDocument {
  return {
    title: 'الميزانية العمومية', subtitle: `كما في ${ymd(b.asOf)}`, fileName: 'balance-sheet',
    fields: [{ label: 'التوازن', value: b.isBalanced ? 'الأصول = الالتزامات + حقوق الملكية' : 'غير متوازنة' }, { label: 'صافي الربح حتى التاريخ', value: num(b.netProfit) }],
    tables: [
      section('الأصول', b.assets, b.totalAssets, 'إجمالي الأصول'),
      section('الالتزامات', b.liabilities, b.totalLiabilities, 'إجمالي الالتزامات'),
      section('حقوق الملكية', [...b.equity, { account: '_np', accountName: 'أرباح الفترة (غير مقفلة)', parent: null, depth: 0, isGroup: false, amount: b.netProfit }], b.totalEquity, 'إجمالي حقوق الملكية'),
    ],
  };
}

export function profitLossDocument(p: ProfitLoss): ExportDocument {
  return {
    title: 'قائمة الأرباح والخسائر', subtitle: `من ${ymd(p.from)} إلى ${ymd(p.to)}`, fileName: 'profit-loss', fields: [{ label: p.netProfit >= 0 ? 'صافي الربح' : 'صافي الخسارة', value: num(p.netProfit) }],
    tables: [section('الإيرادات', p.income, p.totalIncome, 'إجمالي الإيرادات'), section('المصروفات', p.expenses, p.totalExpenses, 'إجمالي المصروفات')],
  };
}

/** <paramref name="omitted"/> = how many lines of the period are not in <paramref name="l"/> because the export limit was reached. */
export function ledgerDocument(l: LedgerStatement, party?: string, omitted = 0): ExportDocument {
  return {
    title: `كشف حساب: ${plain(l.account)}`, subtitle: `من ${ymd(l.from)} إلى ${ymd(l.to)}${party ? ` — ${party}` : ''}`, fileName: 'ledger-statement',
    fields: [{ label: 'الرصيد الافتتاحي', value: num(l.opening) }, { label: 'الرصيد الختامي', value: num(l.closing) }, ...(omitted > 0 ? [{ label: 'تنبيه', value: `الفترة تحوي ${omitted.toLocaleString('en-US')} حركة إضافية لم تُدرج في الملف (حد التصدير)؛ ضيّق المدة` }] : []),
      ...(l.tagFiltered ? [{ label: 'ملاحظة', value: 'الحركات الموسومة فقط، والرصيد تراكمي لها' }] : [])],
    tables: [{
      columns: ['التاريخ', 'المستند', 'الرقم', 'الحساب', 'مدين', 'دائن', 'الرصيد', 'وسوم'],
      rows: l.rows.map((r) => [ymd(r.postingDate), VOUCHER_LABEL[r.voucherType ?? ''] ?? r.voucherType ?? '', r.voucherNo ?? '', r.party ?? '', r.debit ? num(r.debit) : '', r.credit ? num(r.credit) : '', num(r.balance), r.tags.map((t) => t.name).join('، ')]),
      totals: ['', '', '', 'الإجمالي', num(l.totalDebit), num(l.totalCredit), num(l.closing), ''], numericColumns: [4, 5, 6],
    }],
  };
}

export function partyBalancesDocument(pb: PartyBalances): ExportDocument {
  const customers = pb.partyType === 'CUSTOMER';
  return {
    title: customers ? 'الذمم المدينة (الزبائن)' : 'الذمم الدائنة (الموردون)', subtitle: `كما في ${ymd(pb.asOf)}`, fileName: customers ? 'receivables' : 'payables', fields: [],
    tables: [{
      columns: [customers ? 'الزبون' : 'المورّد', 'الرصيد ($)', 'غير مستحق', '1–30 يوماً', '31–60', '61–90', 'أكثر من 90', 'غير مخصص'],
      rows: pb.rows.map((r) => [r.party, num(r.balance), num(r.current), num(r.days1To30), num(r.days31To60), num(r.days61To90), num(r.over90), num(r.unallocated)]),
      totals: ['الإجمالي', num(pb.total), ...(['current', 'days1To30', 'days31To60', 'days61To90', 'over90', 'unallocated'] as const).map((k) => num(pb.rows.reduce((s, r) => s + r[k], 0)))],
      numericColumns: [1, 2, 3, 4, 5, 6, 7],
    }],
  };
}

export function reconciliationStatementDocument(s: ReconciliationStatement): ExportDocument {
  const natural = (d: number, c: number): number => (s.debitNormal ? d - c : c - d);
  return {
    title: `كشف تسوية: ${plain(s.account)}`, subtitle: `كما في ${ymd(s.asOf)}`, fileName: 'reconciliation-statement',
    fields: [
      { label: 'الرصيد حسب الدفاتر', value: num(s.bookBalance) },
      { label: s.debitNormal ? 'ناقص: مدين لم يُسوَّ (إيداعات بالطريق)' : 'ناقص: مدين لم يُسوَّ', value: num(s.unclearedDebits) },
      { label: s.debitNormal ? 'زائد: دائن لم يُسوَّ (شيكات لم تُصرف)' : 'زائد: دائن لم يُسوَّ', value: num(s.unclearedCredits) },
      { label: 'الرصيد حسب الكشف', value: num(s.statementBalance) }, { label: 'آخر تسوية', value: s.lastReconciledOn ? ymd(s.lastReconciledOn) : 'لا توجد' },
    ],
    tables: [{
      title: 'حركات لم تُسوَّ', columns: ['التاريخ', 'المستند', 'الرقم', 'البيان', 'مدين', 'دائن'],
      rows: s.uncleared.map((r) => [ymd(r.postingDate), VOUCHER_LABEL[r.voucherType ?? ''] ?? r.voucherType ?? '', r.voucherNo ?? '', r.remarks ?? '', r.debit ? num(r.debit) : '', r.credit ? num(r.credit) : '']),
      totals: ['', '', '', 'الإجمالي', num(s.unclearedDebits), num(s.unclearedCredits)], numericColumns: [4, 5],
    }],
    footer: `صافي ما لم يُسوَّ بالاتجاه الطبيعي للحساب: ${num(natural(s.unclearedDebits, s.unclearedCredits))}`,
  };
}
