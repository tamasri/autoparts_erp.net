/**
 * An amount kept in dollars, shown in both currencies: the preferred one large (lira by default, from the latest saved rate) and the other small.
 * Display only — nothing is converted or stored. Without a saved rate only the dollar amount is shown.
 */
import { Box, Typography, type TypographyProps } from '@mui/material';
import { useFxMid } from '../../hooks/useFxMid';
import { useDisplayStore } from '../../stores/displayStore';
import { formatSyp, formatUsd } from '../../lib/format';

type Props = {
  usd: number | null | undefined;
  /** The lira amount as recorded on the document; when given it is shown instead of converting at today's rate. */
  syp?: number | null;
  /** One line ("12,000 ل.س · $1.00") instead of two. */
  inline?: boolean;
  variant?: TypographyProps['variant'];
  fontWeight?: number;
  color?: string;
};

export function useMoneyParts(usd: number, syp?: number | null): { main: string; sub: string | null } {
  const mid = useFxMid();
  const primary = useDisplayStore((s) => s.primary);
  const lira = syp ?? (mid > 0 ? usd * mid : null);
  if (lira === null) return { main: formatUsd(usd), sub: null };
  return primary === 'SYP' ? { main: formatSyp(lira), sub: formatUsd(usd) } : { main: formatUsd(usd), sub: formatSyp(lira) };
}

export default function Money({ usd, syp, inline, variant = 'body2', fontWeight, color }: Props): JSX.Element {
  const { main, sub } = useMoneyParts(Number(usd ?? 0), syp);
  if (inline) {
    return (
      <Typography component="span" variant={variant} fontWeight={fontWeight} color={color} sx={{ whiteSpace: 'nowrap' }}>
        {main}{sub ? <Typography component="span" variant="caption" color="text.secondary"> · {sub}</Typography> : null}
      </Typography>
    );
  }
  return (
    <Box component="span" sx={{ display: 'inline-flex', flexDirection: 'column', lineHeight: 1.15 }}>
      <Typography component="span" variant={variant} fontWeight={fontWeight} color={color} sx={{ whiteSpace: 'nowrap' }}>{main}</Typography>
      {sub ? <Typography component="span" variant="caption" color="text.secondary" sx={{ whiteSpace: 'nowrap' }}>{sub}</Typography> : null}
    </Box>
  );
}
