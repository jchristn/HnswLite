import { useState } from 'react';
import { CheckIcon, CopyIcon } from './Icons';

interface CopyIconButtonProps {
  text: string;
  size?: number;
  title?: string;
}

/**
 * Icon-only clipboard-copy control.
 *
 * Uses navigator.clipboard when the page is in a secure context (https or localhost);
 * falls back to a hidden textarea + document.execCommand('copy') for non-secure
 * contexts, which works in every modern browser regardless of the URL scheme.
 */
export default function CopyIconButton({ text, size = 16, title }: CopyIconButtonProps) {
  const [copied, setCopied] = useState<boolean>(false);

  async function doCopy(): Promise<void> {
    let ok = false;

    if (window.isSecureContext && navigator.clipboard && typeof navigator.clipboard.writeText === 'function') {
      try {
        await navigator.clipboard.writeText(text);
        ok = true;
      } catch {
        // fall through to legacy path
      }
    }

    if (!ok) {
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
        ok = document.execCommand('copy');
        document.body.removeChild(ta);
      } catch {
        ok = false;
      }
    }

    if (ok) {
      setCopied(true);
      setTimeout(() => setCopied(false), 1400);
    }
  }

  return (
    <button
      type="button"
      className="btn-icon"
      onClick={(e) => {
        e.stopPropagation();
        void doCopy();
      }}
      title={copied ? 'Copied' : (title ?? 'Copy to clipboard')}
      aria-label={copied ? 'Copied' : (title ?? 'Copy to clipboard')}
      style={
        copied
          ? { color: 'var(--color-success)', borderColor: 'var(--color-success)' }
          : undefined
      }
    >
      {copied ? <CheckIcon size={size} /> : <CopyIcon size={size} />}
    </button>
  );
}
