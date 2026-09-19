import { useEffect, useMemo, useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { customersApi } from '../../api/endpoints/customers';
import { partiesApi } from '../../api/endpoints/parties';
import { unwrapList, unwrapNode } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';

type CustomerDetailRow = {
  id: string;
  partyId?: string;
  code?: string;
  name?: string;
  type?: string;
  city?: string;
  phone?: string;
  creditLimitSyp?: number;
  creditLimitUsd?: number;
  paymentTermsDays?: number;
};

type StatementRow = {
  date?: string;
  entryDate?: string;
  type?: string;
  reference?: string;
  debitSyp?: number;
  creditSyp?: number;
  balanceSyp?: number;
};

export default function CustomerDetail(): JSX.Element {
  const { id } = useParams();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [customer, setCustomer] = useState<CustomerDetailRow | null>(null);
  const [statement, setStatement] = useState<StatementRow[]>([]);

  useEffect(() => {
    if (!id) return;
    let mounted = true;
    async function load(): Promise<void> {
      if (!id) return;
      setLoading(true);
      setError('');
      try {
        const customerRes = await customersApi.getCustomerById(id);
        const customerNode = unwrapNode<CustomerDetailRow>(customerRes.data);
        if (!mounted) return;
        setCustomer(customerNode);
        const partyId = customerNode?.partyId;
        if (partyId) {
          const st = await partiesApi.getArStatement(partyId);
          if (mounted) setStatement(unwrapList<StatementRow>(st.data));
        } else {
          const st = await customersApi.getCustomerStatement(id);
          if (mounted) setStatement(unwrapList<StatementRow>(st.data));
        }
      } catch (e: unknown) {
        if (!mounted) return;
        const msg = (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.detail
          ?? (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.message
          ?? 'تعذر تحميل بيانات العميل';
        setError(msg);
      } finally {
        if (mounted) setLoading(false);
      }
    }
    void load();
    return () => { mounted = false; };
  }, [id]);

  const outstanding = useMemo(
    () => statement.length > 0 ? Number(statement[statement.length - 1].balanceSyp ?? 0) : 0,
    [statement],
  );

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      {/* Page Header */}
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">{customer?.name ?? 'تفاصيل العميل'}</h1>
          <div className="vex-page-header__breadcrumb">
            <Link to="/customers" style={{ color: 'var(--clr-primary)', textDecoration: 'none' }}>العملاء</Link>
            {' / '}{customer?.code ?? id}
          </div>
        </div>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      {/* Info Cards Row */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 16, marginBottom: 20 }}>
        {/* Customer Info */}
        <div className="vex-card" style={{ gridColumn: 'span 2' }}>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 16 }}>
            {[
              { label: 'الكود', value: customer?.code ?? '-' },
              { label: 'النوع', value: customer?.type ?? '-' },
              { label: 'المدينة', value: customer?.city ?? '-' },
              { label: 'الهاتف', value: customer?.phone ?? '-' },
              { label: 'الحد الائتماني ل.س', value: Number(customer?.creditLimitSyp ?? 0).toLocaleString('en-US') + ' ل.س' },
              { label: 'الحد الائتماني $', value: '$' + Number(customer?.creditLimitUsd ?? 0).toLocaleString('en-US') },
              { label: 'شروط الدفع', value: (customer?.paymentTermsDays ?? '-') + ' يوم' },
            ].map((item) => (
              <div key={item.label}>
                <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--txt-muted)', textTransform: 'uppercase', letterSpacing: '0.5px', marginBottom: 4 }}>
                  {item.label}
                </div>
                <div style={{ fontSize: 14, fontWeight: 600, color: 'var(--txt-primary)' }}>
                  {item.value}
                </div>
              </div>
            ))}
          </div>
        </div>

        {/* Balance Card */}
        <div className="vex-card" style={{
          background: outstanding > 0
            ? 'linear-gradient(135deg, #fef2f2, #fff)'
            : 'linear-gradient(135deg, #f0fdf4, #fff)',
          borderRight: `4px solid ${outstanding > 0 ? 'var(--clr-danger)' : '#22c55e'}`,
        }}>
          <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--txt-muted)', textTransform: 'uppercase', marginBottom: 8 }}>
            الرصيد المستحق
          </div>
          <div style={{
            fontSize: 28,
            fontWeight: 800,
            color: outstanding > 0 ? 'var(--clr-danger)' : '#22c55e',
          }}>
            {outstanding.toLocaleString('en-US')}
          </div>
          <div style={{ fontSize: 13, color: 'var(--txt-muted)', marginTop: 4 }}>ليرة سورية</div>
        </div>
      </div>

      {/* Statement Table */}
      <div className="vex-card vex-card--no-pad">
        <div style={{ padding: '16px 20px 12px', borderBottom: '1px solid var(--clr-border)' }}>
          <h2 className="vex-section-title" style={{ margin: 0 }}>كشف الحساب</h2>
        </div>
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead>
              <tr>
                <th>التاريخ</th>
                <th>النوع</th>
                <th>المرجع</th>
                <th>مدين</th>
                <th>دائن</th>
                <th>الرصيد</th>
              </tr>
            </thead>
            <tbody>
              {statement.length === 0 ? (
                <tr>
                  <td colSpan={6} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '32px 0' }}>
                    لا توجد حركات مالية
                  </td>
                </tr>
              ) : statement.map((row, idx) => {
                const debit = Number(row.debitSyp ?? 0);
                const credit = Number(row.creditSyp ?? 0);
                return (
                  <tr key={`${row.reference ?? 'row'}-${idx}`}>
                    <td style={{ color: 'var(--txt-secondary)' }}>{row.date ?? row.entryDate ?? '-'}</td>
                    <td>
                      <span style={{ fontSize: 12, color: 'var(--txt-muted)', background: 'var(--clr-surface-2)', padding: '2px 8px', borderRadius: 'var(--radius-sm)' }}>
                        {row.type ?? '-'}
                      </span>
                    </td>
                    <td style={{ fontWeight: 600, color: 'var(--clr-primary)' }}>{row.reference ?? '-'}</td>
                    <td style={{ fontWeight: 600, color: debit > 0 ? 'var(--clr-danger)' : 'var(--txt-muted)' }}>
                      {debit > 0 ? debit.toLocaleString('en-US') : '—'}
                    </td>
                    <td style={{ fontWeight: 600, color: credit > 0 ? '#22c55e' : 'var(--txt-muted)' }}>
                      {credit > 0 ? credit.toLocaleString('en-US') : '—'}
                    </td>
                    <td style={{ fontWeight: 700, color: Number(row.balanceSyp ?? 0) > 0 ? 'var(--clr-danger)' : 'var(--txt-primary)' }}>
                      {Number(row.balanceSyp ?? 0).toLocaleString('en-US')}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
