export default function LoadingSpinner(): JSX.Element {
  return (
    <div style={{ display: 'grid', placeItems: 'center', padding: '24px', direction: 'rtl' }}>
      <style>
        {`@keyframes spin{to{transform:rotate(360deg)}}`}
      </style>
      <div
        style={{
          width: '28px',
          height: '28px',
          borderRadius: '50%',
          border: '3px solid #cfd8dc',
          borderTopColor: '#00796b',
          animation: 'spin 0.9s linear infinite',
        }}
      />
      <div style={{ marginTop: '10px', color: '#546e7a' }}>جارٍ التحميل...</div>
    </div>
  );
}
