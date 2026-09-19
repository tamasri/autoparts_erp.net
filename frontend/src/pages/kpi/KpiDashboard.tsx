import { useEffect, useState } from 'react';
import { customersApi } from '../../api/endpoints/customers';
import { invoicesApi } from '../../api/endpoints/invoices';
import { inventoryApi } from '../../api/endpoints/inventory';
import { inventoryAlertsApi } from '../../api/endpoints/inventoryAlerts';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import KpiCard from '../../components/common/KpiCard';
import LoadingSpinner from '../../components/common/LoadingSpinner';

export default function KpiDashboard(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  type KpiData = {
    cusTotal: number;
    invPostedTotal: number;
    receivables: number;
    inStock: number;
    outOfStock: number;
    activeAlerts: number;
  };

  const [kpis, setKpis] = useState<KpiData | null>(null);

  useEffect(() => {
    let mounted = true;
    async function load(): Promise<void> {
      setLoading(true);
      setError('');
      try {
        const [cusRes, invPostedRes, invAllRes, stkRes, alertsRes] = await Promise.all([
          customersApi.getCustomers({ page: 1, pageSize: 1, isActive: true }),
          invoicesApi.getInvoices({ page: 1, pageSize: 1, status: 'POSTED' }),
          invoicesApi.getInvoices({ page: 1, pageSize: 200 }),
          inventoryApi.getStock({ page: 1, pageSize: 200 }),
          inventoryAlertsApi.list(),
        ]);
        if (!mounted) return;

        const cusTotal = (cusRes.data as { data?: { totalCount?: number } })?.data?.totalCount ?? unwrapList(cusRes.data).length;
        const invPostedTotal = (invPostedRes.data as { data?: { totalCount?: number } })?.data?.totalCount ?? unwrapList(invPostedRes.data).length;

        type InvRow = { balanceSyp?: number; status?: string };
        const allInvoices = unwrapList<InvRow>(invAllRes.data);
        const receivables = allInvoices
          .filter((i) => (i.status ?? '').toUpperCase() === 'POSTED')
          .reduce((sum, i) => sum + Number(i.balanceSyp ?? 0), 0);

        type StkRow = { quantityOnHand?: number; totalStock?: number };
        const stockRows = unwrapList<StkRow>(stkRes.data);
        const inStock = stockRows.filter((s) => Number(s.totalStock ?? s.quantityOnHand ?? 0) > 0).length;
        const outOfStock = stockRows.filter((s) => Number(s.totalStock ?? s.quantityOnHand ?? 0) <= 0).length;

        type AlertRow = { status?: string };
        const alerts = unwrapList<AlertRow>(alertsRes.data);
        const activeAlerts = alerts.filter((a) => (a.status ?? '').toUpperCase() !== 'RESOLVED').length;

        setKpis({ cusTotal, invPostedTotal, receivables, inStock, outOfStock, activeAlerts });
      } catch (e: unknown) {
        if (!mounted) return;
        const r = e as { response?: { data?: { detail?: string; message?: string } } };
        setError(r.response?.data?.detail ?? r.response?.data?.message ?? 'تعذر تحميل مؤشرات الأداء');
      } finally {
        if (mounted) setLoading(false);
      }
    }
    void load();
    return () => { mounted = false; };
  }, []);

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">مؤشرات الأداء الرئيسية</h1>
          <div className="vex-page-header__breadcrumb">لمحة شاملة عن أداء المنشأة</div>
        </div>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      {kpis ? (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 16 }}>
          <KpiCard
            title="العملاء النشطون"
            value={kpis.cusTotal}
            icon="👥"
            colorVariant="success"
            trend="إجمالي العملاء المفعّلين"
          />
          <KpiCard
            title="فواتير مرحّلة"
            value={kpis.invPostedTotal}
            icon="🧾"
            colorVariant="primary"
            trend="الفواتير ذات الحالة POSTED"
          />
          <KpiCard
            title="الذمم المدينة"
            value={kpis.receivables.toLocaleString('en-US')}
            unit="ل.س"
            icon="💰"
            colorVariant="warning"
            trend="مجموع الأرصدة المستحقة"
          />
          <KpiCard
            title="أصناف متوفرة"
            value={kpis.inStock}
            icon="✅"
            colorVariant="success"
            trend="أصناف بمخزون > 0"
          />
          <KpiCard
            title="أصناف نافدة"
            value={kpis.outOfStock}
            icon="📦"
            colorVariant={kpis.outOfStock > 0 ? 'danger' : 'success'}
            trend="أصناف بمخزون = 0"
          />
          <KpiCard
            title="تنبيهات المخزون"
            value={kpis.activeAlerts}
            icon="🚨"
            colorVariant={kpis.activeAlerts > 0 ? 'danger' : 'success'}
            trend="تنبيهات غير مغلقة"
          />
        </div>
      ) : null}
    </div>
  );
}
