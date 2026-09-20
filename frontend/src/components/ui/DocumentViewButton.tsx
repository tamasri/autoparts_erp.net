/** "عرض / طباعة" button: loads the document on click, then shows it in the shared viewer (print, PDF, Excel, CSV). */
import { useState } from 'react';
import { Button } from '@mui/material';
import DocumentDialog from './DocumentDialog';
import { extractApiError, toast } from '../../lib/toast';
import type { ExportDocument } from '../../lib/exportClient';

export default function DocumentViewButton({ load, label = '👁 عرض / طباعة' }: { load: () => Promise<ExportDocument>; label?: string }): JSX.Element {
  const [open, setOpen] = useState(false);
  const [doc, setDoc] = useState<ExportDocument | null>(null);
  const [loading, setLoading] = useState(false);

  async function show(): Promise<void> {
    setOpen(true); setLoading(true); setDoc(null);
    try {
      setDoc(await load());
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر تحميل المستند'));
      setOpen(false);
    } finally {
      setLoading(false);
    }
  }

  return (
    <>
      <Button size="small" variant="outlined" onClick={() => void show()} sx={{ whiteSpace: 'nowrap' }}>{label}</Button>
      <DocumentDialog open={open} onClose={() => setOpen(false)} document={doc} loading={loading} />
    </>
  );
}
