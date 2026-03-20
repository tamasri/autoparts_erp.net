import { Button, Paper, Stack, TextField, Typography } from '@mui/material';
import { FormEvent, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { partiesApi } from '../../api/endpoints/parties';

export function PartyCreatePage(): JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [displayName, setDisplayName] = useState('');
  const [displayNameAr, setDisplayNameAr] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  const onSubmit = async (event: FormEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault();
    setIsSubmitting(true);
    try {
      const response = await partiesApi.createParty({
        displayName,
        displayNameAr,
        initialTypeCodes: ['CUSTOMER'],
      });

      const partyId = response.data?.data?.id as string | undefined;
      if (partyId) {
        navigate(`/parties/${partyId}`);
        return;
      }

      navigate('/parties');
    } catch {
      setIsSubmitting(false);
    }
  };

  return (
    <Paper component="form" elevation={0} onSubmit={onSubmit} sx={{ p: 4 }}>
      <Typography variant="h5" sx={{ mb: 2 }}>
        {t('pages.partyCreate.title')}
      </Typography>

      <Stack spacing={2}>
        <TextField
          label={t('pages.partyCreate.displayName')}
          value={displayName}
          onChange={(event) => setDisplayName(event.target.value)}
          required
        />
        <TextField
          label={t('pages.partyCreate.displayNameAr')}
          value={displayNameAr}
          onChange={(event) => setDisplayNameAr(event.target.value)}
          required
        />
        <Button type="submit" variant="contained" disabled={isSubmitting}>
          {t('pages.partyCreate.submit')}
        </Button>
      </Stack>
    </Paper>
  );
}
