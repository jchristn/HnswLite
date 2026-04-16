import { useEffect, useMemo, useState } from 'react';
import { getRequestHistory, listAllIndexes, subscribeRequestHistory } from '../api/client';
import type { RequestHistoryEntryClient } from '../api/client';
import type { IndexSummary } from '../types/models';
import HistoryChart, { HistoryChartLegend, RANGE_OPTIONS } from '../components/shared/HistoryChart';
import type { RangeId } from '../components/shared/HistoryChart';

export default function Dashboard() {
  const [indexes, setIndexes] = useState<IndexSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [history, setHistory] = useState<RequestHistoryEntryClient[]>(() => getRequestHistory());
  const [rangeId, setRangeId] = useState<RangeId>('day');

  useEffect(() => {
    const unsub = subscribeRequestHistory(setHistory);
    listAllIndexes()
      .then(setIndexes)
      .catch((e) => setError(e instanceof Error ? e.message : String(e)));
    return unsub;
  }, []);

  const range = useMemo(() => RANGE_OPTIONS.find((r) => r.id === rangeId) ?? RANGE_OPTIONS[1], [rangeId]);

  const rangeEntries = useMemo(() => {
    const cutoff = Date.now() - range.windowMs;
    return history.filter((e) => new Date(e.timestamp).getTime() >= cutoff);
  }, [history, range]);

  const totalVectors = (indexes ?? []).reduce((s, i) => s + i.vectorCount, 0);
  const success = rangeEntries.filter((h) => h.statusCode >= 200 && h.statusCode < 400).length;
  const failure = rangeEntries.length - success;
  const avgMs =
    rangeEntries.length > 0
      ? rangeEntries.reduce((s, h) => s + h.durationMs, 0) / rangeEntries.length
      : 0;
  const successRate = rangeEntries.length > 0 ? ((success / rangeEntries.length) * 100).toFixed(1) : '—';

  return (
    <>
      <div className="workspace-header">
        <div className="workspace-title">
          <h2>Dashboard</h2>
          <p className="workspace-subtitle">Overview of indexes, vectors, and recent API traffic.</p>
        </div>
      </div>

      {error && <div className="form-error">{error}</div>}

      <div className="stat-grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', marginBottom: 16 }}>
        <div className="stat-card">
          <div className="stat-card-label">Indexes</div>
          <div className="stat-card-value">{indexes?.length ?? '—'}</div>
        </div>
        <div className="stat-card">
          <div className="stat-card-label">Total vectors</div>
          <div className="stat-card-value">{indexes ? totalVectors.toLocaleString() : '—'}</div>
        </div>
        <div className="stat-card">
          <div className="stat-card-label">Requests ({range.label.toLowerCase()})</div>
          <div className="stat-card-value">{rangeEntries.length}</div>
          <div className="stat-card-sub">
            {success} ok · {failure} err
          </div>
        </div>
        <div className="stat-card">
          <div className="stat-card-label">Success rate</div>
          <div className="stat-card-value">{successRate === '—' ? '—' : `${successRate}%`}</div>
        </div>
        <div className="stat-card">
          <div className="stat-card-label">Avg latency</div>
          <div className="stat-card-value">{rangeEntries.length ? `${avgMs.toFixed(1)} ms` : '—'}</div>
        </div>
      </div>

      <div className="workspace-card">
        <div className="workspace-card-header">
          <h3>Request activity</h3>
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
          <HistoryChart entries={rangeEntries} range={range} />
          <HistoryChartLegend />
        </div>
      </div>
    </>
  );
}
