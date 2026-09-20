/** Search-and-pick a ledger account from the chart (grouped by its class: assets, liabilities, ...). */
import { Autocomplete, Box, TextField, Typography } from '@mui/material';
import type { Account } from '../../api/endpoints/accounting';
import { ROOT_LABEL, accountTypeLabel } from './labels';

type Props = {
  accounts: Account[];
  value: string | null;
  onChange: (account: Account | null) => void;
  label?: string;
  disabled?: boolean;
  error?: boolean;
  loading?: boolean;
  size?: 'small' | 'medium';
  /** Narrow the list, e.g. to cash and bank accounts. */
  filter?: (account: Account) => boolean;
};

export default function AccountPicker({ accounts, value, onChange, label = 'الحساب', disabled, error, loading, size = 'small', filter }: Props): JSX.Element {
  const options = (filter ? accounts.filter(filter) : accounts).slice().sort((a, b) => (a.rootType ?? '').localeCompare(b.rootType ?? '') || a.accountName.localeCompare(b.accountName));
  return (
    <Autocomplete
      size={size}
      disabled={disabled}
      loading={loading}
      options={options}
      value={options.find((a) => a.name === value) ?? accounts.find((a) => a.name === value) ?? null}
      onChange={(_, a) => onChange(a)}
      isOptionEqualToValue={(a, b) => a.name === b.name}
      getOptionLabel={(a) => a.accountName}
      groupBy={(a) => ROOT_LABEL[a.rootType ?? ''] ?? 'أخرى'}
      noOptionsText="لا توجد حسابات مطابقة"
      renderOption={(props, a) => (
        <Box component="li" {...props} key={a.name} sx={{ display: 'flex', justifyContent: 'space-between', gap: 2 }}>
          <Typography variant="body2">{a.accountName}</Typography>
          <Typography variant="caption" color="text.secondary">{accountTypeLabel(a.accountType)}</Typography>
        </Box>
      )}
      renderInput={(params) => <TextField {...params} label={label} error={error} />}
      sx={{ minWidth: 200 }}
    />
  );
}
