declare const __HNSWLITE_SERVER_URL__: string;

import { useAuth } from '../context/AuthContext';
import { useTheme } from '../context/ThemeContext';
import CopyButton from '../components/shared/CopyButton';

export default function Settings() {
  const { apiKey, apiKeyHeader, logout } = useAuth();
  const { darkMode, toggle } = useTheme();

  const serverUrl = __HNSWLITE_SERVER_URL__ || '(same origin as dashboard)';

  return (
    <>
      <div className="workspace-header">
        <div className="workspace-title">
          <h2>Settings</h2>
          <p className="workspace-subtitle">Session, connection, and appearance settings.</p>
        </div>
      </div>

      <div className="workspace-card">
        <div className="workspace-card-header">
          <h3>Session</h3>
        </div>
        <div className="workspace-card-body" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <div className="field">
            <span>Auth header</span>
            <div className="mono" style={{ padding: '8px 10px', background: 'var(--bg-secondary)', borderRadius: 6, border: '1px solid var(--border-color)' }}>
              {apiKeyHeader}
            </div>
          </div>
          <div className="field">
            <span>API key</span>
            <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
              <div
                className="mono"
                style={{
                  flex: 1,
                  padding: '8px 10px',
                  background: 'var(--bg-secondary)',
                  borderRadius: 6,
                  border: '1px solid var(--border-color)',
                }}
              >
                {apiKey ? maskKey(apiKey) : '(none)'}
              </div>
              {apiKey && <CopyButton text={apiKey} label="Copy full key" />}
            </div>
          </div>
          <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
            <button className="btn btn-danger" onClick={logout}>
              Sign out
            </button>
          </div>
        </div>
      </div>

      <div className="workspace-card">
        <div className="workspace-card-header">
          <h3>Connection</h3>
        </div>
        <div className="workspace-card-body">
          <div className="field">
            <span>Server URL</span>
            <div
              className="mono"
              style={{
                padding: '8px 10px',
                background: 'var(--bg-secondary)',
                borderRadius: 6,
                border: '1px solid var(--border-color)',
              }}
            >
              {serverUrl}
            </div>
          </div>
          <div style={{ fontSize: '0.78rem', color: 'var(--text-secondary)', marginTop: 8 }}>
            Set <span className="mono">HNSWLITE_SERVER_URL</span> at dashboard build time to override.
            In production the nginx container proxies <span className="mono">/v1.0</span> to the server.
          </div>
        </div>
      </div>

      <div className="workspace-card">
        <div className="workspace-card-header">
          <h3>Appearance</h3>
        </div>
        <div className="workspace-card-body" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <div>
            <div style={{ fontWeight: 500 }}>Theme</div>
            <div style={{ fontSize: '0.78rem', color: 'var(--text-secondary)' }}>
              Currently: <b>{darkMode ? 'Dark' : 'Light'}</b>
            </div>
          </div>
          <button className="btn btn-secondary" onClick={toggle}>
            {darkMode ? 'Switch to light' : 'Switch to dark'}
          </button>
        </div>
      </div>

      <div className="workspace-card">
        <div className="workspace-card-header">
          <h3>Request history</h3>
        </div>
        <div className="workspace-card-body" style={{ fontSize: '0.85rem', color: 'var(--text-secondary)' }}>
          Requests made through this dashboard are captured locally and retained for 30 days, then
          automatically purged. Use the <b>Request History</b> page to inspect or clear them.
        </div>
      </div>
    </>
  );
}

function maskKey(key: string): string {
  if (key.length <= 8) return '••••';
  return `${key.slice(0, 4)}…${key.slice(-4)}`;
}
