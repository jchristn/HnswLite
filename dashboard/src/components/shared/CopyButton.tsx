import { useState } from 'react';
import { CheckIcon, CopyIcon } from './Icons';

interface CopyButtonProps {
  text: string;
  label?: string;
}

async function robustCopy(text: string): Promise<boolean> {
  if (window.isSecureContext && navigator.clipboard && typeof navigator.clipboard.writeText === 'function') {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      // fall through
    }
  }
  try {
    const ta = document.createElement('textarea');
    ta.value = text;
    ta.setAttribute('readonly', '');
    ta.style.position = 'fixed';
    ta.style.top = '0';
    ta.style.left = '-9999px';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.focus();
    ta.select();
    ta.setSelectionRange(0, ta.value.length);
    const ok = document.execCommand('copy');
    document.body.removeChild(ta);
    return ok;
  } catch {
    return false;
  }
}

export default function CopyButton({ text, label }: CopyButtonProps) {
  const [copied, setCopied] = useState<boolean>(false);

  async function onClick(): Promise<void> {
    const ok = await robustCopy(text);
    if (ok) {
      setCopied(true);
      setTimeout(() => setCopied(false), 1400);
    }
  }

  return (
    <button
      type="button"
      className="btn btn-secondary btn-sm"
      onClick={onClick}
      title={copied ? 'Copied' : 'Copy'}
      style={copied ? { color: 'var(--color-success)' } : undefined}
    >
      {copied ? <CheckIcon size={14} /> : <CopyIcon size={14} />}
      {label && <span style={{ marginLeft: 4 }}>{copied ? 'Copied' : label}</span>}
    </button>
  );
}
