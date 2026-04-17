import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  addVector,
  addVectors,
  enumerateVectors,
  getIndex,
  getVector,
  listAllIndexes,
  removeVector,
} from '../api/client';
import type {
  AddVectorRequest,
  EnumerationResult,
  IndexSummary,
  VectorEntry,
} from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import Modal from '../components/shared/Modal';
import Pagination from '../components/shared/Pagination';
import ViewJsonModal from '../components/shared/ViewJsonModal';
import { RefreshIcon } from '../components/shared/Icons';

export default function Vectors() {
  const [searchParams, setSearchParams] = useSearchParams();
  const initialIndex = searchParams.get('index') ?? '';

  const [indexes, setIndexes] = useState<IndexSummary[]>([]);
  const [selectedName, setSelectedName] = useState<string>(initialIndex);
  const [selectedIndex, setSelectedIndex] = useState<IndexSummary | null>(null);
  const [page, setPage] = useState<EnumerationResult<VectorEntry> | null>(null);
  const [loading, setLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  const [skip, setSkip] = useState<number>(0);
  const [maxResults, setMaxResults] = useState<number>(50);
  const [prefix, setPrefix] = useState<string>('');
  const [labelsField, setLabelsField] = useState<string>('');
  const [tagsField, setTagsField] = useState<string>('');
  const [caseInsensitive, setCaseInsensitive] = useState<boolean>(false);
  const [filterError, setFilterError] = useState<string | null>(null);
  const [includeVectors, setIncludeVectors] = useState<boolean>(false);

  const [viewingJson, setViewingJson] = useState<VectorEntry | null>(null);
  const [editing, setEditing] = useState<VectorEntry | null>(null);
  const [toDelete, setToDelete] = useState<VectorEntry | null>(null);
  const [showAdd, setShowAdd] = useState<boolean>(false);

  // Load index list once.
  useEffect(() => {
    listAllIndexes()
      .then((list) => {
        setIndexes(list);
        if (!selectedName && list.length > 0) {
          setSelectedName(list[0].name);
        }
      })
      .catch((e) => setError(e instanceof Error ? e.message : String(e)));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const reload = useCallback(async () => {
    if (!selectedName) {
      setPage(null);
      setSelectedIndex(null);
      return;
    }
    setLoading(true);
    setError(null);
    setFilterError(null);

    // Parse optional label/tag filters before issuing the request so parse errors
    // surface immediately instead of shipping an empty query.
    const labels = labelsField.trim().length > 0
      ? labelsField.split(',').map((s) => s.trim()).filter((s) => s.length > 0)
      : undefined;

    let tags: Record<string, string> | undefined;
    if (tagsField.trim().length > 0) {
      try {
        const obj: unknown = JSON.parse(tagsField);
        if (typeof obj !== 'object' || obj === null || Array.isArray(obj)) {
          throw new Error('Tags must be a JSON object.');
        }
        tags = {};
        for (const [k, v] of Object.entries(obj as Record<string, unknown>)) {
          tags[k] = v === null || v === undefined ? '' : String(v);
        }
      } catch (e) {
        setFilterError(e instanceof Error ? e.message : String(e));
        setLoading(false);
        return;
      }
    }

    try {
      const [ix, result] = await Promise.all([
        getIndex(selectedName),
        enumerateVectors(
          selectedName,
          {
            skip,
            maxResults,
            prefix: prefix.trim() || undefined,
            ordering: 'NameAscending',
            labels,
            tags,
            caseInsensitive: caseInsensitive || undefined,
          },
          includeVectors,
        ),
      ]);
      setSelectedIndex(ix);
      setPage(result);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  }, [selectedName, skip, maxResults, prefix, labelsField, tagsField, caseInsensitive, includeVectors]);

  useEffect(() => {
    void reload();
  }, [reload]);

  // Sync the URL with the selected index so links are shareable / back-button works.
  useEffect(() => {
    if (selectedName) {
      if (searchParams.get('index') !== selectedName) {
        setSearchParams({ index: selectedName }, { replace: true });
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedName]);

  async function confirmDelete(): Promise<void> {
    if (!toDelete || !selectedName) return;
    try {
      await removeVector(selectedName, toDelete.guid);
      setToDelete(null);
      await reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  const items = page?.objects ?? [];
  const totalRecords = page?.totalRecords ?? 0;

  return (
    <>
      <div className="workspace-header">
        <div className="workspace-title">
          <div className="workspace-title-row">
            <h2>Vectors</h2>
            {totalRecords > 0 && <span className="count-badge">{totalRecords.toLocaleString()}</span>}
          </div>
          <p className="workspace-subtitle">
            Browse, inspect, and remove vectors in an index. Pick the index from the dropdown.
          </p>
        </div>
        <div className="workspace-actions">
          <button
            className="btn btn-primary"
            onClick={() => setShowAdd(true)}
            disabled={!selectedName}
            title={selectedName ? 'Add vector to this index' : 'Select an index first'}
          >
            Add vector
          </button>
          <button
            className={`btn-icon ${loading ? 'spin-on-active' : ''}`}
            onClick={reload}
            disabled={loading || !selectedName}
            title="Refresh"
            aria-label="Refresh"
          >
            <RefreshIcon />
          </button>
        </div>
      </div>

      {error && <div className="form-error">{error}</div>}
      {filterError && <div className="form-error">Filter parse error: {filterError}</div>}

      <div className="workspace-card" style={{ padding: 12 }}>
        <div className="field-grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))' }}>
          <label className="field">
            <span>Index</span>
            <select
              value={selectedName}
              onChange={(e) => {
                setSelectedName(e.target.value);
                setSkip(0);
              }}
            >
              <option value="">— choose —</option>
              {indexes.map((i) => (
                <option key={i.guid} value={i.name}>
                  {i.name} ({i.dimension}-d, {i.vectorCount.toLocaleString()} vectors)
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span>GUID prefix</span>
            <input
              value={prefix}
              onChange={(e) => {
                setPrefix(e.target.value);
                setSkip(0);
              }}
              placeholder="filter by leading chars of the GUID"
              disabled={!selectedName}
            />
          </label>
          <label className="field" style={{ justifyContent: 'center' }}>
            <span>Include vector values</span>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <input
                id="include-vectors"
                type="checkbox"
                checked={includeVectors}
                onChange={(e) => setIncludeVectors(e.target.checked)}
                disabled={!selectedName}
                style={{ width: 'auto' }}
              />
              <label htmlFor="include-vectors" style={{ fontSize: '0.82rem', color: 'var(--text-secondary)' }}>
                (heavier payload)
              </label>
            </div>
          </label>
        </div>

        <details style={{ marginTop: 12, border: '1px solid var(--border-color)', borderRadius: 6, padding: '8px 12px' }}>
          <summary style={{ cursor: 'pointer', fontSize: '0.85rem', color: 'var(--text-secondary)' }}>
            Metadata filters (Labels &amp; Tags)
          </summary>
          <div style={{ marginTop: 10, display: 'flex', flexDirection: 'column', gap: 10 }}>
            <div style={{ fontSize: '0.78rem', color: 'var(--text-secondary)' }}>
              Both filters use AND semantics — a record is kept only when every label you list is present and
              every tag key/value matches. Records dropped by the filter are reported as <code>FilteredCount</code> in the pagination footer.
            </div>
            <label className="field">
              <span>Labels (comma-separated; every label must be present)</span>
              <input
                value={labelsField}
                onChange={(e) => {
                  setLabelsField(e.target.value);
                  setSkip(0);
                }}
                placeholder="red, small"
                disabled={!selectedName}
              />
            </label>
            <label className="field">
              <span>Tags (JSON object; every key must match)</span>
              <textarea
                rows={3}
                value={tagsField}
                onChange={(e) => {
                  setTagsField(e.target.value);
                  setSkip(0);
                }}
                placeholder='{"env": "prod", "owner": "alice"}'
                disabled={!selectedName}
              />
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: '0.85rem' }}>
              <input
                type="checkbox"
                checked={caseInsensitive}
                onChange={(e) => {
                  setCaseInsensitive(e.target.checked);
                  setSkip(0);
                }}
                disabled={!selectedName}
                style={{ width: 'auto' }}
              />
              <span>Case-insensitive label/tag comparison</span>
            </label>
          </div>
        </details>
      </div>

      <div className="workspace-card">
        <div className="workspace-card-body tight" style={{ overflowX: 'auto' }}>
          <table className="data-table">
            <thead>
              <tr>
                <th>GUID</th>
                <th>Name</th>
                <th>Labels</th>
                {includeVectors && <th>Vector (first 8 dims)</th>}
                <th className="actions-column">Actions</th>
              </tr>
            </thead>
            <tbody>
              {!selectedName && (
                <tr>
                  <td colSpan={includeVectors ? 5 : 4}>
                    <div className="empty-state compact-empty-state">
                      <p className="empty-state-description">Select an index above to view its vectors.</p>
                    </div>
                  </td>
                </tr>
              )}
              {selectedName && loading && (
                <tr>
                  <td colSpan={includeVectors ? 5 : 4}>
                    <div className="loading-spinner">
                      <span className="spinner-ring" /> Loading vectors…
                    </div>
                  </td>
                </tr>
              )}
              {selectedName && !loading && items.length === 0 && (
                <tr>
                  <td colSpan={includeVectors ? 5 : 4}>
                    <div className="empty-state compact-empty-state">
                      <p className="empty-state-description">
                        No vectors in this index yet. Click <b>Add vector</b> to insert one.
                      </p>
                    </div>
                  </td>
                </tr>
              )}
              {selectedName &&
                items.map((v) => (
                  <tr
                    key={v.guid}
                    className="clickable-row"
                    onClick={() => setEditing(v)}
                  >
                    <td className="mono">{v.guid}</td>
                    <td>{v.name ?? <span className="muted">—</span>}</td>
                    <td>
                      {v.labels && v.labels.length > 0
                        ? v.labels.map((l) => <span key={l} className="status-pill muted" style={{ marginRight: 4 }}>{l}</span>)
                        : <span className="muted">—</span>}
                    </td>
                    {includeVectors && (
                      <td className="mono" style={{ color: 'var(--text-secondary)' }}>
                        {v.vector ? formatVectorPreview(v.vector) : '—'}
                      </td>
                    )}
                    <td className="actions-column" onClick={(e) => e.stopPropagation()}>
                      <ActionMenu
                        items={[
                          { key: 'edit', label: 'Edit', onClick: () => setEditing(v) },
                          { key: 'json', label: 'View JSON', onClick: () => setViewingJson(v) },
                          { key: 'delete', label: 'Delete', danger: true, onClick: () => setToDelete(v) },
                        ]}
                      />
                    </td>
                  </tr>
                ))}
            </tbody>
          </table>
        </div>
        {(page?.filteredCount ?? 0) > 0 && (
          <div style={{ padding: '6px 12px', fontSize: '0.78rem', color: 'var(--text-secondary)' }}>
            {page!.filteredCount.toLocaleString()} record{page!.filteredCount === 1 ? '' : 's'} filtered out by metadata filter
          </div>
        )}
        <Pagination
          skip={skip}
          maxResults={maxResults}
          totalRecords={totalRecords}
          onPageChange={(s) => setSkip(s)}
          onPageSizeChange={(m) => {
            setMaxResults(m);
            setSkip(0);
          }}
        />
      </div>

      {editing && selectedName && (
        <EditVectorModal
          indexName={selectedName}
          dimension={selectedIndex?.dimension ?? 0}
          entry={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            void reload();
          }}
        />
      )}

      <ViewJsonModal
        open={viewingJson !== null}
        title={viewingJson ? `Vector ${viewingJson.guid}` : 'Vector JSON'}
        data={viewingJson}
        onClose={() => setViewingJson(null)}
      />

      <ConfirmDialog
        open={toDelete !== null}
        title="Delete vector?"
        message={
          <>
            This will permanently remove <b className="mono">{toDelete?.guid}</b> from{' '}
            <b className="mono">{selectedName}</b>.
          </>
        }
        confirmLabel="Delete"
        dangerous
        onConfirm={confirmDelete}
        onCancel={() => setToDelete(null)}
      />

      {showAdd && selectedName && (
        <AddVectorModal
          indexName={selectedName}
          dimension={selectedIndex?.dimension ?? 0}
          onClose={() => setShowAdd(false)}
          onAdded={() => {
            setShowAdd(false);
            void reload();
          }}
        />
      )}
    </>
  );
}

function formatVectorPreview(vec: number[]): string {
  const n = Math.min(vec.length, 8);
  const head = vec.slice(0, n).map((x) => x.toFixed(3)).join(', ');
  return vec.length > n ? `[${head}, … ×${vec.length - n}]` : `[${head}]`;
}

// ---------------------------------------------------------------------------
// Add-vector modal (Single / Batch tabs)
// ---------------------------------------------------------------------------

interface AddProps {
  indexName: string;
  dimension: number;
  onClose: () => void;
  onAdded: () => void;
}

function AddVectorModal({ indexName, dimension, onClose, onAdded }: AddProps) {
  const [tab, setTab] = useState<'single' | 'batch'>('single');

  return (
    <Modal
      open
      onClose={onClose}
      title={`Add vector to ${indexName}`}
      size="large"
      footer={
        <button className="btn btn-secondary" onClick={onClose}>
          Close
        </button>
      }
    >
      <div className="range-group" style={{ marginBottom: 12 }}>
        <button
          type="button"
          className={`range-btn ${tab === 'single' ? 'active' : ''}`}
          onClick={() => setTab('single')}
        >
          Single
        </button>
        <button
          type="button"
          className={`range-btn ${tab === 'batch' ? 'active' : ''}`}
          onClick={() => setTab('batch')}
        >
          Batch
        </button>
      </div>

      {tab === 'single' ? (
        <SingleForm indexName={indexName} dimension={dimension} onAdded={onAdded} />
      ) : (
        <BatchForm indexName={indexName} dimension={dimension} onAdded={onAdded} />
      )}
    </Modal>
  );
}

function parseVectorInput(value: string, expectedDim: number): number[] {
  const cleaned = value.trim().replace(/^\[|\]$/g, '');
  const nums = cleaned
    .split(/[,\s]+/)
    .filter((s) => s.length > 0)
    .map((s) => Number(s));
  if (nums.some((n) => Number.isNaN(n))) {
    throw new Error('Vector contains non-numeric values');
  }
  if (expectedDim > 0 && nums.length !== expectedDim) {
    throw new Error(`Vector length ${nums.length} does not match index dimension ${expectedDim}`);
  }
  return nums;
}

// ---------------------------------------------------------------------------
// Edit-vector modal
// ---------------------------------------------------------------------------

interface EditProps {
  indexName: string;
  dimension: number;
  entry: VectorEntry;
  onClose: () => void;
  onSaved: () => void;
}

function EditVectorModal({ indexName, dimension, entry, onClose, onSaved }: EditProps) {
  const [loading, setLoading] = useState<boolean>(entry.vector === undefined);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [guid, setGuid] = useState<string>(entry.guid);
  const [vectorText, setVectorText] = useState<string>(
    entry.vector ? entry.vector.join(', ') : '',
  );
  const [nameField, setNameField] = useState<string>(entry.name ?? '');
  const [labelsField, setLabelsField] = useState<string>((entry.labels ?? []).join(', '));
  const [tagsField, setTagsField] = useState<string>(
    entry.tags ? JSON.stringify(entry.tags, null, 2) : '{}',
  );
  const [submitting, setSubmitting] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  // Fetch the full vector body if the row came from a listing without vector values.
  useEffect(() => {
    if (entry.vector !== undefined) return;
    let cancelled = false;
    (async () => {
      try {
        const full = await getVector(indexName, entry.guid);
        if (cancelled) return;
        setGuid(full.guid);
        setVectorText(full.vector ? full.vector.join(', ') : '');
        setNameField(full.name ?? '');
        setLabelsField((full.labels ?? []).join(', '));
        setTagsField(full.tags ? JSON.stringify(full.tags, null, 2) : '{}');
      } catch (err) {
        if (cancelled) return;
        setLoadError(err instanceof Error ? err.message : String(err));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [indexName, entry.guid, entry.vector]);

  function fillRandom(): void {
    if (dimension <= 0) return;
    const vals: number[] = [];
    for (let i = 0; i < dimension; i++) vals.push(Number((Math.random() * 2 - 1).toFixed(4)));
    setVectorText(vals.join(', '));
  }

  async function onSave(e: FormEvent): Promise<void> {
    e.preventDefault();
    setError(null);

    const originalGuid = entry.guid;
    const newGuid = guid.trim();
    if (!newGuid) {
      setError('GUID cannot be empty.');
      return;
    }

    let newVector: number[];
    try {
      newVector = parseVectorInput(vectorText, dimension);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
      return;
    }

    setSubmitting(true);
    try {
      // The server has no update endpoint; remove-then-add is the semantic equivalent.
      // We always remove first, then add, so we can preserve the same GUID when unchanged
      // (adding the new record first would otherwise collide with the existing one).
      const parsedLabels = labelsField.trim().length > 0
        ? labelsField.split(',').map((s) => s.trim()).filter((s) => s.length > 0)
        : undefined;
      let parsedTags: Record<string, unknown> | undefined;
      try {
        const t = JSON.parse(tagsField);
        parsedTags = typeof t === 'object' && t !== null && !Array.isArray(t) ? t : undefined;
      } catch {
        parsedTags = undefined;
      }

      await removeVector(indexName, originalGuid);
      try {
        await addVector(indexName, {
          GUID: newGuid,
          Vector: newVector,
          Name: nameField.trim() || undefined,
          Labels: parsedLabels,
          Tags: parsedTags,
        });
      } catch (addErr) {
        setError(
          `Old vector was removed but the new vector could not be inserted: ${
            addErr instanceof Error ? addErr.message : String(addErr)
          }. The old record is gone — add it back manually if needed.`,
        );
        setSubmitting(false);
        return;
      }
      onSaved();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={`Edit vector ${entry.guid}`}
      size="large"
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button
            className="btn btn-primary"
            type="submit"
            form="edit-vector-form"
            disabled={submitting || loading}
          >
            {submitting ? 'Saving…' : 'Save'}
          </button>
        </>
      }
    >
      {loading ? (
        <div className="loading-spinner">
          <span className="spinner-ring" /> Loading vector values…
        </div>
      ) : loadError ? (
        <div className="form-error">{loadError}</div>
      ) : (
        <form id="edit-vector-form" onSubmit={onSave} style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          {error && <div className="form-error">{error}</div>}
          <div
            style={{
              padding: '8px 12px',
              background: 'var(--color-warning-bg)',
              color: 'var(--text-primary)',
              borderRadius: 6,
              fontSize: '0.82rem',
              border: '1px solid var(--border-color)',
            }}
          >
            Edits are applied as remove-then-insert under the hood (HnswLite has no update endpoint).
            Changing the GUID effectively re-keys the record; the HNSW graph is rebuilt around the new entry.
          </div>
          <label className="field">
            <span>GUID</span>
            <input value={guid} onChange={(e) => setGuid(e.target.value)} className="mono" />
          </label>
          <label className="field">
            <span>Name</span>
            <input value={nameField} onChange={(e) => setNameField(e.target.value)} placeholder="Optional human-readable name" />
          </label>
          <label className="field">
            <span>Labels (comma-separated)</span>
            <input value={labelsField} onChange={(e) => setLabelsField(e.target.value)} placeholder="category-a, category-b" />
          </label>
          <label className="field">
            <span>Tags (JSON object)</span>
            <textarea rows={3} value={tagsField} onChange={(e) => setTagsField(e.target.value)} placeholder='{"source": "openai", "model": "text-embedding-3-small"}' />
          </label>
          <label className="field">
            <span>Vector{dimension > 0 ? ` (${dimension} floats)` : ''}</span>
            <textarea
              rows={8}
              value={vectorText}
              onChange={(e) => setVectorText(e.target.value)}
              placeholder="0.12, -0.34, 0.56, ..."
            />
          </label>
          <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
            <button type="button" className="btn btn-secondary" onClick={fillRandom} disabled={dimension <= 0}>
              Fill with random
            </button>
          </div>
        </form>
      )}
    </Modal>
  );
}

function SingleForm({
  indexName,
  dimension,
  onAdded,
}: {
  indexName: string;
  dimension: number;
  onAdded: () => void;
}) {
  const [vectorText, setVectorText] = useState<string>('');
  const [guid, setGuid] = useState<string>('');
  const [nameField, setNameField] = useState<string>('');
  const [labelsField, setLabelsField] = useState<string>('');
  const [tagsField, setTagsField] = useState<string>('{}');
  const [submitting, setSubmitting] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  const [lastAdded, setLastAdded] = useState<string | null>(null);

  async function onSubmit(e: FormEvent): Promise<void> {
    e.preventDefault();
    setError(null);
    try {
      const vector = parseVectorInput(vectorText, dimension);
      const parsedLabels = labelsField.trim().length > 0
        ? labelsField.split(',').map((s) => s.trim()).filter((s) => s.length > 0)
        : undefined;
      let parsedTags: Record<string, unknown> | undefined;
      try {
        const t = JSON.parse(tagsField);
        parsedTags = typeof t === 'object' && t !== null && !Array.isArray(t) ? t : undefined;
      } catch {
        parsedTags = undefined;
      }
      setSubmitting(true);
      const payload: AddVectorRequest = {
        GUID: guid.trim() || undefined,
        Vector: vector,
        Name: nameField.trim() || undefined,
        Labels: parsedLabels,
        Tags: parsedTags,
      };
      await addVector(indexName, payload);
      setLastAdded(payload.GUID ?? '(auto-generated)');
      setVectorText('');
      setGuid('');
      setNameField('');
      setLabelsField('');
      setTagsField('{}');
      onAdded();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setSubmitting(false);
    }
  }

  function fillRandom(): void {
    if (dimension <= 0) return;
    const vals: number[] = [];
    for (let i = 0; i < dimension; i++) vals.push(Number((Math.random() * 2 - 1).toFixed(4)));
    setVectorText(vals.join(', '));
  }

  return (
    <form onSubmit={onSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      {error && <div className="form-error">{error}</div>}
      {lastAdded && (
        <div
          style={{
            padding: '8px 12px',
            background: 'var(--color-success-bg)',
            color: 'var(--color-success)',
            borderRadius: 6,
            fontSize: '0.85rem',
          }}
        >
          Added <span className="mono">{lastAdded}</span>
        </div>
      )}

      <label className="field">
        <span>GUID (optional — auto-generated if blank)</span>
        <input value={guid} onChange={(e) => setGuid(e.target.value)} placeholder="auto-generated" />
      </label>
      <label className="field">
        <span>Name</span>
        <input value={nameField} onChange={(e) => setNameField(e.target.value)} placeholder="Optional human-readable name" />
      </label>
      <label className="field">
        <span>Labels (comma-separated)</span>
        <input value={labelsField} onChange={(e) => setLabelsField(e.target.value)} placeholder="category-a, category-b" />
      </label>
      <label className="field">
        <span>Tags (JSON object)</span>
        <textarea rows={2} value={tagsField} onChange={(e) => setTagsField(e.target.value)} placeholder='{"source": "openai"}' />
      </label>
      <label className="field">
        <span>Vector{dimension > 0 ? ` (${dimension} floats)` : ''}</span>
        <textarea
          rows={8}
          value={vectorText}
          onChange={(e) => setVectorText(e.target.value)}
          placeholder="0.12, -0.34, 0.56, ..."
        />
      </label>
      <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
        <button type="button" className="btn btn-secondary" onClick={fillRandom} disabled={dimension <= 0}>
          Fill with random
        </button>
        <button type="submit" className="btn btn-primary" disabled={submitting}>
          {submitting ? 'Adding…' : 'Add vector'}
        </button>
      </div>
    </form>
  );
}

function BatchForm({
  indexName,
  dimension,
  onAdded,
}: {
  indexName: string;
  dimension: number;
  onAdded: () => void;
}) {
  const [jsonText, setJsonText] = useState<string>(
    '[\n  { "Vector": [0.1, 0.2] },\n  { "Vector": [0.3, 0.4] }\n]',
  );
  const [submitting, setSubmitting] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  const [lastCount, setLastCount] = useState<number | null>(null);

  async function onSubmit(e: FormEvent): Promise<void> {
    e.preventDefault();
    setError(null);
    try {
      const parsed = JSON.parse(jsonText);
      if (!Array.isArray(parsed)) {
        throw new Error('Top-level JSON must be an array of {"GUID"?, "Vector":[...]} objects.');
      }
      const items: AddVectorRequest[] = parsed.map((p, i) => {
        if (!p || typeof p !== 'object' || !Array.isArray(p.Vector)) {
          throw new Error(`Entry ${i}: missing "Vector" array.`);
        }
        if (dimension > 0 && p.Vector.length !== dimension) {
          throw new Error(
            `Entry ${i}: vector length ${p.Vector.length} does not match index dimension ${dimension}.`,
          );
        }
        for (const v of p.Vector) {
          if (typeof v !== 'number' || !Number.isFinite(v)) {
            throw new Error(`Entry ${i}: vector contains non-numeric values.`);
          }
        }
        return { GUID: typeof p.GUID === 'string' ? p.GUID : undefined, Vector: p.Vector };
      });

      setSubmitting(true);
      await addVectors(indexName, { Vectors: items });
      setLastCount(items.length);
      onAdded();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setSubmitting(false);
    }
  }

  function fillRandom(): void {
    if (dimension <= 0) return;
    const batch: AddVectorRequest[] = [];
    for (let n = 0; n < 5; n++) {
      const vals: number[] = [];
      for (let i = 0; i < dimension; i++) vals.push(Number((Math.random() * 2 - 1).toFixed(4)));
      batch.push({ Vector: vals });
    }
    setJsonText(JSON.stringify(batch, null, 2));
  }

  return (
    <form onSubmit={onSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      {error && <div className="form-error">{error}</div>}
      {lastCount !== null && (
        <div
          style={{
            padding: '8px 12px',
            background: 'var(--color-success-bg)',
            color: 'var(--color-success)',
            borderRadius: 6,
            fontSize: '0.85rem',
          }}
        >
          Added {lastCount.toLocaleString()} vectors.
        </div>
      )}

      <label className="field">
        <span>Batch JSON — array of {'{"GUID"?, "Vector":[...]}'}</span>
        <textarea rows={14} value={jsonText} onChange={(e) => setJsonText(e.target.value)} />
      </label>
      <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
        <button type="button" className="btn btn-secondary" onClick={fillRandom} disabled={dimension <= 0}>
          Fill 5 random
        </button>
        <button type="submit" className="btn btn-primary" disabled={submitting}>
          {submitting ? 'Adding…' : 'Add batch'}
        </button>
      </div>
    </form>
  );
}
