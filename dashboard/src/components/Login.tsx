import { useState } from 'react';
import type { FormEvent } from 'react';
import { useAuth } from '../context/AuthContext';
import { useTheme } from '../context/ThemeContext';
import { MoonIcon, SunIcon } from './shared/Icons';

declare const __APP_VERSION__: string;

export default function Login() {
  const { login } = useAuth();
  const { darkMode, toggle } = useTheme();
  const [key, setKey] = useState<string>('');
  const [showKey, setShowKey] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState<boolean>(false);

  async function onSubmit(e: FormEvent): Promise<void> {
    e.preventDefault();
    if (!key.trim()) {
      setError('API key is required');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      const ok = await login(key.trim());
      if (!ok) setError('Invalid API key — server rejected the credential');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Login failed');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 20,
        background: 'var(--bg-secondary)',
      }}
    >
      <div
        style={{
          background: 'var(--bg-primary)',
          borderRadius: 12,
          boxShadow: '0 4px 20px var(--shadow-color)',
          padding: 40,
          width: 440,
          maxWidth: '100%',
          position: 'relative',
        }}
      >
        <button
          onClick={toggle}
          title={darkMode ? 'Switch to light mode' : 'Switch to dark mode'}
          style={{
            position: 'absolute',
            top: 12,
            right: 12,
            background: 'none',
            border: 'none',
            padding: 6,
            borderRadius: 4,
            cursor: 'pointer',
            color: 'var(--text-secondary)',
            opacity: 0.65,
          }}
          aria-label="Toggle theme"
        >
          {darkMode ? <SunIcon /> : <MoonIcon />}
        </button>

        <div style={{ textAlign: 'center', marginBottom: 28 }}>
          <img src="/dashboard/logo.png" alt="HnswLite" style={{ height: 72, width: 'auto', marginBottom: 16 }} />
          <h1
            style={{
              fontSize: '1.9rem',
              color: 'var(--color-primary)',
              margin: '0 0 6px 0',
              fontWeight: 700,
            }}
          >
            HnswLite
          </h1>
          <p style={{ color: 'var(--text-secondary)', margin: 0, fontSize: '0.9rem' }}>
            HNSW vector-index dashboard
          </p>
        </div>

        <form onSubmit={onSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
          {error && <div className="form-error">{error}</div>}

          <div className="field">
            <span>API Key</span>
            <div style={{ position: 'relative' }}>
              <input
                type={showKey ? 'text' : 'password'}
                autoComplete="off"
                value={key}
                onChange={(e) => setKey(e.target.value)}
                placeholder="Enter admin API key"
                autoFocus
                style={{ paddingRight: 40 }}
              />
              <button
                type="button"
                onClick={() => setShowKey((v) => !v)}
                title={showKey ? 'Hide' : 'Show'}
                style={{
                  position: 'absolute',
                  right: 6,
                  top: '50%',
                  transform: 'translateY(-50%)',
                  background: 'none',
                  border: 'none',
                  padding: 6,
                  borderRadius: 4,
                  color: 'var(--text-muted)',
                  cursor: 'pointer',
                }}
              >
                {showKey ? (
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24" />
                    <line x1="1" y1="1" x2="23" y2="23" />
                  </svg>
                ) : (
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" />
                    <circle cx="12" cy="12" r="3" />
                  </svg>
                )}
              </button>
            </div>
          </div>

          <button
            type="submit"
            className="btn btn-primary"
            disabled={submitting}
            style={{ padding: '12px', fontSize: '0.95rem', fontWeight: 600, marginTop: 4 }}
          >
            {submitting ? 'Connecting…' : 'Sign in'}
          </button>

          <div style={{ textAlign: 'center', fontSize: '0.72rem', color: 'var(--text-muted)', marginTop: 4 }}>
            Dashboard v{__APP_VERSION__}
          </div>
        </form>
      </div>
    </div>
  );
}
