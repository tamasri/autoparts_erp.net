import { Chip } from '@mui/material';

type PeriodLockBadgeProps = {
  isLocked: boolean;
};

export function PeriodLockBadge({ isLocked }: PeriodLockBadgeProps): JSX.Element {
  return (
    <Chip
      color={isLocked ? 'warning' : 'success'}
      label={isLocked ? 'Locked' : 'Open'}
      size="small"
      variant={isLocked ? 'filled' : 'outlined'}
    />
  );
}
