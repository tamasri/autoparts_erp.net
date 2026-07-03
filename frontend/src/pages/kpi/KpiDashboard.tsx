import { useEffect, useState } from 'react';
import { customersApi } from '../../api/endpoints/customers';
import { invoicesApi } from '../../api/endpoints/invoices';
import { inventoryApi } from '../../api/endpoints/inventory';
import { inventoryAlertsApi } from '../../api/endpoints/inventoryAlerts';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';

type KpiValue = { label: string; value: number | string; unit?: string; icon: string; color: string; sub?: string };

function KpiBlock({ label, value, unit, icon, color, sub }: KpiValue): JSX.Element {
  return (
    <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', padding: '18px', borderRight: `4px solid ${color}` }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'start' }}>
        <div>
          <div style={{ color: '#607d8b', fontSize: '13px', marginBottom: '6px' }}>{label}</div>
          <div style={{ color, fontSize: '30px', fontWeight: 800 }}>{value}{unit ? ` ${unit}` : ''}</div>
          {sub ? <div style={{ color: '#90a4ae', fontSize: '12px', marginTop: '4px' }}>{sub}</div> : null}
        </div>
        <div style={{ fontSize: '26px' }}>{icon}</div>
      </div>
    </div>
  );
}

export default function KpiDashboard(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [kpis, setKpis] = useState<KpiValue[]>([]);

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

        // totalCount from paged envelope
        const cusTotal = (cusRes.data as { data?: { totalCount?: number } })?.data?.totalCount ?? unwrapList(cusRes.data).length;
        const invPostedTotal = (invPostedRes.data as { data?: { totalCount?: number } })?.data?.totalCount ?? unwrapList(invPostedRes.data).length;

        type InvRow = { balanceSyp?: number; totalSyp?: number; status?: string };
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

        setKpis([
          { label: 'العملاء النشطون', value: cusTotal, icon: '👥', color: '#2e7d32', sub: 'إجمالي العملاء المفعّلين' },
          { label: 'فواتير مرحّلة', value: invPostedTotal, icon: '🧾', color: '#1565c0', sub: 'الفواتير ذات الحالة POSTED' },
          { label: 'الذمم المدينة', value: receivables.toLocaleString('en-US'), unit: 'ل.س', icon: '💰', color: '#e65100', sub: 'مجموع الأرصدة المستحقة' },
          { label: 'أصناف متوفرة', value: inStock, icon: '✅', color: '#2e7d32', sub: 'أصناف بمخزون > 0' },
          { label: 'أصناف نافدة', value: outOfStock, icon: '📦', color: outOfStock > 0 ? '#c62828' : '#2e7d32', sub: 'أصناف بمخزون = 0' },
          { label: 'تنبيهات المخزون', value: activeAlerts, icon: '🚨', color: activeAlerts > 0 ? '#c62828' : '#2e7d32', sub: 'تنبيهات غير مغلقة' },
        ]);
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
      <h2 style={{ marginTop: 0 }}>مؤشرات الأداء الرئيسية</h2>
      {error ? <ErrorBanner message={error} /> : null}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: '14px' }}>
        {kpis.map((k) => <KpiBlock key={k.label} {...k} />)}
      </div>
    </div>
  );
}
