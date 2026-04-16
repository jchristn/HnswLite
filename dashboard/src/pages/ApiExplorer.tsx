import { useMemo, useState } from 'react';
import type { FormEvent } from 'react';
import { rawRequest } from '../api/client';
import type { RawResponse } from '../api/client';
import StatusBadge from '../components/shared/StatusBadge';
import CopyIconButton from '../components/shared/CopyIconButton';

interface ExplorerRoute {
  key: string;
  method: 'GET' | 'POST' | 'DELETE';
  path: string; // template with {param} placeholders
  description: string;
  params: string[];
  hasBody: boolean;
  bodyTemplate?: string;
}

const ROUTES: ExplorerRoute[] = [
  {
    key: 'list',
    method: 'GET',
    path: '/v1.0/indexes',
    description: 'List all indexes.',
    params: [],
    hasBody: false,
  },
  {
    key: 'get',
    method: 'GET',
    path: '/v1.0/indexes/{name}',
    description: 'Get details for a single index.',
    params: ['name'],
    hasBody: false,
  },
  {
    key: 'create',
    method: 'POST',
    path: '/v1.0/indexes',
    description: 'Create a new index.',
    params: [],
    hasBody: true,
    bodyTemplate: JSON.stringify(
      {
        Name: 'example',
        Dimension: 384,
        StorageType: 'RAM',
        DistanceFunction: 'Cosine',
        M: 16,
        MaxM: 32,
        EfConstruction: 200,
      },
      null,
      2,
    ),
  },
  {
    key: 'delete',
    method: 'DELETE',
    path: '/v1.0/indexes/{name}',
    description: 'Delete an index and all its vectors.',
    params: ['name'],
    hasBody: false,
  },
  {
    key: 'search',
    method: 'POST',
    path: '/v1.0/indexes/{name}/search',
    description: 'Find nearest neighbors for a query vector.',
    params: ['name'],
    hasBody: true,
    bodyTemplate: JSON.stringify({ Vector: [0.1, 0.2, 0.3], K: 10, Ef: null }, null, 2),
  },
  {
    key: 'addvec',
    method: 'POST',
    path: '/v1.0/indexes/{name}/vectors',
    description: 'Add a single vector.',
    params: ['name'],
    hasBody: true,
    bodyTemplate: JSON.stringify({ Vector: [0.1, 0.2, 0.3] }, null, 2),
  },
  {
    key: 'addbatch',
    method: 'POST',
    path: '/v1.0/indexes/{name}/vectors/batch',
    description: 'Add multiple vectors.',
    params: ['name'],
    hasBody: true,
    bodyTemplate: JSON.stringify(
      { Vectors: [{ Vector: [0.1, 0.2, 0.3] }, { Vector: [0.4, 0.5, 0.6] }] },
      null,
      2,
    ),
  },
  {
    key: 'removevec',
    method: 'DELETE',
    path: '/v1.0/indexes/{name}/vectors/{guid}',
    description: 'Remove a single vector.',
    params: ['name', 'guid'],
    hasBody: false,
  },
];

function substitutePath(template: string, params: Record<string, string>): string {
  return template.replace(/\{(\w+)\}/g, (_m, key: string) =>
    encodeURIComponent(params[key] ?? `{${key}}`),
  );
}

function prettyBody(body: string): string {
  if (!body) return '';
  try {
    return JSON.stringify(JSON.parse(body), null, 2);
  } catch {
    return body;
  }
}

export default function ApiExplorer() {
  const [selectedKey, setSelectedKey] = useState<string>(ROUTES[0].key);
  const route = useMemo(() => ROUTES.find((r) => r.key === selectedKey)!, [selectedKey]);
  const [params, setParams] = useState<Record<string, string>>({});
  const [body, setBody] = useState<string>(route.bodyTemplate ?? '');
  const [response, setResponse] = useState<RawResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState<boolean>(false);
  const [activeTab, setActiveTab] = useState<'body' | 'headers'>('body');

  function selectRoute(key: string): void {
    setSelectedKey(key);
    const r = ROUTES.find((x) => x.key === key)!;
    setBody(r.bodyTemplate ?? '');
    setParams({});
    setResponse(null);
    setError(null);
    setActiveTab('body');
  }

  async function onExecute(e: FormEvent): Promise<void> {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    setResponse(null);
    try {
      // Validate JSON body up-front so the user gets a useful error before the network.
      if (route.hasBody && body.trim().length > 0) {
        try {
          JSON.parse(body);
        } catch (jsonErr) {
          throw new Error(`Invalid JSON body: ${jsonErr instanceof Error ? jsonErr.message : String(jsonErr)}`);
        }
      }

      const path = substitutePath(route.path, params);
      const res = await rawRequest(route.method, path, route.hasBody ? body : undefined);
      setResponse(res);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setSubmitting(false);
    }
  }

  const resolvedPath = substitutePath(route.path, params);
  const prettyResBody = response ? prettyBody(response.body) : '';

  return (
    <>
      <div className="workspace-header">
        <div className="workspace-title">
          <h2>API Explorer</h2>
          <p className="workspace-subtitle">
            Interactive playground for every HnswLite REST endpoint — authenticated with your
            current session key.
          </p>
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'minmax(260px, 320px) minmax(0, 1fr)', gap: 16 }}>
        <div className="workspace-card" style={{ alignSelf: 'start' }}>
          <div className="workspace-card-header">
            <h3>Endpoints</h3>
          </div>
          <div className="workspace-card-body" style={{ padding: 6 }}>
            {ROUTES.map((r) => {
              const active = r.key === selectedKey;
              return (
                <button
                  key={r.key}
                  onClick={() => selectRoute(r.key)}
                  style={{
                    display: 'grid',
                    gridTemplateColumns: '56px 1fr',
                    alignItems: 'center',
                    gap: 8,
                    width: '100%',
                    textAlign: 'left',
                    background: active ? 'var(--color-primary-alpha)' : 'transparent',
                    border: 'none',
                    borderRadius: 6,
                    padding: '8px 10px',
                    marginBottom: 2,
                    color: 'var(--text-primary)',
                    cursor: 'pointer',
                    fontFamily: 'inherit',
                  }}
                >
                  <span className={`api-method-badge method-${r.method.toLowerCase()}`}>{r.method}</span>
                  <span className="mono" style={{ fontSize: '0.78rem' }}>{r.path}</span>
                </button>
              );
            })}
          </div>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 16, minWidth: 0 }}>
          <div className="workspace-card">
            <div className="workspace-card-header">
              <div style={{ display: 'flex', alignItems: 'center', gap: 10, minWidth: 0, flex: 1 }}>
                <span className={`api-method-badge method-${route.method.toLowerCase()}`}>{route.method}</span>
                <h3
                  className="mono"
                  style={{
                    fontSize: '0.95rem',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                  }}
                >
                  {resolvedPath}
                </h3>
              </div>
            </div>
            <div className="workspace-card-body">
              <p style={{ marginTop: 0, marginBottom: 12, color: 'var(--text-secondary)', fontSize: '0.85rem' }}>
                {route.description}
              </p>

              <form onSubmit={onExecute} style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                {route.params.length > 0 && (
                  <div className="field-grid">
                    {route.params.map((p) => (
                      <label className="field" key={p}>
                        <span>{p}</span>
                        <input
                          value={params[p] ?? ''}
                          onChange={(e) => setParams({ ...params, [p]: e.target.value })}
                          placeholder={p}
                        />
                      </label>
                    ))}
                  </div>
                )}

                {route.hasBody && (
                  <label className="field">
                    <span>Request body (JSON)</span>
                    <textarea rows={10} value={body} onChange={(e) => setBody(e.target.value)} />
                  </label>
                )}

                <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                  <button type="submit" className="btn btn-primary" disabled={submitting}>
                    {submitting ? 'Executing…' : 'Execute'}
                  </button>
                </div>
              </form>
            </div>
          </div>

          {error && <div className="form-error">{error}</div>}

          {response && (
            <div className="workspace-card">
              <div className="workspace-card-header">
                <div style={{ display: 'flex', alignItems: 'center', gap: 14, flexWrap: 'wrap' }}>
                  <h3>Response</h3>
                  <StatusBadge status={response.status} />
                  {response.statusText && (
                    <span style={{ fontSize: '0.82rem', color: 'var(--text-secondary)' }}>
                      {response.statusText}
                    </span>
                  )}
                  <span
                    style={{
                      fontSize: '0.78rem',
                      color: 'var(--text-secondary)',
                      padding: '2px 10px',
                      border: '1px solid var(--border-color)',
                      borderRadius: 999,
                    }}
                  >
                    {response.durationMs.toFixed(1)} ms
                  </span>
                  <span
                    style={{
                      fontSize: '0.78rem',
                      color: 'var(--text-secondary)',
                      padding: '2px 10px',
                      border: '1px solid var(--border-color)',
                      borderRadius: 999,
                    }}
                  >
                    {response.body.length.toLocaleString()} bytes
                  </span>
                </div>
              </div>
              <div className="workspace-card-body">
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 10 }}>
                  <div className="range-group">
                    <button
                      type="button"
                      className={`range-btn ${activeTab === 'body' ? 'active' : ''}`}
                      onClick={() => setActiveTab('body')}
                    >
                      Body
                    </button>
                    <button
                      type="button"
                      className={`range-btn ${activeTab === 'headers' ? 'active' : ''}`}
                      onClick={() => setActiveTab('headers')}
                    >
                      Headers ({Object.keys(response.headers).length})
                    </button>
                  </div>
                  {activeTab === 'body' ? (
                    <CopyIconButton text={prettyResBody} />
                  ) : (
                    <CopyIconButton
                      text={Object.entries(response.headers)
                        .map(([k, v]) => `${k}: ${v}`)
                        .join('\n')}
                    />
                  )}
                </div>

                {activeTab === 'body' ? (
                  prettyResBody.length > 0 ? (
                    <pre className="code-block" style={{ maxHeight: 500 }}>
                      {prettyResBody}
                    </pre>
                  ) : (
                    <div
                      style={{
                        padding: 16,
                        textAlign: 'center',
                        color: 'var(--text-secondary)',
                        fontSize: '0.85rem',
                      }}
                    >
                      (empty body)
                    </div>
                  )
                ) : (
                  <div style={{ overflowX: 'auto' }}>
                    <table className="data-table">
                      <thead>
                        <tr>
                          <th>Header</th>
                          <th>Value</th>
                        </tr>
                      </thead>
                      <tbody>
                        {Object.keys(response.headers).length === 0 && (
                          <tr>
                            <td colSpan={2}>
                              <div className="empty-state compact-empty-state">
                                <p className="empty-state-description">No response headers.</p>
                              </div>
                            </td>
                          </tr>
                        )}
                        {Object.entries(response.headers).map(([k, v]) => (
                          <tr key={k}>
                            <td className="mono">{k}</td>
                            <td className="mono">{v}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>
            </div>
          )}
        </div>
      </div>
    </>
  );
}
