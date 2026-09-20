/** التقارير المالية — trial balance, balance sheet, profit and loss, ledger statement and reconciliation statement; each exports to PDF, Excel and CSV. */
import { useSearchParams } from 'react-router-dom';
import PageHeader from '../../components/ui/PageHeader';
import RoutedTabs from '../../components/ui/RoutedTabs';
import TrialBalanceReport from '../../features/accounting/reports/TrialBalanceReport';
import BalanceSheetReport from '../../features/accounting/reports/BalanceSheetReport';
import ProfitLossReport from '../../features/accounting/reports/ProfitLossReport';
import LedgerReport from '../../features/accounting/reports/LedgerReport';
import ReconciliationStatementReport from '../../features/accounting/reports/ReconciliationStatementReport';

export default function Reports(): JSX.Element {
  const [params] = useSearchParams();
  return (
    <>
      <PageHeader title="التقارير المالية" subtitle="تُحسب مباشرة من دفتر الأستاذ في ERPNext — وكل تقرير يُصدَّر PDF وExcel وCSV" />
      <RoutedTabs tabs={[
        { key: 'ledger', label: 'كشف حساب', content: <LedgerReport initialAccount={params.get('account')} /> },
        { key: 'trial-balance', label: 'ميزان المراجعة', content: <TrialBalanceReport /> },
        { key: 'balance-sheet', label: 'الميزانية العمومية', content: <BalanceSheetReport /> },
        { key: 'profit-loss', label: 'الأرباح والخسائر', content: <ProfitLossReport /> },
        { key: 'reconciliation', label: 'كشف التسوية', content: <ReconciliationStatementReport /> },
      ]} />
    </>
  );
}
