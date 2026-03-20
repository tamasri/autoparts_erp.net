import { Chip } from '@mui/material';
import { useTranslation } from 'react-i18next';

type TypeBadgeProps = {
  typeCode: string;
};

const colorMap: Record<string, 'default' | 'primary' | 'secondary' | 'success' | 'warning' | 'error' | 'info'> = {
  CUSTOMER: 'primary',
  VENDOR: 'secondary',
  EMPLOYEE: 'info',
  DELIVERY_COMPANY: 'warning',
  GOVERNMENT: 'success',
};

export function TypeBadge({ typeCode }: TypeBadgeProps): JSX.Element {
  const { t } = useTranslation();
  const normalized = typeCode.toUpperCase();

  return (
    <Chip
      size="small"
      color={colorMap[normalized] ?? 'default'}
      label={t(`party.type.${normalized}`, { defaultValue: normalized })}
      variant="outlined"
    />
  );
}
