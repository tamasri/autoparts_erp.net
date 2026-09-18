type BadgeType = 'invoice' | 'customer' | 'approval';

type Props = {
  status: string;
  type: BadgeType;
};

function palette(status: string): { className: string; label: string } {
  const normalized = status.toUpperCase();
  if (
    normalized === 'POSTED' ||
    normalized === 'SUCCESS' ||
    normalized === 'ACTIVE' ||
    normalized === 'APPROVED'
  ) {
    return { className: 'badge badge--success', label: status };
  }
  if (normalized === 'DRAFT') {
    return { className: 'badge badge--draft', label: status };
  }
  if (normalized === 'PENDING') {
    return { className: 'badge badge--warning', label: status };
  }
  if (
    normalized === 'VOID' ||
    normalized === 'FAILED' ||
    normalized === 'REJECTED' ||
    normalized === 'INACTIVE'
  ) {
    return { className: 'badge badge--danger', label: status };
  }
  if (normalized === 'CONFIRMED') {
    return { className: 'badge badge--primary', label: status };
  }
  return { className: 'badge badge--warning', label: status };
}

export default function StatusBadge({ status }: Props): JSX.Element {
  const { className, label } = palette(status);
  return <span className={className}>{label}</span>;
}
