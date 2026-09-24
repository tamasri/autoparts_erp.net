/**
 * "عرض / طباعة" button: loads the document on click, then shows it in the shared viewer (print, PDF, Excel, CSV).
 * With `browse` the viewer also steps through the document's series by number (first / previous / next / last).
 */
import { useState } from 'react';
import { Button } from '@mui/material';
import DocumentDialog from './DocumentDialog';
import DocumentNavigator from '../documents/DocumentNavigator';
import { extractApiError, toast } from '../../lib/toast';
import type { ExportDocument } from '../../lib/exportClient';
import type { DocumentKind } from '../../api/endpoints/documents';

type Browse = { kind: DocumentKind; id: string; load: (id: string) => Promise<ExportDocument> };
type Props = { label?: string } & ({ load: () => Promise<ExportDocument>; browse?: never } | { browse: Browse; load?: never });

export default function DocumentViewButton({ load, browse, label = '👁 عرض / طباعة' }: Props): JSX.Element {
  const [open, setOpen] = useState(false);
  const [doc, setDoc] = useState<ExportDocument | null>(null);
  const [loading, setLoading] = useState(false);
  const [currentId, setCurrentId] = useState<string | null>(null);

  async function show(id?: string): Promise<void> {
    setOpen(true); setLoading(true); setDoc(null);
    try {
      if (browse) {
        const target = id ?? browse.id;
        setCurrentId(target);
        setDoc(await browse.load(target));
      } else {
        setDoc(await load());
      }
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر تحميل المستند'));
      setOpen(false);
    } finally {
      setLoading(false);
    }
  }

  const navigator = browse && currentId
    ? <DocumentNavigator kind={browse.kind} id={currentId} onNavigate={(id) => void show(id)} />
    : undefined;

  return (
    <>
      <Button size="small" variant="outlined" onClick={() => void show()} sx={{ whiteSpace: 'nowrap' }}>{label}</Button>
      <DocumentDialog open={open} onClose={() => setOpen(false)} document={doc} loading={loading} toolbar={navigator} />
    </>
  );
}
