import { Chip } from '@mui/material';

type Tone = 'success' | 'warning' | 'error' | 'info' | 'default';

const STATUS: Record<string, { label: string; tone: Tone }> = {
  ACTIVE: { label: 'نشط', tone: 'success' }, INACTIVE: { label: 'غير نشط', tone: 'default' },
  PENDING: { label: 'معلّق', tone: 'warning' }, IN_REVIEW: { label: 'بانتظار موافقة أخرى', tone: 'info' }, APPROVED: { label: 'موافَق عليه', tone: 'success' }, REJECTED: { label: 'مرفوض', tone: 'error' },
  EXPIRED: { label: 'منتهٍ', tone: 'default' }, SUCCESS: { label: 'نجح', tone: 'success' }, FAILED: { label: 'فشل', tone: 'error' },
  DRAFT: { label: 'مسودة', tone: 'default' }, POSTED: { label: 'مرحّل', tone: 'success' }, VOID: { label: 'ملغى', tone: 'error' },
  OPEN: { label: 'مفتوح', tone: 'warning' }, ACKNOWLEDGED: { label: 'تم الاطلاع', tone: 'info' }, RESOLVED: { label: 'محلول', tone: 'success' },
  LOCKED: { label: 'مقفل', tone: 'error' }, UNLOCKED: { label: 'مفتوح', tone: 'success' },
  IN_TRANSIT: { label: 'قيد النقل', tone: 'info' }, SHIPPED: { label: 'مشحون', tone: 'info' }, RECEIVED: { label: 'مستلَم', tone: 'success' },
  COMPLETED: { label: 'مكتمل', tone: 'success' }, ISSUED: { label: 'مصروف', tone: 'success' }, PICKING: { label: 'قيد الانتقاء', tone: 'info' },
  PICKED: { label: 'منتقى', tone: 'info' }, VERIFIED: { label: 'متحقق منه', tone: 'success' }, PENDING_APPROVAL: { label: 'بانتظار الموافقة', tone: 'warning' },
  CONFIRMED: { label: 'مؤكد', tone: 'info' }, REVERSED: { label: 'معكوس', tone: 'error' },
};

/** A status word as a coloured chip; unknown codes are shown as they are. */
export default function StatusChip({ status, label }: { status?: string | null; label?: string }): JSX.Element {
  const key = (status ?? '').toUpperCase();
  const known = STATUS[key];
  return <Chip size="small" variant="outlined" color={known?.tone ?? 'default'} label={label ?? known?.label ?? (status || '—')} />;
}
