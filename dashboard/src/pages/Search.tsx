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
  const [labelsField, setLabelsField] = useState<string>('');
  const [tagsField, setTagsField] = useState<string>('');
  const [caseInsensitive, setCaseInsensitive] = useState<boolean>(false);
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

      const parsedLabels = labelsField.trim().length > 0
        ? labelsField.split(',').map((s) => s.trim()).filter((s) => s.length > 0)
        : undefined;

      let parsedTags: Record<string, string> | undefined;
      if (tagsField.trim().length > 0) {
        let obj: unknown;
        try {
          obj = JSON.parse(tagsField);
        } catch {
          throw new Error('Tags must be a JSON object mapping string keys to string values.');
        }
        if (typeof obj !== 'object' || obj === null || Array.isArray(obj)) {
          throw new Error('Tags must be a JSON object.');
        }
        const stringified: Record<string, string> = {};
        for (const [key, value] of Object.entries(obj as Record<string, unknown>)) {
          stringified[key] = value === null || value === undefined ? '' : String(value);
        }
        parsedTags = stringified;
      }

      const res = await searchVectors(selected.name, {
        Vector: nums,
        K: k,
        Ef: ef.trim() ? parseInt(ef, 10) : null,
        Labels: parsedLabels,
        Tags: parsedTags,
        CaseInsensitive: caseInsensitive || undefined,
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

            <details style={{ border: '1px solid var(--border-color)', borderRadius: 6, padding: '8px 12px' }}>
              <summary style={{ cursor: 'pointer', fontSize: '0.85rem', color: 'var(--text-secondary)' }}>
                Metadata filters (Labels &amp; Tags)
              </summary>
              <div style={{ marginTop: 10, display: 'flex', flexDirection: 'column', gap: 10 }}>
                <div style={{ fontSize: '0.78rem', color: 'var(--text-secondary)' }}>
                  Both filters use AND semantics — a result is kept only when every label you list is present and
                  every tag key/value matches. The response includes a <code>FilteredCount</code> showing how many
                  HNSW candidates were dropped.
                </div>
                <label className="field">
                  <span>Labels (comma-separated; every label must be present)</span>
                  <input
                    value={labelsField}
                    onChange={(e) => setLabelsField(e.target.value)}
                    placeholder="red, small"
                  />
                </label>
                <label className="field">
                  <span>Tags (JSON object; every key must match)</span>
                  <textarea
                    rows={3}
                    value={tagsField}
                    onChange={(e) => setTagsField(e.target.value)}
                    placeholder='{"env": "prod", "owner": "alice"}'
                  />
                </label>
                <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: '0.85rem' }}>
                  <input
                    type="checkbox"
                    checked={caseInsensitive}
                    onChange={(e) => setCaseInsensitive(e.target.checked)}
                    style={{ width: 'auto' }}
                  />
                  <span>Case-insensitive label/tag comparison</span>
                </label>
              </div>
            </details>

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
              {result.filteredCount > 0 && ` · ${result.filteredCount} filtered out`}
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
