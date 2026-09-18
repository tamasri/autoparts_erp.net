export default function LoadingSpinner(): JSX.Element {
  return (
    <div className="vex-spinner-wrap">
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 16 }}>
        <div className="vex-spinner" />
        <span style={{ fontSize: 13, color: 'var(--txt-muted)', fontWeight: 500 }}>جارٍ التحميل...</span>
      </div>
    </div>
  );
}
