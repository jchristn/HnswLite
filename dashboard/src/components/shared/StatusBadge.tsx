interface StatusBadgeProps {
  status: number;
}

export default function StatusBadge({ status }: StatusBadgeProps) {
  let cls = 'muted';
  if (status >= 200 && status < 300) cls = 'success';
  else if (status >= 300 && status < 400) cls = 'warn';
  else if (status >= 400) cls = 'error';
  else if (status === 0) cls = 'error';
  return <span className={`status-pill ${cls}`}>{status === 0 ? 'ERR' : status}</span>;
}
