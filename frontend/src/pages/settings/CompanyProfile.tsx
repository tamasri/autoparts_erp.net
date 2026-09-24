/**
 * "بيانات المنشأة": the company's own details printed on every document — the issuer block, the footer contacts, the bank details
 * under an invoice, the manager's name in the approval box — and the default texts of the printed forms.
 * Anyone signed in can read it (it is printed anyway); saving needs system:config_write.
 */
import { useEffect, useState } from 'react';
import { Alert, Box, Button, CircularProgress, Paper, Stack, TextField, Typography } from '@mui/material';
import PageHeader from '../../components/ui/PageHeader';
import { companyProfileApi, type CompanyProfile as Profile } from '../../api/endpoints/companyProfile';
import { unwrapNode } from '../../api/apiData';
import { useCan } from '../../hooks/useCan';
import { extractApiError, toast } from '../../lib/toast';
import { exportPdf, previewPdfFrom } from '../../lib/exportClient';

const EMPTY: Profile = {
  name: '', managerName: null, address: null, city: null, phone: null, email: null, website: null, taxNumber: null, whatsApp: null,
  bankName: null, bankAccount: null, iban: null, invoiceSubtitle: null, invoiceTerms: null, receiptNote: null, statementNote: null,
};

type Field = { key: keyof Profile; label: string; hint?: string; multiline?: number; dir?: 'ltr' };

const SECTIONS: Array<{ title: string; hint: string; fields: Field[] }> = [
  {
    title: 'المنشأة', hint: 'تظهر في كتلة «صادرة عن» أعلى كل مستند',
    fields: [
      { key: 'name', label: 'الاسم التجاري *' }, { key: 'managerName', label: 'المدير المسؤول', hint: 'يُطبع في مربع «اعتماد الإدارة»' },
      { key: 'address', label: 'العنوان' }, { key: 'city', label: 'المدينة' }, { key: 'taxNumber', label: 'الرقم الضريبي' },
    ],
  },
  {
    title: 'التواصل', hint: 'تظهر في تذييل كل مستند',
    fields: [
      { key: 'phone', label: 'الهاتف', dir: 'ltr' }, { key: 'email', label: 'البريد الإلكتروني', dir: 'ltr' }, { key: 'website', label: 'الموقع', dir: 'ltr' },
      { key: 'whatsApp', label: 'رقم واتساب', hint: 'يُطبع كرمز QR «راسلنا واتساب» على الفاتورة', dir: 'ltr' },
    ],
  },
  {
    title: 'الحساب المصرفي', hint: 'يظهر في «طريقة الدفع» أسفل الفاتورة',
    fields: [{ key: 'bankName', label: 'اسم البنك' }, { key: 'bankAccount', label: 'رقم الحساب', dir: 'ltr' }, { key: 'iban', label: 'آيبان (IBAN)', dir: 'ltr' }],
  },
  {
    title: 'نصوص المستندات', hint: 'النصوص الثابتة في النماذج المطبوعة',
    fields: [
      { key: 'invoiceSubtitle', label: 'العنوان الفرعي للفاتورة', hint: 'مثال: يعتبر هذا المستند بمثابة إشعار تسليم' },
      { key: 'invoiceTerms', label: 'الشروط والأحكام', hint: 'شرط في كل سطر', multiline: 5 },
      { key: 'receiptNote', label: 'ملاحظة سند القبض', multiline: 2 }, { key: 'statementNote', label: 'إشعار المطابقة في كشف الحساب', multiline: 2 },
    ],
  },
];

export default function CompanyProfile(): JSX.Element {
  const canWrite = useCan('system:config_write');
  const [profile, setProfile] = useState<Profile>(EMPTY);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    companyProfileApi.get()
      .then((r) => setProfile({ ...EMPTY, ...unwrapNode<Profile>(r.data) }))
      .catch((e: unknown) => setError(extractApiError(e, 'تعذر تحميل بيانات المنشأة')))
      .finally(() => setLoading(false));
  }, []);

  const set = (key: keyof Profile, value: string): void => setProfile((p) => ({ ...p, [key]: value }));

  async function save(): Promise<void> {
    if (!profile.name.trim()) { setError('الاسم التجاري مطلوب'); return; }
    setSaving(true); setError('');
    try {
      setProfile({ ...EMPTY, ...unwrapNode<Profile>((await companyProfileApi.update(profile)).data) });
      toast.success('حُفظت بيانات المنشأة — تظهر في كل مستند يُطبع من الآن');
    } catch (e: unknown) {
      setError(extractApiError(e, 'تعذر حفظ بيانات المنشأة'));
    } finally {
      setSaving(false);
    }
  }

  /** A sample page with the saved letterhead and footer (save first to see changes). */
  const preview = (): Promise<void> => previewPdfFrom(exportPdf({
    title: 'نموذج ترويسة', subtitle: 'معاينة الترويسة والتذييل ببيانات المنشأة المحفوظة',
    fields: [{ label: 'المنشأة', value: profile.name }, { label: 'المدير المسؤول', value: profile.managerName }],
    tables: [{ columns: ['البند', 'القيمة'], rows: [['الهاتف', profile.phone], ['البريد', profile.email], ['آيبان', profile.iban]] }],
    fileName: 'letterhead-sample',
  }));

  if (loading) return <Box sx={{ display: 'grid', placeItems: 'center', minHeight: '40vh' }}><CircularProgress /></Box>;

  return (
    <Box>
      <PageHeader
        title="بيانات المنشأة"
        subtitle="تُطبع في ترويسة وتذييل كل المستندات: الفواتير، السندات، كشوف الحساب، والقوائم"
        actions={(
          <Stack direction="row" gap={1}>
            <Button variant="outlined" onClick={() => void preview()}>👁 معاينة الترويسة</Button>
            {canWrite ? <Button variant="contained" disabled={saving} onClick={() => void save()}>{saving ? 'جارٍ الحفظ...' : 'حفظ'}</Button> : null}
          </Stack>
        )}
      />
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
      {!canWrite ? <Alert severity="info" sx={{ mb: 2 }}>للعرض فقط — التعديل يتطلب صلاحية إعدادات النظام.</Alert> : null}

      <Stack spacing={2}>
        {SECTIONS.map((section) => (
          <Paper key={section.title} variant="outlined" sx={{ p: 2.5, borderRadius: 3 }}>
            <Typography variant="h6" fontWeight={700}>{section.title}</Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>{section.hint}</Typography>
            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}>
              {section.fields.map((f) => (
                <TextField
                  key={f.key}
                  label={f.label}
                  helperText={f.hint}
                  value={profile[f.key] ?? ''}
                  onChange={(e) => set(f.key, e.target.value)}
                  disabled={!canWrite}
                  multiline={Boolean(f.multiline)}
                  minRows={f.multiline}
                  inputProps={f.dir ? { dir: f.dir } : undefined}
                  sx={f.multiline ? { gridColumn: { md: '1 / -1' } } : undefined}
                />
              ))}
            </Box>
          </Paper>
        ))}
      </Stack>
    </Box>
  );
}
