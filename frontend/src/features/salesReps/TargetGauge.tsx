/** How much of a rep's target was reached: a gauge (capped at 100 %) with the percentage, what was achieved and the target itself. */
import { Box, Stack, Typography } from '@mui/material';
import { Gauge, gaugeClasses } from '@mui/x-charts/Gauge';
import Money from '../../components/ui/Money';

type Props = { achieved: number; target: number; achievementPct: number | null; size?: number };

export default function TargetGauge({ achieved, target, achievementPct, size = 150 }: Props): JSX.Element {
  if (achievementPct === null) {
    return <Typography variant="body2" color="text.secondary">لا هدف محدد لهذا المندوب</Typography>;
  }

  const tone = achievementPct >= 100 ? 'success' : achievementPct >= 60 ? 'primary' : 'warning';
  return (
    <Stack alignItems="center" spacing={0.5}>
      <Box sx={{ width: size, height: size * 0.75 }}>
        <Gauge
          value={Math.min(Math.max(achievementPct, 0), 100)}
          startAngle={-110}
          endAngle={110}
          innerRadius="72%"
          text={`${achievementPct}%`}
          sx={(theme) => ({
            [`& .${gaugeClasses.valueText}`]: { fontSize: size / 7, fontWeight: 800 },
            [`& .${gaugeClasses.valueArc}`]: { fill: theme.palette[tone].main },
          })}
        />
      </Box>
      <Typography variant="caption" color="text.secondary">
        أُنجز <Money usd={achieved} inline variant="caption" fontWeight={700} /> من <Money usd={target} inline variant="caption" />
      </Typography>
    </Stack>
  );
}
