import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { createIndex, deleteIndex, enumerateIndexes } from '../api/client';
import type {
  CreateIndexRequest,
  EnumerationOrder,
  EnumerationResult,
  IndexSummary,
} from '../types/models';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import Modal from '../components/shared/Modal';
import ActionMenu from '../components/shared/ActionMenu';
import ViewJsonModal from '../components/shared/ViewJsonModal';
import Pagination from '../components/shared/Pagination';
import { RefreshIcon } from '../components/shared/Icons';

export default function Indexes() {
  const navigate = useNavigate();
  const [page, setPage] = useState<EnumerationResult<IndexSummary> | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [showCreate, setShowCreate] = useState<boolean>(false);
  const [editing, setEditing] = useState<IndexSummary | null>(null);
  const [viewingJson, setViewingJson] = useState<IndexSummary | null>(null);
  const [toDelete, setToDelete] = useState<IndexSummary | null>(null);

  // Pagination / filter / sort state
  const [skip, setSkip] = useState<number>(0);
  const [maxResults, setMaxResults] = useState<number>(25);
  const [prefix, setPrefix] = useState<string>('');
  const [ordering, setOrdering] = useState<EnumerationOrder>('CreatedDescending');

  const reload = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await enumerateIndexes({
        maxResults,
        skip,
        prefix: prefix.trim() || undefined,
        ordering,
      });
      setPage(result);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  }, [maxResults, skip, prefix, ordering]);

  useEffect(() => {
    void reload();
  }, [reload]);

  const indexes = page?.objects ?? [];
  const totalRecords = page?.totalRecords ?? 0;

  async function onConfirmDelete(): Promise<void> {
    if (!toDelete) return;
    try {
      await deleteIndex(toDelete.name);
      setToDelete(null);
      await reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  return (
    <>
      <div className="workspace-header">
        <div className="workspace-title">
          <div className="workspace-title-row">
            <h2>Indices</h2>
            <span className="count-badge">{totalRecords.toLocaleString()}</span>
          </div>
          <p className="workspace-subtitle">
            Create and manage HNSW indices. Use the <b>Vectors</b> page to browse or insert vector data, and the <b>Search</b> page to run queries.
          </p>
        </div>
        <div className="workspace-actions">
          <button
            className={`btn-icon ${loading ? 'spin-on-active' : ''}`}
            onClick={reload}
            disabled={loading}
            title="Refresh"
            aria-label="Refresh"
          >
            <RefreshIcon />
          </button>
          <button className="btn btn-primary" onClick={() => setShowCreate(true)}>
            Create index
          </button>
        </div>
      </div>

      {error && <div className="form-error">{error}</div>}

      <div className="workspace-card" style={{ padding: 12 }}>
        <div className="field-grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))' }}>
          <label className="field">
            <span>Name prefix</span>
            <input
              value={prefix}
              onChange={(e) => {
                setPrefix(e.target.value);
                setSkip(0);
              }}
              placeholder="filter by start of name"
            />
          </label>
          <label className="field">
            <span>Order</span>
            <select
              value={ordering}
              onChange={(e) => {
                setOrdering(e.target.value as EnumerationOrder);
                setSkip(0);
              }}
            >
              <option value="CreatedDescending">Newest first</option>
              <option value="CreatedAscending">Oldest first</option>
              <option value="NameAscending">Name A–Z</option>
              <option value="NameDescending">Name Z–A</option>
            </select>
          </label>
        </div>
      </div>

      <div className="workspace-card">
        <div className="workspace-card-body tight" style={{ overflowX: 'auto' }}>
          <table className="data-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Dim.</th>
                <th>Storage</th>
                <th>Distance</th>
                <th>M / MaxM / EfC</th>
                <th>Vectors</th>
                <th>Created</th>
                <th className="actions-column">Actions</th>
              </tr>
            </thead>
            <tbody>
              {loading && (
                <tr>
                  <td colSpan={8}>
                    <div className="loading-spinner">
                      <span className="spinner-ring" /> Loading indexes…
                    </div>
                  </td>
                </tr>
              )}
              {!loading && indexes.length === 0 && (
                <tr>
                  <td colSpan={8}>
                    <div className="empty-state compact-empty-state">
                      <p className="empty-state-description">
                        No indexes yet. Click <b>Create index</b> to add one.
                      </p>
                    </div>
                  </td>
                </tr>
              )}
              {indexes.map((ix) => (
                <tr
                  key={ix.guid}
                  className="clickable-row"
                  onClick={() => setEditing(ix)}
                >
                  <td className="mono">{ix.name}</td>
                  <td>{ix.dimension}</td>
                  <td>
                    <span className="status-pill muted">{ix.storageType}</span>
                  </td>
                  <td>{ix.distanceFunction}</td>
                  <td className="mono">
                    {ix.m} / {ix.maxM} / {ix.efConstruction}
                  </td>
                  <td>{ix.vectorCount.toLocaleString()}</td>
                  <td style={{ color: 'var(--text-secondary)' }}>
                    {new Date(ix.createdUtc).toLocaleString()}
                  </td>
                  <td className="actions-column" onClick={(e) => e.stopPropagation()}>
                    <ActionMenu
                      items={[
                        {
                          key: 'vectors',
                          label: 'View vectors',
                          onClick: () => navigate(`/vectors?index=${encodeURIComponent(ix.name)}`),
                        },
                        {
                          key: 'search',
                          label: 'Search in this index',
                          onClick: () => navigate(`/search?index=${encodeURIComponent(ix.name)}`),
                        },
                        { key: 'edit', label: 'Edit configuration', onClick: () => setEditing(ix) },
                        { key: 'json', label: 'View JSON', onClick: () => setViewingJson(ix) },
                        { key: 'delete', label: 'Delete', danger: true, onClick: () => setToDelete(ix) },
                      ]}
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
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

      {showCreate && (
        <CreateIndexModal
          onClose={() => setShowCreate(false)}
          onCreated={() => {
            setShowCreate(false);
            void reload();
          }}
        />
      )}

      {editing && <EditIndexModal index={editing} onClose={() => setEditing(null)} />}

      <ViewJsonModal
        open={viewingJson !== null}
        title={viewingJson ? `Index: ${viewingJson.name}` : 'Index JSON'}
        data={viewingJson}
        onClose={() => setViewingJson(null)}
      />

      <ConfirmDialog
        open={toDelete !== null}
        title="Delete index?"
        message={
          <>
            This will permanently delete <b className="mono">{toDelete?.name}</b> and all its vectors.
          </>
        }
        confirmLabel="Delete"
        dangerous
        onConfirm={onConfirmDelete}
        onCancel={() => setToDelete(null)}
      />
    </>
  );
}

function EditIndexModal({ index, onClose }: { index: IndexSummary; onClose: () => void }) {
  // The HnswLite server does not expose an index-update endpoint — index
  // configuration is immutable after creation. This modal presents the
  // configuration in read-only form with a clear notice.
  return (
    <Modal
      open
      onClose={onClose}
      title={`Edit index: ${index.name}`}
      footer={
        <button className="btn btn-secondary" onClick={onClose}>
          Close
        </button>
      }
    >
      <div
        style={{
          padding: '10px 12px',
          marginBottom: 14,
          borderRadius: 6,
          background: 'var(--color-warning-bg)',
          color: 'var(--text-primary)',
          fontSize: '0.82rem',
          border: '1px solid var(--border-color)',
        }}
      >
        Index configuration is immutable after creation — fields are shown read-only.
      </div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        <label className="field">
          <span>Name</span>
          <input value={index.name} readOnly />
        </label>
        <label className="field">
          <span>GUID</span>
          <input value={index.guid} readOnly className="mono" />
        </label>
        <div className="field-grid">
          <label className="field">
            <span>Dimension</span>
            <input value={index.dimension} readOnly />
          </label>
          <label className="field">
            <span>Storage</span>
            <input value={index.storageType} readOnly />
          </label>
          <label className="field">
            <span>Distance function</span>
            <input value={index.distanceFunction} readOnly />
          </label>
        </div>
        <div className="field-grid">
          <label className="field">
            <span>M</span>
            <input value={index.m} readOnly />
          </label>
          <label className="field">
            <span>MaxM</span>
            <input value={index.maxM} readOnly />
          </label>
          <label className="field">
            <span>EfConstruction</span>
            <input value={index.efConstruction} readOnly />
          </label>
        </div>
        <div className="field-grid">
          <label className="field">
            <span>Vector count</span>
            <input value={index.vectorCount} readOnly />
          </label>
          <label className="field">
            <span>Created</span>
            <input value={new Date(index.createdUtc).toLocaleString()} readOnly />
          </label>
        </div>
      </div>
    </Modal>
  );
}

function CreateIndexModal({ onClose, onCreated }: { onClose: () => void; onCreated: () => void }) {
  const [form, setForm] = useState<CreateIndexRequest>({
    Name: '',
    Dimension: 384,
    StorageType: 'SQLite',
    DistanceFunction: 'Cosine',
    M: 16,
    MaxM: 32,
    EfConstruction: 200,
  });
  const [submitting, setSubmitting] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(e: FormEvent): Promise<void> {
    e.preventDefault();
    if (!form.Name.trim()) {
      setError('Name is required');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await createIndex(form);
      onCreated();
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
      title="Create index"
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" form="create-index-form" className="btn btn-primary" disabled={submitting}>
            {submitting ? 'Creating…' : 'Create'}
          </button>
        </>
      }
    >
      <form id="create-index-form" onSubmit={onSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
        {error && <div className="form-error">{error}</div>}

        <label className="field">
          <span>Name</span>
          <input value={form.Name} onChange={(e) => setForm({ ...form, Name: e.target.value })} autoFocus />
        </label>

        <div className="field-grid">
          <label className="field">
            <span>Dimension</span>
            <input
              type="number"
              min={1}
              value={form.Dimension}
              onChange={(e) => setForm({ ...form, Dimension: parseInt(e.target.value, 10) || 0 })}
            />
          </label>
          <label className="field">
            <span>Storage</span>
            <select
              value={form.StorageType}
              onChange={(e) => setForm({ ...form, StorageType: e.target.value as 'RAM' | 'SQLite' })}
            >
              <option value="RAM">RAM</option>
              <option value="SQLite">SQLite</option>
            </select>
          </label>
          <label className="field">
            <span>Distance function</span>
            <select
              value={form.DistanceFunction}
              onChange={(e) =>
                setForm({ ...form, DistanceFunction: e.target.value as 'Euclidean' | 'Cosine' | 'DotProduct' })
              }
            >
              <option value="Euclidean">Euclidean</option>
              <option value="Cosine">Cosine</option>
              <option value="DotProduct">DotProduct</option>
            </select>
          </label>
        </div>

        <div className="field-grid">
          <label className="field">
            <span>M (connections)</span>
            <input
              type="number"
              min={2}
              max={100}
              value={form.M}
              onChange={(e) => setForm({ ...form, M: parseInt(e.target.value, 10) || 16 })}
            />
          </label>
          <label className="field">
            <span>MaxM</span>
            <input
              type="number"
              min={1}
              max={200}
              value={form.MaxM}
              onChange={(e) => setForm({ ...form, MaxM: parseInt(e.target.value, 10) || 32 })}
            />
          </label>
          <label className="field">
            <span>EfConstruction</span>
            <input
              type="number"
              min={1}
              max={2000}
              value={form.EfConstruction}
              onChange={(e) => setForm({ ...form, EfConstruction: parseInt(e.target.value, 10) || 200 })}
            />
          </label>
        </div>
      </form>
    </Modal>
  );
}
