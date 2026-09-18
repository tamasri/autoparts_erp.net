type Props = { message: string };

export default function ErrorBanner({ message }: Props): JSX.Element {
  return (
    <div className="vex-alert vex-alert--error">
      <span className="vex-alert__icon">⚠</span>
      <span>{message}</span>
    </div>
  );
}
