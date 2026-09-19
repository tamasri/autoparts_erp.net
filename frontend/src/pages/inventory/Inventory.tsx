import { useMemo } from 'react';
import { inventoryApi } from '../../api/endpoints/inventory';
import { usePagedList } from '../../hooks/usePagedList';
import Pagination from '../../components/common/Pagination';
import ErrorBanner from '../../components/common/ErrorBanner';

type StockRow = {
  id: string;
  skuCode?: string;
  code?: string;
  skuName?: string;
  name?: string;
  quantityOnHand?: number;
  totalStock?: number;
  stockMain?: number;
  stockWh2?: number;
  stockVan1?: number;
  isStopShip?: boolean;
};

export default function Inventory(): JSX.Element {
  const list = usePagedList<StockRow>({
    errorMessage: 'تعذر تحميل المخزون',
    pageSize: 25,
    fetcher: ({ page, pageSize, search }) => inventoryApi.getStock({ page, pageSize, searchTerm: search || undefined }),
  });
  const { items: activeRows, error, loading } = list;
  const query = list.searchInput;

  const lowCount = useMemo(() => activeRows.filter((r) => Number(r.totalStock ?? r.quantityOnHand ?? 0) <= 0).length, [activeRows]);
  const stopShipCount = useMemo(() => activeRows.filter((r) => r.isStopShip).length, [activeRows]);

  return (
    <div style={{ direction: 'rtl' }}>
      {/* Page Header */}
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">المخزون</h1>
          <div className="vex-page-header__breadcrumb">استعلام عن مستويات المخزون الحالية</div>
        </div>
        {/* KPI pills */}
        <div style={{ display: 'flex', gap: 10 }}>
          {lowCount > 0 && (
            <div style={{
              background: '#fef2f2', color: 'var(--clr-danger)',
              border: '1px solid #fecaca', borderRadius: 'var(--radius-pill)',
              padding: '6px 14px', fontSize: 13, fontWeight: 700,
            }}>
              ⚠ نافد (بالصفحة): {lowCount}
            </div>
          )}
          {stopShipCount > 0 && (
            <div style={{
              background: '#fefce8', color: '#92400e',
              border: '1px solid #fde68a', borderRadius: 'var(--radius-pill)',
              padding: '6px 14px', fontSize: 13, fontWeight: 700,
            }}>
              🚫 موقوف (بالصفحة): {stopShipCount}
            </div>
          )}
        </div>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      {/* Search */}
      <div style={{ position: 'relative', marginBottom: 16 }}>
        <span style={{
          position: 'absolute', top: '50%', right: 14,
          transform: 'translateY(-50%)', color: 'var(--txt-muted)',
          pointerEvents: 'none', fontSize: 18,
        }}>🔍</span>
        <input
          value={query}
          onChange={(e) => list.setSearchInput(e.target.value)}
          placeholder="ابحث عن قطعة غيار... مثال: A 169 540 16 17"
          className="vex-input"
          style={{ paddingRight: 44, fontSize: 15 }}
        />
      </div>

      {/* Stock Table */}
      <div className="vex-card vex-card--no-pad" style={{ opacity: loading ? 0.6 : 1, transition: 'opacity 120ms' }}>
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead>
              <tr>
                <th>رقم القطعة</th>
                <th>الاسم</th>
                <th>المستودع الرئيسي</th>
                <th>مستودع الفرع</th>
                <th>الفان</th>
                <th>الحالة</th>
              </tr>
            </thead>
            <tbody>
              {activeRows.length === 0 ? (
                <tr>
                  <td colSpan={6} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '36px 0' }}>
                    {query ? 'لا توجد نتائج مطابقة' : 'لا توجد بيانات مخزون'}
                  </td>
                </tr>
              ) : activeRows.map((item) => {
                const main = Number(item.stockMain ?? item.quantityOnHand ?? 0);
                const wh2 = Number(item.stockWh2 ?? 0);
                const van = Number(item.stockVan1 ?? 0);
                const total = Number(item.totalStock ?? main + wh2 + van);
                const out = total <= 0;
                const stop = Boolean(item.isStopShip);
                return (
                  <tr
                    key={item.id}
                    style={{
                      background: out ? '#fef2f2' : stop ? '#fefce8' : undefined,
                    }}
                  >
                    <td>
                      <span style={{
                        background: 'var(--clr-primary-light)', color: 'var(--clr-primary-dark)',
                        padding: '2px 8px', borderRadius: 'var(--radius-sm)', fontSize: 12, fontWeight: 700,
                        fontFamily: 'monospace',
                      }}>
                        {item.skuCode ?? item.code ?? item.id.slice(0, 8)}
                      </span>
                    </td>
                    <td style={{ fontWeight: 500, color: 'var(--txt-primary)' }}>{item.skuName ?? item.name ?? '-'}</td>
                    <td>
                      <span style={{ fontWeight: 700, color: main > 0 ? '#22c55e' : 'var(--clr-danger)' }}>{main}</span>
                    </td>
                    <td>
                      <span style={{ fontWeight: 700, color: wh2 > 0 ? '#22c55e' : 'var(--clr-danger)' }}>{wh2}</span>
                    </td>
                    <td>
                      <span style={{ fontWeight: 700, color: van > 0 ? '#22c55e' : 'var(--clr-danger)' }}>{van}</span>
                    </td>
                    <td>
                      {stop
                        ? <span className="badge badge--warning">موقوف</span>
                        : out
                          ? <span className="badge badge--danger">نافد</span>
                          : <span className="badge badge--success">متوفر</span>}
                    </td>
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
