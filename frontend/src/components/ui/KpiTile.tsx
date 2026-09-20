import type { ReactNode } from 'react';
import { Card, CardContent, Typography } from '@mui/material';

type Tone = 'primary' | 'success' | 'warning' | 'error';

/** One headline number with a short caption; the coloured edge says whether it needs attention. */
export default function KpiTile({ title, value, hint, tone = 'primary', icon }: { title: string; value: ReactNode; hint?: string; tone?: Tone; icon?: string }): JSX.Element {
  return (
    <Card variant="outlined" sx={{ borderRadius: 3, borderInlineStart: 4, borderInlineStartColor: `${tone}.main` }}>
      <CardContent>
        <Typography variant="caption" color="text.secondary">{icon ? `${icon} ` : ''}{title}</Typography>
        <Typography variant="h5" fontWeight={800} color={`${tone}.main`} sx={{ my: 0.5 }}>{value}</Typography>
        {hint ? <Typography variant="caption" color="text.secondary">{hint}</Typography> : null}
      </CardContent>
    </Card>
  );
}
