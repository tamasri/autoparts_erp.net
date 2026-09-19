import { useEffect, useMemo, useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { invoicesApi } from '../../api/endpoints/invoices';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';
import StatusBadge from '../../components/common/StatusBadge';

type Invoice = {
  id: string;
  invoiceNumber?: string;
  customerName?: string;
  invoiceDate?: string;
  dueDate?: string;
  totalSyp?: number;
  totalUsd?: number;
  balanceSyp?: number;
  status?: string;
};

const STATUS_TABS = [
  { key: 'ALL', label: 'الكل' },
  { key: 'DRAFT', label: 'مسودة' },
  { key: 'CONFIRMED', label: 'مؤكدة' },
  { key: 'POSTED', label: 'مرحّلة' },
  { key: 'VOID', label: 'ملغاة' },
];

export default function Invoices(): JSX.Element {
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [status, setStatus] = useState<string>('ALL');
  const [items, setItems] = useState<Invoice[]>([]);

  useEffect(() => {
    let mounted = true;
    async function load(): Promise<void> {
      setLoading(true);
      setError('');
      try {
        const res = await invoicesApi.getInvoices({
          page: 1,
          pageSize: 50,
          status: status === 'ALL' ? undefined : status,
        });
        if (mounted) setItems(unwrapList<Invoice>(res.data));
      } catch (e: unknown) {
        if (!mounted) return;
        const msg = (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.detail
          ?? (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.message
          ?? 'تعذر تحميل الفواتير';
        setError(msg);
      } finally {
        if (mounted) setLoading(false);
      }
    }
    void load();
    return () => { mounted = false; };
  }, [status]);

  const today = useMemo(() => new Date(), []);

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      {/* Page Header */}
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">الفواتير</h1>
          <div className="vex-page-header__breadcrumb">إدارة وتتبع فواتير المبيعات</div>
        </div>
        <button type="button" onClick={() => navigate('/invoices/new')} className="btn-primary">
          ＋ فاتورة جديدة
        </button>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      {/* Status Tab Pills */}
      <div style={{ display: 'flex', gap: 8, marginBottom: 16, flexWrap: 'wrap' }}>
        {STATUS_TABS.map((tab) => (
          <button
            key={tab.key}
            type="button"
            onClick={() => setStatus(tab.key)}
            style={{
              border: 'none',
              borderRadius: 'var(--radius-pill)',
              padding: '7px 18px',
              cursor: 'pointer',
              fontSize: 13,
              fontWeight: 600,
              fontFamily: 'inherit',
              transition: 'all var(--transition-fast)',
              background: status === tab.key
                ? 'linear-gradient(135deg, var(--clr-primary), var(--clr-primary-mid))'
                : 'var(--clr-surface-2)',
              color: status === tab.key ? '#fff' : 'var(--txt-secondary)',
              boxShadow: status === tab.key ? '0 2px 8px rgba(92,84,255,0.3)' : 'none',
            }}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {/* Table Card */}
      <div className="vex-card vex-card--no-pad">
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead>
              <tr>
                <th>رقم الفاتورة</th>
                <th>العميل</th>
                <th>التاريخ</th>
                <th>تاريخ الاستحقاق</th>
                <th>الإجمالي ل.س</th>
                <th>الإجمالي $</th>
                <th>المتبقي ل.س</th>
                <th>الحالة</th>
              </tr>
            </thead>
            <tbody>
              {items.length === 0 ? (
                <tr>
                  <td colSpan={8} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '36px 0' }}>
                    لا توجد فواتير لهذه الفئة
                  </td>
                </tr>
              ) : items.map((invoice) => {
                const due = invoice.dueDate ? new Date(invoice.dueDate) : null;
                const overdue = Boolean(due && due < today && (invoice.status ?? '').toUpperCase() === 'POSTED');
                return (
                  <tr
                    key={invoice.id}
                    onClick={() => navigate(`/invoices/${invoice.id}`)}
                    style={{ cursor: 'pointer' }}
                  >
                    <td>
                      <Link
                        to={`/invoices/${invoice.id}`}
                        onClick={(e) => e.stopPropagation()}
                        style={{ color: 'var(--clr-primary)', fontWeight: 600, textDecoration: 'none' }}
                      >
                        {invoice.invoiceNumber ?? invoice.id.slice(0, 8)}
                      </Link>
                    </td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{invoice.customerName ?? '-'}</td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{invoice.invoiceDate ?? '-'}</td>
                    <td>
                      {overdue ? (
                        <span className="badge badge--danger">{invoice.dueDate}</span>
                      ) : (
                        <span style={{ color: 'var(--txt-secondary)' }}>{invoice.dueDate ?? '-'}</span>
                      )}
                    </td>
                    <td style={{ fontWeight: 600 }}>{Number(invoice.totalSyp ?? 0).toLocaleString('en-US')}</td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{Number(invoice.totalUsd ?? 0).toLocaleString('en-US')}</td>
                    <td style={{ fontWeight: 600, color: Number(invoice.balanceSyp ?? 0) > 0 ? 'var(--clr-danger)' : 'var(--txt-primary)' }}>
                      {Number(invoice.balanceSyp ?? 0).toLocaleString('en-US')}
                    </td>
                    <td><StatusBadge status={invoice.status ?? 'UNKNOWN'} type="invoice" /></td>
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
