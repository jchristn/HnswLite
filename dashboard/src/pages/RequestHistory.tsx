import { useEffect, useMemo, useState } from 'react';
import {
  clearRequestHistory,
  getRequestHistory,
  removeRequestHistoryEntry,
  subscribeRequestHistory,
} from '../api/client';
import type { RequestHistoryEntryClient } from '../api/client';
import HistoryChart, {
  HistoryChartLegend,
  RANGE_OPTIONS,
} from '../components/shared/HistoryChart';
import type { RangeId } from '../components/shared/HistoryChart';
import JsonViewer from '../components/shared/JsonViewer';
import StatusBadge from '../components/shared/StatusBadge';
import CopyIconButton from '../components/shared/CopyIconButton';
import Modal from '../components/shared/Modal';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import ActionMenu from '../components/shared/ActionMenu';
import ViewJsonModal from '../components/shared/ViewJsonModal';

export default function RequestHistory() {
  const [entries, setEntries] = useState<RequestHistoryEntryClient[]>(() => getRequestHistory());
  const [rangeId, setRangeId] = useState<RangeId>('day');
  const [methodFilter, setMethodFilter] = useState<string>('');
  const [statusFilter, setStatusFilter] = useState<string>('');
  const [pathFilter, setPathFilter] = useState<string>('');
  const [detail, setDetail] = useState<RequestHistoryEntryClient | null>(null);
  const [viewingJson, setViewingJson] = useState<RequestHistoryEntryClient | null>(null);
  const [confirmClear, setConfirmClear] = useState<boolean>(false);

  useEffect(() => subscribeRequestHistory(setEntries), []);

  const range = useMemo(() => RANGE_OPTIONS.find((r) => r.id === rangeId) ?? RANGE_OPTIONS[1], [rangeId]);

  const rangeEntries = useMemo(() => {
    const cutoff = Date.now() - range.windowMs;
    return entries.filter((e) => new Date(e.timestamp).getTime() >= cutoff);
  }, [entries, range]);

  const filtered = useMemo(() => {
    return rangeEntries.filter((e) => {
      if (methodFilter && e.method !== methodFilter) return false;
      if (statusFilter) {
        const sc = parseInt(statusFilter, 10);
        if (!Number.isNaN(sc) && e.statusCode !== sc) return false;
      }
      if (pathFilter && !e.path.toLowerCase().includes(pathFilter.toLowerCase())) return false;
      return true;
    });
  }, [rangeEntries, methodFilter, statusFilter, pathFilter]);

  const success = filtered.filter((e) => e.statusCode >= 200 && e.statusCode < 400).length;
  const failure = filtered.length - success;
  const avgMs = filtered.length > 0 ? filtered.reduce((s, e) => s + e.durationMs, 0) / filtered.length : 0;
  const successRate = filtered.length > 0 ? ((success / filtered.length) * 100).toFixed(1) : '—';

  function resetFilters(): void {
    setMethodFilter('');
    setStatusFilter('');
    setPathFilter('');
  }

  return (
    <>
      <div className="workspace-header">
        <div className="workspace-title">
          <div className="workspace-title-row">
            <h2>Request History</h2>
            <span className="count-badge">{filtered.length}</span>
          </div>
          <p className="workspace-subtitle">
            Browser-side capture of every API call. Retained for 30 days and auto-purged.
          </p>
        </div>
        <div className="workspace-actions">
          <button className="btn btn-secondary" onClick={resetFilters}>
            Reset filters
          </button>
          <button
            className="btn btn-danger"
            onClick={() => setConfirmClear(true)}
            disabled={entries.length === 0}
          >
            Clear all
          </button>
        </div>
      </div>

      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'minmax(260px, 340px) minmax(0, 1fr)',
          gap: 16,
          marginBottom: 16,
        }}
        className="rh-grid"
      >
        <div className="workspace-card">
          <div className="workspace-card-header">
            <h3>Summary</h3>
            <span className="workspace-subtitle">{range.label}</span>
          </div>
          <div className="workspace-card-body" style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 12 }}>
            <div className="stat-card">
              <div className="stat-card-label">Total</div>
              <div className="stat-card-value">{filtered.length}</div>
            </div>
            <div className="stat-card">
              <div className="stat-card-label">Success rate</div>
              <div className="stat-card-value">{successRate === '—' ? '—' : `${successRate}%`}</div>
            </div>
            <div className="stat-card">
              <div className="stat-card-label">Failed</div>
              <div className="stat-card-value">{failure}</div>
            </div>
            <div className="stat-card">
              <div className="stat-card-label">Avg duration</div>
              <div className="stat-card-value">{filtered.length ? `${avgMs.toFixed(1)} ms` : '—'}</div>
            </div>
          </div>
        </div>

        <div className="workspace-card">
          <div className="workspace-card-header">
            <h3>Activity</h3>
            <div className="range-group" role="tablist" aria-label="Activity range">
              {RANGE_OPTIONS.map((r) => (
                <button
                  key={r.id}
                  type="button"
                  className={`range-btn ${rangeId === r.id ? 'active' : ''}`}
                  onClick={() => setRangeId(r.id)}
                >
                  {r.label}
                </button>
              ))}
            </div>
          </div>
          <div className="workspace-card-body">
            <HistoryChart entries={filtered} range={range} />
            <HistoryChartLegend />
          </div>
        </div>
      </div>

      <div className="workspace-card">
        <div className="workspace-card-header">
          <h3>Filters</h3>
        </div>
        <div className="workspace-card-body">
          <div className="field-grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))' }}>
            <label className="field">
              <span>Method</span>
              <select value={methodFilter} onChange={(e) => setMethodFilter(e.target.value)}>
                <option value="">All methods</option>
                <option>GET</option>
                <option>POST</option>
                <option>PUT</option>
                <option>DELETE</option>
                <option>PATCH</option>
                <option>OPTIONS</option>
                <option>HEAD</option>
              </select>
            </label>
            <label className="field">
              <span>Status code</span>
              <input value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} placeholder="e.g. 404" />
            </label>
            <label className="field">
              <span>Path contains</span>
              <input value={pathFilter} onChange={(e) => setPathFilter(e.target.value)} placeholder="/v1.0/indexes" />
            </label>
          </div>
        </div>
      </div>

      <div className="workspace-card">
        <div className="workspace-card-header">
          <h3>Entries</h3>
          <span className="workspace-subtitle">{filtered.length} visible</span>
        </div>
        <div className="workspace-card-body tight" style={{ overflowX: 'auto' }}>
          <table className="data-table">
            <thead>
              <tr>
                <th>When</th>
                <th>Method</th>
                <th>Path</th>
                <th>Status</th>
                <th>Duration</th>
                <th className="actions-column">Actions</th>
              </tr>
            </thead>
            <tbody>
              {filtered.length === 0 && (
                <tr>
                  <td colSpan={6}>
                    <div className="empty-state compact-empty-state">
                      <p className="empty-state-description">No entries match the current filters.</p>
                    </div>
                  </td>
                </tr>
              )}
              {filtered.map((e) => (
                <tr key={e.id} className="clickable-row" onClick={() => setDetail(e)}>
                  <td style={{ color: 'var(--text-secondary)' }}>{new Date(e.timestamp).toLocaleString()}</td>
                  <td>
                    <span className={`api-method-badge method-${e.method.toLowerCase()}`}>{e.method}</span>
                  </td>
                  <td>
                    <code>{e.path}</code>
                  </td>
                  <td>
                    <StatusBadge status={e.statusCode} />
                  </td>
                  <td>{e.durationMs.toFixed(1)} ms</td>
                  <td className="actions-column" onClick={(ev) => ev.stopPropagation()}>
                    <ActionMenu
                      items={[
                        { key: 'edit', label: 'View details', onClick: () => setDetail(e) },
                        { key: 'json', label: 'View JSON', onClick: () => setViewingJson(e) },
                        {
                          key: 'delete',
                          label: 'Remove from history',
                          danger: true,
                          onClick: () => removeRequestHistoryEntry(e.id),
                        },
                      ]}
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {detail && <DetailModal entry={detail} onClose={() => setDetail(null)} />}

      <ViewJsonModal
        open={viewingJson !== null}
        title={viewingJson ? `${viewingJson.method} ${viewingJson.path}` : 'Entry JSON'}
        data={viewingJson}
        onClose={() => setViewingJson(null)}
      />

      <ConfirmDialog
        open={confirmClear}
        title="Clear all request history?"
        message={<>This will delete all captured request history from this browser. The server is unaffected.</>}
        confirmLabel="Clear all"
        dangerous
        onConfirm={() => {
          clearRequestHistory();
          setConfirmClear(false);
        }}
        onCancel={() => setConfirmClear(false)}
      />
    </>
  );
}

function DetailModal({ entry, onClose }: { entry: RequestHistoryEntryClient; onClose: () => void }) {
  return (
    <Modal
      open
      onClose={onClose}
      title="Request details"
      size="large"
      footer={<button className="btn btn-secondary" onClick={onClose}>Close</button>}
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        <div
          className="stat-grid"
          style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(140px, 1fr))' }}
        >
          <div className="stat-card">
            <div className="stat-card-label">Method</div>
            <div>
              <span className={`api-method-badge method-${entry.method.toLowerCase()}`}>{entry.method}</span>
            </div>
          </div>
          <div className="stat-card">
            <div className="stat-card-label">Status</div>
            <div>
              <StatusBadge status={entry.statusCode} />
            </div>
          </div>
          <div className="stat-card">
            <div className="stat-card-label">Duration</div>
            <div className="stat-card-value" style={{ fontSize: '1.1rem' }}>
              {entry.durationMs.toFixed(2)} ms
            </div>
          </div>
          <div className="stat-card">
            <div className="stat-card-label">When</div>
            <div style={{ fontSize: '0.85rem' }}>{new Date(entry.timestamp).toLocaleString()}</div>
          </div>
        </div>

        <div>
          <div
            style={{
              fontSize: '0.72rem',
              textTransform: 'uppercase',
              letterSpacing: '0.05em',
              color: 'var(--text-secondary)',
              marginBottom: 6,
            }}
          >
            Path
          </div>
          <div className="code-block" style={{ maxHeight: 'unset' }}>
            {entry.path}
          </div>
        </div>

        {entry.requestBody && (
          <section>
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                marginBottom: 6,
              }}
            >
              <div
                style={{
                  fontSize: '0.72rem',
                  textTransform: 'uppercase',
                  letterSpacing: '0.05em',
                  color: 'var(--text-secondary)',
                }}
              >
                Request body
              </div>
              <CopyIconButton text={entry.requestBody} />
            </div>
            <JsonViewer data={entry.requestBody} />
          </section>
        )}

        {entry.responseBody && (
          <section>
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                marginBottom: 6,
              }}
            >
              <div
                style={{
                  fontSize: '0.72rem',
                  textTransform: 'uppercase',
                  letterSpacing: '0.05em',
                  color: 'var(--text-secondary)',
                }}
              >
                Response body
              </div>
              <CopyIconButton text={entry.responseBody} />
            </div>
            <JsonViewer data={entry.responseBody} />
          </section>
        )}

        {entry.errorMessage && !entry.responseBody && (
          <div className="form-error">{entry.errorMessage}</div>
        )}
      </div>
    </Modal>
  );
}
