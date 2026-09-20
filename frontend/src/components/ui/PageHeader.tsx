import type { ReactNode } from 'react';
import { Box, Breadcrumbs, Link as MuiLink, Stack, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';

type Crumb = { label: string; to?: string };

export default function PageHeader({ title, subtitle, crumbs, actions }: { title: string; subtitle?: string; crumbs?: Crumb[]; actions?: ReactNode }): JSX.Element {
  return (
    <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} spacing={2} sx={{ mb: 3 }}>
      <Box>
        {crumbs && crumbs.length > 0 ? (
          <Breadcrumbs sx={{ mb: 0.5, fontSize: 13 }}>
            {crumbs.map((c) => (c.to
              ? <MuiLink key={c.label} component={RouterLink} to={c.to} underline="hover" color="primary">{c.label}</MuiLink>
              : <Typography key={c.label} variant="caption" color="text.secondary">{c.label}</Typography>))}
          </Breadcrumbs>
        ) : null}
        <Typography variant="h5" fontWeight={800}>{title}</Typography>
        {subtitle ? <Typography variant="body2" color="text.secondary">{subtitle}</Typography> : null}
      </Box>
      {actions ? <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">{actions}</Stack> : null}
    </Stack>
  );
}
