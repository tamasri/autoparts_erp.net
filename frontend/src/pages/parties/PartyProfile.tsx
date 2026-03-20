import { CircularProgress, Paper, Stack, Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router-dom';
import { partiesApi } from '../../api/endpoints/parties';
import { CombinedStatementView } from '../../components/party/CombinedStatementView';
import { TypeBadge } from '../../components/party/TypeBadge';

type PartyProfile = {
  id: string;
  code: string;
  displayName: string;
  displayNameAr: string;
  hasCombinedStatement: boolean;
  typeAssignments: Array<{ typeCode: string; isActive: boolean }>;
};

export function PartyProfilePage(): JSX.Element {
  const { t } = useTranslation();
  const { id } = useParams();
  const [isLoading, setIsLoading] = useState(true);
  const [profile, setProfile] = useState<PartyProfile | null>(null);

  useEffect(() => {
    let isMounted = true;
    if (!id) {
      setIsLoading(false);
      return;
    }

    partiesApi.getPartyById(id)
      .then((response) => {
        if (isMounted) {
          setProfile(response.data?.data ?? null);
        }
      })
      .finally(() => {
        if (isMounted) {
          setIsLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [id]);

  if (isLoading) {
    return (
      <Paper elevation={0} sx={{ p: 4, textAlign: 'center' }}>
        <CircularProgress />
      </Paper>
    );
  }

  if (!profile) {
    return (
      <Paper elevation={0} sx={{ p: 4 }}>
        <Typography>{t('pages.partyProfile.notFound')}</Typography>
      </Paper>
    );
  }

  return (
    <Stack spacing={2}>
      <Paper elevation={0} sx={{ p: 4 }}>
        <Typography variant="h5">{t('pages.partyProfile.title')}</Typography>
        <Typography color="text.secondary">{profile.code}</Typography>
        <Typography sx={{ mt: 1 }}>{profile.displayName}</Typography>
        <Typography color="text.secondary">{profile.displayNameAr}</Typography>

        <Stack direction="row" spacing={1} sx={{ mt: 2, flexWrap: 'wrap' }}>
          {profile.typeAssignments
            .filter((assignment) => assignment.isActive)
            .map((assignment) => (
              <TypeBadge key={assignment.typeCode} typeCode={assignment.typeCode} />
            ))}
        </Stack>
      </Paper>

      {profile.hasCombinedStatement ? (
        <CombinedStatementView
          arOutstandingSyp={0}
          arOutstandingUsd={0}
          apOutstandingSyp={0}
          apOutstandingUsd={0}
        />
      ) : null}
    </Stack>
  );
}
