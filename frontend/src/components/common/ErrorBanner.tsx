type Props = {
  message: string;
};

export default function ErrorBanner({ message }: Props): JSX.Element {
  return (
    <div
      style={{
        marginBottom: '12px',
        background: '#ffebee',
        color: '#b71c1c',
        border: '1px solid #ef9a9a',
        borderRadius: '8px',
        padding: '10px 12px',
        direction: 'rtl',
      }}
    >
      {message}
    </div>
  );
}
