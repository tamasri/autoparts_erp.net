/**
 * The business KPIs with filters (period, warehouse, customer, rep, category; the display currency is the switch in the top bar). Every figure comes from GET /dashboard/kpis,
 * which aggregates on the server; each card links to the list or report that explains it.
 */
import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import {
  Alert, Autocomplete, Box, Button, Card, CardActionArea, CardContent, Chip, LinearProgress, Link, MenuItem, Paper, Stack, TextField,
  Typography,
} from '@mui/material';
import { BarChart } from '@mui/x-charts/BarChart';
import { chartColors } from '../../theme/theme';
import { dashboardApi, type BusinessKpis as Kpis, type KpiAgeing, type KpiFilters, type KpiRank } from '../../api/endpoints/dashboard';
import { salesRepsApi, type SalesRep } from '../../api/endpoints/salesReps';
import { customersApi } from '../../api/endpoints/customers';
import { unwrapNode, unwrapPaged } from '../../api/apiData';
import { useLoad } from '../../hooks/useLoad';
import { useLocations } from '../../hooks/useLocations';
import { formatPct, formatQty } from '../../lib/format';
import { today } from '../../lib/money';
import Money from '../../components/ui/Money';
import DataTable from '../../components/ui/DataTable';
import EntityPicker, { type PickerOption } from '../../components/pickers/EntityPicker';

type Category = { id: string; path: string; name: string; nameAr?: string | null };

const MONTHS = ['كانون الثاني', 'شباط', 'آذار', 'نيسان', 'أيار', 'حزيران', 'تموز', 'آب', 'أيلول', 'تشرين الأول', 'تشرين الثاني', 'كانون الأول'];
const iso = (d: Date): string => d.toLocaleDateString('en-CA');

function preset(key: string): { from: string; to: string } {
  const now = new Date();
  const y = now.getFullYear(); const m = now.getMonth();
  switch (key) {
    case 'last-month': return { from: iso(new Date(y, m - 1, 1)), to: iso(new Date(y, m, 0)) };
    case 'quarter': return { from: iso(new Date(y, Math.floor(m / 3) * 3, 1)), to: today() };
    case 'year': return { from: `${y}-01-01`, to: today() };
    default: return { from: iso(new Date(y, m, 1)), to: today() };
  }
}

/** A headline figure; the whole card opens the report behind it. */
function Stat({ title, to, children, hint, tone = 'primary.main' }: { title: string; to?: string; children: React.ReactNode; hint?: React.ReactNode; tone?: string }): JSX.Element {
  const body = (
    <CardContent>
      <Typography variant="caption" color="text.secondary">{title}</Typography>
      <Box sx={{ my: 0.5, color: tone }}>{children}</Box>
      {hint ? <Typography variant="caption" color="text.secondary" component="div">{hint}</Typography> : null}
    </CardContent>
  );
  return (
    <Card variant="outlined" sx={{ borderRadius: 3, borderInlineStart: 4, borderInlineStartColor: tone }}>
      {to ? <CardActionArea component={RouterLink} to={to} sx={{ height: '100%' }}>{body}</CardActionArea> : body}
    </Card>
  );
}

function Ageing({ title, a, to }: { title: string; a: KpiAgeing; to: string }): JSX.Element {
  const rows: Array<[string, number, string]> = [
    ['غير مستحق', a.notDueUsd, 'success.main'], ['1–30 يوماً', a.days1To30Usd, 'info.main'], ['31–60', a.days31To60Usd, 'warning.main'],
    ['61–90', a.days61To90Usd, 'warning.dark'], ['أكثر من 90', a.over90Usd, 'error.main'],
  ];
  return (
    <Paper variant="outlined" sx={{ p: 2, borderRadius: 3 }}>
      <Stack direction="row" justifyContent="space-between" alignItems="baseline" sx={{ mb: 1 }}>
        <Link component={RouterLink} to={to} underline="hover" fontWeight={700}>{title}</Link>
        <Money usd={a.totalUsd} inline fontWeight={700} />
      </Stack>
      {rows.map(([label, v, color]) => (
        <Box key={label} sx={{ mb: 0.75 }}>
          <Stack direction="row" justifyContent="space-between"><Typography variant="caption">{label}</Typography><Money usd={v} inline variant="caption" /></Stack>
          <LinearProgress variant="determinate" value={a.totalUsd > 0 ? (v / a.totalUsd) * 100 : 0} sx={{ height: 6, borderRadius: 3, '& .MuiLinearProgress-bar': { bgcolor: color } }} />
        </Box>
      ))}
      <Typography variant="caption" color="text.secondary">
        {a.overdueCount} متأخرة{a.daysSalesOutstanding !== null ? ` · متوسط التحصيل (DSO) ${a.daysSalesOutstanding} يوماً` : ''}
      </Typography>
    </Paper>
  );
}

function Ranking({ title, rows, secondary, to }: { title: string; rows: KpiRank[]; secondary: (r: KpiRank) => React.ReactNode; to?: (r: KpiRank) => string | null }): JSX.Element {
  const max = Math.max(...rows.map((r) => r.amountUsd), 1);
  return (
    <Paper variant="outlined" sx={{ p: 2, borderRadius: 3 }}>
      <Typography fontWeight={700} sx={{ mb: 1 }}>{title}</Typography>
      {rows.length === 0 ? <Typography variant="body2" color="text.secondary">لا توجد مبيعات في الفترة</Typography> : rows.map((r) => {
        const link = to?.(r);
        return (
          <Box key={`${r.id}-${r.name}`} sx={{ mb: 1 }}>
            <Stack direction="row" justifyContent="space-between" gap={1}>
              <Typography variant="body2" noWrap title={r.name}>{link ? <Link component={RouterLink} to={link} underline="hover">{r.name}</Link> : r.name}</Typography>
              <Money usd={r.amountUsd} inline variant="body2" fontWeight={700} />
            </Stack>
            <LinearProgress variant="determinate" value={Math.max(0, (r.amountUsd / max) * 100)} sx={{ height: 5, borderRadius: 3, my: 0.25 }} />
            <Typography variant="caption" color="text.secondary">{secondary(r)}</Typography>
          </Box>
        );
      })}
    </Paper>
  );
}

export default function BusinessKpis(): JSX.Element {
  const [period, setPeriod] = useState('month');
  const [range, setRange] = useState(preset('month'));
  const [warehouseId, setWarehouseId] = useState('');
  const [customer, setCustomer] = useState<PickerOption | null>(null);
  const [salesRepId, setSalesRepId] = useState('');
  const [category, setCategory] = useState<Category | null>(null);
  const { locations } = useLocations('WAREHOUSE');
  const [reps, setReps] = useState<SalesRep[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);

  useEffect(() => {
    salesRepsApi.list({ includeInactive: true }).then((r) => setReps(unwrapNode<SalesRep[]>(r.data) ?? [])).catch(() => setReps([]));
    dashboardApi.getCategories().then((r) => setCategories(unwrapNode<Category[]>(r.data) ?? [])).catch(() => setCategories([]));
  }, []);

  const searchCustomers = useCallback(async (text: string): Promise<PickerOption[]> => {
    const res = await customersApi.getCustomers({ page: 1, pageSize: 10, searchTerm: text || undefined });
    return unwrapPaged<{ id: string; name?: string; code?: string }>(res.data).items.map((c) => ({ id: c.id, label: c.name ?? c.id.slice(0, 8), sublabel: c.code }));
  }, []);

  const filters: KpiFilters = useMemo(() => ({
    from: range.from, to: range.to, warehouseId: warehouseId || undefined, customerId: customer?.id, salesRepId: salesRepId || undefined, categoryId: category?.id,
  }), [range, warehouseId, customer, salesRepId, category]);
  const { data, loading, error } = useLoad(
    async () => unwrapNode<Kpis>((await dashboardApi.getKpis(filters)).data) as Kpis, [filters], 'تعذر تحميل مؤشرات الأداء');
  const filtered = Boolean(filters.warehouseId || filters.customerId || filters.salesRepId || filters.categoryId);
  const period$ = `?from=${range.from}&to=${range.to}`;

  return (
    <Box>
      <Paper variant="outlined" sx={{ p: 1.5, borderRadius: 3, mb: 2 }}>
        <Stack direction="row" gap={1.5} flexWrap="wrap" alignItems="center">
          <TextField select size="small" label="الفترة" value={period} sx={{ width: 150 }}
            onChange={(e) => { setPeriod(e.target.value); if (e.target.value !== 'custom') setRange(preset(e.target.value)); }}>
            <MenuItem value="month">هذا الشهر</MenuItem><MenuItem value="last-month">الشهر الماضي</MenuItem>
            <MenuItem value="quarter">هذا الربع</MenuItem><MenuItem value="year">هذه السنة</MenuItem><MenuItem value="custom">مخصّصة</MenuItem>
          </TextField>
          <TextField size="small" type="date" label="من" value={range.from} InputLabelProps={{ shrink: true }} onChange={(e) => { setPeriod('custom'); setRange({ ...range, from: e.target.value }); }} />
          <TextField size="small" type="date" label="إلى" value={range.to} InputLabelProps={{ shrink: true }} onChange={(e) => { setPeriod('custom'); setRange({ ...range, to: e.target.value }); }} />
          <TextField select size="small" label="المستودع" value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} sx={{ width: 160 }}>
            <MenuItem value="">الكل</MenuItem>
            {locations.map((l) => <MenuItem key={l.id} value={l.id}>{l.name}</MenuItem>)}
          </TextField>
          <Box sx={{ width: 220 }}><EntityPicker value={customer} onChange={setCustomer} search={searchCustomers} placeholder="الزبون (الكل)" /></Box>
          <TextField select size="small" label="المندوب" value={salesRepId} onChange={(e) => setSalesRepId(e.target.value)} sx={{ width: 160 }}>
            <MenuItem value="">الكل</MenuItem>
            {reps.map((r) => <MenuItem key={r.userId} value={r.userId}>{r.fullName}</MenuItem>)}
          </TextField>
          <Autocomplete
            size="small" sx={{ width: 200 }} options={categories} value={category} onChange={(_, v) => setCategory(v)}
            getOptionLabel={(c) => `${'  '.repeat(c.path.split('.').length - 1)}${c.nameAr || c.name}`} isOptionEqualToValue={(a, b) => a.id === b.id}
            renderInput={(p) => <TextField {...p} label="فئة الأصناف" />}
          />
          {filtered ? <Button size="small" onClick={() => { setWarehouseId(''); setCustomer(null); setSalesRepId(''); setCategory(null); }}>مسح الفلاتر</Button> : null}
        </Stack>
      </Paper>
      {loading ? <LinearProgress sx={{ mb: 1 }} /> : null}
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}

      {data ? (
        <>
          <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: 'repeat(auto-fit, minmax(210px, 1fr))', mb: 2 }}>
            <Stat title="صافي المبيعات" to="/invoices" hint={<>{data.sales.invoiceCount} فاتورة · {data.sales.customerCount} زبون · مرتجعات <Money usd={data.sales.returnsUsd} inline variant="caption" /></>}>
              <Money usd={data.sales.netSalesUsd} variant="h5" fontWeight={800} />
            </Stat>
            <Stat title="مجمل الربح" tone="success.main" hint={<>الهامش {data.sales.grossMarginPct !== null ? formatPct(data.sales.grossMarginPct) : '—'} · التكلفة <Money usd={data.sales.costUsd} inline variant="caption" /></>}>
              <Money usd={data.sales.grossProfitUsd} variant="h5" fontWeight={800} />
            </Stat>
            <Stat title="صافي الربح (دفتر الأستاذ)" to={`/accounting/reports?tab=profit-loss`} tone={data.ledger && data.ledger.netProfitUsd < 0 ? 'error.main' : 'success.main'}
              hint={data.ledger ? <>إيرادات <Money usd={data.ledger.incomeUsd} inline variant="caption" /> · مصاريف <Money usd={data.ledger.expensesUsd} inline variant="caption" /></> : data.ledgerNote}>
              {data.ledger ? <Money usd={data.ledger.netProfitUsd} variant="h5" fontWeight={800} /> : <Typography variant="h5">—</Typography>}
            </Stat>
            {data.purchases ? (
              <Stat title="المشتريات" to="/purchasing" tone="info.main" hint={`${data.purchases.billCount} فاتورة شراء`}>
                <Money usd={data.purchases.totalUsd} variant="h5" fontWeight={800} />
              </Stat>
            ) : null}
            {data.cash ? (
              <Stat title="التدفق النقدي" to="/payments" tone={data.cash.netUsd !== null && data.cash.netUsd < 0 ? 'error.main' : 'success.main'}
                hint={<>مقبوضات <Money usd={data.cash.receiptsUsd} inline variant="caption" />{data.cash.supplierPaymentsUsd !== null ? <> · مدفوعات <Money usd={data.cash.supplierPaymentsUsd} inline variant="caption" /></> : null}</>}>
                <Money usd={data.cash.netUsd ?? data.cash.receiptsUsd} variant="h5" fontWeight={800} />
              </Stat>
            ) : null}
            {data.stock ? (
              <Stat title="قيمة المخزون (بالتكلفة)" to="/inventory/warehouses" tone="warning.main"
                hint={`${data.stock.skusInStock} متوفر · ${data.stock.skusOutOfStock} نافد · ${data.stock.skusBelowReorder} تحت حد الطلب`}>
                <Money usd={data.stock.valueUsd} variant="h5" fontWeight={800} />
              </Stat>
            ) : null}
          </Box>

          <Paper variant="outlined" sx={{ p: 2, borderRadius: 3, mb: 2 }}>
            <Typography fontWeight={700}>آخر 12 شهراً ($)</Typography>
            <BarChart
              colors={chartColors}
              height={280}
              xAxis={[{ scaleType: 'band', data: data.months.map((m) => `${MONTHS[m.month - 1]} ${String(m.year).slice(2)}`) }]}
              series={[
                { data: data.months.map((m) => m.netSalesUsd), label: 'صافي المبيعات' },
                { data: data.months.map((m) => m.grossProfitUsd), label: 'مجمل الربح' },
                ...(data.purchases ? [{ data: data.months.map((m) => m.purchasesUsd), label: 'المشتريات' }] : []),
                ...(data.cash ? [{ data: data.months.map((m) => m.receiptsUsd), label: 'المقبوضات' }] : []),
              ]}
              margin={{ top: 50, bottom: 30, left: 60, right: 10 }}
            />
          </Paper>

          <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))', mb: 2 }}>
            <Ageing title="الذمم المدينة وأعمارها" a={data.receivables} to="/accounting/balances" />
            {data.payables ? <Ageing title="الذمم الدائنة وأعمارها" a={data.payables} to="/accounting/balances?tab=payables" /> : null}
            <Ranking title="أفضل الزبائن" rows={data.topCustomers} to={(r) => (r.id ? `/customers/${r.id}` : null)}
              secondary={(r) => <>مجمل ربح <Money usd={r.secondary} inline variant="caption" /> · {r.count} فاتورة</>} />
            <Ranking title="أكثر الأصناف مبيعاً" rows={data.topItems} secondary={(r) => `الكمية ${formatQty(r.secondary)} · ${r.count} فاتورة`} />
            <Ranking title="المبيعات حسب المندوب" rows={data.salesByRep} to={(r) => (r.id ? `/sales-reps/${r.id}${period$}` : null)}
              secondary={(r) => <>مجمل ربح <Money usd={r.secondary} inline variant="caption" /> · {r.count} فاتورة</>} />
          </Box>

          <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: 'repeat(auto-fit, minmax(420px, 1fr))', mb: 2 }}>
            <Box>
              <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 1 }}>
                <Typography fontWeight={700}>الفواتير المتأخرة (الأكبر رصيداً)</Typography>
                <Chip size="small" color={data.receivables.overdueCount ? 'error' : 'success'} label={`${data.receivables.overdueCount} متأخرة`} />
              </Stack>
              <DataTable
                rows={data.overdue} getKey={(o) => o.invoiceId} empty="لا توجد فواتير متأخرة"
                columns={[
                  { header: 'الفاتورة', render: (o) => <Link component={RouterLink} to={`/invoices/${o.invoiceId}`}>{o.invoiceNumber ?? o.invoiceId.slice(0, 8)}</Link>, nowrap: true },
                  { header: 'الزبون', render: (o) => o.customerName },
                  { header: 'متأخرة', render: (o) => `${o.daysOverdue} يوماً`, nowrap: true },
                  { header: 'الرصيد', render: (o) => <Money usd={o.balanceUsd} inline />, numeric: true },
                ]}
              />
            </Box>
            {data.stock ? (
              <Box>
                <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 1 }}>
                  <Typography fontWeight={700}>أصناف بطيئة الحركة (لا مبيع منذ 90 يوماً)</Typography>
                  <Chip size="small" color={data.stock.slowMoverCount ? 'warning' : 'success'} label={<>{data.stock.slowMoverCount} صنف · <Money usd={data.stock.slowMoverValueUsd} inline variant="caption" /></>} />
                </Stack>
                <DataTable
                  rows={data.slowMovers} getKey={(m) => m.skuId} empty="لا توجد أصناف راكدة"
                  columns={[
                    { header: 'الصنف', render: (m) => <><b>{m.code}</b> {m.name}</> },
                    { header: 'الكمية', render: (m) => formatQty(m.quantity), numeric: true },
                    { header: 'القيمة', render: (m) => <Money usd={m.valueUsd} inline />, numeric: true },
                    { header: 'آخر بيع', render: (m) => m.lastSale ?? 'لم يُبع', nowrap: true },
                  ]}
                />
              </Box>
            ) : null}
          </Box>
          <Typography variant="caption" color="text.secondary" component="div">
            المبيعات من الفواتير المرحّلة بعد الخصومات وبدون أجور التوصيل، والتكلفة كما سُجّلت عند البيع. الذمم أرصدة اليوم للفواتير المؤرخة حتى نهاية الفترة.
            {filtered ? ' الفلاتر لا تنطبق إلا على ما له معنى: الذمم الدائنة والمشتريات لا تُصفّى بالزبون أو المندوب، والذمم المدينة لا تُصفّى بالمستودع أو الفئة.' : ''}
          </Typography>
        </>
      ) : null}
    </Box>
  );
}
