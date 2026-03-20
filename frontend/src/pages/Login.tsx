import { Button, Paper, Stack, TextField, Typography } from '@mui/material';
import { useTranslation } from 'react-i18next';

export function LoginPage(): JSX.Element {
  const { t } = useTranslation();

  return (
    <Paper elevation={0} sx={{ p: 4 }}>
      <Stack spacing={2}>
        <Typography variant="h5">{t('pages.login.title')}</Typography>
        <TextField label={t('auth.username')} />
        <TextField label={t('auth.password')} type="password" />
        <Button variant="contained">{t('auth.signIn')}</Button>
      </Stack>
    </Paper>
  );
}
