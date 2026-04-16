import { useCallback, useEffect, useState } from 'react';
import { listAllIndexes, ping } from '../api/client';
import type { IndexSummary } from '../types/models';
import { RefreshIcon } from '../components/shared/Icons';

declare const __APP_VERSION__: string;
declare const __HNSWLITE_SERVER_URL__: string;

export default function ServerInfo() {
  const [alive, setAlive] = useState<boolean | null>(null);
  const [indexes, setIndexes] = useState<IndexSummary[]>([]);
  const [checking, setChecking] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  const check = useCallback(async () => {
    setChecking(true);
    setError(null);
    try {
      const ok = await ping();
      setAlive(ok);
      if (ok) setIndexes(await listAllIndexes());
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setChecking(false);
    }
  }, []);

  useEffect(() => {
    void check();
  }, [check]);

  const totalVectors = indexes.reduce((s, i) => s + i.vectorCount, 0);
  const storage = countByKey(indexes, 'storageType');
  const distance = countByKey(indexes, 'distanceFunction');

  return (
    <>
      <div className="workspace-header">
        <div className="workspace-title">
          <h2>Server Info</h2>
          <p className="workspace-subtitle">Reachability and high-level index statistics.</p>
        </div>
        <div className="workspace-actions">
          <button
            className={`btn-icon ${checking ? 'spin-on-active' : ''}`}
            onClick={check}
            disabled={checking}
            title="Refresh"
            aria-label="Refresh"
          >
            <RefreshIcon />
          </button>
        </div>
      </div>

      {error && <div className="form-error">{error}</div>}

      <div
        className="stat-grid"
        style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', marginBottom: 16 }}
      >
        <div className="stat-card">
          <div className="stat-card-label">Reachability</div>
          <div className="stat-card-value" style={{ fontSize: '1.1rem' }}>
            {alive === null ? (
              '—'
            ) : alive ? (
              <span className="status-pill success">Online</span>
            ) : (
              <span className="status-pill error">Unreachable</span>
            )}
          </div>
        </div>
        <div className="stat-card">
          <div className="stat-card-label">Indexes</div>
          <div className="stat-card-value">{indexes.length}</div>
        </div>
        <div className="stat-card">
          <div className="stat-card-label">Total vectors</div>
          <div className="stat-card-value">{totalVectors.toLocaleString()}</div>
        </div>
        <div className="stat-card">
          <div className="stat-card-label">Dashboard version</div>
          <div className="stat-card-value mono" style={{ fontSize: '1.1rem' }}>
            {__APP_VERSION__}
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
              {__HNSWLITE_SERVER_URL__ || '(same origin as dashboard)'}
            </div>
          </div>
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))', gap: 16 }}>
        <div className="workspace-card">
          <div className="workspace-card-header">
            <h3>Indexes by storage type</h3>
          </div>
          <div className="workspace-card-body tight">
            <KeyValueTable entries={storage} />
          </div>
        </div>
        <div className="workspace-card">
          <div className="workspace-card-header">
            <h3>Indexes by distance function</h3>
          </div>
          <div className="workspace-card-body tight">
            <KeyValueTable entries={distance} />
          </div>
        </div>
      </div>
    </>
  );
}

function countByKey(
  indexes: IndexSummary[],
  key: 'storageType' | 'distanceFunction',
): Array<[string, number]> {
  const map = new Map<string, number>();
  for (const ix of indexes) {
    const k = ix[key];
    map.set(k, (map.get(k) ?? 0) + 1);
  }
  return [...map.entries()].sort((a, b) => b[1] - a[1]);
}

function KeyValueTable({ entries }: { entries: Array<[string, number]> }) {
  if (entries.length === 0) {
    return (
      <div className="empty-state compact-empty-state">
        <p className="empty-state-description">None</p>
      </div>
    );
  }
  return (
    <table className="data-table">
      <tbody>
        {entries.map(([k, v]) => (
          <tr key={k}>
            <td>{k}</td>
            <td style={{ textAlign: 'right' }}>{v}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
