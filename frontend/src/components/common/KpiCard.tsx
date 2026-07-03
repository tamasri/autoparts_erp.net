type Props = {
  title: string;
  value: string | number;
  unit?: string;
  icon: string;
  trend?: string;
};

export default function KpiCard({
  title,
  value,
  unit,
  icon,
  trend,
}: Props): JSX.Element {
  return (
    <div
      style={{
        background: '#fff',
        borderRadius: '12px',
        boxShadow: '0 2px 8px #00000012',
        padding: '14px',
        direction: 'rtl',
        borderRight: '4px solid #00796b',
      }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'start' }}>
        <div>
          <div style={{ color: '#607d8b', fontSize: '13px' }}>{title}</div>
          <div style={{ color: '#00796b', fontSize: '28px', fontWeight: 800 }}>
            {value}
            {unit ? ` ${unit}` : ''}
          </div>
          {trend ? <div style={{ color: '#90a4ae', fontSize: '12px' }}>{trend}</div> : null}
        </div>
        <div style={{ fontSize: '22px' }}>{icon}</div>
      </div>
    </div>
  );
}
