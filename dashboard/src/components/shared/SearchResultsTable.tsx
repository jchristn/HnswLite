import { useState } from 'react';
import type { VectorSearchResult } from '../../types/models';
import { removeVector } from '../../api/client';
import ActionMenu from './ActionMenu';
import Modal from './Modal';
import ViewJsonModal from './ViewJsonModal';
import ConfirmDialog from './ConfirmDialog';

interface SearchResultsTableProps {
  indexName: string;
  results: VectorSearchResult[];
  onDeleted?: () => void;
}

export default function SearchResultsTable({ indexName, results, onDeleted }: SearchResultsTableProps) {
  const [editing, setEditing] = useState<VectorSearchResult | null>(null);
  const [viewingJson, setViewingJson] = useState<VectorSearchResult | null>(null);
  const [toDelete, setToDelete] = useState<VectorSearchResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function onConfirmDelete(): Promise<void> {
    if (!toDelete) return;
    try {
      await removeVector(indexName, toDelete.guid);
      setToDelete(null);
      onDeleted?.();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  return (
    <>
      {error && <div className="form-error">{error}</div>}
      <div style={{ overflowX: 'auto' }}>
        <table className="data-table">
          <thead>
            <tr>
              <th>GUID</th>
              <th>Distance</th>
              <th className="actions-column">Actions</th>
            </tr>
          </thead>
          <tbody>
            {results.map((r) => (
              <tr key={r.guid} className="clickable-row" onClick={() => setEditing(r)}>
                <td className="mono">{r.guid}</td>
                <td>{r.distance.toFixed(6)}</td>
                <td className="actions-column" onClick={(e) => e.stopPropagation()}>
                  <ActionMenu
                    items={[
                      { key: 'edit', label: 'Edit', onClick: () => setEditing(r) },
                      { key: 'json', label: 'View JSON', onClick: () => setViewingJson(r) },
                      { key: 'delete', label: 'Delete', danger: true, onClick: () => setToDelete(r) },
                    ]}
                  />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {editing && <EditVectorModal vector={editing} onClose={() => setEditing(null)} />}

      <ViewJsonModal
        open={viewingJson !== null}
        title={viewingJson ? `Vector: ${viewingJson.guid}` : 'Vector JSON'}
        data={viewingJson}
        onClose={() => setViewingJson(null)}
      />

      <ConfirmDialog
        open={toDelete !== null}
        title="Delete vector?"
        message={
          <>
            This will remove <b className="mono">{toDelete?.guid}</b> from{' '}
            <b className="mono">{indexName}</b>.
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

function EditVectorModal({ vector, onClose }: { vector: VectorSearchResult; onClose: () => void }) {
  return (
    <Modal
      open
      onClose={onClose}
      title={`Edit vector: ${vector.guid}`}
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
        Vector payloads are immutable after insertion — fields are shown read-only.
      </div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        <label className="field">
          <span>GUID</span>
          <input value={vector.guid} readOnly className="mono" />
        </label>
        <label className="field">
          <span>Distance from query</span>
          <input value={vector.distance.toFixed(6)} readOnly />
        </label>
        <label className="field">
          <span>Vector ({vector.vector.length} floats)</span>
          <textarea rows={8} value={vector.vector.join(', ')} readOnly />
        </label>
      </div>
    </Modal>
  );
}
