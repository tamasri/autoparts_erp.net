/**
 * First / previous / next / last through a document series, by number. Deleted numbers are skipped; the numbers themselves never
 * change, so stepping always follows the order the documents were created in.
 */
import { useEffect, useState } from 'react';
import { Button, ButtonGroup, Tooltip, Typography } from '@mui/material';
import { documentsApi, type DocumentKind, type DocumentNeighbors, type DocumentRef } from '../../api/endpoints/documents';
import { unwrapNode } from '../../api/apiData';

type Props = { kind: DocumentKind; id: string; onNavigate: (id: string) => void };

export default function DocumentNavigator({ kind, id, onNavigate }: Props): JSX.Element | null {
  const [nav, setNav] = useState<DocumentNeighbors | null>(null);

  useEffect(() => {
    let alive = true;
    documentsApi.neighbors(kind, id)
      .then((r) => { if (alive) setNav(unwrapNode<DocumentNeighbors>(r.data)); })
      .catch(() => { if (alive) setNav(null); });
    return () => { alive = false; };
  }, [kind, id]);

  if (!nav) return null;

  const step = (label: string, target: DocumentRef | null): JSX.Element => (
    <Tooltip title={target ? target.number : 'لا يوجد'}>
      <span>
        <Button disabled={!target || target.id === id} onClick={() => target && onNavigate(target.id)}>{label}</Button>
      </span>
    </Tooltip>
  );

  return (
    <ButtonGroup size="small" variant="outlined" aria-label={`التنقل بين ${nav.seriesNameAr} حسب الرقم`}>
      {step('الأول', nav.first)}
      {step('السابق', nav.previous)}
      <Button disabled sx={{ '&.Mui-disabled': { color: 'text.primary' } }}>
        <Typography variant="body2" sx={{ fontFamily: 'monospace', fontWeight: 700 }}>{nav.number}</Typography>
      </Button>
      {step('التالي', nav.next)}
      {step('الأخير', nav.last)}
    </ButtonGroup>
  );
}
