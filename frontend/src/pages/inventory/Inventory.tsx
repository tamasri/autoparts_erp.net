import { useEffect, useMemo, useState } from 'react';
import { inventoryApi } from '../../api/endpoints/inventory';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';

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
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [query, setQuery] = useState('');
  const [stockRows, setStockRows] = useState<StockRow[]>([]);
  const [searchRows, setSearchRows] = useState<StockRow[]>([]);

  useEffect(() => {
    let mounted = true;
    async function loadSummary(): Promise<void> {
      setLoading(true);
      setError('');
      try {
        const res = await inventoryApi.getStock({ page: 1, pageSize: 200 });
        if (mounted) setStockRows(unwrapList<StockRow>(res.data));
      } catch (e: unknown) {
        if (!mounted) return;
        const msg = (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.detail
          ?? (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.message
          ?? 'تعذر تحميل المخزون';
        setError(msg);
      } finally {
        if (mounted) setLoading(false);
      }
    }
    void loadSummary();
    return () => {
      mounted = false;
    };
  }, []);

  useEffect(() => {
    const handle = window.setTimeout(async () => {
      if (!query.trim()) {
        setSearchRows([]);
        return;
      }
      try {
        const res = await inventoryApi.searchItems(query.trim(), 1, 100);
        setSearchRows(unwrapList<StockRow>(res.data));
      } catch {
        setSearchRows([]);
      }
    }, 300);
    return () => window.clearTimeout(handle);
  }, [query]);

  const activeRows = query.trim() ? searchRows : stockRows;
  const lowCount = useMemo(() => activeRows.filter((r) => Number(r.totalStock ?? r.quantityOnHand ?? 0) <= 0).length, [activeRows]);

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      {error ? <ErrorBanner message={error} /> : null}
      {lowCount > 0 ? (
        <div style={{ background: '#fff8e1', color: '#e65100', border: '1px solid #ffe082', borderRadius: '8px', padding: '10px', marginBottom: '12px' }}>
          تنبيه: يوجد {lowCount} مادة عند حد النفاد
        </div>
      ) : null}

      <input
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder="ابحث عن قطعة غيار... مثال: A 169 540 16 17"
        style={{ width: '100%', padding: '12px', border: '2px solid #00796b', borderRadius: '10px', marginBottom: '12px', fontSize: '15px' }}
      />

      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', overflow: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
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
            {activeRows.map((item) => {
              const main = Number(item.stockMain ?? item.quantityOnHand ?? 0);
              const wh2 = Number(item.stockWh2 ?? 0);
              const van = Number(item.stockVan1 ?? 0);
              const total = Number(item.totalStock ?? main + wh2 + van);
              const out = total <= 0;
              const stop = Boolean(item.isStopShip);
              return (
                <tr key={item.id} style={{ background: out ? '#fff8f8' : stop ? '#fffde7' : '#fff' }}>
                  <td>{item.skuCode ?? item.code ?? item.id.slice(0, 8)}</td>
                  <td>{item.skuName ?? item.name ?? '-'}</td>
                  <td style={{ color: main > 0 ? '#2e7d32' : '#c62828', fontWeight: 700 }}>{main}</td>
                  <td style={{ color: wh2 > 0 ? '#2e7d32' : '#c62828', fontWeight: 700 }}>{wh2}</td>
                  <td style={{ color: van > 0 ? '#2e7d32' : '#c62828', fontWeight: 700 }}>{van}</td>
                  <td>{stop ? <span style={{ background: '#fff59d', borderRadius: '999px', padding: '2px 8px' }}>موقوف</span> : out ? 'نفاد' : 'متوفر'}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
