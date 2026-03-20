import { Paper, Typography } from '@mui/material';
import { useTranslation } from 'react-i18next';

export function DashboardPage(): JSX.Element {
  const { t } = useTranslation();

  return (
    <Paper elevation={0} sx={{ p: 4 }}>
      <Typography variant="h5">{t('pages.dashboard.title')}</Typography>
      <Typography color="text.secondary">{t('pages.dashboard.subtitle')}</Typography>
    </Paper>
  );
}
