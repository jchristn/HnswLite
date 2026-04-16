import Modal from './Modal';
import CopyIconButton from './CopyIconButton';

interface ViewJsonModalProps {
  open: boolean;
  title: string;
  data: unknown;
  onClose: () => void;
}

function stringify(data: unknown): string {
  if (typeof data === 'string') {
    try {
      return JSON.stringify(JSON.parse(data), null, 2);
    } catch {
      return data;
    }
  }
  try {
    return JSON.stringify(data, null, 2);
  } catch {
    return String(data);
  }
}

export default function ViewJsonModal({ open, title, data, onClose }: ViewJsonModalProps) {
  const text = stringify(data);
  return (
    <Modal
      open={open}
      onClose={onClose}
      title={title}
      size="large"
      footer={
        <button className="btn btn-secondary" onClick={onClose}>
          Close
        </button>
      }
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          marginBottom: 8,
        }}
      >
        <div style={{ fontSize: '0.78rem', color: 'var(--text-secondary)' }}>
          {text.length.toLocaleString()} bytes
        </div>
        <CopyIconButton text={text} />
      </div>
      <pre className="code-block" style={{ maxHeight: 'unset' }}>
        {text}
      </pre>
    </Modal>
  );
}
