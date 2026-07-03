type Props = {
  icon: string;
  message: string;
  actionLabel?: string;
  onAction?: () => void;
};

export default function EmptyState({
  icon,
  message,
  actionLabel,
  onAction,
}: Props): JSX.Element {
  return (
    <div style={{ textAlign: 'center', padding: '36px 16px', color: '#607d8b', direction: 'rtl' }}>
      <div style={{ fontSize: '40px' }}>{icon}</div>
      <p>{message}</p>
      {actionLabel && onAction && (
        <button
          type="button"
          onClick={onAction}
          style={{
            border: 'none',
            borderRadius: '8px',
            background: '#00796b',
            color: '#fff',
            padding: '8px 12px',
            cursor: 'pointer',
          }}
        >
          {actionLabel}
        </button>
      )}
    </div>
  );
}
