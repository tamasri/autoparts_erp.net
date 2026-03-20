import { Button, Paper, Stack, Typography } from '@mui/material';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink } from 'react-router-dom';

export function PartiesPage(): JSX.Element {
  const { t } = useTranslation();

  return (
    <Paper elevation={0} sx={{ p: 4 }}>
      <Stack direction="row" alignItems="center" justifyContent="space-between" sx={{ mb: 2 }}>
        <Typography variant="h5">{t('pages.parties.title')}</Typography>
        <Button component={RouterLink} to="/parties/new" variant="contained">
          {t('pages.parties.new')}
        </Button>
      </Stack>
      <Typography color="text.secondary">{t('pages.parties.stub')}</Typography>
    </Paper>
  );
}
