import { useEffect, useState } from 'react';
import { Accordion, AccordionDetails, AccordionSummary, Alert, Box, Chip, CircularProgress, Stack, Typography } from '@mui/material';
import { rolesApi } from '../../api/endpoints/roles';
import { unwrapList } from '../../api/apiData';
import { extractApiError } from '../../lib/toast';
import PageHeader from '../../components/ui/PageHeader';

type Role = { id: string; code?: string; name?: string; description?: string; permissions?: string[] };

/** "invoices:post" → module "invoices": permissions are grouped under their module so a long list stays readable. */
function groupByModule(permissions: string[]): Array<[string, string[]]> {
  const groups = new Map<string, string[]>();
  for (const p of permissions) {
    const module = p.split(/[:.]/)[0];
    groups.set(module, [...(groups.get(module) ?? []), p]);
  }
  return [...groups.entries()].sort(([a], [b]) => a.localeCompare(b));
}

export default function Roles(): JSX.Element {
  const [roles, setRoles] = useState<Role[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    rolesApi.getRoles()
      .then((r) => setRoles(unwrapList<Role>(r.data)))
      .catch((e: unknown) => setError(extractApiError(e, 'تعذر تحميل الأدوار')))
      .finally(() => setLoading(false));
  }, []);

  return (
    <>
      <PageHeader title="الأدوار والصلاحيات" subtitle="ما يستطيع كل دور فعله في النظام" actions={<Chip variant="outlined" label={`${roles.length} دور`} />} />
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
      {loading ? <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box> : null}
      {roles.map((role) => {
        const permissions = role.permissions ?? [];
        return (
          <Accordion key={role.id} variant="outlined" disableGutters sx={{ mb: 1, borderRadius: 2, '&:before': { display: 'none' } }}>
            <AccordionSummary>
              <Stack direction="row" gap={2} alignItems="center" sx={{ width: '100%' }}>
                <Typography fontWeight={800} sx={{ minWidth: 190 }}>{role.code ?? role.name}</Typography>
                <Typography variant="body2" color="text.secondary" sx={{ flex: 1 }}>{role.description || ' '}</Typography>
                <Chip size="small" color={permissions.length > 0 ? 'primary' : 'default'} variant="outlined" label={`${permissions.length} صلاحية`} />
              </Stack>
            </AccordionSummary>
            <AccordionDetails>
              {permissions.length === 0 ? <Typography color="text.secondary">لا توجد صلاحيات ممنوحة لهذا الدور.</Typography> : groupByModule(permissions).map(([module, items]) => (
                <Box key={module} sx={{ mb: 1.5 }}>
                  <Typography variant="caption" color="text.secondary" fontWeight={700}>{module}</Typography>
                  <Stack direction="row" gap={0.5} flexWrap="wrap" sx={{ mt: 0.5 }}>
                    {items.map((p) => <Chip key={p} size="small" label={p} sx={{ fontFamily: 'monospace' }} />)}
                  </Stack>
                </Box>
              ))}
            </AccordionDetails>
          </Accordion>
        );
      })}
    </>
  );
}
