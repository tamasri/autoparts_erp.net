import type { SalesDay } from '../../api/endpoints/dashboard';

type Props = { days: SalesDay[] };

/** Dependency-free SVG bar chart of daily sales (USD). Oldest day on the right, matching RTL reading order. */
export default function SalesChart({ days }: Props): JSX.Element {
  const width = 720;
  const height = 180;
  const pad = { top: 12, bottom: 22, side: 8 };
  const max = Math.max(...days.map((d) => d.totalUsd), 1);
  const slot = (width - pad.side * 2) / Math.max(days.length, 1);
  const barW = Math.max(slot * 0.62, 2);
  const plotH = height - pad.top - pad.bottom;

  return (
    <div style={{ width: '100%', overflowX: 'auto' }}>
      <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label="المبيعات اليومية" style={{ width: '100%', minWidth: 420, height: 'auto', direction: 'ltr' }}>
        <line x1={pad.side} x2={width - pad.side} y1={height - pad.bottom} y2={height - pad.bottom} stroke="var(--clr-border)" />
        {days.map((d, i) => {
          const h = (d.totalUsd / max) * plotH;
          // ltr drawing space: index 0 (oldest) on the right to mirror RTL.
          const x = width - pad.side - (i + 1) * slot + (slot - barW) / 2;
          const y = height - pad.bottom - h;
          return (
            <g key={d.day}>
              <title>{`${d.day}: $${d.totalUsd.toLocaleString('en-US')} · ${d.totalSyp.toLocaleString('en-US')} ل.س · ${d.invoiceCount} فاتورة`}</title>
              <rect x={x} y={d.totalUsd > 0 ? y : height - pad.bottom - 1} width={barW} height={d.totalUsd > 0 ? Math.max(h, 2) : 1}
                rx={2} fill={d.totalUsd > 0 ? 'var(--clr-primary)' : 'var(--clr-border)'} />
              {i % 5 === 0 ? (
                <text x={x + barW / 2} y={height - 6} textAnchor="middle" fontSize="9" fill="var(--txt-muted)">{d.day.slice(5)}</text>
              ) : null}
            </g>
          );
        })}
      </svg>
    </div>
  );
}
