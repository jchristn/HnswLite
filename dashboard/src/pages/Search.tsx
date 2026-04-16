import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { useSearchParams } from 'react-router-dom';
import { listAllIndexes, searchVectors } from '../api/client';
import type { IndexSummary, SearchResponse } from '../types/models';
import JsonViewer from '../components/shared/JsonViewer';
import SearchResultsTable from '../components/shared/SearchResultsTable';

export default function Search() {
  const [searchParams, setSearchParams] = useSearchParams();
  const [indexes, setIndexes] = useState<IndexSummary[]>([]);
  const [selectedName, setSelectedName] = useState<string>(searchParams.get('index') ?? '');
  const [vectorText, setVectorText] = useState<string>('');
  const [k, setK] = useState<number>(10);
  const [ef, setEf] = useState<string>('');
  const [result, setResult] = useState<SearchResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState<boolean>(false);

  const reload = useCallback(async () => {
    try {
      const list = await listAllIndexes();
      setIndexes(list);
      if (!selectedName && list.length > 0) setSelectedName(list[0].name);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }, [selectedName]);

  useEffect(() => {
    void reload();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Keep the URL in sync with the selected index so links are shareable.
  useEffect(() => {
    if (selectedName && searchParams.get('index') !== selectedName) {
      setSearchParams({ index: selectedName }, { replace: true });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedName]);

  const selected = indexes.find((i) => i.name === selectedName);

  async function onSubmit(e: FormEvent): Promise<void> {
    e.preventDefault();
    setError(null);
    setResult(null);
    if (!selected) return;
    try {
      const nums = vectorText
        .trim()
        .replace(/^\[|\]$/g, '')
        .split(/[,\s]+/)
        .filter((s) => s.length > 0)
        .map((s) => Number(s));
      if (nums.some((n) => Number.isNaN(n))) throw new Error('Non-numeric values in vector');
      if (nums.length !== selected.dimension) {
        throw new Error(`Vector length ${nums.length} does not match index dimension ${selected.dimension}`);
      }
      setSubmitting(true);
      const res = await searchVectors(selected.name, {
        Vector: nums,
        K: k,
        Ef: ef.trim() ? parseInt(ef, 10) : null,
      });
      setResult(res);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setSubmitting(false);
    }
  }

  function fillRandom(): void {
    if (!selected) return;
    const vals: number[] = [];
    for (let i = 0; i < selected.dimension; i++) vals.push(Number((Math.random() * 2 - 1).toFixed(4)));
    setVectorText(vals.join(', '));
  }

  return (
    <>
      <div className="workspace-header">
        <div className="workspace-title">
          <h2>Search</h2>
          <p className="workspace-subtitle">Run nearest-neighbor queries against any index.</p>
        </div>
      </div>

      {error && <div className="form-error">{error}</div>}

      <div className="workspace-card">
        <div className="workspace-card-header">
          <h3>Query</h3>
        </div>
        <div className="workspace-card-body">
          <form onSubmit={onSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
            <div className="field-grid" style={{ gridTemplateColumns: '2fr 1fr 1fr' }}>
              <label className="field">
                <span>Index</span>
                <select value={selectedName} onChange={(e) => setSelectedName(e.target.value)}>
                  <option value="">— choose —</option>
                  {indexes.map((i) => (
                    <option key={i.guid} value={i.name}>
                      {i.name} ({i.dimension}-d, {i.vectorCount} vectors)
                    </option>
                  ))}
                </select>
              </label>
              <label className="field">
                <span>K</span>
                <input type="number" min={1} value={k} onChange={(e) => setK(parseInt(e.target.value, 10) || 1)} />
              </label>
              <label className="field">
                <span>Ef (optional)</span>
                <input type="number" min={1} value={ef} onChange={(e) => setEf(e.target.value)} />
              </label>
            </div>

            <label className="field">
              <span>Query vector{selected ? ` (${selected.dimension} floats)` : ''}</span>
              <textarea rows={6} value={vectorText} onChange={(e) => setVectorText(e.target.value)} placeholder="0.12, -0.34, 0.56, ..." />
            </label>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
              <button type="button" className="btn btn-secondary" onClick={fillRandom} disabled={!selected}>
                Fill with random
              </button>
              <button type="submit" className="btn btn-primary" disabled={submitting || !selected}>
                {submitting ? 'Searching…' : 'Search'}
              </button>
            </div>
          </form>
        </div>
      </div>

      {result && (
        <div className="workspace-card">
          <div className="workspace-card-header">
            <h3>Results</h3>
            <span className="workspace-subtitle">
              {result.searchTimeMs.toFixed(2)} ms · {result.results.length} matches
            </span>
          </div>
          <div className="workspace-card-body tight">
            {selected && <SearchResultsTable indexName={selected.name} results={result.results} />}
          </div>
          <div style={{ padding: 16, borderTop: '1px solid var(--border-color)' }}>
            <details>
              <summary style={{ cursor: 'pointer', fontSize: '0.78rem', color: 'var(--text-secondary)' }}>
                Raw response
              </summary>
              <div style={{ marginTop: 8 }}>
                <JsonViewer data={result} />
              </div>
            </details>
          </div>
        </div>
      )}
    </>
  );
}
