/** Read-only ERPNext lists with accounting meaning: cost centres, modes of payment, tax templates, fiscal years, exchange rates. */
import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Alert, Box, Chip, Link, Stack, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material';
import { erpnextApi, type ReferenceList } from '../../api/endpoints/erpnext';
import { unwrapNode } from '../../api/apiData';
import { useLoad } from '../../hooks/useLoad';
import DataTable, { type Column } from '../../components/ui/DataTable';

type Row = Record<string, string | null>;

const KINDS: Array<{ key: string; label: string; hint: string }> = [
  { key: 'cost-centers', label: 'مراكز التكلفة', hint: 'تُستعمل لتوزيع الإيرادات والمصاريف في تقارير ERPNext' },
  { key: 'modes-of-payment', label: 'طرق الدفع', hint: 'طرق الدفع المعرّفة في ERPNext' },
  { key: 'sales-taxes', label: 'قوالب ضرائب المبيعات', hint: 'لا تُطبَّق الضرائب على فواتير هذا النظام بعد' },
  { key: 'purchase-taxes', label: 'قوالب ضرائب المشتريات', hint: 'لا تُطبَّق الضرائب على فواتير الشراء بعد' },
  { key: 'fiscal-years', label: 'السنوات المالية', hint: 'إقفال الفترات يتم هنا في «إقفال الفترات»' },
  { key: 'exchange-rates', label: 'أسعار الصرف', hint: 'مع سعرنا المحفوظ لليوم نفسه' },
];

const LABEL: Record<string, string> = {
  name: 'الاسم', cost_center_name: 'المركز', parent_cost_center: 'يتبع', is_group: 'مجموعة', disabled: 'موقوف', type: 'النوع', enabled: 'مفعّل',
  title: 'العنوان', is_default: 'افتراضي', year_start_date: 'البداية', year_end_date: 'النهاية', date: 'التاريخ', from_currency: 'من', to_currency: 'إلى',
  exchange_rate: 'سعر ERPNext', local_mid_rate: 'سعرنا لليوم نفسه',
};
const FLAGS = new Set(['is_group', 'disabled', 'enabled', 'is_default']);

function cell(row: Row, col: string): JSX.Element | string {
  const v = row[col];
  if (FLAGS.has(col)) return v === '1' ? '✓' : '';
  if (col === 'local_mid_rate') {
    if (v === null) return <Link component={RouterLink} to="/fx-rates" underline="hover">لا يوجد — أضِف سعراً</Link>;
    const same = Number(v) === Number(row.exchange_rate);
    return <Chip size="small" color={same ? 'success' : 'warning'} variant="outlined" label={`${Number(v).toLocaleString('en-US')}${same ? '' : ' (مختلف)'}`} />;
  }
  return v ?? '';
}

export default function ErpNextReference(): JSX.Element {
  const [kind, setKind] = useState(KINDS[0].key);
  const { data, loading, error } = useLoad(
    async () => unwrapNode<ReferenceList>((await erpnextApi.reference(kind)).data) as ReferenceList, [kind], 'تعذرت القراءة من ERPNext');

  const columns: Column<Row>[] = (data?.columns ?? []).map((c) => ({ header: LABEL[c] ?? c, render: (r: Row) => cell(r, c), nowrap: true }));
  return (
    <Box>
      <ToggleButtonGroup size="small" exclusive value={kind} onChange={(_, v: string | null) => { if (v) setKind(v); }} sx={{ mb: 1, flexWrap: 'wrap' }}>
        {KINDS.map((k) => <ToggleButton key={k.key} value={k.key}>{k.label}</ToggleButton>)}
      </ToggleButtonGroup>
      <Stack direction="row" gap={1} sx={{ mb: 2 }}>
        <Typography variant="caption" color="text.secondary">{KINDS.find((k) => k.key === kind)?.hint} — للقراءة فقط؛ تُعدّل من ERPNext.</Typography>
      </Stack>
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
      <DataTable columns={columns} rows={data?.rows ?? []} getKey={(r) => JSON.stringify(r)} loading={loading} empty="لا توجد بيانات في ERPNext" />
    </Box>
  );
}
