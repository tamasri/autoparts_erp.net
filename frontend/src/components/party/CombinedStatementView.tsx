import { Paper, Stack, Typography } from '@mui/material';
import { useTranslation } from 'react-i18next';

type CombinedStatementViewProps = {
  arOutstandingSyp: number;
  arOutstandingUsd: number;
  apOutstandingSyp: number;
  apOutstandingUsd: number;
};

export function CombinedStatementView({
  arOutstandingSyp,
  arOutstandingUsd,
  apOutstandingSyp,
  apOutstandingUsd,
}: CombinedStatementViewProps): JSX.Element {
  const { t } = useTranslation();

  return (
    <Paper elevation={0} sx={{ p: 3 }}>
      <Typography variant="h6" sx={{ mb: 2 }}>
        {t('party.combinedStatement.title')}
      </Typography>

      <Stack spacing={1}>
        <Typography>{t('party.combinedStatement.arSyp', { value: arOutstandingSyp.toFixed(2) })}</Typography>
        <Typography>{t('party.combinedStatement.arUsd', { value: arOutstandingUsd.toFixed(2) })}</Typography>
        <Typography>{t('party.combinedStatement.apSyp', { value: apOutstandingSyp.toFixed(2) })}</Typography>
        <Typography>{t('party.combinedStatement.apUsd', { value: apOutstandingUsd.toFixed(2) })}</Typography>
      </Stack>
    </Paper>
  );
}
