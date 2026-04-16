import { useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import type { RequestHistoryEntryClient } from '../../api/client';

export type RangeId = 'hour' | 'day' | 'week' | 'month';

export interface RangeOption {
  id: RangeId;
  label: string;
  windowMs: number;
  bucketMs: number;
}

export const RANGE_OPTIONS: RangeOption[] = [
  { id: 'hour', label: 'Last hour', windowMs: 60 * 60 * 1000, bucketMs: 60 * 1000 }, // 1-min buckets → 60 bars
  { id: 'day', label: 'Last 24 hours', windowMs: 24 * 60 * 60 * 1000, bucketMs: 30 * 60 * 1000 }, // 30-min → 48 bars
  { id: 'week', label: 'Last 7 days', windowMs: 7 * 24 * 60 * 60 * 1000, bucketMs: 3 * 60 * 60 * 1000 }, // 3-h → 56 bars
  { id: 'month', label: 'Last 30 days', windowMs: 30 * 24 * 60 * 60 * 1000, bucketMs: 12 * 60 * 60 * 1000 }, // 12-h → 60 bars
];

interface Bucket {
  start: number;
  end: number;
  total: number;
  success: number;
  failure: number;
  avgDurationMs: number;
}

interface HistoryChartProps {
  entries: RequestHistoryEntryClient[];
  range: RangeOption;
}

function computeBuckets(entries: RequestHistoryEntryClient[], range: RangeOption): Bucket[] {
  const now = Date.now();
  const cutoff = now - range.windowMs;
  const start = Math.floor(cutoff / range.bucketMs) * range.bucketMs;
  const count = Math.ceil(range.windowMs / range.bucketMs);

  const buckets: Bucket[] = [];
  for (let i = 0; i < count; i++) {
    const s = start + i * range.bucketMs;
    buckets.push({ start: s, end: s + range.bucketMs, total: 0, success: 0, failure: 0, avgDurationMs: 0 });
  }

  const durationSums = new Array<number>(count).fill(0);
  for (const e of entries) {
    const t = new Date(e.timestamp).getTime();
    if (!Number.isFinite(t) || t < cutoff || t > now) continue;
    const idx = Math.min(count - 1, Math.floor((t - start) / range.bucketMs));
    if (idx < 0) continue;
    const b = buckets[idx];
    b.total += 1;
    if (e.statusCode >= 200 && e.statusCode < 400) b.success += 1;
    else b.failure += 1;
    durationSums[idx] += e.durationMs;
  }
  for (let i = 0; i < count; i++) {
    buckets[i].avgDurationMs = buckets[i].total > 0 ? durationSums[i] / buckets[i].total : 0;
  }
  return buckets;
}

function formatBucketTime(range: RangeId, ts: number): string {
  const d = new Date(ts);
  if (range === 'hour') return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  if (range === 'day') return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  return d.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}

function buildAxisLabels(buckets: Bucket[], range: RangeId): Array<{ idx: number; label: string }> {
  if (buckets.length === 0) return [];
  const labels: Array<{ idx: number; label: string }> = [];
  const desired = 5;
  const step = Math.max(1, Math.floor(buckets.length / (desired - 1)));
  for (let i = 0; i < buckets.length; i += step) {
    labels.push({ idx: i, label: formatBucketTime(range, buckets[i].start) });
    if (labels.length >= desired) break;
  }
  const lastIdx = buckets.length - 1;
  if (labels.length === 0 || labels[labels.length - 1].idx !== lastIdx) {
    labels.push({ idx: lastIdx, label: formatBucketTime(range, buckets[lastIdx].start) });
  }
  return labels;
}

interface TooltipState {
  bucket: Bucket;
  clientX: number;
  clientY: number;
}

export default function HistoryChart({ entries, range }: HistoryChartProps) {
  const buckets = useMemo(() => computeBuckets(entries, range), [entries, range]);
  const [tooltip, setTooltip] = useState<TooltipState | null>(null);

  const max = Math.max(1, ...buckets.map((b) => b.total));
  const totalCount = buckets.reduce((s, b) => s + b.total, 0);

  if (totalCount === 0) {
    return (
      <div className="empty-state compact-empty-state">
        <p className="empty-state-description">No requests in the selected range.</p>
      </div>
    );
  }

  const axisTicks = Array.from(new Set([max, Math.round(max / 2), 0]));
  const axisLabels = buildAxisLabels(buckets, range.id);

  return (
    <div className="history-chart-shell">
      <div className="history-chart-axis-label">Requests</div>
      <div className="history-chart-main">
        <div className="history-chart-axis-values">
          {axisTicks.map((t) => (
            <span key={t}>{t}</span>
          ))}
        </div>
        <div className="history-chart-plot">
          {buckets.map((b, i) => {
            const total = b.total;
            const heightPct = total > 0 ? Math.max(6, (total / max) * 100) : 3;
            const successPct = total > 0 ? (b.success / total) * 100 : 0;
            const failurePct = total > 0 ? (b.failure / total) * 100 : 0;
            return (
              <div
                key={i}
                className="history-chart-column"
                onMouseEnter={(e) => setTooltip({ bucket: b, clientX: e.clientX, clientY: e.clientY })}
                onMouseMove={(e) => setTooltip({ bucket: b, clientX: e.clientX, clientY: e.clientY })}
                onMouseLeave={() => setTooltip(null)}
              >
                <div className="history-chart-bar" style={{ height: `${heightPct}%` }}>
                  <span className="history-chart-bar-failure" style={{ height: `${failurePct}%` }} />
                  <span className="history-chart-bar-success" style={{ height: `${successPct}%` }} />
                </div>
              </div>
            );
          })}
        </div>
        <div className="history-chart-label-row">
          {axisLabels.map((l) => (
            <span key={l.idx}>{l.label}</span>
          ))}
        </div>
      </div>

      {tooltip && createPortal(
        <div
          className="history-chart-tooltip"
          style={{
            left: `${Math.min(tooltip.clientX + 14, window.innerWidth - 300)}px`,
            top: `${Math.max(tooltip.clientY - 60, 8)}px`,
          }}
        >
          <strong>
            {new Date(tooltip.bucket.start).toLocaleString()} – {new Date(tooltip.bucket.end).toLocaleTimeString()}
          </strong>
          <span>{tooltip.bucket.total} total</span>
          <span>{tooltip.bucket.success} success</span>
          <span>{tooltip.bucket.failure} failed</span>
          <span>{tooltip.bucket.avgDurationMs.toFixed(2)} ms avg</span>
        </div>,
        document.body,
      )}
    </div>
  );
}

export function HistoryChartLegend() {
  return (
    <div className="history-chart-legend">
      <span>
        <span className="legend-dot success" />
        2xx/3xx
      </span>
      <span>
        <span className="legend-dot failure" />
        4xx/5xx/errors
      </span>
    </div>
  );
}
