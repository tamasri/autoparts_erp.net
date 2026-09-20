import { useMemo, useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { invoicesApi } from '../../api/endpoints/invoices';
import { usePagedList } from '../../hooks/usePagedList';
import Pagination from '../../components/common/Pagination';
import ErrorBanner from '../../components/common/ErrorBanner';
import StatusBadge from '../../components/common/StatusBadge';
import ExportMenu from '../../components/ui/ExportMenu';
import { unwrapPaged } from '../../api/apiData';
import { num, ymd, type ExportDocument } from '../../lib/exportClient';

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
  const [status, setStatus] = useState<string>('ALL');
  const list = usePagedList<Invoice>({
    errorMessage: 'تعذر تحميل الفواتير',
    deps: [status],
    fetcher: ({ page, pageSize, search }) => invoicesApi.getInvoices({
      page,
      pageSize,
      status: status === 'ALL' ? undefined : status,
      searchTerm: search || undefined,
    }),
  });
  const { items, error, loading } = list;

  const today = useMemo(() => new Date(), []);

  const buildExport = async (): Promise<ExportDocument> => {
    const data = unwrapPaged<Invoice>((await invoicesApi.getInvoices({ page: 1, pageSize: 100, status: status === 'ALL' ? undefined : status, searchTerm: list.searchInput.trim() || undefined })).data);
    const tab = STATUS_TABS.find((t) => t.key === status)?.label ?? 'الكل';
    return {
      title: 'الفواتير', subtitle: `${tab} — ${data.totalCount} فاتورة${data.totalCount > 100 ? ' (أول 100)' : ''}`, fileName: 'invoices', fields: [],
      tables: [{
        columns: ['رقم الفاتورة', 'الزبون', 'التاريخ', 'الاستحقاق', 'الإجمالي (ل.س)', 'الإجمالي ($)', 'الحالة'],
        rows: data.items.map((i) => [i.invoiceNumber ?? '', i.customerName ?? '', ymd(i.invoiceDate), ymd(i.dueDate), num(i.totalSyp), num(i.totalUsd), i.status ?? '']),
        numericColumns: [4, 5],
      }],
    };
  };

  return (
    <div style={{ direction: 'rtl' }}>
      {/* Page Header */}
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">الفواتير</h1>
          <div className="vex-page-header__breadcrumb">إدارة وتتبع فواتير المبيعات</div>
        </div>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <ExportMenu build={buildExport} />
          <button type="button" onClick={() => navigate('/invoices/new')} className="btn-primary">
            ＋ فاتورة جديدة
          </button>
        </div>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      <input
        value={list.searchInput}
        onChange={(e) => list.setSearchInput(e.target.value)}
        placeholder="ابحث برقم الفاتورة أو اسم الزبون..."
        className="vex-input"
        style={{ marginBottom: 16, maxWidth: 420 }}
      />

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
      <div className="vex-card vex-card--no-pad" style={{ opacity: loading ? 0.6 : 1, transition: 'opacity 120ms' }}>
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead>
              <tr>
                <th>رقم الفاتورة</th>
                <th>الزبون</th>
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
        <Pagination page={list.page} pageSize={list.pageSize} totalCount={list.totalCount} onPageChange={list.setPage} onPageSizeChange={list.changePageSize} />
      </div>
    </div>
  );
}
