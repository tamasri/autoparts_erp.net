import { Paper, Typography } from '@mui/material';
import { useTranslation } from 'react-i18next';

export function ApprovalsPage(): JSX.Element {
  const { t } = useTranslation();

  return (
    <Paper elevation={0} sx={{ p: 4 }}>
      <Typography variant="h5">{t('pages.approvals.title')}</Typography>
      <Typography color="text.secondary">{t('pages.stub')}</Typography>
    </Paper>
  );
}
