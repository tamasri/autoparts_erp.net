type Props = {
  title: string;
  value: string | number;
  unit?: string;
  icon: string;
  trend?: string;
  colorVariant?: 'primary' | 'success' | 'warning' | 'danger';
};

export default function KpiCard({
  title,
  value,
  unit,
  icon,
  trend,
  colorVariant = 'primary',
}: Props): JSX.Element {
  return (
    <div
      className="vex-card"
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 16,
        direction: 'rtl',
        cursor: 'default',
        transition: 'transform var(--transition-base), box-shadow var(--transition-base)',
      }}
      onMouseEnter={(e) => {
        (e.currentTarget as HTMLElement).style.transform = 'translateY(-3px)';
      }}
      onMouseLeave={(e) => {
        (e.currentTarget as HTMLElement).style.transform = 'translateY(0)';
      }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
        <div style={{ flex: 1 }}>
          <div style={{ fontSize: 12.5, fontWeight: 500, color: 'var(--txt-muted)', marginBottom: 6, textTransform: 'uppercase', letterSpacing: '0.4px' }}>
            {title}
          </div>
          <div style={{ fontSize: 30, fontWeight: 800, color: 'var(--txt-primary)', lineHeight: 1.1 }}>
            {value}
            {unit && <span style={{ fontSize: 14, fontWeight: 500, color: 'var(--txt-secondary)', marginRight: 4 }}>{unit}</span>}
          </div>
          {trend && (
            <div style={{ fontSize: 12, color: 'var(--txt-muted)', marginTop: 6 }}>
              {trend}
            </div>
          )}
        </div>
        <div className={`kpi-icon-wrap kpi-icon-wrap--${colorVariant}`}>
          {icon}
        </div>
      </div>

      {/* Bottom accent bar */}
      <div style={{
        height: 3,
        borderRadius: 'var(--radius-pill)',
        background: colorVariant === 'primary' ? 'linear-gradient(90deg, var(--clr-primary), var(--clr-primary-mid))'
          : colorVariant === 'success' ? 'linear-gradient(90deg, #22c55e, #4ade80)'
          : colorVariant === 'warning' ? 'linear-gradient(90deg, #f59e0b, #fbbf24)'
          : 'linear-gradient(90deg, #ef4444, #f87171)',
        marginTop: 4,
      }} />
    </div>
  );
}
