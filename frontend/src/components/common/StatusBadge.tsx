type BadgeType = 'invoice' | 'customer' | 'approval';

type Props = {
  status: string;
  type: BadgeType;
};

function palette(status: string): { bg: string; fg: string } {
  const normalized = status.toUpperCase();
  if (normalized === 'POSTED' || normalized === 'SUCCESS' || normalized === 'ACTIVE' || normalized === 'APPROVED') {
    return { bg: '#e8f5e9', fg: '#1b5e20' };
  }
  if (normalized === 'DRAFT' || normalized === 'PENDING') {
    return { bg: '#eceff1', fg: '#37474f' };
  }
  if (normalized === 'VOID' || normalized === 'FAILED' || normalized === 'REJECTED' || normalized === 'INACTIVE') {
    return { bg: '#ffebee', fg: '#b71c1c' };
  }
  return { bg: '#fff8e1', fg: '#e65100' };
}

export default function StatusBadge({ status }: Props): JSX.Element {
  const p = palette(status);
  return (
    <span
      style={{
        padding: '2px 8px',
        borderRadius: '999px',
        background: p.bg,
        color: p.fg,
        fontSize: '12px',
        fontWeight: 700,
      }}
    >
      {status}
    </span>
  );
}
