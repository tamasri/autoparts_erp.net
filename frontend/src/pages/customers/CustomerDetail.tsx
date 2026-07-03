import { useEffect, useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
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
    return () => {
      mounted = false;
    };
  }, [id]);

  const outstanding = useMemo(
    () => statement.length > 0 ? Number(statement[statement.length - 1].balanceSyp ?? 0) : 0,
    [statement],
  );

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      {error ? <ErrorBanner message={error} /> : null}
      <div style={{ display: 'flex', justifyContent: 'space-between', gap: '12px', marginBottom: '12px' }}>
        <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', padding: '12px', flex: 1 }}>
          <h2 style={{ margin: 0 }}>{customer?.name ?? '-'}</h2>
          <div style={{ color: '#607d8b' }}>{customer?.code} | {customer?.type} | {customer?.city ?? '-'}</div>
        </div>
        <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', padding: '12px', minWidth: '220px' }}>
          <div style={{ color: '#607d8b' }}>الرصيد المستحق</div>
          <div style={{ color: '#00796b', fontSize: '24px', fontWeight: 800 }}>{outstanding.toLocaleString('en-US')} ل.س</div>
        </div>
      </div>

      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', padding: '12px', marginBottom: '12px' }}>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(180px,1fr))', gap: '10px' }}>
          <div>الهاتف: {customer?.phone ?? '-'}</div>
          <div>الحد الائتماني ل.س: {Number(customer?.creditLimitSyp ?? 0).toLocaleString('en-US')}</div>
          <div>الحد الائتماني $: {Number(customer?.creditLimitUsd ?? 0).toLocaleString('en-US')}</div>
          <div>شروط الدفع: {customer?.paymentTermsDays ?? '-'} يوم</div>
        </div>
      </div>

      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', overflow: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
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
            {statement.map((row, idx) => (
              <tr key={`${row.reference ?? 'row'}-${idx}`}>
                <td>{row.date ?? row.entryDate ?? '-'}</td>
                <td>{row.type ?? '-'}</td>
                <td>{row.reference ?? '-'}</td>
                <td>{Number(row.debitSyp ?? 0).toLocaleString('en-US')}</td>
                <td>{Number(row.creditSyp ?? 0).toLocaleString('en-US')}</td>
                <td>{Number(row.balanceSyp ?? 0).toLocaleString('en-US')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
