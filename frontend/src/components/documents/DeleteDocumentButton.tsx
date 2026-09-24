/**
 * The Super Admin's delete, for a draft or a voided document only. The number stays used (a recorded gap in the series, never
 * reused), and the document is kept in the deletion log with who, when and why.
 */
import { useState } from 'react';
import { Button } from '@mui/material';
import ReasonDialog from '../ui/ReasonDialog';
import { useCan } from '../../hooks/useCan';
import { toast, extractApiError } from '../../lib/toast';
import { DELETABLE_KINDS, DELETABLE_STATUSES, DELETE_DOCUMENTS, documentsApi, type DocumentKind } from '../../api/endpoints/documents';

type Props = { kind: DocumentKind; id: string; number: string; status: string; onDeleted: () => void; size?: 'small' | 'medium' };

export default function DeleteDocumentButton({ kind, id, number, status, onDeleted, size = 'small' }: Props): JSX.Element | null {
  const canDelete = useCan(DELETE_DOCUMENTS);
  const [open, setOpen] = useState(false);

  if (!canDelete || !DELETABLE_KINDS.includes(kind) || !DELETABLE_STATUSES.includes(status.toUpperCase())) return null;

  async function remove(reason: string): Promise<void> {
    try {
      await documentsApi.remove(kind, id, reason);
      setOpen(false);
      toast.success(`حُذف ${number}؛ يبقى رقمه محجوزاً في السلسلة ولا يُعاد استخدامه`);
      onDeleted();
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر الحذف'));
    }
  }

  return (
    <>
      <Button size={size} color="error" onClick={() => setOpen(true)}>حذف</Button>
      <ReasonDialog open={open} title={`حذف ${number} نهائياً — سبب الحذف`} confirmLabel="حذف" minLength={5}
        onClose={() => setOpen(false)} onConfirm={remove} />
    </>
  );
}
