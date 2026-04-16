import type { ReactNode } from 'react';
import Modal from './Modal';

interface ConfirmDialogProps {
  open: boolean;
  title: string;
  message: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  dangerous?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

export default function ConfirmDialog(props: ConfirmDialogProps) {
  return (
    <Modal
      open={props.open}
      onClose={props.onCancel}
      title={props.title}
      footer={
        <>
          <button className="btn btn-secondary" onClick={props.onCancel}>
            {props.cancelLabel ?? 'Cancel'}
          </button>
          <button
            className={`btn ${props.dangerous ? 'btn-danger' : 'btn-primary'}`}
            onClick={props.onConfirm}
          >
            {props.confirmLabel ?? 'Confirm'}
          </button>
        </>
      }
    >
      <div style={{ fontSize: '0.9rem', color: 'var(--text-primary)' }}>{props.message}</div>
    </Modal>
  );
}
