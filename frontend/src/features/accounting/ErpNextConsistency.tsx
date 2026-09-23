/** Local records against ERPNext, section by section. Read-only: every difference is listed; nothing is fixed silently. */
import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import {
  Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, Chip, Link, Stack, Typography,
} from '@mui/material';
import { erpnextApi, type ConsistencyReport, type ConsistencySection } from '../../api/endpoints/erpnext';
import { unwrapNode } from '../../api/apiData';
import { useLoad } from '../../hooks/useLoad';
import { money } from '../../lib/money';
import { num, type ExportDocument } from '../../lib/exportClient';
import DataTable from '../../components/ui/DataTable';
import ExportMenu from '../../components/ui/ExportMenu';
import { DOCTYPE_LABEL, localRoute } from './erpnextLinks';

const KIND: Record<string, { label: string; color: 'error' | 'warning' | 'info' }> = {
  NOT_SENT: { label: 'لم يصل إلى ERPNext', color: 'error' },
  MISSING_IN_ERPNEXT: { label: 'أُرسل ثم اختفى من ERPNext', color: 'error' },
  ONLY_IN_ERPNEXT: { label: 'في ERPNext فقط', color: 'warning' },
  CANCELLED_IN_ERPNEXT_ONLY: { label: 'ملغى في ERPNext فقط', color: 'error' },
  NOT_CANCELLED_IN_ERPNEXT: { label: 'ملغى هنا وفعّال في ERPNext', color: 'error' },
  AMOUNT_DIFFERS: { label: 'المبلغ مختلف', color: 'warning' },
};

function SectionView({ s }: { s: ConsistencySection }): JSX.Element {
  const issueTotal = Object.values(s.issueCounts).reduce((a, b) => a + b, 0);
  return (
    <Accordion variant="outlined" disableGutters sx={{ borderRadius: 2, mb: 1, '&:before': { display: 'none' } }} defaultExpanded={issueTotal > 0 && issueTotal < 50}>
      <AccordionSummary expandIcon={<span>▾</span>}>
        <Stack direction="row" alignItems="center" gap={1.5} flexWrap="wrap" sx={{ width: '100%' }}>
          <Typography fontWeight={700} sx={{ minWidth: 190 }}>{DOCTYPE_LABEL[s.doctype] ?? s.doctype}</Typography>
          {s.error ? <Chip size="small" color="error" label="تعذرت القراءة من ERPNext" /> : issueTotal === 0 ? <Chip size="small" color="success" label="متطابق" /> : <Chip size="small" color="warning" label={`${issueTotal} فرق`} />}
          <Typography variant="body2" color="text.secondary">
            هنا {s.localCount}{s.localTotal !== null ? ` ($${money(s.localTotal)})` : ''} · في ERPNext {s.erpNextCount}{s.erpNextTotal !== null ? ` ($${money(s.erpNextTotal)})` : ''} · متطابق {s.matchedCount}
          </Typography>
        </Stack>
      </AccordionSummary>
      <AccordionDetails>
        {s.error ? <Alert severity="error" sx={{ mb: 1 }}>{s.error}</Alert> : null}
        {issueTotal > 0 ? (
          <>
            <Stack direction="row" gap={1} flexWrap="wrap" sx={{ mb: 1 }}>
              {Object.entries(s.issueCounts).map(([k, n]) => <Chip key={k} size="small" variant="outlined" color={KIND[k]?.color ?? 'default'} label={`${KIND[k]?.label ?? k}: ${n}`} />)}
            </Stack>
            {issueTotal > s.issues.length ? <Typography variant="caption" color="text.secondary">يُعرض أول {s.issues.length} فرقاً من {issueTotal}.</Typography> : null}
            <DataTable
              rows={s.issues} getKey={(i) => `${i.kind}-${i.localId ?? ''}-${i.erpNextName ?? ''}`}
              columns={[
                { header: 'الفرق', render: (i) => <Chip size="small" color={KIND[i.kind]?.color ?? 'default'} label={KIND[i.kind]?.label ?? i.kind} />, nowrap: true },
                {
                  header: 'السجل هنا',
                  render: (i) => {
                    const to = localRoute(i.localEntityType, i.localId);
                    return i.localRef ? (to ? <Link component={RouterLink} to={to}>{i.localRef}</Link> : i.localRef) : '—';
                  },
                },
                { header: 'في ERPNext', render: (i) => <Typography component="span" sx={{ fontFamily: 'monospace', fontSize: 12 }}>{i.erpNextName ?? '—'}</Typography> },
                { header: 'المبلغ هنا ($)', render: (i) => (i.localAmount !== null ? money(i.localAmount) : ''), numeric: true },
                { header: 'في ERPNext ($)', render: (i) => (i.erpNextAmount !== null ? money(i.erpNextAmount) : ''), numeric: true },
                { header: 'التفاصيل', render: (i) => <Typography variant="caption" color="text.secondary">{i.detail}</Typography> },
              ]}
            />
          </>
        ) : !s.error ? <Typography variant="body2" color="text.secondary">لا فروق.</Typography> : null}
      </AccordionDetails>
    </Accordion>
  );
}

export default function ErpNextConsistency({ canSync, onSync }: { canSync: boolean; onSync: () => void }): JSX.Element {
  const [tick, setTick] = useState(0);
  const { data, loading, error } = useLoad(
    async () => unwrapNode<ConsistencyReport>((await erpnextApi.consistency()).data) as ConsistencyReport, [tick], 'تعذر فحص التطابق مع ERPNext');

  const buildExport = async (): Promise<ExportDocument> => ({
    title: 'التطابق مع ERPNext', subtitle: data ? new Date(data.checkedAt).toLocaleString('ar') : '', fileName: 'erpnext-consistency', fields: [],
    tables: [
      {
        title: 'الملخص', columns: ['النوع', 'هنا', 'مبلغ هنا ($)', 'في ERPNext', 'مبلغ ERPNext ($)', 'متطابق', 'فروق'],
        rows: (data?.sections ?? []).map((s) => [DOCTYPE_LABEL[s.doctype] ?? s.doctype, num(s.localCount), num(s.localTotal), num(s.erpNextCount), num(s.erpNextTotal), num(s.matchedCount),
          num(Object.values(s.issueCounts).reduce((a, b) => a + b, 0))]),
        numericColumns: [1, 2, 3, 4, 5, 6],
      },
      {
        title: 'الفروق', columns: ['النوع', 'الفرق', 'السجل هنا', 'في ERPNext', 'هنا ($)', 'ERPNext ($)', 'التفاصيل'],
        rows: (data?.sections ?? []).flatMap((s) => s.issues.map((i) => [DOCTYPE_LABEL[s.doctype] ?? s.doctype, KIND[i.kind]?.label ?? i.kind, i.localRef ?? '', i.erpNextName ?? '',
          num(i.localAmount), num(i.erpNextAmount), i.detail ?? ''])),
        numericColumns: [4, 5],
      },
    ],
  });

  return (
    <Box>
      <Stack direction="row" gap={1} alignItems="center" flexWrap="wrap" sx={{ mb: 2 }}>
        <Typography variant="body2" color="text.secondary" sx={{ flex: 1, minWidth: 260 }}>
          يقارن كل سجل هنا بمستنده في ERPNext عبر سجل المزامنة. لا يُصلح شيئاً تلقائياً: «مزامنة الآن» تعيد إرسال ما فشل، وما عدا ذلك يُعالج يدوياً.
        </Typography>
        <Button size="small" disabled={loading} onClick={() => setTick((t) => t + 1)}>{loading ? 'جارٍ الفحص...' : '↻ إعادة الفحص'}</Button>
        {canSync ? <Button size="small" variant="outlined" onClick={onSync}>⇄ مزامنة الآن</Button> : null}
        <ExportMenu build={buildExport} disabled={!data} />
      </Stack>
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
      {data ? <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 1 }}>آخر فحص: {new Date(data.checkedAt).toLocaleString('ar')}</Typography> : null}
      {(data?.sections ?? []).map((s) => <SectionView key={s.key} s={s} />)}
    </Box>
  );
}
