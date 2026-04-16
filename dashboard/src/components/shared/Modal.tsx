import { useEffect } from 'react';
import type { ReactNode } from 'react';

interface ModalProps {
  open: boolean;
  title: string;
  onClose: () => void;
  children: ReactNode;
  size?: 'medium' | 'large' | 'xlarge';
  footer?: ReactNode;
}

export default function Modal(props: ModalProps) {
  useEffect(() => {
    if (!props.open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') props.onClose();
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [props]);

  if (!props.open) return null;

  const sizeClass =
    props.size === 'large' ? 'modal-large' : props.size === 'xlarge' ? 'modal-xlarge' : '';

  return (
    <div className="modal-overlay" onClick={props.onClose}>
      <div className={`modal-container ${sizeClass}`} onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h3 className="modal-title">{props.title}</h3>
          <button className="modal-close" onClick={props.onClose} aria-label="Close">
            ×
          </button>
        </div>
        <div className="modal-body">{props.children}</div>
        {props.footer && (
          <div
            style={{
              padding: '12px 16px',
              borderTop: '1px solid var(--border-color)',
              display: 'flex',
              justifyContent: 'flex-end',
              gap: 8,
            }}
          >
            {props.footer}
          </div>
        )}
      </div>
    </div>
  );
}
